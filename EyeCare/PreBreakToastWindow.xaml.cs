using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare;

public sealed partial class PreBreakToastWindow : Window
{
    private DispatcherTimer? _timer;
    private int _remainingSeconds = 30;

    public PreBreakToastWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        StartCountdown();
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // 移除标准标题栏与最大小化按钮
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        appWindow.IsShownInSwitchers = false;

        // Win32 工具窗口样式（不进入 Alt+Tab，不占任务栏）
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        // 按 DPI 缩放窗口尺寸（136 DIP 确保内容完整容纳两行文字与两个操作按钮）
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        int widthPhys = (int)Math.Round(340 * scale);
        int heightPhys = (int)Math.Round(136 * scale);
        appWindow.Resize(new SizeInt32(widthPhys, heightPhys));

        // 定位到主显示器工作区右上角（仅当小组件启用且位于默认右上角时错开其高度）
        var area = DisplayArea.Primary ?? DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (area != null)
        {
            var wa = area.WorkArea;
            int margin = (int)Math.Round(24 * scale);
            bool offsetWidget = App.Settings.Data.DesktopWidgetEnabled && !App.Settings.Data.WidgetPositionCustomized;
            int widgetOffset = offsetWidget ? (int)Math.Round(110 * scale) : 0;
            int x = wa.X + wa.Width - widthPhys - margin;
            int y = wa.Y + margin + widgetOffset;
            appWindow.Move(new PointInt32(x, y));
        }

        Closed += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
        };
    }

    private void StartCountdown()
    {
        _remainingSeconds = 30;
        CountdownText.Text = $"{_remainingSeconds}s";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            CountdownText.Text = $"{_remainingSeconds}s";
            if (_remainingSeconds <= 0)
            {
                Dismiss();
            }
        };
        _timer.Start();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        App.BreakReminder.SnoozeWork(5);
        Dismiss();
    }

    private void BreakNowButton_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        App.BreakReminder.StartBreakNow();
    }

    public void Dismiss()
    {
        _timer?.Stop();
        _timer = null;
        try
        {
            Close();
        }
        catch { }
    }
}
