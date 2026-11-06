using System;
using System.Runtime.InteropServices;
using EyeCare.Native;

namespace EyeCare.Services;

/// <summary>
/// DDC/CI 物理硬件显示器背光控制：
/// 优先通过 DDC/CI 协议调节外接显示器硬件背光（更护眼、更省电、保留原生对比度）。
/// 若硬件不支持（如笔记本内屏或部分廉价扩展坞），平滑由软件遮罩接力，绝不报错或崩溃。
/// </summary>
public class DdcCiService : IDisposable
{
    private readonly SettingsService _settings;

    public DdcCiService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// 尝试调节所有支持 DDC/CI 的物理显示器背光（0.0 ~ 1.0）。
    /// </summary>
    public bool TrySetHardwareBrightness(double brightnessNormalized)
    {
        if (!_settings.Data.DdcCiBrightnessEnabled) return false;

        bool anySuccess = false;

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr param) =>
            {
                if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                {
                    var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
                    if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                    {
                        foreach (var pm in physicalMonitors)
                        {
                            if (NativeMethods.GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint cur, out uint max))
                            {
                                uint target = (uint)Math.Clamp(min + (max - min) * brightnessNormalized, min, max);
                                if (NativeMethods.SetMonitorBrightness(pm.hPhysicalMonitor, target))
                                {
                                    anySuccess = true;
                                }
                            }
                        }
                        NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 硬件驱动或协议异常静默忽略
        }

        return anySuccess;
    }

    public void Dispose()
    {
    }
}
