using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EyeCare.Pages;

/// <summary>
/// 蓝光过滤与亮度调节设置页。
/// </summary>
public sealed partial class FilterPage : Page
{
    private bool _loading = true;

    public FilterPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var s = App.Settings.Data;
        BlueLightSwitch.IsOn = s.BlueLightEnabled;
        BrightnessSwitch.IsOn = s.BrightnessEnabled;
        TempSlider.Value = s.ColorTemperature;
        StrengthSlider.Value = s.FilterStrength;
        BrightnessSlider.Value = s.Brightness;
        UpdateLabels();
        _loading = false;
    }

    private void UpdateLabels()
    {
        // Slider 的 ValueChanged 可能在 InitializeComponent 尚未完成时触发。
        // 所有控件创建完成后才更新标签，避免导航到本页时崩溃。
        if (TempValueText is null || StrengthValueText is null || BrightnessValueText is null)
            return;

        TempValueText.Text = $"{TempSlider.Value:N0} K" + TemperatureHint((int)TempSlider.Value);
        StrengthValueText.Text = $"{(StrengthSlider.Value * 100):N0}%";
        BrightnessValueText.Text = $"{(BrightnessSlider.Value * 100):N0}%";
    }

    private static string TemperatureHint(int k)
    {
        return k switch
        {
            < 3000 => "（很暖 · 强护眼）",
            < 4500 => "（暖色 · 推荐）",
            < 6500 => "（自然）",
            _ => "（偏冷）"
        };
    }

    private void Apply()
    {
        if (_loading) return;
        App.Settings.Save();
        App.FilterOverlay.ApplySettings();
    }

    private void BlueLightSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.BlueLightEnabled = BlueLightSwitch.IsOn;
        Apply();
    }

    private void BrightnessSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (BrightnessSwitch.IsOn && App.Settings.Data.Brightness >= 0.999)
        {
            App.Settings.Data.Brightness = 0.85;
            BrightnessSlider.Value = 0.85;
        }
        App.Settings.Data.BrightnessEnabled = BrightnessSwitch.IsOn;
        Apply();
    }

    private void TempSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.ColorTemperature = (int)TempSlider.Value;
        UpdateLabels();
        Apply();
    }

    private void StrengthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.FilterStrength = StrengthSlider.Value;
        UpdateLabels();
        Apply();
    }

    private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.Brightness = BrightnessSlider.Value;
        UpdateLabels();
        Apply();
    }
}