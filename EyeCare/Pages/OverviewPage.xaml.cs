using System;
using EyeCare.Models;
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

        UpdateCurrentStatus();
        UpdateProfileSelection();
        UpdateHealthStats();
        App.BreakReminder.WorkTick += OnWorkTick;
        App.Settings.SettingsChanged += OnSettingsChanged;
        App.Stats.StatsUpdated += OnStatsUpdated;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.BreakReminder.WorkTick -= OnWorkTick;
        App.Settings.SettingsChanged -= OnSettingsChanged;
        App.Stats.StatsUpdated -= OnStatsUpdated;
    }

    private void OnStatsUpdated()
    {
        DispatcherQueue.TryEnqueue(UpdateHealthStats);
    }

    private void OnSettingsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _loading = true;
            var s = App.Settings.Data;
            BlueLightToggle.IsOn = s.BlueLightEnabled;
            BrightnessToggle.IsOn = s.BrightnessEnabled;
            BreakToggle.IsOn = s.BreakReminderEnabled;
            UpdateCurrentStatus();
            UpdateProfileSelection();
            _loading = false;
        });
    }

    private void UpdateCurrentStatus()
    {
        var (status, pct) = App.BreakReminder.GetCurrentStatus();
        WorkProgress.Value = pct;
        WorkStatusText.Text = status;
    }

    private void SwitchProfile(SceneProfile profile)
    {
        App.Settings.Data.ApplyProfile(profile);
        App.Settings.Save();
        App.ApplyFilters();
        UpdateProfileSelection();
    }

    private void UpdateProfileSelection()
    {
        var cur = App.Settings.Data.CurrentProfile;
        HighlightButton(ProfileOfficeBtn, cur == SceneProfile.Office);
        HighlightButton(ProfileReadingBtn, cur == SceneProfile.Reading);
        HighlightButton(ProfileSleepBtn, cur == SceneProfile.Sleep);
        HighlightButton(ProfileOriginalBtn, cur == SceneProfile.Original);
    }

    private static void HighlightButton(Button btn, bool active)
    {
        if (btn == null) return;
        var style = Helpers.ThemeResources.ResolveStyle(active ? "AccentButtonStyle" : "DefaultButtonStyle");
        if (style is not null) btn.Style = style;
    }

    private static Microsoft.UI.Xaml.Media.Brush ResolveThemeBrush(string key, byte r, byte g, byte b)
        => Helpers.ThemeResources.ResolveBrush(key, r, g, b);

    private void ProfileOffice_Click(object sender, RoutedEventArgs e) => SwitchProfile(SceneProfile.Office);
    private void ProfileReading_Click(object sender, RoutedEventArgs e) => SwitchProfile(SceneProfile.Reading);
    private void ProfileSleep_Click(object sender, RoutedEventArgs e) => SwitchProfile(SceneProfile.Sleep);
    private void ProfileOriginal_Click(object sender, RoutedEventArgs e) => SwitchProfile(SceneProfile.Original);

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
        App.ApplyFilters();
    }

    private void BrightnessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (BrightnessToggle.IsOn && App.Settings.Data.Brightness >= 0.999)
            App.Settings.Data.Brightness = 0.85;
        App.Settings.Data.BrightnessEnabled = BrightnessToggle.IsOn;
        App.Settings.Save();
        App.ApplyFilters();
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

    private void UpdateHealthStats()
    {
        var today = App.Stats.GetTodayRecord();
        TodayScreenMinutesText.Text = $"{today.ScreenMinutes} 分钟";
        TodayFilterMinutesText.Text = $"{today.FilterMinutes} 分钟";
        TodayBreaksText.Text = $"{today.CompletedBreaks} 次";

        int streak = App.Stats.CalculateStreak();
        StreakText.Text = streak > 0 ? $"🔥 连续打卡 {streak} 天" : "🌱 开启今日打卡";

        Render7DaysChart();
    }

    private void Render7DaysChart()
    {
        ChartContainer.Children.Clear();
        ChartContainer.ColumnDefinitions.Clear();

        var days = App.Stats.GetLast7Days();
        int maxMin = 60;
        foreach (var d in days)
        {
            if (d.ScreenMinutes > maxMin) maxMin = d.ScreenMinutes;
        }

        var secTextBrush = ResolveThemeBrush("TextFillColorSecondaryBrush", 0x9E, 0x9E, 0x9E);
        var priTextBrush = ResolveThemeBrush("TextFillColorPrimaryBrush", 0xFF, 0xFF, 0xFF);
        var accentBrush = ResolveThemeBrush("AccentFillColorDefaultBrush", 0x00, 0x78, 0xD4);
        var layerBrush = ResolveThemeBrush("LayerFillColorDefaultBrush", 0x44, 0x44, 0x44);

        for (int i = 0; i < days.Count; i++)
        {
            ChartContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var record = days[i];

            var colPanel = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(colPanel, i);

            var valText = new TextBlock
            {
                Text = record.ScreenMinutes > 0 ? $"{record.ScreenMinutes}" : "-",
                FontSize = 10,
                Foreground = secTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            double pct = (double)record.ScreenMinutes / maxMin;
            double barHeight = Math.Max(6, pct * 60);

            var bar = new Border
            {
                Height = barHeight,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = record.ScreenMinutes > 0 ? accentBrush : layerBrush
            };

            string dayLabel = "今天";
            if (i < 6)
            {
                if (DateTime.TryParse(record.Date, out var dt))
                {
                    dayLabel = dt.ToString("M/d");
                }
            }

            var labelText = new TextBlock
            {
                Text = dayLabel,
                FontSize = 11,
                FontWeight = i == 6 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = i == 6 ? priTextBrush : secTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            colPanel.Children.Add(valText);
            colPanel.Children.Add(bar);
            colPanel.Children.Add(labelText);

            ChartContainer.Children.Add(colPanel);
        }
    }
}