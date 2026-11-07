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
    private bool _lastCompactMode;
    private bool _disposed;

    public DesktopWidgetService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>启动服务：订阅设置变化，并同步小组件的显示状态。</summary>
    public void Start()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        _settings.SettingsChanged += OnSettingsChanged;
        App.UiDispatcher.TryEnqueue(ApplyVisibility);
    }

    /// <summary>隐藏小组件。</summary>
    public void Stop()
    {
        App.UiDispatcher.TryEnqueue(HideWidget);
    }

    /// <summary>重置小组件到当前主屏工作区右上角默认位置。</summary>
    public void ResetPosition()
    {
        App.UiDispatcher.TryEnqueue(() => _window?.ResetToDefaultPosition());
    }

    /// <summary>立即刷新小组件状态。</summary>
    public void Update()
    {
        App.UiDispatcher.TryEnqueue(Refresh);
    }

    private void OnSettingsChanged()
    {
        App.UiDispatcher.TryEnqueue(ApplyVisibility);
    }

    private void ApplyVisibility()
    {
        if (_disposed) return;

        var s = _settings.Data;
        if (s.DesktopWidgetEnabled)
        {
            EnsureWindow();

            if (_window is not null && !_visible)
            {
                _window.AppWindow.Show();
                _window.KeepTopmost();
                _visible = true;
            }

            EnsureTimer();

            // 设置页切换紧凑模式后立即作用于已存在的窗口（无需重启应用）。
            if (_window is not null && s.WidgetCompactMode != _lastCompactMode)
            {
                _lastCompactMode = s.WidgetCompactMode;
                _window.RefreshLayout();
            }
        }
        else
        {
            StopTimer();
            HideWidget();
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;

        var win = new WidgetWindow();
        // 仅当字段仍指向本窗口时才处理，避免旧窗口的延迟 Closed 事件破坏新窗口状态。
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, win))
            {
                _window = null;
                _visible = false;
            }
        };
        _window = win;
        _visible = true;
        _lastCompactMode = _settings.Data.WidgetCompactMode;
        win.Activate();
    }

    private void HideWidget()
    {
        if (_window is not null && _visible)
        {
            _window.AppWindow.Hide();
            _visible = false;
        }
    }

    private void EnsureTimer()
    {
        _timer ??= new Timer(OnTick, null, 0, 1000);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? state)
    {
        if (_disposed || !_settings.Data.DesktopWidgetEnabled) return;
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
        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
        StopTimer();

        App.UiDispatcher.TryEnqueue(() =>
        {
            _window?.Close();
            _window = null;
            _visible = false;
        });
    }
}
