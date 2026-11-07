using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace EyeCare.Services;

/// <summary>
/// 休息提醒服务：跟踪工作 / 休息状态，按 20-20-20 法则或自定义间隔触发休息。
/// 支持智能暂停（检测用户离开）、长短休息交替。
/// </summary>
public class BreakReminderService : IDisposable
{
    private readonly SettingsService _settings;
    private Timer? _timer;
    private int _elapsedWorkSeconds;
    private int _remainingBreakSeconds;
    private bool _onBreak;
    private int _workedSinceLongBreakSeconds;

    /// <summary>工作计时每秒进度（秒）。</summary>
    public event Action<int, int>? WorkTick;     // (已工作秒, 工作间隔秒)

    /// <summary>休息中每秒进度（秒）。</summary>
    public event Action<int>? BreakTick;         // (剩余休息秒)

    /// <summary>开始休息（参数为是否长休息）。</summary>
    public event Action<bool>? BreakStarted;

    /// <summary>休息前预告（参数为剩余预警秒数，如 30）。</summary>
    public event Action<int>? PreBreakNotice;

    /// <summary>休息结束，恢复工作（参数表示是否完整完成，false 表示被跳过）。</summary>
    public event Action<bool>? BreakFinished;

    /// <summary>完成一次完整休息（未被跳过）。</summary>
    public event Action? BreakCompleted;

    /// <summary>用户跳过本次休息。</summary>
    public event Action? BreakSkipped;

    /// <summary>累计工作满一分钟（供健康用眼统计）。</summary>
    public event Action? MinuteWorked;

    public bool IsOnBreak => _onBreak;

    /// <summary>本轮已工作秒数（供小组件/概览页显示）。</summary>
    public int ElapsedWorkSeconds => _elapsedWorkSeconds;

    /// <summary>当前休息剩余秒数。</summary>
    public int RemainingBreakSeconds => Math.Max(0, _remainingBreakSeconds);

    /// <summary>获取当前工作/休息状态的即时可读文本描述（供概览页打开时即刻展示）。</summary>
    public (string status, int progressPercent) GetCurrentStatus()
    {
        var s = _settings.Data;
        if (!s.BreakReminderEnabled)
            return ("休息提醒已关闭", 0);

        if (_onBreak)
            return ($"正在休息中 (剩余 {_remainingBreakSeconds} 秒)", 100);

        if (s.PauseOnFullscreen && App.FullscreenPause.IsFullscreenActive)
            return ("全屏程序运行中 (已暂停计时)", 0);

        if (s.SmartPause && IsUserAway())
            return ("离开电脑中 (已智能暂停)", 0);

        int total = Math.Clamp(s.WorkIntervalMinutes, 1, 180) * 60;
        int pct = total > 0 ? (int)(_elapsedWorkSeconds * 100.0 / total) : 0;
        string text = $"已工作 {_elapsedWorkSeconds / 60} 分 {_elapsedWorkSeconds % 60} 秒 / 目标 {total / 60} 分钟";
        return (text, Math.Clamp(pct, 0, 100));
    }

    public BreakReminderService(SettingsService settings)
    {
        _settings = settings;
    }

    public void ApplySettings(bool resetTimer = false)
    {
        if (resetTimer)
        {
            ResetWorkTimer();
        }
        Start();
    }

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(Tick, null, 0, 1000);
    }

    /// <summary>手动手动触发一次休息。</summary>
    public void StartBreakNow(bool longBreak = false)
    {
        BeginBreak(longBreak);
    }

    /// <summary>推迟休息：将已工作计时往前回拨指定分钟数。</summary>
    public void SnoozeWork(int minutes)
    {
        if (_onBreak) return;
        _elapsedWorkSeconds = Math.Max(0, _elapsedWorkSeconds - minutes * 60);
    }

    /// <summary>结束当前休息，立即恢复工作。</summary>
    public void SkipBreak()
    {
        if (!_onBreak) return;
        _onBreak = false;
        _elapsedWorkSeconds = 0;
        _remainingBreakSeconds = 0;
        BreakSkipped?.Invoke();
        BreakFinished?.Invoke(false);
    }

    /// <summary>手动重置工作计时。</summary>
    public void ResetWorkTimer()
    {
        _elapsedWorkSeconds = 0;
    }

    private void Tick(object? state)
    {
        var s = _settings.Data;
        if (!s.BreakReminderEnabled)
            return;

        // 全屏程序（游戏/视频/演示）期间暂停计时，避免休息弹窗打断
        if (s.PauseOnFullscreen && App.FullscreenPause.IsFullscreenActive && !_onBreak)
        {
            return; // 暂停工作计时
        }

        // 智能暂停：用户离开电脑时暂停
        if (s.SmartPause && IsUserAway())
        {
            if (!_onBreak)
            {
                return; // 暂停工作计时
            }
        }

        if (_onBreak)
        {
            _remainingBreakSeconds--;
            BreakTick?.Invoke(Math.Max(0, _remainingBreakSeconds));
            if (_remainingBreakSeconds <= 0)
            {
                _onBreak = false;
                _elapsedWorkSeconds = 0;
                BreakCompleted?.Invoke();
                BreakFinished?.Invoke(true);
            }
            return;
        }

        // 工作状态
        _elapsedWorkSeconds++;
        _workedSinceLongBreakSeconds++;
        if (_elapsedWorkSeconds > 0 && _elapsedWorkSeconds % 60 == 0)
        {
            MinuteWorked?.Invoke();
        }
        int intervalMin = Math.Clamp(s.WorkIntervalMinutes, 1, 180);
        int workInterval = intervalMin * 60;
        WorkTick?.Invoke(_elapsedWorkSeconds, workInterval);

        // 休息前温和预告（提前 30 秒）
        if (s.PreBreakHeadsUpEnabled && (workInterval - _elapsedWorkSeconds == 30))
        {
            PreBreakNotice?.Invoke(30);
        }

        // 到达工作间隔 → 判断是短休息还是长休息
        if (_elapsedWorkSeconds >= workInterval)
        {
            bool isLong = s.LongBreakEnabled &&
                _workedSinceLongBreakSeconds >= s.LongBreakIntervalMinutes * 60;
            BeginBreak(isLong);
        }
    }

    private void BeginBreak(bool longBreak)
    {
        var s = _settings.Data;
        _onBreak = true;
        _remainingBreakSeconds = longBreak ? s.LongBreakSeconds : s.ShortBreakSeconds;
        _elapsedWorkSeconds = 0;

        if (longBreak)
            _workedSinceLongBreakSeconds = 0;

        BreakStarted?.Invoke(longBreak);
    }

    /// <summary>检测用户是否离开电脑（无输入超过 60 秒）。</summary>
    public static bool IsUserAway()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return false;
        uint idleMs = (uint)Environment.TickCount - lii.dwTime;
        return idleMs > 60000;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}