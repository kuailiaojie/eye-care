using System;
using System.IO;
using System.Text;
using System.Threading;
using EyeCare.Native;

namespace EyeCare.Services;

/// <summary>
/// 色彩敏感应用白名单避让服务：
/// 监测当前前台激活程序，若属于设计、绘图、调色或影音白名单（如 photoshop.exe / figma.exe / potplayer.exe），
/// 自动平滑淡出蓝光过滤，还原真实色彩；离开该应用后自动平滑恢复。
/// </summary>
public class AppBypassService : IDisposable
{
    private readonly SettingsService _settings;
    private Timer? _checkTimer;
    private bool _isBypassActive;
    private string _currentProcessName = "";

    /// <summary>当前前台窗口是否属于色彩敏感白名单软件。</summary>
    public bool IsBypassActive => _isBypassActive;

    /// <summary>当前前台进程可执行文件名。</summary>
    public string CurrentProcessName => _currentProcessName;

    /// <summary>白名单避让状态发生改变（激活 / 退出激活）。</summary>
    public event Action? StateChanged;

    public AppBypassService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        _checkTimer ??= new Timer(CheckForegroundApp, null, 1000, 1000);
    }

    public void Stop()
    {
        _checkTimer?.Dispose();
        _checkTimer = null;
        if (_isBypassActive)
        {
            _isBypassActive = false;
            StateChanged?.Invoke();
        }
    }

    private void CheckForegroundApp(object? state)
    {
        var s = _settings.Data;
        if (!s.AppBypassEnabled)
        {
            if (_isBypassActive)
            {
                _isBypassActive = false;
                StateChanged?.Invoke();
            }
            return;
        }

        string procName = GetForegroundProcessName();
        _currentProcessName = procName;

        bool shouldBypass = false;
        if (!string.IsNullOrEmpty(procName))
        {
            foreach (var item in s.BypassProcesses)
            {
                if (string.Equals(procName, item, StringComparison.OrdinalIgnoreCase))
                {
                    shouldBypass = true;
                    break;
                }
            }
        }

        if (shouldBypass != _isBypassActive)
        {
            _isBypassActive = shouldBypass;
            StateChanged?.Invoke();
        }
    }

    private static string GetForegroundProcessName()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "";

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return "";

        var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return "";

        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
            {
                return Path.GetFileName(sb.ToString()).ToLowerInvariant();
            }
        }
        catch
        {
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }

        return "";
    }

    public void Dispose()
    {
        Stop();
    }
}
