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

        appWindow.IsShownInSwitchers = false;

        // 鼠标穿透、不抢前台焦点、不显示在任务栏（分层样式与阅读标尺保持一致，确保透明合成）
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle |
            NativeMethods.WS_EX_TOOLWINDOW |
            NativeMethods.WS_EX_NOACTIVATE |
            NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_LAYERED);

        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        int widthPhys = (int)Math.Round(310 * scale);
        int heightPhys = (int)Math.Round(66 * scale);
        int marginX = (int)Math.Round(24 * scale);
        int marginY = (int)Math.Round(36 * scale);

        // 定位到主屏右下角
        var displayArea = DisplayArea.Primary ?? DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var wa = displayArea.WorkArea;
            int x = wa.X + wa.Width - widthPhys - marginX;
            int y = wa.Y + wa.Height - heightPhys - marginY;
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, widthPhys, heightPhys));
        }

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();

        Closed += (_, _) => _animTimer.Stop();
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
