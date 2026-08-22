using System;
using System.Threading;

namespace EyeCare.Services;

/// <summary>
/// 桌面小组件服务：应用在托盘运行时，桌面角落持续显示使用时长与当前状态。
/// 独立 WinUI 3 置顶窗口，可拖动、双击打开主界面，设置页可开关。
/// </summary>
public class DesktopWidgetService : IDisposable
{
    private readonly SettingsService _settings;
    private WidgetWindow? _window;
    private Timer? _timer;
    private bool _visible;

    public DesktopWidgetService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>启动服务：创建小组件（如启用）并开始每秒刷新。</summary>
    public void Start()
    {
        _settings.SettingsChanged += OnSettingsChanged;
        _timer = new Timer(OnTick, null, 0, 1000);
        ApplyVisibility();
    }

    private void OnSettingsChanged()
    {
        App.UiDispatcher.TryEnqueue(ApplyVisibility);
    }

    private void ApplyVisibility()
    {
        if (_settings.Data.DesktopWidgetEnabled)
        {
            if (_window is null)
            {
                // 位置由 WidgetWindow 在 Loaded 后自行恢复（默认右上角或自定义位置）
                _window = new WidgetWindow();
                _window.Activate();
                _visible = true;
            }
            else if (!_visible)
            {
                _window.AppWindow.Show();
                _window.KeepTopmost();
                _visible = true;
            }
        }
        else if (_window is not null && _visible)
        {
            _window.AppWindow.Hide();
            _visible = false;
        }
    }

    private void OnTick(object? state)
    {
        if (!_settings.Data.DesktopWidgetEnabled) return;
        App.UiDispatcher.TryEnqueue(Refresh);
    }

    private void Refresh()
    {
        if (_window is null || !_visible) return;

        var s = _settings.Data;
        var br = App.BreakReminder;

        // 与休息计时相同的暂停逻辑（全屏 / 离开电脑）
        bool pausedFullscreen = s.PauseOnFullscreen && App.FullscreenPause.IsFullscreenActive && !br.IsOnBreak;
        bool userAway = s.SmartPause && BreakReminderService.IsUserAway() && !br.IsOnBreak;

        _window.Update(
            elapsedSeconds: br.ElapsedWorkSeconds,
            workIntervalSeconds: s.WorkIntervalMinutes * 60,
            onBreak: br.IsOnBreak,
            remainingBreakSeconds: br.RemainingBreakSeconds,
            pausedFullscreen: pausedFullscreen,
            userAway: userAway,
            reminderEnabled: s.BreakReminderEnabled,
            blueLightEnabled: s.BlueLightEnabled);
    }

    /// <summary>主窗口关闭后仍保持小组件（托盘运行时小组件常驻）。</summary>
    public void Dispose()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        _timer?.Dispose();
        _timer = null;

        App.UiDispatcher.TryEnqueue(() =>
        {
            _window?.Close();
            _window = null;
            _visible = false;
        });
    }
}
