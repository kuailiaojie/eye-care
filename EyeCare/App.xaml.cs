using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace EyeCare;

/// <summary>
/// 护眼软件应用入口。
/// </summary>
public partial class App : Application
{
    // ---- 启动崩溃诊断（正式版可保留，仅崩溃时写文件）----
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EyeCare", "crash.log");

    private static readonly string RunningFlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EyeCare", ".running_flag");

    /// <summary>启动阶段标记：无论是否抛出托管异常，最后一条标记即为崩溃点。内置大小限制防止日志膨胀。</summary>
    internal static void Trace(string stage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            if (File.Exists(CrashLogPath) && new FileInfo(CrashLogPath).Length > 1024 * 1024)
            {
                string backup = CrashLogPath + ".old";
                try { File.Delete(backup); } catch { }
                try { File.Move(CrashLogPath, backup); } catch { }
            }
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {stage}\n");
        }
        catch { /* 日志失败不影响运行 */ }
    }

    /// <summary>记录异常。</summary>
    internal static void LogCrash(string stage, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {stage}: {ex}\n\n");
        }
        catch { }
    }

    public static Services.SettingsService Settings { get; private set; } = null!;
    public static Services.FilterOverlayService FilterOverlay { get; private set; } = null!;
    public static Services.GammaRampService GammaRamp { get; private set; } = null!;
    public static Services.BreakReminderService BreakReminder { get; private set; } = null!;
    public static Services.TrayIconService TrayIcon { get; private set; } = null!;
    public static Services.StartupService Startup { get; private set; } = null!;
    public static Services.FullscreenPauseService FullscreenPause { get; private set; } = null!;
    public static Services.DesktopWidgetService Widget { get; private set; } = null!;
    public static Services.EyeHealthStatsStore Stats { get; private set; } = null!;
    public static Services.GlobalHotkeyService Hotkeys { get; private set; } = null!;
    public static Services.AppBypassService AppBypass { get; private set; } = null!;
    public static Services.DdcCiService DdcCi { get; private set; } = null!;

    private static MainWindow? _window;
    private static PreBreakToastWindow? _preBreakToast;
    private static ReadingRulerWindow? _readingRulerWindow;
    private static System.Threading.Timer? _microNudgeTimer;
    private static bool _isExiting;

    /// <summary>UI 线程调度器（后台线程需要更新界面时使用）。</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue UiDispatcher { get; private set; } = null!;

    /// <summary>是否正在退出（托盘「退出」触发后，其余入口不再响应）。</summary>
    public static bool IsExiting => _isExiting;

    /// <summary>同时刷新覆盖层、Gamma 校正与硬件背光（所有过滤设置变更统一走这里）。</summary>
    public static void ApplyFilters()
    {
        FilterOverlay?.ApplySettings();
        GammaRamp?.ApplySettings();

        // 若启用了硬件背光控制，优先尝试调节物理显示器背光
        if (Settings?.Data.BrightnessEnabled == true && Settings.Data.DdcCiBrightnessEnabled)
        {
            DdcCi?.TrySetHardwareBrightness(Settings.Data.Brightness);
        }
    }

    /// <summary>切换阅读聚焦标尺窗口。</summary>
    public static void ToggleReadingRuler()
    {
        UiDispatcher?.TryEnqueue(() =>
        {
            if (_readingRulerWindow != null)
            {
                _readingRulerWindow.Close();
                _readingRulerWindow = null;
                Settings.Data.ReadingRulerEnabled = false;
                TrayIcon?.Notify("视线标尺", "已关闭阅读聚焦视线标尺");
            }
            else
            {
                _readingRulerWindow = new ReadingRulerWindow();
                _readingRulerWindow.Activate();
                _readingRulerWindow.Closed += (_, _) => _readingRulerWindow = null;
                Settings.Data.ReadingRulerEnabled = true;
                TrayIcon?.Notify("视线标尺", "已开启阅读聚焦视线标尺（按 Alt+R 可快速关闭）");
            }
            Settings.Save();
        });
    }

    /// <summary>弹出微型润眼 / 坐姿体态温馨提示气泡。</summary>
    public static void ShowMicroNudge()
    {
        UiDispatcher?.TryEnqueue(() =>
        {
            if (_isExiting) return;
            var s = Settings?.Data;
            if (s != null && s.MicroNudgeEnabled && !FullscreenPause.IsFullscreenActive && !BreakReminder.IsOnBreak)
            {
                var nudge = new MicroNudgeWindow();
                nudge.Activate();
            }
        });
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

    /// <summary>从托盘切换屏幕变暗亮度调节。</summary>
    private static void ToggleBrightnessFromTray()
    {
        var s = Settings.Data;
        s.BrightnessEnabled = !s.BrightnessEnabled;
        if (s.BrightnessEnabled && s.Brightness >= 0.999)
            s.Brightness = 0.85;
        Settings.Save();
        ApplyFilters();
    }

    /// <summary>从托盘切换桌面小组件。</summary>
    private static void ToggleWidgetFromTray()
    {
        var s = Settings.Data;
        s.DesktopWidgetEnabled = !s.DesktopWidgetEnabled;
        Settings.Save();
        if (s.DesktopWidgetEnabled)
            Widget?.Start();
        else
            Widget?.Stop();
    }

    public static void ShowPreBreakToast()
    {
        if (_isExiting) return;
        _preBreakToast?.Dismiss();
        _preBreakToast = new PreBreakToastWindow();
        _preBreakToast.Activate();
        _preBreakToast.Closed += (_, _) => { _preBreakToast = null; };
    }

    public static void DismissPreBreakToast()
    {
        _preBreakToast?.Dismiss();
        _preBreakToast = null;
    }

    private static void ShowBreakWindow(bool longBreak)
    {
        DismissPreBreakToast();
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
        _microNudgeTimer?.Dispose();
        _readingRulerWindow?.Close();
        Hotkeys?.Dispose();
        AppBypass?.Dispose();
        DdcCi?.Dispose();
        Stats?.Save();
        Settings?.SaveImmediate();  // 确保退出前所有防抖设置立即落盘
        GammaRamp.Dispose();        // 恢复所有显示器原始 Gamma Ramp
        FullscreenPause.Dispose();
        FilterOverlay.Dispose();
        BreakReminder.Dispose();
        TrayIcon?.Dispose();
        Widget?.Dispose();
        _window?.Close();

        try
        {
            if (File.Exists(RunningFlagPath))
                File.Delete(RunningFlagPath);
        }
        catch { }
    }

    public App()
    {
        // 全局异常钩子：任何未处理异常都先落盘，便于定位崩溃
        UnhandledException += (_, e) => LogCrash("Application.UnhandledException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException",
                e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        Trace("App ctor begin");
        InitializeComponent();
        Trace("App ctor end");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // 单实例保证：若已有运行实例，重定向激活并唤醒其主窗口，新进程静默退出
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("EyeCare_SingleInstance");
            if (!mainInstance.IsCurrent)
            {
                var currentArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                await mainInstance.RedirectActivationToAsync(currentArgs);
                Environment.Exit(0);
                return;
            }

            mainInstance.Activated += (_, _) =>
            {
                UiDispatcher?.TryEnqueue(ShowMainWindow);
            };

            OnLaunchedCore(args);
            Trace("OnLaunched done");
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched", ex);
            throw;
        }
    }

    private void OnLaunchedCore(LaunchActivatedEventArgs args)
    {
        Trace("OnLaunched begin");
        try { File.WriteAllText(RunningFlagPath, DateTime.Now.ToString("o")); } catch { }
        UiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // 初始化服务
        Settings = new Services.SettingsService();
        Settings.Load();

        FilterOverlay = new Services.FilterOverlayService(Settings);
        GammaRamp = new Services.GammaRampService(Settings);
        BreakReminder = new Services.BreakReminderService(Settings);
        Startup = new Services.StartupService();
        FullscreenPause = new Services.FullscreenPauseService();
        Stats = new Services.EyeHealthStatsStore();
        AppBypass = new Services.AppBypassService(Settings);
        DdcCi = new Services.DdcCiService(Settings);
        Hotkeys = new Services.GlobalHotkeyService(Settings);
        Trace("services created");

        // 全屏状态变化时刷新过滤层与 Gamma 校正
        FullscreenPause.StateChanged += () => UiDispatcher.TryEnqueue(() =>
        {
            FilterOverlay.ApplySettings();
            GammaRamp.ApplySettings();
        });

        // 色彩敏感白名单程序切换时平滑淡出/恢复滤镜
        AppBypass.StateChanged += () => UiDispatcher.TryEnqueue(ApplyFilters);
        AppBypass.Start();

        // 启动系统全局快捷键监听
        Hotkeys.Start();

        // 订阅休息事件，收集本地健康用眼统计
        BreakReminder.MinuteWorked += () => Stats.RecordScreenMinute(Settings.Data.BlueLightEnabled);
        BreakReminder.BreakCompleted += () => Stats.RecordCompletedBreak();
        BreakReminder.BreakSkipped += () => Stats.RecordSkippedBreak();

        // 启动微气泡润眼/体态微提醒定时器
        int nudgeInterval = Math.Clamp(Settings.Data.MicroNudgeIntervalMinutes, 5, 120);
        _microNudgeTimer = new System.Threading.Timer(_ => ShowMicroNudge(), null, nudgeInterval * 60 * 1000, nudgeInterval * 60 * 1000);

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
        Trace("services started");

        // 创建主窗口
        _window = new MainWindow();
        Trace("MainWindow created");
        _window.Activate();
        Trace("MainWindow activated");
        // 主窗口被关闭（关闭到托盘被关闭时）置空引用，托盘再次打开时自动重建
        _window.Closed += (_, _) => { if (!_isExiting) _window = null; };

        // 托盘事件只订阅一次（主窗口可安全重建，不会重复订阅导致多次触发）
        TrayIcon.OpenRequested += () => UiDispatcher.TryEnqueue(ShowMainWindow);
        TrayIcon.ToggleBlueLightRequested += () => UiDispatcher.TryEnqueue(ToggleBlueLightFromTray);
        TrayIcon.ToggleBrightnessRequested += () => UiDispatcher.TryEnqueue(ToggleBrightnessFromTray);
        TrayIcon.ToggleWidgetRequested += () => UiDispatcher.TryEnqueue(ToggleWidgetFromTray);
        TrayIcon.ToggleRulerRequested += () => UiDispatcher.TryEnqueue(ToggleReadingRuler);
        TrayIcon.BreakNowRequested += () => UiDispatcher.TryEnqueue(() => BreakReminder.StartBreakNow());
        TrayIcon.ExitRequested += () => UiDispatcher.TryEnqueue(ExitApp);
        TrayIcon.DisplayChangeDetected += () => UiDispatcher.TryEnqueue(() =>
        {
            FilterOverlay.Reinitialize();
            GammaRamp.Reinitialize();
            Widget?.Update();
        });

        // 休息提醒事件（后台计时线程 → 调度到 UI 线程）
        BreakReminder.PreBreakNotice += (seconds) => UiDispatcher.TryEnqueue(ShowPreBreakToast);
        BreakReminder.BreakStarted += (bool longBreak) => UiDispatcher.TryEnqueue(() => ShowBreakWindow(longBreak));
        BreakReminder.BreakFinished += () => UiDispatcher.TryEnqueue(HideBreakWindow);
        BreakReminder.WorkTick += (elapsed, total) =>
        {
            int remainMin = Math.Max(0, (total - elapsed + 59) / 60);
            TrayIcon.UpdateTooltip($"护眼助手 - 距离休息还有 {remainMin} 分钟");
        };

        // 显示系统托盘图标
        TrayIcon.Show();
        Trace("TrayIcon shown");

        // 桌面小组件（托盘运行时常驻显示使用时长与状态）
        Widget = new Services.DesktopWidgetService(Settings);
        Widget.Start();
        Trace("Widget started");
    }
}
