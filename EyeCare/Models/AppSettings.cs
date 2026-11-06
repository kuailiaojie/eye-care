using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EyeCare.Models;

/// <summary>
/// 蓝光过滤实现方式。
/// </summary>
public enum FilterMode
{
    /// <summary>分层透明窗口叠加琥珀色（兼容性最好，所有模式均可用）。</summary>
    Overlay,

    /// <summary>系统级 Gamma 校正（f.lux / LightBulb 同款方案），直接压缩显示器蓝光通道输出。</summary>
    GammaRamp
}

/// <summary>
/// 预设场景模式。
/// </summary>
public enum SceneProfile
{
    Custom,     // 自定义
    Office,     // 办公/编程 (5000K, 35%, 90%)
    Reading,    // 阅读/纸质 (4200K, 55%, 80%)
    Sleep,      // 深夜助眠 (3200K, 75%, 65%)
    Original    // 设计/原色 (6500K, 0%, 100%)
}

/// <summary>
/// 应用设置数据模型。所有字段可序列化到 JSON 持久化。
/// </summary>
public class AppSettings
{
    // ---------- 场景预设 ----------
    /// <summary>当前选中的场景预设模式。</summary>
    public SceneProfile CurrentProfile { get; set; } = SceneProfile.Office;

    /// <summary>应用指定场景预设，自动设置推荐的色温、强度与亮度。</summary>
    public void ApplyProfile(SceneProfile profile)
    {
        CurrentProfile = profile;
        switch (profile)
        {
            case SceneProfile.Office:
                BlueLightEnabled = true;
                ColorTemperature = 5000;
                FilterStrength = 0.35;
                BrightnessEnabled = true;
                Brightness = 0.90;
                break;
            case SceneProfile.Reading:
                BlueLightEnabled = true;
                ColorTemperature = 4200;
                FilterStrength = 0.55;
                BrightnessEnabled = true;
                Brightness = 0.80;
                break;
            case SceneProfile.Sleep:
                BlueLightEnabled = true;
                ColorTemperature = 3200;
                FilterStrength = 0.75;
                BrightnessEnabled = true;
                Brightness = 0.65;
                break;
            case SceneProfile.Original:
                BlueLightEnabled = false;
                ColorTemperature = 6500;
                FilterStrength = 0.0;
                BrightnessEnabled = false;
                Brightness = 1.0;
                break;
            case SceneProfile.Custom:
                break;
        }
    }

    // ---------- 蓝光过滤 ----------
    /// <summary>是否启用蓝光过滤。</summary>
    public bool BlueLightEnabled { get; set; } = true;

    /// <summary>色温（开尔文）。范围 1000K ~ 10000K，越低越暖（越护眼）。</summary>
    public int ColorTemperature { get; set; } = 4500;

    /// <summary>过滤强度 0.0 ~ 1.0。</summary>
    public double FilterStrength { get; set; } = 0.5;

    /// <summary>过滤实现方式（叠加层 / Gamma 校正）。</summary>
    public FilterMode FilterMode { get; set; } = FilterMode.Overlay;

    /// <summary>是否根据日出日落自动切换。</summary>
    public bool AutoDayNight { get; set; } = false;

    /// <summary>白天色温（AutoDayNight 开启时生效）。</summary>
    public int DayColorTemperature { get; set; } = 6500;

    /// <summary>夜间色温（AutoDayNight 开启时生效，越低越利于褪黑素分泌）。</summary>
    public int NightColorTemperature { get; set; } = 3500;

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

    private int _workIntervalMinutes = 20;
    /// <summary>工作间隔（分钟）。默认 20，最小防御为 1 分钟。</summary>
    public int WorkIntervalMinutes
    {
        get => _workIntervalMinutes;
        set => _workIntervalMinutes = Math.Clamp(value, 1, 180);
    }

    private int _shortBreakSeconds = 20;
    /// <summary>短休息时长（秒）。默认 20。</summary>
    public int ShortBreakSeconds
    {
        get => _shortBreakSeconds;
        set => _shortBreakSeconds = Math.Clamp(value, 5, 3600);
    }

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

    /// <summary>休息前 30 秒温和微提示浮窗（留出保存工作缓冲期）。</summary>
    public bool PreBreakHeadsUpEnabled { get; set; } = true;

    /// <summary>防冲动跳过模式：休息开始前 5 秒锁定跳过按钮，打断秒关肌肉记忆。</summary>
    public bool AntiImpulsiveSkip { get; set; } = true;

    /// <summary>休息结束时播放轻柔提示音。</summary>
    public bool BreakSoundEnabled { get; set; } = true;

    // ---------- 桌面小组件 ----------
    /// <summary>是否在托盘运行时显示桌面小组件（使用时长与状态）。</summary>
    public bool DesktopWidgetEnabled { get; set; } = true;

    /// <summary>小组件是否使用紧凑迷你胶囊模式。</summary>
    public bool WidgetCompactMode { get; set; } = false;

    /// <summary>小组件位置是否已由用户拖动自定义（false 时使用默认右上角）。</summary>
    public bool WidgetPositionCustomized { get; set; } = false;

    /// <summary>小组件 X 坐标（虚拟屏幕坐标，仅 WidgetPositionCustomized 时生效）。</summary>
    public int WidgetX { get; set; }

    /// <summary>小组件 Y 坐标（虚拟屏幕坐标，仅 WidgetPositionCustomized 时生效）。</summary>
    public int WidgetY { get; set; }

    // ---------- 通用 ----------
    /// <summary>开机自启动。</summary>
    public bool AutoStartEnabled { get; set; } = false;

    /// <summary>关闭窗口时最小化到托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>检测到全屏程序（游戏 / 视频 / 演示）时自动暂停过滤与休息计时。</summary>
    public bool PauseOnFullscreen { get; set; } = true;

    // ---------- 平滑过渡 ----------
    /// <summary>是否在切换预设、开关、全屏自动恢复时启用色温与亮度的平滑渐变。</summary>
    public bool SmoothTransitionEnabled { get; set; } = true;

    // ---------- 全局快捷键 ----------
    /// <summary>是否启用系统全局快捷键（Alt+End 切换蓝光，Alt+PageUp/Down 调亮度，Alt+B 立即休息，Alt+R 视线尺）。</summary>
    public bool EnableGlobalHotkeys { get; set; } = true;

    // ---------- 色彩敏感软件白名单自动避让 ----------
    /// <summary>检测到设计、调色、绘图或特定影音软件在前台激活时，自动暂停蓝光过滤还原真彩。</summary>
    public bool AppBypassEnabled { get; set; } = true;

    /// <summary>色彩敏感白名单进程列表（小写可执行文件名）。</summary>
    public List<string> BypassProcesses { get; set; } = new()
    {
        "photoshop.exe",
        "lightroom.exe",
        "illustrator.exe",
        "premiere.exe",
        "afterfx.exe",
        "figma.exe",
        "davinci resolve.exe",
        "potplayer.exe",
        "vlc.exe",
        "mpv.exe"
    };

    // ---------- DDC/CI 物理硬件显示器背光 ----------
    /// <summary>优先通过 DDC/CI 调节外接物理显示器硬件背光（节能且不损失对比度）。</summary>
    public bool DdcCiBrightnessEnabled { get; set; } = true;

    // ---------- 护眼辅助小工具 ----------
    /// <summary>阅读聚焦视线标尺（高亮跟随鼠标水平行，屏幕其余变暗）。</summary>
    public bool ReadingRulerEnabled { get; set; } = false;

    /// <summary>阅读视线标尺高度（像素）。</summary>
    public int ReadingRulerHeight { get; set; } = 85;

    /// <summary>定时微气泡润眼/坐姿提示（屏幕角落弹出 3 秒自动隐退）。</summary>
    public bool MicroNudgeEnabled { get; set; } = true;

    /// <summary>微气泡提示间隔（分钟）。</summary>
    public int MicroNudgeIntervalMinutes { get; set; } = 15;
}
