using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorktreeHelper;

/// <summary>Work area (screen minus taskbar) of the monitor a window is on.</summary>
internal static class MonitorWorkArea
{
    /// <summary>Work area in DIPs, for the monitor nearest <paramref name="window"/>.</summary>
    public static Rect For(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    // GetMonitorInfo reports physical pixels; WPF sizes in DIPs.
                    var dpi = VisualTreeHelper.GetDpi(window);
                    var r = info.rcWork;
                    return new Rect(
                        r.left / dpi.DpiScaleX,
                        r.top / dpi.DpiScaleY,
                        (r.right - r.left) / dpi.DpiScaleX,
                        (r.bottom - r.top) / dpi.DpiScaleY);
                }
            }
        }
        catch { /* fall back to the primary monitor */ }

        return SystemParameters.WorkArea;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // DllImport rather than LibraryImport: the source generator needs AllowUnsafeBlocks,
    // not worth turning on project-wide for two calls.
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
