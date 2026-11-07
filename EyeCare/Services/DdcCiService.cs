using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EyeCare.Native;
using RECT = EyeCare.Native.NativeMethods.RECT;

namespace EyeCare.Services;

/// <summary>
/// DDC/CI 物理硬件显示器背光控制：
/// 优先通过 DDC/CI 协议调节外接显示器硬件背光（更护眼、更省电、保留原生对比度）。
/// 若硬件不支持（如笔记本内屏或部分廉价扩展坞），平滑由软件遮罩接力，绝不报错或崩溃。
/// </summary>
public class DdcCiService : IDisposable
{
    /// <summary>单次显示器枚举中识别一台物理显示器的键（显示器矩形 + 物理序号）。</summary>
    private readonly record struct MonitorKey(int Left, int Top, int Right, int Bottom, int Physical);

    private readonly SettingsService _settings;

    /// <summary>
    /// 变暗前各物理显示器自身的原始背光值。恢复时写回原值而不是最大值，
    /// 避免把用户手工设好的显示器亮度强行拉到 100%。
    /// </summary>
    private readonly Dictionary<MonitorKey, uint> _originalBrightness = new();

    /// <summary>当前被硬件背光实际调暗的显示器矩形（供覆盖层跳过重复的软件遮罩）。</summary>
    private readonly List<RECT> _hardwareDimmedRects = new();

    /// <summary>最近一次成功应用的亮度系数。</summary>
    private double _lastApplied = 1.0;

    public DdcCiService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>被硬件背光调暗的显示器矩形（物理像素，虚拟屏幕坐标）。</summary>
    internal IReadOnlyList<RECT> HardwareDimmedRects
    {
        get
        {
            lock (_hardwareDimmedRects)
            {
                return _lastApplied < 0.999 ? _hardwareDimmedRects.ToArray() : Array.Empty<RECT>();
            }
        }
    }

    /// <summary>
    /// 尝试调节所有支持 DDC/CI 的物理显示器背光（0.0 ~ 1.0）。
    /// </summary>
    public bool TrySetHardwareBrightness(double brightnessNormalized)
    {
        if (!_settings.Data.DdcCiBrightnessEnabled) return false;

        brightnessNormalized = Math.Clamp(brightnessNormalized, 0.0, 1.0);
        bool anySuccess = false;
        var dimmedRects = new List<RECT>();

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr param) =>
            {
                bool monitorDimmed = false;

                if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                {
                    var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                    {
                        for (int i = 0; i < physicalMonitors.Length; i++)
                        {
                            var pm = physicalMonitors[i];
                            if (!NativeMethods.GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint cur, out uint max))
                                continue;

                            var key = new MonitorKey(rect.Left, rect.Top, rect.Right, rect.Bottom, i);
                            if (!_originalBrightness.ContainsKey(key))
                            {
                                _originalBrightness[key] = cur;
                            }

                            uint target = (uint)Math.Clamp(min + (max - min) * brightnessNormalized, min, max);
                            if (NativeMethods.SetMonitorBrightness(pm.hPhysicalMonitor, target))
                            {
                                anySuccess = true;
                                monitorDimmed |= brightnessNormalized < 0.999;
                            }
                        }
                        NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
                    }
                }

                if (monitorDimmed)
                {
                    lock (_hardwareDimmedRects)
                    {
                        dimmedRects.Add(rect);
                    }
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 硬件驱动或协议异常静默忽略
        }

        if (anySuccess)
        {
            _lastApplied = brightnessNormalized;
        }

        lock (_hardwareDimmedRects)
        {
            _hardwareDimmedRects.Clear();
            _hardwareDimmedRects.AddRange(dimmedRects);
        }

        return anySuccess;
    }

    /// <summary>
    /// 把之前调暗过的物理显示器背光恢复为各自变暗前的原始亮度。
    /// 从未调节过时不做任何事，绝不把用户的显示器亮度强行改成最大值。
    /// </summary>
    public void RestoreHardwareBrightness()
    {
        lock (_hardwareDimmedRects)
        {
            _hardwareDimmedRects.Clear();
        }

        if (_originalBrightness.Count == 0)
        {
            _lastApplied = 1.0;
            return;
        }

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr param) =>
            {
                if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                {
                    var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                    {
                        for (int i = 0; i < physicalMonitors.Length; i++)
                        {
                            var key = new MonitorKey(rect.Left, rect.Top, rect.Right, rect.Bottom, i);
                            var pm = physicalMonitors[i];
                            if (_originalBrightness.TryGetValue(key, out uint original) &&
                                NativeMethods.GetMonitorBrightness(pm.hPhysicalMonitor, out _, out _, out uint max))
                            {
                                NativeMethods.SetMonitorBrightness(pm.hPhysicalMonitor, Math.Min(original, max));
                            }
                        }
                        NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        _originalBrightness.Clear();
        _lastApplied = 1.0;
    }

    public void Dispose()
    {
        // 仅在确实调暗过时恢复，避免退出时把所有 DDC/CI 显示器拉到最大亮度。
        RestoreHardwareBrightness();
    }
}
