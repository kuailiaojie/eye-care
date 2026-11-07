using System;
using System.Runtime.InteropServices;
using EyeCare.Native;

namespace EyeCare.Services;

/// <summary>
/// 系统全局快捷键服务：
/// - Alt + End: 切换蓝光过滤
/// - Alt + PageUp: 屏幕亮度提高 5%
/// - Alt + PageDown: 屏幕亮度降低 5%
/// - Alt + B: 立即触发休息
/// - Alt + R: 开关阅读视线标尺
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private const string WindowClassName = "EyeCareHotkeyListener";
    private const uint WM_HOTKEY = 0x0312;

    private const int HOTKEY_ID_TOGGLE_FILTER = 9001;
    private const int HOTKEY_ID_BRIGHTNESS_UP = 9002;
    private const int HOTKEY_ID_BRIGHTNESS_DOWN = 9003;
    private const int HOTKEY_ID_START_BREAK = 9004;
    private const int HOTKEY_ID_TOGGLE_RULER = 9005;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint VK_END = 0x23;
    private const uint VK_PRIOR = 0x21; // PageUp
    private const uint VK_NEXT = 0x22;  // PageDown
    private const uint VK_B = 0x42;
    private const uint VK_R = 0x52;

    private readonly SettingsService _settings;
    private IntPtr _hwnd;
    private NativeMethods.WndProc? _wndProcDelegate;
    private bool _registered;

    public GlobalHotkeyService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        if (_hwnd != IntPtr.Zero) return;

        _wndProcDelegate = WndProc;
        var hInstance = NativeMethods.GetModuleHandle(null);

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = WindowClassName
        };
        NativeMethods.RegisterClassEx(ref wc);

        _hwnd = NativeMethods.CreateWindowEx(
            0, WindowClassName, WindowClassName, 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        RegisterHotkeys();
    }

    public void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero || _registered) return;
        if (!_settings.Data.EnableGlobalHotkeys) return;

        NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_TOGGLE_FILTER, MOD_ALT | MOD_NOREPEAT, VK_END);
        NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_BRIGHTNESS_UP, MOD_ALT | MOD_NOREPEAT, VK_PRIOR);
        NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_BRIGHTNESS_DOWN, MOD_ALT | MOD_NOREPEAT, VK_NEXT);
        NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_START_BREAK, MOD_ALT | MOD_NOREPEAT, VK_B);
        NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID_TOGGLE_RULER, MOD_ALT | MOD_NOREPEAT, VK_R);
        _registered = true;
    }

    public void UnregisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero || !_registered) return;

        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_TOGGLE_FILTER);
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_BRIGHTNESS_UP);
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_BRIGHTNESS_DOWN);
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_START_BREAK);
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID_TOGGLE_RULER);
        _registered = false;
    }

    public void UpdateSettings()
    {
        if (_settings.Data.EnableGlobalHotkeys)
        {
            RegisterHotkeys();
        }
        else
        {
            UnregisterHotkeys();
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                App.UiDispatcher?.TryEnqueue(() => HandleHotkey(id));
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        catch
        {
            // 异常穿越原生 WndProc 边界会导致进程直接崩溃，静默忽略。
            return IntPtr.Zero;
        }
    }

    private void HandleHotkey(int id)
    {
        var s = _settings.Data;
        switch (id)
        {
            case HOTKEY_ID_TOGGLE_FILTER:
                s.BlueLightEnabled = !s.BlueLightEnabled;
                _settings.Save();
                App.ApplyFilters();
                App.TrayIcon?.Notify("蓝光过滤", s.BlueLightEnabled ? "已开启蓝光过滤" : "已关闭蓝光过滤");
                break;

            case HOTKEY_ID_BRIGHTNESS_UP:
                s.Brightness = Math.Clamp(Math.Round(s.Brightness + 0.05, 2), 0.20, 1.0);
                s.BrightnessEnabled = s.Brightness < 0.999;
                _settings.Save();
                App.ApplyFilters();
                App.TrayIcon?.Notify("屏幕亮度", $"已调节至 {(int)(s.Brightness * 100)}%");
                break;

            case HOTKEY_ID_BRIGHTNESS_DOWN:
                s.Brightness = Math.Clamp(Math.Round(s.Brightness - 0.05, 2), 0.20, 1.0);
                s.BrightnessEnabled = true;
                _settings.Save();
                App.ApplyFilters();
                App.TrayIcon?.Notify("屏幕亮度", $"已调节至 {(int)(s.Brightness * 100)}%");
                break;

            case HOTKEY_ID_START_BREAK:
                App.BreakReminder.StartBreakNow();
                break;

            case HOTKEY_ID_TOGGLE_RULER:
                App.ToggleReadingRuler();
                break;
        }
    }

    public void Dispose()
    {
        UnregisterHotkeys();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
