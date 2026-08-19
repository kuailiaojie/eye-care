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

    /// <summary>休息结束，恢复工作。</summary>
    public event Action? BreakFinished;

    public bool IsOnBreak => _onBreak;

    public BreakReminderService(SettingsService settings)
    {
        _settings = settings;
    }

    public void ApplySettings()
    {
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

    /// <summary>结束当前休息，立即恢复工作。</summary>
    public void SkipBreak()
    {
        if (!_onBreak) return;
        _onBreak = false;
        _elapsedWorkSeconds = 0;
        _remainingBreakSeconds = 0;
        BreakFinished?.Invoke();
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
                BreakFinished?.Invoke();
            }
            return;
        }

        // 工作状态
        _elapsedWorkSeconds++;
        _workedSinceLongBreakSeconds++;
        int workInterval = s.WorkIntervalMinutes * 60;
        WorkTick?.Invoke(_elapsedWorkSeconds, workInterval);

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
    private static bool IsUserAway()
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