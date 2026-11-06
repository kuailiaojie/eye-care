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
        WidgetCompactSwitch.IsOn = s.WidgetCompactMode;
        SmoothTransitionSwitch.IsOn = s.SmoothTransitionEnabled;
        GlobalHotkeySwitch.IsOn = s.EnableGlobalHotkeys;
        DdcCiSwitch.IsOn = s.DdcCiBrightnessEnabled;
        AppBypassSwitch.IsOn = s.AppBypassEnabled;
        ReadingRulerSwitch.IsOn = s.ReadingRulerEnabled;
        MicroNudgeSwitch.IsOn = s.MicroNudgeEnabled;
        UpdateVersionText();
        _loading = false;
    }

    private void UpdateVersionText()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            VersionText.Text = $"版本 {v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"版本 {v?.Major ?? 1}.{v?.Minor ?? 1}.{v?.Build ?? 3}.{v?.Revision ?? 0}";
        }
    }

    private void ResetGammaButton_Click(object sender, RoutedEventArgs e)
    {
        App.GammaRamp.ResetToStandardSRgb();
        ResetStatusText.Text = "已成功恢复所有屏幕默认线性色彩！";
        ResetStatusText.Visibility = Visibility.Visible;
    }

    private void ResetWidgetButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Data.WidgetPositionCustomized = false;
        App.Settings.Save();
        App.Widget.Stop();
        if (App.Settings.Data.DesktopWidgetEnabled)
            App.Widget.Start();

        ResetStatusText.Text = "小组件已成功重置到屏幕右上角！";
        ResetStatusText.Visibility = Visibility.Visible;
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

    private void WidgetCompactSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.WidgetCompactMode = WidgetCompactSwitch.IsOn;
        App.Settings.Save();
    }

    private void SmoothTransitionSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.SmoothTransitionEnabled = SmoothTransitionSwitch.IsOn;
        App.Settings.Save();
    }

    private void GlobalHotkeySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.EnableGlobalHotkeys = GlobalHotkeySwitch.IsOn;
        App.Settings.Save();
        App.Hotkeys.UpdateSettings();
    }

    private void DdcCiSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.DdcCiBrightnessEnabled = DdcCiSwitch.IsOn;
        App.Settings.Save();
        App.ApplyFilters();
    }

    private void AppBypassSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.AppBypassEnabled = AppBypassSwitch.IsOn;
        App.Settings.Save();
        App.ApplyFilters();
    }

    private void ReadingRulerSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReadingRulerSwitch.IsOn != App.Settings.Data.ReadingRulerEnabled)
        {
            App.ToggleReadingRuler();
        }
    }

    private void MicroNudgeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.MicroNudgeEnabled = MicroNudgeSwitch.IsOn;
        App.Settings.Save();
    }
}