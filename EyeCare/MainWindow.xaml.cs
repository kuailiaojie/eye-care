using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using EyeCare.Pages;

namespace EyeCare;

/// <summary>
/// 主设置窗口。
/// </summary>
public sealed partial class MainWindow : Window
{
    private BreakWindow? _breakWindow;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        // 设置窗口初始尺寸与图标
        var appWindow = AppWindow;
        appWindow.Resize(new Windows.Graphics.SizeInt32(980, 680));
        appWindow.Title = "护眼助手 · EyeCare";
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "eye.ico");
            if (System.IO.File.Exists(iconPath))
                appWindow.SetIcon(iconPath);
        }
        catch { /* 图标设置失败不影响启动 */ }

        ContentFrame.Navigate(typeof(OverviewPage));
        NavView.SelectedItem = NavView.MenuItems[0];

        // 订阅托盘事件（后台线程 → 调度到 UI 线程）
        if (App.TrayIcon is not null)
        {
            App.TrayIcon.OpenRequested += () => DispatcherQueue.TryEnqueue(ShowAndActivate);
            App.TrayIcon.ToggleBlueLightRequested += () => DispatcherQueue.TryEnqueue(OnToggleBlueLight);
            App.TrayIcon.BreakNowRequested += () => DispatcherQueue.TryEnqueue(() => App.BreakReminder.StartBreakNow());
            App.TrayIcon.ExitRequested += () => DispatcherQueue.TryEnqueue(OnExit);
        }

        // 订阅休息提醒事件（后台计时线程 → 调度到 UI 线程）
        App.BreakReminder.BreakStarted += (bool longBreak) =>
            DispatcherQueue.TryEnqueue(() => OnBreakStarted(longBreak));
        App.BreakReminder.BreakFinished += () =>
            DispatcherQueue.TryEnqueue(() => OnBreakFinished());

        // 点击关闭按钮：最小化到托盘（可配置），托盘菜单「退出」才真正退出
        AppWindow.Closing += (_, args) =>
        {
            if (App.Settings.Data.MinimizeToTray && !_isExiting)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag?.ToString();
        Type pageType = tag switch
        {
            "filter" => typeof(FilterPage),
            "break" => typeof(BreakPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(OverviewPage)
        };
        ContentFrame.Navigate(pageType);
    }

    private void ShowAndActivate()
    {
        AppWindow.Show();
        Activate();
    }

    private void OnToggleBlueLight()
    {
        var s = App.Settings.Data;
        s.BlueLightEnabled = !s.BlueLightEnabled;
        App.Settings.Save();
        App.ApplyFilters();
    }

    private void OnBreakStarted(bool longBreak)
    {
        if (_breakWindow is null)
        {
            _breakWindow = new BreakWindow();
            _breakWindow.Closed += (_, _) => _breakWindow = null;
        }
        _breakWindow.Show(longBreak);
        _breakWindow.Activate();
    }

    private void OnBreakFinished()
    {
        _breakWindow?.HideBreak();
    }

    private void OnExit()
    {
        _isExiting = true;
        App.GammaRamp.Dispose();        // 恢复所有显示器原始 Gamma Ramp
        App.FullscreenPause.Dispose();
        App.FilterOverlay.Dispose();
        App.BreakReminder.Dispose();
        _breakWindow?.Close();
        App.TrayIcon?.Dispose();
        Close();
    }
}