using System;
using System.Threading;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare.Services;

/// <summary>
/// 全屏程序检测服务：轮询前台窗口，若其矩形覆盖所在显示器完整屏幕，
/// 判定为全屏程序（游戏 / 全屏视频 / 演示）。
/// 全屏期间过滤层与休息计时自动暂停（f.lux 同类行为），避免打扰。
/// </summary>
public class FullscreenPauseService : IDisposable
{
    private Timer? _timer;

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
    /// 检测前台窗口是否全屏覆盖其所在显示器。
    /// 使用 GetForegroundWindow + GetWindowRect + MonitorFromWindow 比较窗口与显示器工作区。
    /// </summary>
    private static bool DetectFullscreen()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)) return false;

        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return false;

        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return false;

        var monitor = mi.rcMonitor;
        int width = monitor.Right - monitor.Left;
        int height = monitor.Bottom - monitor.Top;
        int winWidth = windowRect.Right - windowRect.Left;
        int winHeight = windowRect.Bottom - windowRect.Top;

        // 窗口尺寸覆盖显示器 99% 以上即视为全屏（容忍 1-2px 的窗口边框误差）
        return winWidth >= width * 0.99 && winHeight >= height * 0.99;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
