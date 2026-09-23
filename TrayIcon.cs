using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WorktreeHelper;

/// <summary>
/// A notification-area (tray) icon, driven straight through Shell_NotifyIcon.
/// </summary>
/// <remarks>
/// Hand-rolled rather than using System.Windows.Forms.NotifyIcon: that would pull
/// the whole WinForms stack into a project that otherwise has no dependencies, and
/// its context menu is a WinForms one that ignores the Fluent light/dark theme the
/// rest of the app follows. Here the menu is an ordinary WPF ContextMenu.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Raised on left-click. </summary>
    public event Action? Clicked;

    /// <summary>Raised on right-click, with the anchor in physical screen pixels.</summary>
    public event Action<int, int>? ContextMenuRequested;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private NOTIFYICONDATA _data;
    private bool _disposed;

    /// <param name="icon">
    /// The icon to show, at tray size. Ownership passes here: it is destroyed on
    /// <see cref="SetIcon"/> and on <see cref="Dispose"/>.
    /// </param>
    public TrayIcon(string tip, IntPtr icon)
    {
        // Explorer broadcasts this if it restarts, and every tray icon has to re-add
        // itself when it does.
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // A hidden window purely to receive the icon's callbacks. Top-level rather than
        // message-only, because message-only windows are left out of broadcasts, and
        // TaskbarCreated is one: the icon would never come back after Explorer restarts.
        // A tool window, so that even if something did show it, it would not take a
        // taskbar button.
        _source = new HwndSource(new HwndSourceParameters("WorktreeHelperTray")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        });
        _source.AddHook(WndProc);

        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = icon,
            szTip = tip,
            szInfo = "",
            szInfoTitle = "",
            uVersion = NOTIFYICON_VERSION_4,
        };
        Add();
    }

    private void Add()
    {
        if (!Shell_NotifyIcon(NIM_ADD, ref _data)) return;
        // Version 4 delivers the anchor point in wParam and the event in lParam.
        Shell_NotifyIcon(NIM_SETVERSION, ref _data);
    }

    /// <summary>
    /// Swaps the icon, which is how the notification area follows a change of theme.
    /// </summary>
    public void SetIcon(IntPtr icon)
    {
        if (_disposed || icon == IntPtr.Zero || icon == _data.hIcon) return;

        var old = _data.hIcon;
        _data.hIcon = icon;
        _data.uFlags = NIF_ICON;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
        _data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    public void SetTooltip(string tip)
    {
        if (_disposed) return;
        _data.szTip = tip;
        _data.uFlags = NIF_TIP | NIF_SHOWTIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
        _data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONUP:
                    Clicked?.Invoke();
                    handled = true;
                    break;
                case WM_CONTEXTMENU:
                    var packed = wParam.ToInt64();
                    ContextMenuRequested?.Invoke((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
                    handled = true;
                    break;
            }
        }
        else if (msg == _taskbarCreated)
        {
            Add();
        }
        return IntPtr.Zero;
    }

    /// <summary>The size the notification area wants its icons at.</summary>
    public static (int Width, int Height) IconSize
        => (GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIcon(NIM_DELETE, ref _data);
        if (_data.hIcon != IntPtr.Zero) DestroyIcon(_data.hIcon);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    // ---- Win32 ---------------------------------------------------------------

    private const uint TrayIconId = 1;
    private const int WM_APP = 0x8000;
    private const int CallbackMessage = WM_APP + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    private const int WS_EX_TOOLWINDOW = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

}
