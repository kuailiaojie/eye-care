using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using EyeCare.Native;

namespace EyeCare;

/// <summary>
/// 阅读聚焦视线标尺：
/// 置顶半透明全屏窗口，鼠标完全穿透；
/// 仅鼠标指针当前所在的水平行保持完全透明并带有柔和边界微光，上下视野轻微变暗，
/// 帮助长时间阅读大量文档、报表或代码时高度聚焦、防串行、缓解视疲劳。
/// </summary>
public sealed partial class ReadingRulerWindow : Window
{
    private readonly DispatcherTimer _timer;
    private double _currentY = 400;
    private double _targetY = 400;
    private readonly int _rulerHeight = 85;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int GWL_EXSTYLE = -20;

    public ReadingRulerWindow()
    {
        InitializeComponent();

        _rulerHeight = Math.Clamp(App.Settings.Data.ReadingRulerHeight, 50, 160);
        RulerBand.Height = _rulerHeight;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // 覆盖虚拟屏幕全域（支持多显示器全域）
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0)
        {
            vw = 1920; vh = 1080; vx = 0; vy = 0;
        }
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(vx, vy, vw, vh));

        // 鼠标完全穿透且不显示在任务栏
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle |
            NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_TOOLWINDOW |
            NativeMethods.WS_EX_LAYERED);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _timer.Tick += OnFollowTick;
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    private void OnFollowTick(object? sender, object e)
    {
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _targetY = pt.y;
            // 阻尼缓动插值，使视线带移动柔和顺滑
            _currentY += (_targetY - _currentY) * 0.35;

            double top = Math.Max(0, _currentY - (_rulerHeight / 2.0));
            TopMask.Height = top;
            RulerBand.Margin = new Thickness(0, top, 0, 0);
            BottomMask.Margin = new Thickness(0, top + _rulerHeight, 0, 0);
        }
    }
}
