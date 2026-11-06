using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using EyeCare.Native;

namespace EyeCare;

/// <summary>
/// 眨眼与坐姿定时微气泡（屏幕右下角温和浮现 3 秒后淡出）：
/// 针对办公室久坐与长期盯屏导致的干眼症和颈椎僵硬，进行低侵扰、零打扰的微提示。
/// </summary>
public sealed partial class MicroNudgeWindow : Window
{
    private static readonly (string Icon, string Title, string Msg)[] Tips = new[]
    {
        ("💧", "润眼提示", "轻眨双眼 3~5 次，湿润眼球角膜"),
        ("🧘", "体态微调", "挺拔背脊，放松下沉耸起的双肩"),
        ("👀", "远眺放松", "目光移开屏幕，眺望远方片刻"),
        ("🍵", "健康补水", "喝一口温水，促进血液循环")
    };
    private static int _tipIndex = 0;

    private readonly DispatcherTimer _animTimer;
    private double _opacity = 0;
    private int _stage = 0; // 0: fadeIn, 1: hold, 2: fadeOut
    private int _holdTicks = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;

    public MicroNudgeWindow()
    {
        InitializeComponent();

        var tip = Tips[_tipIndex % Tips.Length];
        _tipIndex++;
        IconText.Text = tip.Icon;
        TitleText.Text = tip.Title;
        MessageText.Text = tip.Msg;

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

        // 不显示在任务栏
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);

        const int width = 310;
        const int height = 66;

        // 定位到主屏右下角
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var wa = displayArea.WorkArea;
        int x = wa.X + wa.Width - width - 24;
        int y = wa.Y + wa.Height - height - 36;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
    }

    private void OnAnimTick(object? sender, object e)
    {
        if (_stage == 0) // 淡入
        {
            _opacity += 0.08;
            if (_opacity >= 1.0)
            {
                _opacity = 1.0;
                _stage = 1;
                _holdTicks = 0;
            }
        }
        else if (_stage == 1) // 保持 3 秒 (150 * 20ms)
        {
            _holdTicks++;
            if (_holdTicks > 150)
            {
                _stage = 2;
            }
        }
        else if (_stage == 2) // 淡出
        {
            _opacity -= 0.06;
            if (_opacity <= 0.0)
            {
                _opacity = 0;
                _animTimer.Stop();
                Close();
                return;
            }
        }

        CardBorder.Opacity = _opacity;
    }
}
