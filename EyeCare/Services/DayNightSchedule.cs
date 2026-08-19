using System;
using EyeCare.Models;

namespace EyeCare.Services;

/// <summary>
/// 自动昼夜色温调度：根据本地时间在白天色温与夜间色温之间平滑过渡。
/// 过渡区间为日出（06:00-07:00）与日落（17:00-18:00），
/// 避免色温突变造成视觉不适（参考 f.lux 的平滑渐变设计）。
/// </summary>
public static class DayNightSchedule
{
    /// <summary>日出过渡开始（小时）。</summary>
    private const double SunriseStartHour = 6.0;

    /// <summary>日落过渡开始（小时）。</summary>
    private const double SunsetStartHour = 17.0;

    /// <summary>过渡时长（小时）。</summary>
    private const double TransitionHours = 1.0;

    /// <summary>
    /// 计算当前应使用的有效色温。
    /// 若未开启自动昼夜模式，直接返回手动色温。
    /// </summary>
    public static int GetEffectiveColorTemperature(AppSettings settings, DateTime? now = null)
    {
        if (!settings.AutoDayNight)
            return settings.ColorTemperature;

        var time = now ?? DateTime.Now;
        double hour = time.Hour + time.Minute / 60.0 + time.Second / 3600.0;

        // 白天：日出过渡结束后 ~ 日落过渡开始
        double dayStart = SunriseStartHour + TransitionHours;      // 07:00
        double nightStart = SunsetStartHour;                        // 17:00

        double k; // 0.0 = 夜间色温, 1.0 = 白天色温
        if (hour < SunriseStartHour || hour >= nightStart + TransitionHours)
        {
            k = 0.0; // 夜间
        }
        else if (hour >= dayStart && hour < nightStart)
        {
            k = 1.0; // 白天
        }
        else if (hour < dayStart)
        {
            // 日出过渡：06:00 ~ 07:00
            k = (hour - SunriseStartHour) / TransitionHours;
        }
        else
        {
            // 日落过渡：17:00 ~ 18:00
            k = 1.0 - (hour - nightStart) / TransitionHours;
        }

        k = Math.Clamp(k, 0.0, 1.0);
        double temp = settings.NightColorTemperature + (settings.DayColorTemperature - settings.NightColorTemperature) * k;
        return (int)Math.Round(temp);
    }
}
