using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EyeCare.Pages;

/// <summary>
/// 休息提醒设置页。
/// </summary>
public sealed partial class BreakPage : Page
{
    private bool _loading = true;

    public BreakPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var s = App.Settings.Data;
        BreakSwitch.IsOn = s.BreakReminderEnabled;
        RuleSwitch.IsOn = s.Use202020Rule;
        WorkIntervalBox.Value = s.WorkIntervalMinutes;
        ShortBreakBox.Value = s.ShortBreakSeconds;
        WorkIntervalBox.IsEnabled = !s.Use202020Rule;
        ShortBreakBox.IsEnabled = !s.Use202020Rule;
        LongBreakSwitch.IsOn = s.LongBreakEnabled;
        LongIntervalBox.Value = s.LongBreakIntervalMinutes;
        LongBreakBox.Value = s.LongBreakSeconds;
        PreBreakHeadsUpSwitch.IsOn = s.PreBreakHeadsUpEnabled;
        AntiImpulsiveSwitch.IsOn = s.AntiImpulsiveSkip;
        BreakSoundSwitch.IsOn = s.BreakSoundEnabled;
        SmartPauseSwitch.IsOn = s.SmartPause;
        EnforceSwitch.IsOn = s.EnforceBreak;
        _loading = false;
    }

    private void Apply(bool resetWorkTimer = false)
    {
        if (_loading) return;
        App.Settings.Save();
        App.BreakReminder.ApplySettings(resetWorkTimer);
    }

    private void BreakSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.BreakReminderEnabled = BreakSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void RuleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.Use202020Rule = RuleSwitch.IsOn;
        WorkIntervalBox.IsEnabled = !RuleSwitch.IsOn;
        ShortBreakBox.IsEnabled = !RuleSwitch.IsOn;
        if (RuleSwitch.IsOn)
        {
            App.Settings.Data.WorkIntervalMinutes = 20;
            App.Settings.Data.ShortBreakSeconds = 20;
            WorkIntervalBox.Value = 20;
            ShortBreakBox.Value = 20;
        }
        Apply(resetWorkTimer: false);
    }

    private void LongBreakSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.LongBreakEnabled = LongBreakSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void PreBreakHeadsUpSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.PreBreakHeadsUpEnabled = PreBreakHeadsUpSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void AntiImpulsiveSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.AntiImpulsiveSkip = AntiImpulsiveSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void BreakSoundSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.BreakSoundEnabled = BreakSoundSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void SmartPauseSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.SmartPause = SmartPauseSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void EnforceSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.EnforceBreak = EnforceSwitch.IsOn;
        Apply(resetWorkTimer: false);
    }

    private void WorkIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value)) return;
        int newInterval = (int)Math.Clamp(sender.Value, 1, 120);
        bool needsReset = App.BreakReminder.ElapsedWorkSeconds >= newInterval * 60;
        App.Settings.Data.WorkIntervalMinutes = newInterval;
        Apply(resetWorkTimer: needsReset);
    }

    private void ShortBreakBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value)) return;
        App.Settings.Data.ShortBreakSeconds = (int)Math.Clamp(sender.Value, 5, 600);
        Apply();
    }

    private void LongIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value)) return;
        App.Settings.Data.LongBreakIntervalMinutes = (int)Math.Clamp(sender.Value, 30, 240);
        Apply();
    }

    private void LongBreakBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value)) return;
        App.Settings.Data.LongBreakSeconds = (int)Math.Clamp(sender.Value, 60, 1800);
        Apply();
    }

    private void BreakNowButton_Click(object sender, RoutedEventArgs e)
    {
        App.BreakReminder.StartBreakNow();
    }
}