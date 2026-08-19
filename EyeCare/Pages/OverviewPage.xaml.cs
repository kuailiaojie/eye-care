using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EyeCare.Pages;

/// <summary>
/// 概览页：快捷开关 + 工作计时 + 立即休息。
/// </summary>
public sealed partial class OverviewPage : Page
{
    private bool _loading = true;

    public OverviewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var s = App.Settings.Data;
        BlueLightToggle.IsOn = s.BlueLightEnabled;
        BrightnessToggle.IsOn = s.BrightnessEnabled;
        BreakToggle.IsOn = s.BreakReminderEnabled;
        _loading = false;

        App.BreakReminder.WorkTick += OnWorkTick;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.BreakReminder.WorkTick -= OnWorkTick;
    }

    private void OnWorkTick(int elapsedSeconds, int totalSeconds)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            int pct = totalSeconds > 0 ? (int)(elapsedSeconds * 100.0 / totalSeconds) : 0;
            WorkProgress.Value = Math.Clamp(pct, 0, 100);
            WorkStatusText.Text = $"已工作 {elapsedSeconds / 60} 分 {elapsedSeconds % 60} 秒 / 目标 {totalSeconds / 60} 分钟";
        });
    }

    private void BlueLightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.BlueLightEnabled = BlueLightToggle.IsOn;
        App.Settings.Save();
        App.FilterOverlay.ApplySettings();
    }

    private void BrightnessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (BrightnessToggle.IsOn && App.Settings.Data.Brightness >= 0.999)
            App.Settings.Data.Brightness = 0.85;
        App.Settings.Data.BrightnessEnabled = BrightnessToggle.IsOn;
        App.Settings.Save();
        App.FilterOverlay.ApplySettings();
    }

    private void BreakToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.BreakReminderEnabled = BreakToggle.IsOn;
        App.Settings.Save();
        App.BreakReminder.ApplySettings();
    }

    private void BreakNowButton_Click(object sender, RoutedEventArgs e)
    {
        App.BreakReminder.StartBreakNow();
    }
}