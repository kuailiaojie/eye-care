using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EyeCare.Models;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare.Services;

/// <summary>
/// 系统级 Gamma 校正服务（f.lux / LightBulb 同款方案）。
/// 通过修改显示器 Gamma Ramp 直接压缩蓝光通道输出，无需任何叠加窗口：
/// 对全屏独占游戏、视频播放器等叠加层无法生效的场景同样有效。
///
/// 实现要点：
/// - 逐显示器枚举（EnumDisplayDevices + CreateDC + SetDeviceGammaRamp）
/// - 首次应用时保存原始 Ramp，禁用 / 退出时恢复
/// - 定时重放（游戏全屏独占、驱动更新会重置 Gamma Ramp）
/// - 支持自动昼夜色温（DayNightSchedule）
/// </summary>
public class GammaRampService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly List<DisplayRamp> _displays = new();
    private Timer? _reapplyTimer;
    private bool _active;

    /// <summary>Gamma 校正当前是否生效。</summary>
    public bool IsActive => _active;

    private class DisplayRamp
    {
        public string DeviceName = "";
        public ushort[] Original = new ushort[256 * 3];
    }

    public GammaRampService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>按当前设置应用（或关闭）Gamma 校正。</summary>
    public void ApplySettings()
    {
        var s = _settings.Data;
        bool shouldBeActive = s.BlueLightEnabled && s.FilterMode == FilterMode.GammaRamp;

        if (!shouldBeActive)
        {
            Disable();
            return;
        }

        EnableAndApply();
    }

    private void EnableAndApply()
    {
        if (!_active)
        {
            // 首次启用：枚举显示器并保存原始 Ramp
            try
            {
                EnumerateDisplays();
            }
            catch
            {
                // 枚举失败（无显示器 / 驱动异常）时静默降级，不崩溃
                _displays.Clear();
                return;
            }
            _active = true;
            _reapplyTimer?.Dispose();
            // 每 10 秒重放一次，抵抗游戏/驱动对 Gamma Ramp 的重置
            _reapplyTimer = new Timer(_ => ApplyRamps(), null, 10000, 10000);
        }

        ApplyRamps();
    }

    public void Disable()
    {
        if (!_active) return;
        _active = false;
        _reapplyTimer?.Dispose();
        _reapplyTimer = null;
        RestoreOriginals();
    }

    private void EnumerateDisplays()
    {
        _displays.Clear();

        uint i = 0;
        while (true)
        {
            var dd = new NativeMethods.DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
            if (!NativeMethods.EnumDisplayDevices(null, i, ref dd, 0))
                break;

            if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                var hdc = NativeMethods.CreateDC(null, dd.DeviceName, null, IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    var original = new ushort[256 * 3];
                    if (NativeMethods.GetDeviceGammaRamp(hdc, original))
                    {
                        _displays.Add(new DisplayRamp { DeviceName = dd.DeviceName, Original = original });
                    }
                    NativeMethods.DeleteDC(hdc);
                }
            }

            i++;
        }
    }

    /// <summary>把当前设置换算的 Ramp 应用到所有显示器。</summary>
    private void ApplyRamps()
    {
        if (!_active) return;

        try
        {
            var s = _settings.Data;
            int kelvin = DayNightSchedule.GetEffectiveColorTemperature(s);
            ushort[] ramp = BuildRamp(kelvin, s.FilterStrength);

            foreach (var display in _displays)
            {
                var hdc = NativeMethods.CreateDC(null, display.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) continue;
                NativeMethods.SetDeviceGammaRamp(hdc, ramp);
                NativeMethods.DeleteDC(hdc);
            }
        }
        catch
        {
            // 显示器驱动异常时静默跳过本轮，不崩溃
        }
    }

    /// <summary>
    /// 由色温与强度生成 Gamma Ramp。
    /// 使用 Tanner Helland 黑体近似（f.lux 同款算法）得到 R/G/B 倍率，
    /// 再按强度向 1.0（不调整）插值，最后生成线性 Ramp。
    /// </summary>
    internal static ushort[] BuildRamp(int kelvin, double strength)
    {
        double r = 1.0, g = 1.0, b = 1.0;
        GetTemperatureMultipliers(kelvin, ref r, ref g, ref b);

        strength = Math.Clamp(strength, 0.0, 1.0);
        r = 1.0 - (1.0 - r) * strength;
        g = 1.0 - (1.0 - g) * strength;
        b = 1.0 - (1.0 - b) * strength;

        var ramp = new ushort[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            int value = (int)Math.Round(i * 257.0 * r); // 0..255 → 0..65535
            ramp[i] = (ushort)Math.Clamp(value, 0, 65535);
            value = (int)Math.Round(i * 257.0 * g);
            ramp[256 + i] = (ushort)Math.Clamp(value, 0, 65535);
            value = (int)Math.Round(i * 257.0 * b);
            ramp[512 + i] = (ushort)Math.Clamp(value, 0, 65535);
        }
        return ramp;
    }

    /// <summary>
    /// 色温（开尔文）→ R/G/B 输出倍率（1.0 = 不调整）。
    /// 色温越低，蓝通道压缩越狠、绿通道轻微压缩，红通道基本不变。
    /// </summary>
    private static void GetTemperatureMultipliers(int kelvin, ref double r, ref double g, ref double b)
    {
        kelvin = Math.Clamp(kelvin, 1000, 10000);

        // Tanner Helland 黑体近似（f.lux 采用算法）
        double t = kelvin / 100.0;
        double red, green, blue;

        if (t <= 66)
        {
            red = 255.0;
            green = 99.4708025861 * Math.Log(t) - 161.1195681661;
            blue = t <= 19 ? 0.0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        }
        else
        {
            red = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            green = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            blue = 255.0;
        }

        // 以 6500K（自然白）为基准归一化 → 倍率
        // 6500K 时 (255, ~255, ~255)，倍率 ≈ 1.0
        double baseRed = 255.0, baseGreen = 255.0, baseBlue = 255.0;
        r = Math.Clamp(red / baseRed, 0.05, 1.0);
        g = Math.Clamp(green / baseGreen, 0.05, 1.0);
        b = Math.Clamp(blue / baseBlue, 0.05, 1.0);
    }

    private void RestoreOriginals()
    {
        try
        {
            foreach (var display in _displays)
            {
                var hdc = NativeMethods.CreateDC(null, display.DeviceName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero) continue;
                NativeMethods.SetDeviceGammaRamp(hdc, display.Original);
                NativeMethods.DeleteDC(hdc);
            }
        }
        catch
        {
            // 恢复失败不影响退出流程
        }
        _displays.Clear();
    }

    public void Dispose()
    {
        Disable();
    }
}
