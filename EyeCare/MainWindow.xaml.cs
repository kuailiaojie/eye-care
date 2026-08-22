using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EyeCare.Pages;

namespace EyeCare;

/// <summary>
/// 主设置窗口。
/// </summary>
public sealed partial class MainWindow : Window
{
    private BreakWindow? _breakWindow;

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

        // 点击关闭按钮：最小化到托盘（托盘菜单「退出」才真正退出）
        AppWindow.Closing += (_, args) =>
        {
            if (App.Settings.Data.MinimizeToTray && !App.IsExiting)
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

    internal void ShowAndActivate()
    {
        AppWindow.Show();
        Activate();
    }

    /// <summary>展示休息窗口（由 App 在休息事件中调用）。</summary>
    internal void ShowBreak(bool longBreak)
    {
        if (_breakWindow is null)
        {
            _breakWindow = new BreakWindow();
            _breakWindow.Closed += (_, _) => _breakWindow = null;
        }
        _breakWindow.Show(longBreak);
        _breakWindow.Activate();
    }

    /// <summary>隐藏休息窗口。</summary>
    internal void HideBreak()
    {
        _breakWindow?.HideBreak();
    }
}
