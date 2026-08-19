using System;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace EyeCare;

/// <summary>
/// 全屏休息提醒窗口（覆盖屏幕，休息结束后自动隐藏）。
/// </summary>
public sealed partial class BreakWindow : Window
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    public BreakWindow()
    {
        InitializeComponent();
        App.BreakReminder.BreakTick += OnBreakTick;

        // 设为全屏 + 置顶
        var area = Windows.Graphics.DisplayArea.Primary;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height));
    }

    public void Show(bool longBreak)
    {
        BreakTypeText.Text = longBreak ? "长休息 · 好好放松一下" : "短休息 · 20-20-20 法则";
        SkipButton.Visibility = App.Settings.Data.EnforceBreak ? Visibility.Collapsed : Visibility.Visible;
        Activate();
        SetTopmost();
    }

    public void HideBreak()
    {
        AppWindow.Hide();
    }

    private void OnBreakTick(int remainingSeconds)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CountdownText.Text = TimeSpan.FromSeconds(Math.Max(0, remainingSeconds)).ToString(@"mm\:ss");
        });
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        App.BreakReminder.SkipBreak();
    }

    private void SetTopmost()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}