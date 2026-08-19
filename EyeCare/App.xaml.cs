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

    private MainWindow? _window;

    /// <summary>UI 线程调度器（后台线程需要更新界面时使用）。</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue UiDispatcher { get; private set; } = null!;

    /// <summary>同时刷新覆盖层与 Gamma 校正（所有过滤设置变更统一走这里）。</summary>
    public static void ApplyFilters()
    {
        FilterOverlay.ApplySettings();
        GammaRamp.ApplySettings();
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

        // 先创建托盘服务，使 MainWindow 构造函数能订阅到托盘事件
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

        // 创建主窗口（构造函数中订阅托盘与休息事件）
        _window = new MainWindow();
        _window.Activate();

        // 显示系统托盘图标
        TrayIcon.Show();
    }
}
