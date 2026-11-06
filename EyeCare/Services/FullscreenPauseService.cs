using System;
using System.Text;
using System.Threading;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare.Services;

/// <summary>
/// 全屏程序检测服务：结合 Windows 原生通知状态与前台窗口矩形判定，
/// 排除桌面、任务栏及自身窗口，精准识别全屏游戏、全屏视频与 PPT 演示。
/// 全屏期间过滤层与休息计时自动暂停（f.lux 同类行为），避免打扰。
/// </summary>
public class FullscreenPauseService : IDisposable
{
    private Timer? _timer;
    private static readonly uint CurrentPid = (uint)Environment.ProcessId;

    /// <summary>当前是否有全屏程序处于前台。</summary>
    public bool IsFullscreenActive { get; private set; }

    /// <summary>全屏状态变化时触发（后台线程）。</summary>
    public event Action? StateChanged;

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(Check, null, 0, 2000); // 每 2 秒检测一次
    }

    private void Check(object? state)
    {
        bool fullscreen = DetectFullscreen();
        if (fullscreen != IsFullscreenActive)
        {
            IsFullscreenActive = fullscreen;
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 检测前台是否有全屏或独占程序。
    /// 1. 优先使用 Windows 原生 SHQueryUserNotificationState 检测 Direct3D 全屏与演示模式；
    /// 2. 检查前台窗口尺寸，排除桌面、Shell 任务栏与当前应用自身窗口。
    /// </summary>
    private static bool DetectFullscreen()
    {
        // 1. 原生 API 检测独占游戏或 PPT 演示
        if (NativeMethods.SHQueryUserNotificationState(out var quns) == 0)
        {
            if (quns is NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                     or NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
            {
                return true;
            }
        }

        // 2. 前台窗口检测
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // 排除自身进程窗口（如 BreakWindow 或覆盖层获得焦点）
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == CurrentPid) return false;

        // 排除桌面与任务栏窗口
        var sb = new StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0)
        {
            string className = sb.ToString();
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)) return false;

        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return false;

        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return false;

        var monitor = mi.rcMonitor;

        // 严格检查窗口矩形是否包围或完全对齐屏幕（允许 2px 误差）
        bool matchLeft = Math.Abs(windowRect.Left - monitor.Left) <= 4;
        bool matchTop = Math.Abs(windowRect.Top - monitor.Top) <= 4;
        bool matchRight = Math.Abs(windowRect.Right - monitor.Right) <= 4;
        bool matchBottom = Math.Abs(windowRect.Bottom - monitor.Bottom) <= 4;

        if (matchLeft && matchTop && matchRight && matchBottom)
        {
            return true;
        }

        // 备用：尺寸覆盖 99% 且左上角对齐
        int width = monitor.Right - monitor.Left;
        int height = monitor.Bottom - monitor.Top;
        int winWidth = windowRect.Right - windowRect.Left;
        int winHeight = windowRect.Bottom - windowRect.Top;

        return winWidth >= width * 0.99 && winHeight >= height * 0.99 && matchLeft && matchTop;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
