using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace EyeCare.Services;

/// <summary>
/// 开机自启动服务：
/// 1. 打包模式（MSIX）：优先使用 Windows.ApplicationModel.StartupTask API 遵守微软商店规范；
/// 2. 独立 / 非打包模式：平滑降级使用注册表 HKCU Run 键。
/// </summary>
public class StartupService
{
    private const string TaskId = "EyeCareStartupTask";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EyeCare";

    private static bool IsPackaged
    {
        get
        {
            try
            {
                return Windows.ApplicationModel.Package.Current != null;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string ExePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Environment.ProcessPath ?? "EyeCare.exe";
            return exe;
        }
    }

    public async Task<bool> EnableAutoStartAsync()
    {
        if (IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                var state = await task.RequestEnableAsync();
                return state == Windows.ApplicationModel.StartupTaskState.Enabled;
            }
            catch
            {
                // 打包 API 失败时降级到注册表
            }
        }

        return EnableRegistryAutoStart();
    }

    public async Task DisableAutoStartAsync()
    {
        if (IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                task.Disable();
                return;
            }
            catch
            {
                // 打包 API 失败时降级
            }
        }

        DisableRegistryAutoStart();
    }

    public async Task<bool> IsAutoStartEnabledAsync()
    {
        if (IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                return task.State == Windows.ApplicationModel.StartupTaskState.Enabled;
            }
            catch
            {
                // 降级
            }
        }

        return IsRegistryAutoStartEnabled();
    }

    // 同步适配接口
    public void EnableAutoStart() => _ = EnableAutoStartAsync();
    public void DisableAutoStart() => _ = DisableAutoStartAsync();

    public bool IsAutoStartEnabled()
    {
        try
        {
            return IsAutoStartEnabledAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return IsRegistryAutoStartEnabled();
        }
    }

    private static bool EnableRegistryAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, $"\"{ExePath}\"", RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void DisableRegistryAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }

    private static bool IsRegistryAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
