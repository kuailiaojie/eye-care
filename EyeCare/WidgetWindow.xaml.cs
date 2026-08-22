using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.Graphics;

namespace EyeCare;

/// <summary>
/// 桌面小组件窗口：托盘运行时在桌面角落显示使用时长与当前状态。
/// 无边框、置顶、可拖动、双击打开主界面。
/// </summary>
public sealed partial class WidgetWindow : Window
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>小组件宽度（逻辑像素，随 DPI 缩放）。</summary>
    private const double ContentWidthDip = 280;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    // 状态点主题色（懒解析：优先 Fluent 语义色，资源缺失时回退内置色）
    private static readonly Brush StatusSuccess = ResolveThemeBrush("SystemFillColorSuccessBrush", 0x4C, 0xC3, 0x8A);
    private static readonly Brush StatusCaution = ResolveThemeBrush("SystemFillColorCautionBrush", 0xE8, 0x9C, 0x2B);
    private static readonly Brush StatusMuted = ResolveThemeBrush("TextFillColorSecondaryBrush", 0x9E, 0x9E, 0x9E);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;
    private int _pendingX, _pendingY;
    private int _lastWidthPhys = -1, _lastHeightPhys = -1;

    public WidgetWindow()
    {
        InitializeComponent();

        // 无边框、不可调整大小、置顶、不出现在 Alt+Tab
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        // 按当前 DPI 换算初始尺寸（AppWindow 坐标均为物理像素），Loaded 后再按内容精调
        var hwnd = WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32(
            (int)Math.Round(ContentWidthDip * scale),
            (int)Math.Round(140 * scale)));
        AppWindow.IsShownInSwitchers = false;

        // 工具窗口样式：不显示在任务栏
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(exStyle | WS_EX_TOOLWINDOW));

        // 拖动结束后（防抖）持久化位置
        AppWindow.Changed += OnAppWindowChanged;
        Closed += (_, _) => _saveTimer?.Stop();
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            var s = App.Settings.Data;
            s.WidgetPositionCustomized = true;
            s.WidgetX = _pendingX;
            s.WidgetY = _pendingY;
            App.Settings.Save();
        };

        // 首次显示与 DPI 变化时：窗口尺寸贴合内容 + 恢复位置
        RootGrid.Loaded += (_, _) =>
        {
            FitToContent();
            RestorePosition();
        };
        RootGrid.SizeChanged += (_, _) => FitToContent();
    }

    /// <summary>移动到屏幕指定位置（虚拟屏幕坐标，物理像素）。</summary>
    public void MoveTo(int x, int y)
    {
        AppWindow.Move(new PointInt32(x, y));
    }

    /// <summary>更新小组件显示内容。后台线程调用会安全调度到 UI 线程。</summary>
    public void Update(int elapsedSeconds, int workIntervalSeconds,
                       bool onBreak, int remainingBreakSeconds,
                       bool pausedFullscreen, bool userAway,
                       bool reminderEnabled, bool blueLightEnabled)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (onBreak)
            {
                StatusDot.Fill = StatusCaution;
                StatusText.Text = "休息中";
                TimeText.Text = remainingBreakSeconds > 0
                    ? $"剩余 {remainingBreakSeconds / 60:00}:{remainingBreakSeconds % 60:00}"
                    : "即将结束";
                WorkProgress.Value = 0;
                DetailText.Text = "放松一下眼睛 👀";
            }
            else if (!reminderEnabled)
            {
                StatusDot.Fill = StatusMuted;
                StatusText.Text = "提醒已关闭";
                TimeText.Text = "休息提醒未开启";
                WorkProgress.Value = 0;
                DetailText.Text = "可在设置中重新开启";
            }
            else if (pausedFullscreen)
            {
                StatusDot.Fill = StatusMuted;
                StatusText.Text = "全屏 · 已暂停";
                TimeText.Text = $"已工作 {elapsedSeconds / 60} 分 {elapsedSeconds % 60} 秒";
                WorkProgress.Value = 0;
                DetailText.Text = "全屏应用运行时不计时";
            }
            else if (userAway)
            {
                StatusDot.Fill = StatusMuted;
                StatusText.Text = "离开 · 已暂停";
                TimeText.Text = $"已工作 {elapsedSeconds / 60} 分 {elapsedSeconds % 60} 秒";
                WorkProgress.Value = 0;
                DetailText.Text = "检测到长时间无操作";
            }
            else
            {
                StatusDot.Fill = StatusSuccess;
                StatusText.Text = "工作中";
                TimeText.Text = $"已工作 {elapsedSeconds / 60} 分 {elapsedSeconds % 60} 秒";
                int pct = workIntervalSeconds > 0
                    ? (int)Math.Clamp(elapsedSeconds * 100.0 / workIntervalSeconds, 0, 100)
                    : 0;
                WorkProgress.Value = pct;
                int remainMin = Math.Max(0, (workIntervalSeconds - elapsedSeconds + 59) / 60);
                DetailText.Text = $"距离休息还有约 {remainMin} 分钟";
            }

            BlueLightText.Text = blueLightEnabled ? "蓝光过滤 开" : "蓝光过滤 关";
        });
    }

    /// <summary>窗口尺寸贴合内容：保证所有内容完整可见（随 DPI / 系统文本缩放自适应）。</summary>
    private void FitToContent()
    {
        var xamlRoot = RootGrid.XamlRoot;
        if (xamlRoot is null) return;

        // 以固定宽度 + 无限高度测量卡片，得到内容实际需要的高度（逻辑像素）
        CardBorder.Measure(new Windows.Foundation.Size(ContentWidthDip, double.PositiveInfinity));
        double scale = xamlRoot.RasterizationScale;
        int w = (int)Math.Round(ContentWidthDip * scale);
        int h = (int)Math.Round(CardBorder.DesiredSize.Height * scale);

        // 无边框窗口：若窗口外层尺寸与客户区存在差值则补足
        var outer = AppWindow.Size;
        var client = AppWindow.ClientSize;
        if (client.Width > 0 && client.Height > 0)
        {
            w += Math.Max(0, outer.Width - client.Width);
            h += Math.Max(0, outer.Height - client.Height);
        }

        if (w == _lastWidthPhys && h == _lastHeightPhys) return;
        _lastWidthPhys = w;
        _lastHeightPhys = h;
        AppWindow.Resize(new SizeInt32(w, h));
    }

    /// <summary>恢复窗口位置：已自定义则用保存的位置，否则默认主屏工作区右上角。</summary>
    private void RestorePosition()
    {
        var s = App.Settings.Data;
        if (s.WidgetPositionCustomized)
        {
            AppWindow.Move(new PointInt32(s.WidgetX, s.WidgetY));
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (area is null) return;

        var wa = area.WorkArea;
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        int margin = (int)Math.Round(24 * scale); // 24 逻辑像素边距 → 物理像素
        int x = wa.X + wa.Width - AppWindow.Size.Width - margin;
        int y = wa.Y + margin;
        AppWindow.Move(new PointInt32(Math.Max(wa.X, x), Math.Max(wa.Y, y)));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange) return;
        var pos = sender.Position;
        _pendingX = pos.X;
        _pendingY = pos.Y;
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    /// <summary>拖动窗口（系统级 HTCAPTION 移动）。</summary>
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(RootGrid).Properties;
        if (!props.IsLeftButtonPressed) return;

        var hwnd = WindowNative.GetWindowHandle(this);
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }

    /// <summary>双击打开主界面。</summary>
    private void RootGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        App.ShowMainWindow();
    }

    /// <summary>保持置顶（在可能被覆盖后重新置顶）。</summary>
    public void KeepTopmost()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>解析 Fluent 语义主题画刷；缺失时回退内置颜色。</summary>
    private static Brush ResolveThemeBrush(string key, byte r, byte g, byte b)
    {
        if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue(key, out object? value) && value is Brush brush)
            return brush;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}
