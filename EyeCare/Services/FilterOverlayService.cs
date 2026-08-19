using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare.Services;

/// <summary>
/// 覆盖窗口服务：在每个显示器上创建分层透明窗口，
/// 实现「蓝光过滤」（琥珀色覆盖）与「屏幕亮度调节」（黑色覆盖）。
/// 窗口置顶且鼠标穿透，不影响正常操作。
/// </summary>
public class FilterOverlayService
{
    private const string BlueLightClass = "EyeCareBlueLightOverlay";
    private const string DimClass = "EyeCareDimOverlay";

    // 蓝光琥珀色基调（COLORREF = 0x00BBGGRR，固定色相，通过 alpha 控制强度）
    private const uint AmberColor = 0x000082FF; // R=255, G=130, B=0

    private readonly SettingsService _settings;
    private readonly List<MonitorOverlay> _overlays = new();
    private bool _initialized;

    private class MonitorOverlay
    {
        public IntPtr BlueLight { get; set; }
        public IntPtr Dim { get; set; }
    }

    // 保持委托引用防止被 GC 回收
    private static readonly NativeMethods.WndProc _wndProc = WndProc;

    public FilterOverlayService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>确保窗口类已注册并创建所有覆盖窗口。</summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;

        RegisterClass(BlueLightClass, AmberColor);
        RegisterClass(DimClass, NativeMethods.RGB(0, 0, 0));

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumProc, IntPtr.Zero);
        _initialized = true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);

    private bool EnumProc(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT lprcMonitor, IntPtr lParam)
    {
        var mi = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return true;

        var rect = mi.rcMonitor;
        int x = rect.Left;
        int y = rect.Top;
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;

        var overlay = new MonitorOverlay
        {
            BlueLight = CreateOverlayWindow(BlueLightClass, x, y, w, h),
            Dim = CreateOverlayWindow(DimClass, x, y, w, h)
        };
        _overlays.Add(overlay);
        return true;
    }

    private IntPtr CreateOverlayWindow(string className, int x, int y, int w, int h)
    {
        var hInstance = NativeMethods.GetModuleHandle(null);
        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
            className, className, NativeMethods.WS_POPUP,
            x, y, w, h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        // 初始完全透明
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        return hwnd;
    }

    private void RegisterClass(string className, uint color)
    {
        var hInstance = NativeMethods.GetModuleHandle(null);
        var brush = NativeMethods.CreateSolidBrush(color);

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            hCursor = IntPtr.Zero,
            hIcon = IntPtr.Zero,
            hIconSm = IntPtr.Zero,
            hbrBackground = brush,
            lpszClassName = className,
            lpszMenuName = null
        };

        NativeMethods.RegisterClassEx(ref wc);
        // 画笔由窗口类持有，应用进程结束后自动释放。
    }

    /// <summary>根据当前设置刷新所有覆盖窗口的颜色 / 透明度。</summary>
    public void ApplySettings()
    {
        EnsureInitialized();

        var s = _settings.Data;

        // 蓝光 alpha：启用时按「色温系数 × 强度」计算
        int blueLightAlpha = 0;
        if (s.BlueLightEnabled)
        {
            double tempFactor = ColorTemperatureFactor(s.ColorTemperature);
            blueLightAlpha = (int)Math.Clamp(tempFactor * s.FilterStrength * 255.0, 0, 255);
        }

        // 亮度 alpha：亮度越低遮挡越强
        int dimAlpha = 0;
        if (s.BrightnessEnabled)
        {
            dimAlpha = (int)Math.Clamp((1.0 - s.Brightness) * 255.0, 0, 255);
        }

        foreach (var overlay in _overlays)
        {
            if (overlay.BlueLight != IntPtr.Zero)
            {
                NativeMethods.SetLayeredWindowAttributes(overlay.BlueLight, 0, (byte)blueLightAlpha, NativeMethods.LWA_ALPHA);
                NativeMethods.ShowWindow(overlay.BlueLight, blueLightAlpha > 0 ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
            }
            if (overlay.Dim != IntPtr.Zero)
            {
                NativeMethods.SetLayeredWindowAttributes(overlay.Dim, 0, (byte)dimAlpha, NativeMethods.LWA_ALPHA);
                NativeMethods.ShowWindow(overlay.Dim, dimAlpha > 0 ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
            }
        }
    }

    /// <summary>
    /// 色温（开尔文）→ 蓝光强度系数。
    /// 1000K → 1.0（最强暖色），6500K → 0.25，10000K → 0.0。
    /// </summary>
    private static double ColorTemperatureFactor(int kelvin)
    {
        kelvin = Math.Clamp(kelvin, 1000, 10000);
        // 分段线性：1000→1.0, 6500→0.25, 10000→0.0
        if (kelvin <= 6500)
            return 1.0 - (kelvin - 1000.0) / (6500.0 - 1000.0) * 0.75;
        return 0.25 - (kelvin - 6500.0) / (10000.0 - 6500.0) * 0.25;
    }

    /// <summary>关闭并销毁所有覆盖窗口。</summary>
    public void Dispose()
    {
        foreach (var overlay in _overlays)
        {
            if (overlay.BlueLight != IntPtr.Zero) NativeMethods.DestroyWindow(overlay.BlueLight);
            if (overlay.Dim != IntPtr.Zero) NativeMethods.DestroyWindow(overlay.Dim);
        }
        _overlays.Clear();
        _initialized = false;
    }
}