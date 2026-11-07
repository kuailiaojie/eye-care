using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EyeCare.Native;
using Microsoft.UI.Dispatching;

namespace EyeCare;

/// <summary>
/// 阅读聚焦视线标尺。
///
/// 采用与蓝光覆盖层一致的原生 Win32 分层窗口实现，而不是 WinUI 3 窗口：
/// WinUI 3 窗口由 DirectComposition 合成，附加 WS_EX_LAYERED 后会被 DWM
/// 画成一整块不透明的灰色板子（实测整屏变灰），无法做出「中间镂空」的聚焦带。
/// 这里用上下两层全屏黑色遮罩夹出一条完全透明的横带，再叠加两条细高亮线；
/// 遮罩透明度由 SetLayeredWindowAttributes 控制，不依赖窗口内容的逐像素 alpha，
/// 因此在任何 Windows 版本、任意 DPI 下表现一致，也不会出现混合 DPI 坐标错位。
///
/// 所有窗口鼠标穿透（WM_NCHITTEST → HTTRANSPARENT）、不抢焦点
/// （WS_EX_NOACTIVATE + SW_SHOWNOACTIVATE），不会打断正在进行的阅读或输入。
/// </summary>
public sealed class ReadingRulerWindow : IDisposable
{
    private const string MaskClass = "EyeCareRulerMask";
    private const string LineClass = "EyeCareRulerLine";

    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const int HTTRANSPARENT = -1;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    // 与旧版 XAML 保持一致的视觉效果：#90000000 遮罩、#7040C8FF 边线
    private const byte MaskAlpha = 0x90;
    private const byte LineAlpha = 0x70;
    private const double LineDip = 1.5;

    private static readonly NativeMethods.WndProc _wndProc = WndProc;
    private static bool _classesRegistered;
    private static ReadingRulerWindow? _active;

    private readonly DispatcherQueueTimer _timer;
    private readonly int _rulerDip;
    private readonly List<IntPtr> _windows = new();

    private IntPtr _topMask;
    private IntPtr _bottomMask;
    private IntPtr _topLine;
    private IntPtr _bottomLine;

    private double _currentY;
    private double _targetY;
    private bool _closed;

    /// <summary>标尺被关闭时触发（与旧版 Window.Closed 语义一致）。</summary>
    public event EventHandler? Closed;

    public ReadingRulerWindow()
    {
        EnsureClassesRegistered();

        _rulerDip = Math.Clamp(App.Settings.Data.ReadingRulerHeight, 50, 160);

        _topMask = CreateOverlay(MaskClass, MaskAlpha);
        _bottomMask = CreateOverlay(MaskClass, MaskAlpha);
        _topLine = CreateOverlay(LineClass, LineAlpha);
        _bottomLine = CreateOverlay(LineClass, LineAlpha);

        // 初始直接对准光标，避免从默认位置滑动过来
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _currentY = _targetY = pt.y;
        }
        else
        {
            var (_, vy, _, vh) = GetVirtualBounds();
            _currentY = _targetY = vy + vh / 2.0;
        }

        _active = this;

        var queue = App.UiDispatcher ?? DispatcherQueue.GetForCurrentThread();
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(25);
        _timer.Tick += (_, _) => Update();
        _timer.Start();

        Update();
    }

    /// <summary>显示标尺。原生分层窗口创建后即已显示，这里仅刷新一次位置，不会抢焦点。</summary>
    public void Activate()
    {
        if (!_closed) Update();
    }

    /// <summary>关闭并销毁所有标尺窗口。</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;

        _timer.Stop();

        foreach (var hwnd in _windows)
        {
            if (hwnd != IntPtr.Zero)
                NativeMethods.DestroyWindow(hwnd);
        }
        _windows.Clear();
        _topMask = _bottomMask = _topLine = _bottomLine = IntPtr.Zero;

        if (ReferenceEquals(_active, this))
            _active = null;

        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();

    private void Update()
    {
        if (_closed) return;

        var (vx, vy, vw, vh) = GetVirtualBounds();
        if (vw <= 0 || vh <= 0) return;

        double scale = 1.0;
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _targetY = pt.y;
            scale = GetScaleForPoint(pt.x, pt.y);
        }

        // 阻尼缓动插值，使视线带移动柔和顺滑
        _currentY += (_targetY - _currentY) * 0.35;

        int bandHeight = Math.Max(1, (int)Math.Round(_rulerDip * scale));
        int lineHeight = Math.Max(1, (int)Math.Round(LineDip * scale));

        // 聚焦带保持在屏幕范围内
        int bandTop = (int)Math.Round(_currentY - bandHeight / 2.0);
        bandTop = Math.Clamp(bandTop, vy, Math.Max(vy, vy + vh - bandHeight));
        int bandBottom = bandTop + bandHeight;

        // 上下遮罩之间的空隙即完全透明的聚焦带
        SetBounds(_topMask, vx, vy, vw, bandTop - vy);
        SetBounds(_bottomMask, vx, bandBottom, vw, vy + vh - bandBottom);
        SetBounds(_topLine, vx, bandTop, vw, lineHeight);
        SetBounds(_bottomLine, vx, bandBottom - lineHeight, vw, lineHeight);
    }

    private static void SetBounds(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd == IntPtr.Zero) return;

        if (width <= 0 || height <= 0)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            return;
        }

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static (int X, int Y, int Width, int Height) GetVirtualBounds()
    {
        int x = NativeMethods.GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = NativeMethods.GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = NativeMethods.GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (w <= 0 || h <= 0)
        {
            x = 0; y = 0; w = 1920; h = 1080;
        }
        return (x, y, w, h);
    }

    /// <summary>取光标所在显示器的有效 DPI 缩放（用于把 DIP 设置的标尺高度换算成物理像素）。</summary>
    private static double GetScaleForPoint(int x, int y)
    {
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(x, y), NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Shcore 不可用时退回 100%
        }
        return 1.0;
    }

    private IntPtr CreateOverlay(string className, byte alpha)
    {
        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
            className, className, NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);

        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            _windows.Add(hwnd);
        }
        return hwnd;
    }

    private static void EnsureClassesRegistered()
    {
        if (_classesRegistered) return;

        RegisterClass(MaskClass, NativeMethods.RGB(0, 0, 0));
        RegisterClass(LineClass, NativeMethods.RGB(0x40, 0xC8, 0xFF));
        _classesRegistered = true;
    }

    private static void RegisterClass(string className, uint color)
    {
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = NativeMethods.GetModuleHandle(null),
            hbrBackground = NativeMethods.CreateSolidBrush(color),
            lpszClassName = className
        };
        NativeMethods.RegisterClassEx(ref wc);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // 完全鼠标穿透，不拦截任何点击
            if (msg == WM_NCHITTEST)
                return new IntPtr(HTTRANSPARENT);

            // 显示器插拔/分辨率变化后重新贴合当前虚拟屏幕
            if (msg == WM_DISPLAYCHANGE && _active is not null)
            {
                App.UiDispatcher?.TryEnqueue(_active.Update);
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        catch
        {
            // 异常穿越原生 WndProc 边界会导致进程直接崩溃，静默忽略
            return IntPtr.Zero;
        }
    }
}
