using Microsoft.UI.Xaml;

namespace EyeCare;

/// <summary>
/// 护眼软件应用入口。
/// </summary>
public partial class App : Application
{
    public static Services.SettingsService Settings { get; private set; } = null!;
    public static Services.FilterOverlayService FilterOverlay { get; private set; } = null!;
    public static Services.GammaRampService GammaRamp { get; private set; } = null!;
    public static Services.BreakReminderService BreakReminder { get; private set; } = null!;
    public static Services.TrayIconService TrayIcon { get; private set; } = null!;
    public static Services.StartupService Startup { get; private set; } = null!;
    public static Services.FullscreenPauseService FullscreenPause { get; private set; } = null!;
    public static Services.DesktopWidgetService Widget { get; private set; } = null!;

    private static MainWindow? _window;
    private static bool _isExiting;

    /// <summary>UI 线程调度器（后台线程需要更新界面时使用）。</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue UiDispatcher { get; private set; } = null!;

    /// <summary>是否正在退出（托盘「退出」触发后，其余入口不再响应）。</summary>
    public static bool IsExiting => _isExiting;

    /// <summary>同时刷新覆盖层与 Gamma 校正（所有过滤设置变更统一走这里）。</summary>
    public static void ApplyFilters()
    {
        FilterOverlay.ApplySettings();
        GammaRamp.ApplySettings();
    }

    /// <summary>显示并激活主窗口（托盘 / 小组件双击时调用）。
    /// 主窗口已被关闭（如关闭到托盘被关闭）时自动重建，避免在已销毁窗口上调用导致崩溃。</summary>
    public static void ShowMainWindow()
    {
        if (_isExiting) return;
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Activate();
        }
        else
        {
            _window.ShowAndActivate();
        }
    }

    /// <summary>从托盘切换蓝光过滤。</summary>
    private static void ToggleBlueLightFromTray()
    {
        var s = Settings.Data;
        s.BlueLightEnabled = !s.BlueLightEnabled;
        Settings.Save();
        ApplyFilters();
    }

    private static void ShowBreakWindow(bool longBreak)
    {
        // 主窗口被关闭时先重建，再展示休息窗口
        if (_window is null) ShowMainWindow();
        _window?.ShowBreak(longBreak);
    }

    private static void HideBreakWindow() => _window?.HideBreak();

    /// <summary>退出应用（托盘菜单「退出」）。</summary>
    public static void ExitApp()
    {
        if (_isExiting) return;
        _isExiting = true;
        GammaRamp.Dispose();        // 恢复所有显示器原始 Gamma Ramp
        FullscreenPause.Dispose();
        FilterOverlay.Dispose();
        BreakReminder.Dispose();
        TrayIcon?.Dispose();
        Widget?.Dispose();
        _window?.Close();
    }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        UiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // 初始化服务
        Settings = new Services.SettingsService();
        Settings.Load();

        FilterOverlay = new Services.FilterOverlayService(Settings);
        GammaRamp = new Services.GammaRampService(Settings);
        BreakReminder = new Services.BreakReminderService(Settings);
        Startup = new Services.StartupService();
        FullscreenPause = new Services.FullscreenPauseService();

        // 全屏状态变化时刷新过滤层与 Gamma 校正
        FullscreenPause.StateChanged += () => UiDispatcher.TryEnqueue(() =>
        {
            FilterOverlay.ApplySettings();
            GammaRamp.ApplySettings();
        });

        // 先创建托盘服务，供后续订阅
        TrayIcon = new Services.TrayIconService(Settings);

        // 应用设置到服务
        FilterOverlay.ApplySettings();
        GammaRamp.ApplySettings();
        BreakReminder.ApplySettings();
        if (Settings.Data.AutoStartEnabled)
            Startup.EnableAutoStart();
        else
            Startup.DisableAutoStart();

        // 启动全屏检测
        FullscreenPause.Start();

        // 创建主窗口
        _window = new MainWindow();
        _window.Activate();
        // 主窗口被关闭（关闭到托盘被关闭时）置空引用，托盘再次打开时自动重建
        _window.Closed += (_, _) => { if (!_isExiting) _window = null; };

        // 托盘事件只订阅一次（主窗口可安全重建，不会重复订阅导致多次触发）
        TrayIcon.OpenRequested += () => UiDispatcher.TryEnqueue(ShowMainWindow);
        TrayIcon.ToggleBlueLightRequested += () => UiDispatcher.TryEnqueue(ToggleBlueLightFromTray);
        TrayIcon.BreakNowRequested += () => UiDispatcher.TryEnqueue(() => BreakReminder.StartBreakNow());
        TrayIcon.ExitRequested += () => UiDispatcher.TryEnqueue(ExitApp);

        // 休息提醒事件（后台计时线程 → 调度到 UI 线程）
        BreakReminder.BreakStarted += (bool longBreak) => UiDispatcher.TryEnqueue(() => ShowBreakWindow(longBreak));
        BreakReminder.BreakFinished += () => UiDispatcher.TryEnqueue(HideBreakWindow);

        // 显示系统托盘图标
        TrayIcon.Show();

        // 桌面小组件（托盘运行时常驻显示使用时长与状态）
        Widget = new Services.DesktopWidgetService(Settings);
        Widget.Start();
    }
}
