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

    // 平滑缓动过渡状态
    private double _currentR = 1.0;
    private double _currentG = 1.0;
    private double _currentB = 1.0;
    private double _targetR = 1.0;
    private double _targetG = 1.0;
    private double _targetB = 1.0;
    private Timer? _transitionTimer;
    private readonly object _rampLock = new();

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

    /// <summary>按当前设置应用（或关闭）Gamma 校正（支持平滑渐变与白名单避让）。</summary>
    public void ApplySettings(bool immediate = false)
    {
        var s = _settings.Data;
        bool fullscreenPaused = s.PauseOnFullscreen && App.FullscreenPause.IsFullscreenActive;
        bool bypassActive = s.AppBypassEnabled && App.AppBypass != null && App.AppBypass.IsBypassActive;
        bool shouldBeActive = s.BlueLightEnabled && s.FilterMode == FilterMode.GammaRamp && !fullscreenPaused && !bypassActive;

        double targetR = 1.0, targetG = 1.0, targetB = 1.0;
        if (shouldBeActive)
        {
            int kelvin = DayNightSchedule.GetEffectiveColorTemperature(s);
            GetTemperatureMultipliers(kelvin, ref targetR, ref targetG, ref targetB);
            double strength = Math.Clamp(s.FilterStrength, 0.0, 1.0);
            targetR = 1.0 - (1.0 - targetR) * strength;
            targetG = 1.0 - (1.0 - targetG) * strength;
            targetB = 1.0 - (1.0 - targetB) * strength;
        }

        if (shouldBeActive && !_active)
        {
            try
            {
                EnumerateDisplays();
            }
            catch
            {
                _displays.Clear();
                return;
            }
            _active = true;
            _reapplyTimer?.Dispose();
            _reapplyTimer = new Timer(_ => ApplyRamps(), null, 10000, 10000);
        }

        lock (_rampLock)
        {
            _targetR = targetR;
            _targetG = targetG;
            _targetB = targetB;

            if (immediate || !s.SmoothTransitionEnabled || !_active)
            {
                _currentR = targetR;
                _currentG = targetG;
                _currentB = targetB;
                _transitionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                if (!shouldBeActive)
                {
                    Disable();
                }
                else
                {
                    ApplyRampsWithMultiplier(targetR, targetG, targetB);
                }
            }
            else
            {
                _transitionTimer ??= new Timer(OnTransitionTick, null, 0, 25);
                _transitionTimer.Change(0, 25);
            }
        }
    }

    private void OnTransitionTick(object? state)
    {
        lock (_rampLock)
        {
            double dR = _targetR - _currentR;
            double dG = _targetG - _currentG;
            double dB = _targetB - _currentB;

            bool done = Math.Abs(dR) < 0.005 && Math.Abs(dG) < 0.005 && Math.Abs(dB) < 0.005;
            if (done)
            {
                _currentR = _targetR;
                _currentG = _targetG;
                _currentB = _targetB;
                _transitionTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                if (_targetR >= 0.999 && _targetG >= 0.999 && _targetB >= 0.999)
                {
                    Disable();
                    return;
                }
            }
            else
            {
                _currentR += dR * 0.25;
                _currentG += dG * 0.25;
                _currentB += dB * 0.25;
            }

            ApplyRampsWithMultiplier(_currentR, _currentG, _currentB);
        }
    }

    public void Disable()
    {
        if (!_active) return;
        _active = false;
        _transitionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _reapplyTimer?.Dispose();
        _reapplyTimer = null;
        RestoreOriginals();
        _currentR = 1.0;
        _currentG = 1.0;
        _currentB = 1.0;
    }

    /// <summary>显示器插拔或分辨率变更后重新枚举并挂接新显示器。</summary>
    public void Reinitialize()
    {
        if (_active)
        {
            RestoreOriginals();
            try
            {
                EnumerateDisplays();
            }
            catch
            {
                _displays.Clear();
                return;
            }
            ApplyRamps();
        }
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
                        // 防御上次异常崩溃残留暖色：若 B 通道最大值严重低于 R 通道（<75%），说明读取到的是已被染黄的 Ramp
                        if (original[255] > 0 && original[512 + 255] < original[255] * 0.75)
                        {
                            original = GenerateLinearRamp();
                        }
                        _displays.Add(new DisplayRamp { DeviceName = dd.DeviceName, Original = original });
                    }
                    NativeMethods.DeleteDC(hdc);
                }
            }

            i++;
        }
    }

    /// <summary>把当前色温系数的 Ramp 应用到所有显示器。</summary>
    private void ApplyRamps()
    {
        if (!_active) return;
        ApplyRampsWithMultiplier(_currentR, _currentG, _currentB);
    }

    private void ApplyRampsWithMultiplier(double r, double g, double b)
    {
        try
        {
            foreach (var display in _displays)
            {
                // 基于每个显示器自身备份的 ICC/Original 曲线进行乘法缩放，保留用户校色配置
                ushort[] ramp = BuildRampForDisplay(display.Original, r, g, b);
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
    /// 基于指定显示器的原始 Ramp（或线性回退）乘以 R/G/B 色温衰减倍率。
    /// 完整保留用户显示器自带的 ICC 色彩校准曲线。
    /// </summary>
    internal static ushort[] BuildRampForDisplay(ushort[]? original, double r, double g, double b)
    {
        var ramp = new ushort[256 * 3];
        bool useOriginal = original != null && original.Length == 256 * 3 && original[255] > 0;

        for (int i = 0; i < 256; i++)
        {
            double baseR = useOriginal ? original![i] : (i * 257.0);
            double baseG = useOriginal ? original![256 + i] : (i * 257.0);
            double baseB = useOriginal ? original![512 + i] : (i * 257.0);

            ramp[i] = (ushort)Math.Clamp((int)Math.Round(baseR * r), 0, 65535);
            ramp[256 + i] = (ushort)Math.Clamp((int)Math.Round(baseG * g), 0, 65535);
            ramp[512 + i] = (ushort)Math.Clamp((int)Math.Round(baseB * b), 0, 65535);
        }
        return ramp;
    }

    /// <summary>
    /// 由色温与强度生成线性 Gamma Ramp（兼容保留）。
    /// </summary>
    internal static ushort[] BuildRamp(int kelvin, double strength)
    {
        double r = 1.0, g = 1.0, b = 1.0;
        GetTemperatureMultipliers(kelvin, ref r, ref g, ref b);

        strength = Math.Clamp(strength, 0.0, 1.0);
        r = 1.0 - (1.0 - r) * strength;
        g = 1.0 - (1.0 - g) * strength;
        b = 1.0 - (1.0 - b) * strength;

        return BuildRampForDisplay(null, r, g, b);
    }

    /// <summary>
    /// 重置所有显示器为标准单调线性 sRGB Ramp（用于解除异常暖色锁死或手动一键急救恢复）。
    /// </summary>
    public void ResetToStandardSRgb()
    {
        try
        {
            var linear = GenerateLinearRamp();
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
                        NativeMethods.SetDeviceGammaRamp(hdc, linear);
                        NativeMethods.DeleteDC(hdc);
                    }
                }
                i++;
            }

            foreach (var d in _displays)
            {
                Array.Copy(linear, d.Original, linear.Length);
            }
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>生成标准单调递增线性 sRGB Ramp。</summary>
    public static ushort[] GenerateLinearRamp()
    {
        var ramp = new ushort[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            ushort val = (ushort)Math.Clamp(i * 257, 0, 65535);
            ramp[i] = val;
            ramp[256 + i] = val;
            ramp[512 + i] = val;
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
        _transitionTimer?.Dispose();
        _transitionTimer = null;
        Disable();
    }
}
