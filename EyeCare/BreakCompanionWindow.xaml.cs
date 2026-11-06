using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace EyeCare;

/// <summary>
/// 多显示器副屏遮罩窗口：在主屏进行休息时，副屏同步遮蔽，避免视觉分心。
/// </summary>
public sealed partial class BreakCompanionWindow : Window
{
    public BreakCompanionWindow(RectInt32 bounds)
    {
        InitializeComponent();
        Configure(bounds);
    }

    private void Configure(RectInt32 bounds)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        appWindow.MoveAndResize(bounds);
    }
}
