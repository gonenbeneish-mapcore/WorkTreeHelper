using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WorktreeHelper;

/// <summary>Places an already-open WPF popup at exact screen pixels.</summary>
internal static class PopupPlacement
{
    /// <summary>
    /// Moves <paramref name="menu"/> so its bottom-right corner lands on
    /// (<paramref name="anchorX"/>, <paramref name="anchorY"/>) in physical screen
    /// pixels — how a notification-area menu opens from its icon.
    /// </summary>
    /// <remarks>
    /// Measured from the popup's own window rather than from ActualWidth: WPF does not
    /// scale the popup window by the monitor's DPI, so a size derived from ActualWidth
    /// and the DPI scale overshoots (68 px at 125%) and the menu opens away from the
    /// icon. Working in raw pixels also keeps this right when the tray sits on a monitor
    /// whose scaling differs from the window's.
    /// </remarks>
    public static void AnchorBottomRight(ContextMenu menu, int anchorX, int anchorY)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource source) return;
        if (!GetWindowRect(source.Handle, out var rect)) return;

        SetWindowPos(source.Handle, IntPtr.Zero,
                     anchorX - (rect.right - rect.left), anchorY - (rect.bottom - rect.top),
                     0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Makes an open popup the foreground window.
    /// </summary>
    /// <remarks>
    /// A tray click leaves the shell in the foreground, and a background process cannot
    /// take a mouse capture — which is how WPF notices the click that should dismiss the
    /// menu. Without this the menu stays up until one of its own items is picked.
    /// </remarks>
    public static void Activate(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is HwndSource source)
            SetForegroundWindow(source.Handle);
    }

    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
