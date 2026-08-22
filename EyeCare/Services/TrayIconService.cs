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
    private const uint WM_NULL = 0x0000;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_CLOSE = 0x0010;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    // 保持对实例方法委托的引用，防止被 GC 回收导致函数指针失效
    private NativeMethods.WndProc _wndProcDelegate = null!;
    private IntPtr _windowHandle;
    private bool _menuOpen;
    private bool _disposed;

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

        if (hwnd == IntPtr.Zero)
            return;

        _windowHandle = hwnd;

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
        _windowHandle = IntPtr.Zero;
        NativeMethods.UnregisterClass(className, hInstance);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_TRAYICON)
            {
                // 使用 NOTIFYICON_VERSION_4 后，lParam 的低位字才是鼠标消息，
                // 高位字包含图标 ID；直接比较整个 lParam 会导致点击事件失效。
                uint notification = (uint)lParam.ToInt64() & 0xFFFF;
                if (notification == WM_LBUTTONUP)
                    OpenRequested?.Invoke();
                else if (notification is WM_RBUTTONUP or WM_CONTEXTMENU)
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
            if (msg == WM_CLOSE)
            {
                NativeMethods.DestroyWindow(hWnd);
                return IntPtr.Zero;
            }
            if (msg == WM_DESTROY)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        catch
        {
            // 任何异常都不得穿越原生 WndProc 边界（否则直接导致进程崩溃），
            // 托盘交互失败应静默忽略而非终止应用。
            return IntPtr.Zero;
        }
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        if (_menuOpen) return; // 防止重复右键消息弹出重叠菜单
        _menuOpen = true;
        try
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
            // 使用 TPM_RETURNCMD：TrackPopupMenu 会阻塞直到菜单关闭并返回所选命令，
            // 之后才能安全 DestroyMenu —— 否则菜单仍显示时销毁句柄会造成崩溃。
            uint cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, IntPtr.Zero);
            // 这是 Windows 托盘菜单的约定，可确保菜单在失焦后正确关闭。
            PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            DestroyMenu(menu);

            switch (cmd)
            {
                case CmdOpen: OpenRequested?.Invoke(); break;
                case CmdToggleBlue: ToggleBlueLightRequested?.Invoke(); break;
                case CmdBreakNow: BreakNowRequested?.Invoke(); break;
                case CmdExit: ExitRequested?.Invoke(); break;
            }
        }
        catch
        {
            // 菜单弹出/点击异常不得导致进程崩溃
        }
        finally
        {
            _menuOpen = false;
        }
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
        Shell_NotifyIcon(NIM_ADD, ref nid);

        // 与现代任务栏协商回调格式，确保鼠标事件在 Windows 10/11 上正确投递。
        nid.uVersionOrTimeout = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref nid);
    }

    private static void RemoveTrayIcon(IntPtr hwnd)
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);
    }

    public void Notify(string title, string message)
    {
        // 简单的气球提示（NIF_INFO）
        // 可通过 hwnd 定位；为简洁此处省略，后续可扩展。
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        var hwnd = _windowHandle;
        if (hwnd != IntPtr.Zero)
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);
}
