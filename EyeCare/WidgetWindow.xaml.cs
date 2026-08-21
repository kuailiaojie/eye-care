using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
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
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;
    private int _pendingX, _pendingY;

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

        AppWindow.Resize(new SizeInt32(280, 128));
        AppWindow.IsShownInSwitchers = false;

        // 工具窗口样式：不显示在任务栏
        var hwnd = WindowNative.GetWindowHandle(this);
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | (int)WS_EX_TOOLWINDOW);

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
    }

    /// <summary>移动到屏幕指定位置（虚拟屏幕坐标）。</summary>
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
                StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE8, 0x9C, 0x2B));
                StatusText.Text = "休息中";
                TimeText.Text = remainingBreakSeconds > 0
                    ? $"剩余 {remainingBreakSeconds / 60:00}:{remainingBreakSeconds % 60:00}"
                    : "即将结束";
                WorkProgress.Value = 0;
                DetailText.Text = "放松一下眼睛 👀";
            }
            else if (!reminderEnabled)
            {
                StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x9E, 0x9E, 0x9E));
                StatusText.Text = "提醒已关闭";
                TimeText.Text = "休息提醒未开启";
                WorkProgress.Value = 0;
                DetailText.Text = "可在设置中重新开启";
            }
            else if (pausedFullscreen)
            {
                StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x9E, 0x9E, 0x9E));
                StatusText.Text = "全屏 · 已暂停";
                TimeText.Text = $"已工作 {elapsedSeconds / 60} 分 {elapsedSeconds % 60} 秒";
                WorkProgress.Value = 0;
                DetailText.Text = "全屏应用运行时不计时";
            }
            else if (userAway)
            {
                StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x9E, 0x9E, 0x9E));
                StatusText.Text = "离开 · 已暂停";
                TimeText.Text = $"已工作 {elapsedSeconds / 60} 分 {elapsedSeconds % 60} 秒";
                WorkProgress.Value = 0;
                DetailText.Text = "检测到长时间无操作";
            }
            else
            {
                StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4C, 0xC3, 0x8A));
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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hInsertAfter, int x, int y, int cx, int cy, uint flags);
}
