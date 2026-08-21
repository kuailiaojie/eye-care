using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EyeCare.Pages;

/// <summary>
/// 常规设置页。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var s = App.Settings.Data;
        AutoStartSwitch.IsOn = s.AutoStartEnabled;
        MinimizeTraySwitch.IsOn = s.MinimizeToTray;
        DesktopWidgetSwitch.IsOn = s.DesktopWidgetEnabled;
        _loading = false;
    }

    private void AutoStartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.AutoStartEnabled = AutoStartSwitch.IsOn;
        if (AutoStartSwitch.IsOn)
            App.Startup.EnableAutoStart();
        else
            App.Startup.DisableAutoStart();
        App.Settings.Save();
    }

    private void MinimizeTraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.MinimizeToTray = MinimizeTraySwitch.IsOn;
        App.Settings.Save();
    }

    private void DesktopWidgetSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.DesktopWidgetEnabled = DesktopWidgetSwitch.IsOn;
        App.Settings.Save();
        // SettingsChanged 事件会由 Save 触发，DesktopWidgetService 据此显示/隐藏小组件
    }
}