using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NativeMethods = EyeCare.Native.NativeMethods;

namespace EyeCare.Services;

/// <summary>
/// 系统托盘图标服务：使用 Win32 Shell_NotifyIcon 实现。
/// 在独立后台 STA 线程上运行隐藏消息窗口的消息循环，完全自包含。
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly SettingsService _settings;
    private Thread? _thread;
    private volatile bool _running;

    // 菜单命令 ID
    private const int CmdOpen = 1;
    private const int CmdToggleBlue = 2;
    private const int CmdBreakNow = 3;
    private const int CmdExit = 4;

    private const uint WM_TRAYICON = NativeMethods.WM_TRAYICON;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CLOSE = 0x0010;

    // 保持对实例方法委托的引用，防止被 GC 回收导致函数指针失效
    private NativeMethods.WndProc _wndProcDelegate = null!;

    /// <summary>请求打开主界面。</summary>
    public event Action? OpenRequested;
    /// <summary>切换蓝光过滤状态。</summary>
    public event Action? ToggleBlueLightRequested;
    /// <summary>立即休息。</summary>
    public event Action? BreakNowRequested;
    /// <summary>退出应用。</summary>
    public event Action? ExitRequested;

    public TrayIconService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Show()
    {
        if (_thread is { IsAlive: true }) return;
        _running = true;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "EyeCareTray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>刷新托盘菜单勾选状态（如蓝光过滤开关）。</summary>
    public void RefreshMenu() { /* 菜单在每次弹出时读取最新设置，无需缓存 */ }

    private void MessageLoop()
    {
        var className = "EyeCareTrayWindow";
        var hInstance = NativeMethods.GetModuleHandle(null);

        _wndProcDelegate = WndProc;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = className
        };
        NativeMethods.RegisterClassEx(ref wc);

        var hwnd = NativeMethods.CreateWindowEx(0, className, className, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        IntPtr hIcon = LoadAppIcon();
        AddTrayIcon(hwnd, hIcon, "护眼助手");

        // 消息循环
        MSG msg;
        while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        RemoveTrayIcon(hwnd);
        if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        NativeMethods.DestroyWindow(hwnd);
        NativeMethods.UnregisterClass(className, hInstance);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            uint lp = (uint)lParam.ToInt64();
            if (lp == WM_LBUTTONUP)
                OpenRequested?.Invoke();
            else if (lp == WM_RBUTTONUP)
                ShowContextMenu(hWnd);
            return IntPtr.Zero;
        }
        if (msg == WM_COMMAND)
        {
            int cmd = (int)((long)wParam & 0xFFFF);
            switch (cmd)
            {
                case CmdOpen: OpenRequested?.Invoke(); break;
                case CmdToggleBlue: ToggleBlueLightRequested?.Invoke(); break;
                case CmdBreakNow: BreakNowRequested?.Invoke(); break;
                case CmdExit: ExitRequested?.Invoke(); break;
            }
            return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        IntPtr menu = CreatePopupMenu();
        AppendMenu(menu, 0x00000000, new UIntPtr(CmdOpen), "打开护眼助手");
        AppendMenu(menu, 0x00000800, UIntPtr.Zero, null); // 分隔线

        uint blueFlag = _settings.Data.BlueLightEnabled ? 0x00000008u : 0u; // MF_CHECKED
        AppendMenu(menu, blueFlag, new UIntPtr(CmdToggleBlue), "蓝光过滤");
        AppendMenu(menu, 0x00000000, new UIntPtr(CmdBreakNow), "现在休息一下");
        AppendMenu(menu, 0x00000800, UIntPtr.Zero, null);
        AppendMenu(menu, 0x00000000, new UIntPtr(CmdExit), "退出");

        // 获取当前光标位置弹出菜单
        GetCursorPos(out POINT pt);
        SetForegroundWindow(hwnd);
        TrackPopupMenu(menu, 0x00000010 | 0x00000100, pt.x, pt.y, 0, hwnd, IntPtr.Zero); // TPM_RIGHTBUTTON | TPM_NONOTIFY
        DestroyMenu(menu);
    }

    // ---------- 图标加载 ----------
    private static IntPtr LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "eye.ico");
        if (File.Exists(iconPath))
        {
            IntPtr h = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x10); // IMAGE_ICON | LR_LOADFROMFILE
            if (h != IntPtr.Zero) return h;
        }
        // 回退：系统默认图标
        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    // ---------- Shell_NotifyIcon ----------
    private static void AddTrayIcon(IntPtr hwnd, IntPtr hIcon, string tip)
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = 0x01 | 0x02 | 0x04, // NIF_MESSAGE | NIF_ICON | NIF_TIP
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = tip
        };
        Shell_NotifyIcon(0x00, ref nid); // NIM_ADD
    }

    private static void RemoveTrayIcon(IntPtr hwnd)
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1
        };
        Shell_NotifyIcon(0x02, ref nid); // NIM_DELETE
    }

    public void Notify(string title, string message)
    {
        // 简单的气球提示（NIF_INFO）
        // 可通过 hwnd 定位；为简洁此处省略，后续可扩展。
    }

    public void Dispose()
    {
        _running = false;
    }

    // ================= P/Invoke =================
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}