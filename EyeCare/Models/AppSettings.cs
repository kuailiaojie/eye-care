using System.Text.Json.Serialization;

namespace EyeCare.Models;

/// <summary>
/// 应用设置数据模型。所有字段可序列化到 JSON 持久化。
/// </summary>
public class AppSettings
{
    // ---------- 蓝光过滤 ----------
    /// <summary>是否启用蓝光过滤。</summary>
    public bool BlueLightEnabled { get; set; } = true;

    /// <summary>色温（开尔文）。范围 1000K ~ 10000K，越低越暖（越护眼）。</summary>
    public int ColorTemperature { get; set; } = 4500;

    /// <summary>过滤强度 0.0 ~ 1.0。</summary>
    public double FilterStrength { get; set; } = 0.5;

    /// <summary>是否根据日出日落自动切换。</summary>
    public bool AutoDayNight { get; set; } = false;

    // ---------- 亮度控制 ----------
    /// <summary>是否启用亮度调节（通过降低画面亮度减轻刺眼）。</summary>
    public bool BrightnessEnabled { get; set; } = false;

    /// <summary>亮度系数 0.0 ~ 1.0（1.0 为原始亮度）。</summary>
    public double Brightness { get; set; } = 0.85;

    /// <summary>是否根据时间自动调节亮度。</summary>
    public bool AutoBrightness { get; set; } = false;

    // ---------- 休息提醒 ----------
    /// <summary>是否启用休息提醒。</summary>
    public bool BreakReminderEnabled { get; set; } = true;

    /// <summary>使用 20-20-20 护眼法则（每 20 分钟休息 20 秒）。</summary>
    public bool Use202020Rule { get; set; } = true;

    /// <summary>工作间隔（分钟）。默认 20。</summary>
    public int WorkIntervalMinutes { get; set; } = 20;

    /// <summary>短休息时长（秒）。默认 20。</summary>
    public int ShortBreakSeconds { get; set; } = 20;

    /// <summary>是否启用长休息。</summary>
    public bool LongBreakEnabled { get; set; } = true;

    /// <summary>长休息间隔（分钟）。默认 60（每工作 60 分钟）。</summary>
    public int LongBreakIntervalMinutes { get; set; } = 60;

    /// <summary>长休息时长（秒）。默认 300（5 分钟）。</summary>
    public int LongBreakSeconds { get; set; } = 300;

    /// <summary>智能暂停：检测到用户离开电脑时暂停计时。</summary>
    public bool SmartPause { get; set; } = true;

    /// <summary>强制休息：休息时锁定屏幕。</summary>
    public bool EnforceBreak { get; set; } = false;

    // ---------- 通用 ----------
    /// <summary>开机自启动。</summary>
    public bool AutoStartEnabled { get; set; } = false;

    /// <summary>关闭窗口时最小化到托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;
}