using Microsoft.UI.Xaml;

namespace EyeCare;

/// <summary>
/// 护眼软件应用入口。
/// </summary>
public partial class App : Application
{
    public static Services.SettingsService Settings { get; private set; } = null!;
    public static Services.FilterOverlayService FilterOverlay { get; private set; } = null!;
    public static Services.BreakReminderService BreakReminder { get; private set; } = null!;
    public static Services.TrayIconService TrayIcon { get; private set; } = null!;
    public static Services.StartupService Startup { get; private set; } = null!;

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 初始化服务
        Settings = new Services.SettingsService();
        Settings.Load();

        FilterOverlay = new Services.FilterOverlayService(Settings);
        BreakReminder = new Services.BreakReminderService(Settings);
        Startup = new Services.StartupService();

        // 先创建托盘服务，使 MainWindow 构造函数能订阅到托盘事件
        TrayIcon = new Services.TrayIconService(Settings);

        // 应用设置到服务
        FilterOverlay.ApplySettings();
        BreakReminder.ApplySettings();
        if (Settings.Data.AutoStartEnabled)
            Startup.EnableAutoStart();
        else
            Startup.DisableAutoStart();

        // 创建主窗口（构造函数中订阅托盘与休息事件）
        _window = new MainWindow();
        _window.Activate();

        // 显示系统托盘图标
        TrayIcon.Show();
    }
}