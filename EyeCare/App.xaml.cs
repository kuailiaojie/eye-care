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
    private static MicroNudgeWindow? _microNudgeWindow;
    private static System.Threading.Timer? _microNudgeTimer;
    private static bool _isExiting;

    /// <summary>UI 线程调度器（后台线程需要更新界面时使用）。</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue UiDispatcher { get; private set; } = null!;

    /// <summary>是否正在退出（托盘「退出」触发后，其余入口不再响应）。</summary>
    public static bool IsExiting => _isExiting;

    /// <summary>同时刷新覆盖层、Gamma 校正与硬件背光（所有过滤设置变更统一走这里）。</summary>
    public static void ApplyFilters()
    {
        // 先更新硬件背光，覆盖层再据此判断是否还需要叠加软件变暗，避免双重调暗。
        ApplyHardwareBrightness();

        FilterOverlay?.ApplySettings();
        GammaRamp?.ApplySettings();
    }

    /// <summary>按当前设置调节或恢复物理显示器硬件背光。</summary>
    private static void ApplyHardwareBrightness()
    {
        var s = Settings?.Data;
        if (s is null) return;

        if (s.DdcCiBrightnessEnabled && s.BrightnessEnabled)
            DdcCi?.TrySetHardwareBrightness(s.Brightness);
        else
            DdcCi?.RestoreHardwareBrightness();
    }

    /// <summary>显式设置阅读聚焦标尺窗口状态并同步配置。</summary>
    public static void SetReadingRuler(bool enable)
    {
        UiDispatcher?.TryEnqueue(() =>
        {
            if (enable)
            {
                bool created = false;
                if (_readingRulerWindow is null)
                {
                    var win = new ReadingRulerWindow();
                    win.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(_readingRulerWindow, win))
                            _readingRulerWindow = null;

                        // 退出流程中不要再弹托盘气泡；正常关闭时同步开关状态
                        if (!_isExiting && Settings.Data.ReadingRulerEnabled)
                        {
                            Settings.Data.ReadingRulerEnabled = false;
                            Settings.Save();
                            TrayIcon?.Notify("视线标尺", "已关闭阅读聚焦视线标尺");
                        }
                    };
                    _readingRulerWindow = win;
                    win.Activate();
                    created = true;
                }

                if (!Settings.Data.ReadingRulerEnabled)
                {
                    Settings.Data.ReadingRulerEnabled = true;
                    Settings.Save();
                }

                if (created)
                    TrayIcon?.Notify("视线标尺", "已开启阅读聚焦视线标尺（按 Alt+R 可快速关闭）");
            }
            else
            {
                bool wasActive = Settings.Data.ReadingRulerEnabled || _readingRulerWindow is not null;

                // 先同步开关状态再关窗：Closed 处理器看到已关闭状态后不会重复发通知。
                if (Settings.Data.ReadingRulerEnabled)
                {
                    Settings.Data.ReadingRulerEnabled = false;
                    Settings.Save();
                }

                if (_readingRulerWindow is not null)
                {
                    var win = _readingRulerWindow;
                    _readingRulerWindow = null;
                    try { win.Close(); } catch { }
                }

                if (wasActive)
                    TrayIcon?.Notify("视线标尺", "已关闭阅读聚焦视线标尺");
            }
        });
    }

    /// <summary>切换阅读聚焦标尺窗口。</summary>
    public static void ToggleReadingRuler()
    {
        SetReadingRuler(_readingRulerWindow is null);
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
                try { _microNudgeWindow?.Close(); } catch { }

                var nudge = new MicroNudgeWindow();
                // 仅当字段仍指向本实例时才清空，避免旧窗口延迟触发 Closed 把新窗口引用清掉。
                nudge.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_microNudgeWindow, nudge)) _microNudgeWindow = null;
                };
                _microNudgeWindow = nudge;
                nudge.Activate();
            }
        });
    }

    /// <summary>关闭当前微型气泡提示。</summary>
    public static void DismissMicroNudge()
    {
        try { _microNudgeWindow?.Close(); } catch { }
        _microNudgeWindow = null;
    }

    /// <summary>显示并激活主窗口（托盘 / 小组件双击时调用）。
    /// 主窗口已被关闭（如关闭到托盘被关闭）时自动重建，避免在已销毁窗口上调用导致崩溃。</summary>
    public static void ShowMainWindow()
    {
        if (_isExiting) return;
        EnsureMainWindow(activate: true);
    }

    /// <summary>确保主窗口存在（需要时重建），并统一订阅 Closed 事件。
    /// 主窗口重建后若不再订阅 Closed，关闭后 _window 会残留已销毁窗口引用，托盘再次打开时崩溃。</summary>
    private static MainWindow EnsureMainWindow(bool activate)
    {
        if (_window is null)
        {
            var win = new MainWindow();
            win.Closed += (_, _) =>
            {
                if (!_isExiting && ReferenceEquals(_window, win))
                    _window = null;
            };
            _window = win;
        }

        if (activate)
            _window.ShowAndActivate();

        return _window;
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

        var toast = new PreBreakToastWindow();
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_preBreakToast, toast)) _preBreakToast = null;
        };
        _preBreakToast = toast;
        toast.Activate();
    }

    public static void DismissPreBreakToast()
    {
        _preBreakToast?.Dismiss();
        _preBreakToast = null;
    }

    private static void ShowBreakWindow(bool longBreak)
    {
        DismissPreBreakToast();
        // 主窗口被关闭时重建，但只作为休息窗口的宿主，不主动弹出设置界面抢走焦点。
        EnsureMainWindow(activate: false).ShowBreak(longBreak);
    }

    private static void HideBreakWindow(bool completed) => _window?.HideBreak(completed);

    /// <summary>退出应用（托盘菜单「退出」）。</summary>
    public static void ExitApp()
    {
        if (_isExiting) return;
        _isExiting = true;
        _microNudgeTimer?.Dispose();
        _microNudgeTimer = null;
        DismissMicroNudge();
        DismissPreBreakToast();
        if (_readingRulerWindow is not null)
        {
            try { _readingRulerWindow.Close(); } catch { }
            _readingRulerWindow = null;
        }
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

        // 全屏状态变化时统一刷新过滤层、Gamma 校正与硬件背光
        FullscreenPause.StateChanged += () => UiDispatcher.TryEnqueue(ApplyFilters);

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

        // 应用设置到服务（覆盖层 / Gamma / 硬件背光统一走 ApplyFilters）
        ApplyFilters();
        BreakReminder.ApplySettings();
        if (Settings.Data.AutoStartEnabled)
            Startup.EnableAutoStart();
        else
            Startup.DisableAutoStart();

        Settings.Data.ReadingRulerEnabled = false;

        // 启动全屏检测
        FullscreenPause.Start();
        Trace("services started");

        // 创建主窗口（统一由 EnsureMainWindow 创建并订阅 Closed，重建后引用不会失效）
        var mainWindow = EnsureMainWindow(activate: false);
        Trace("MainWindow created");
        mainWindow.Activate();
        Trace("MainWindow activated");

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
        BreakReminder.BreakFinished += completed => UiDispatcher.TryEnqueue(() => HideBreakWindow(completed));
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
