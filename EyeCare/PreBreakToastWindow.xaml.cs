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

        // Win32 工具窗口样式（不进入 Alt+Tab，不占任务栏）
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        // 窗口尺寸
        int widthPhys = 340;
        int heightPhys = 120;
        appWindow.Resize(new SizeInt32(widthPhys, heightPhys));

        // 定位到主显示器工作区右上角（在小组件下方或靠近顶部）
        var area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (area != null)
        {
            var wa = area.WorkArea;
            int margin = 24;
            int x = wa.X + wa.Width - widthPhys - margin;
            int y = wa.Y + margin + 110; // 错开小组件的高度
            appWindow.Move(new PointInt32(x, y));
        }
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

    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
