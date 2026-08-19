using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EyeCare.Models;

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

        FilterModeSelector.SelectedIndex = s.FilterMode == FilterMode.GammaRamp ? 1 : 0;

        DayNightSwitch.IsOn = s.AutoDayNight;
        DayTempSlider.Value = s.DayColorTemperature;
        NightTempSlider.Value = s.NightColorTemperature;

        FullscreenPauseSwitch.IsOn = s.PauseOnFullscreen;

        UpdateLabels();
        UpdateDayNightVisibility();
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

        if (DayTempValueText is not null)
            DayTempValueText.Text = $"{DayTempSlider.Value:N0} K";
        if (NightTempValueText is not null)
            NightTempValueText.Text = $"{NightTempSlider.Value:N0} K";

        if (FilterModeHintText is not null)
        {
            FilterModeHintText.Text = App.Settings.Data.FilterMode == FilterMode.GammaRamp
                ? "通过修改显示器 Gamma Ramp 直接压缩蓝光输出，无需叠加窗口；全屏独占游戏、视频播放器同样生效。HDR 模式下可能不生效，建议使用叠加层。"
                : "在屏幕上方叠加柔和的琥珀色窗口，兼容所有显示器与 HDR 模式。";
        }
    }

    private void UpdateDayNightVisibility()
    {
        if (DayNightPanel is null) return;
        DayNightPanel.Visibility = DayNightSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
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
        App.ApplyFilters();
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

    private void FilterModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.FilterMode = FilterModeSelector.SelectedIndex == 1 ? FilterMode.GammaRamp : FilterMode.Overlay;
        UpdateLabels();
        Apply();
    }

    private void DayNightSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.AutoDayNight = DayNightSwitch.IsOn;
        UpdateDayNightVisibility();
        Apply();
    }

    private void DayTempSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.DayColorTemperature = (int)DayTempSlider.Value;
        UpdateLabels();
        Apply();
    }

    private void NightTempSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.NightColorTemperature = (int)NightTempSlider.Value;
        UpdateLabels();
        Apply();
    }

    private void FullscreenPauseSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Data.PauseOnFullscreen = FullscreenPauseSwitch.IsOn;
        Apply();
    }
}
