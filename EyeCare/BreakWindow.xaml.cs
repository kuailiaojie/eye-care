using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare;

/// <summary>
/// 全屏休息提醒窗口：全屏覆盖、4-7-8 呼吸引导动效、多屏同步遮蔽与防冲动跳过锁定。
/// </summary>
public sealed partial class BreakWindow : Window
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly List<BreakCompanionWindow> _companionWindows = new();
    private DispatcherTimer? _breathTimer;
    private int _breathStep = 0; // 0..18 (4-7-8 呼吸周期: 4s吸气 + 7s屏息 + 8s呼气)
    private int _skipLockSeconds = 5;

    public BreakWindow()
    {
        InitializeComponent();
        App.BreakReminder.BreakTick += OnBreakTick;
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        if (displayArea is not null)
        {
            // 全屏覆盖（不限于工作区）
            AppWindow.MoveAndResize(displayArea.OuterBounds);
        }
    }

    public void Show(bool longBreak)
    {
        BreakTypeText.Text = longBreak ? "长休息 · 好好放松一下" : "短休息 · 20-20-20 法则";

        // 防冲动跳过策略
        if (App.Settings.Data.EnforceBreak)
        {
            SkipButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SkipButton.Visibility = Visibility.Visible;
            if (App.Settings.Data.AntiImpulsiveSkip)
            {
                _skipLockSeconds = 5;
                SkipButton.IsEnabled = false;
                SkipButton.Content = $"继续工作 ({_skipLockSeconds}s)";
            }
            else
            {
                SkipButton.IsEnabled = true;
                SkipButton.Content = "继续工作（跳过）";
            }
        }

        ShowCompanionWindows();
        StartBreathingGuide();

        Activate();
        SetTopmost();
    }

    private void ShowCompanionWindows()
    {
        CloseCompanionWindows();

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var primaryMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr lp) =>
            {
                if (hMon != primaryMon)
                {
                    int w = rc.Right - rc.Left;
                    int h = rc.Bottom - rc.Top;
                    var bounds = new RectInt32(rc.Left, rc.Top, w, h);
                    var comp = new BreakCompanionWindow(bounds);
                    comp.Activate();
                    _companionWindows.Add(comp);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 伴随窗口异常不影响主休息窗口运行
        }
    }

    private void CloseCompanionWindows()
    {
        foreach (var comp in _companionWindows)
        {
            try { comp.Close(); } catch { }
        }
        _companionWindows.Clear();
    }

    private void StartBreathingGuide()
    {
        _breathStep = 0;
        _breathTimer?.Stop();
        _breathTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _breathTimer.Tick += (_, _) => UpdateBreathStep();
        _breathTimer.Start();
        UpdateBreathStep();
    }

    private void UpdateBreathStep()
    {
        _breathStep = (_breathStep + 1) % 19; // 0..3 (4s), 4..10 (7s), 11..18 (8s)

        if (_breathStep < 4)
        {
            // 吸气 4 秒 (圆环逐渐放大)
            BreathPhaseText.Text = "吸气";
            BreathSecText.Text = $"{4 - _breathStep}s";
            double scale = 1.0 + (_breathStep + 1) * (0.35 / 4.0);
            BreathScale.ScaleX = scale;
            BreathScale.ScaleY = scale;
        }
        else if (_breathStep < 11)
        {
            // 屏息 7 秒 (保持放大)
            BreathPhaseText.Text = "屏息";
            BreathSecText.Text = $"{11 - _breathStep}s";
            BreathScale.ScaleX = 1.35;
            BreathScale.ScaleY = 1.35;
        }
        else
        {
            // 呼气 8 秒 (平滑收缩)
            BreathPhaseText.Text = "呼气";
            BreathSecText.Text = $"{19 - _breathStep}s";
            int stepInPhase = _breathStep - 11;
            double scale = 1.35 - (stepInPhase + 1) * (0.35 / 8.0);
            BreathScale.ScaleX = Math.Max(1.0, scale);
            BreathScale.ScaleY = Math.Max(1.0, scale);
        }

        // 处理防冲动跳过倒计时
        if (_skipLockSeconds > 0)
        {
            _skipLockSeconds--;
            if (_skipLockSeconds <= 0)
            {
                SkipButton.IsEnabled = true;
                SkipButton.Content = "继续工作（跳过）";
            }
            else
            {
                SkipButton.Content = $"继续工作 ({_skipLockSeconds}s)";
            }
        }
    }

    public void HideBreak()
    {
        _breathTimer?.Stop();
        _breathTimer = null;
        CloseCompanionWindows();

        if (App.Settings.Data.BreakSoundEnabled)
        {
            PlayGentleBell();
        }

        AppWindow.Hide();
    }

    private static void PlayGentleBell()
    {
        try
        {
            MessageBeep(0); // MB_OK 系统提示音
        }
        catch { }
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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
}
