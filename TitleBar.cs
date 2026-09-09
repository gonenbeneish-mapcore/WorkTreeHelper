using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorktreeHelper;

/// <summary>
/// Paints the title bar in the window's own colour rather than the Windows accent, so the
/// app reads as one surface instead of a coloured bar above a dark body.
/// </summary>
/// <remarks>
/// These attributes arrived with Windows 11 (build 22000). On anything older every call
/// fails harmlessly and the title bar stays as Windows drew it.
/// </remarks>
internal static class TitleBar
{
    /// <summary>
    /// Matches the caption to the colour the window is actually showing. Safe to call again
    /// whenever that may have changed — switching the system between light and dark repaints
    /// the window but not its caption.
    /// </summary>
    public static void Match(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Not window.Background: the Fluent theme leaves that fully transparent (#00FFFFFF)
        // so its Mica backdrop can show through, and reading it paints a white caption over
        // a dark app. This is the brush that decides what the window looks like.
        if (window.TryFindResource("ApplicationBackgroundBrush") is not SolidColorBrush { Color: var background }) return;

        Set(hwnd, DWMWA_CAPTION_COLOR, ColorRef(background));
        Set(hwnd, DWMWA_BORDER_COLOR, ColorRef(background));

        if (window.TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush text)
            Set(hwnd, DWMWA_TEXT_COLOR, ColorRef(text.Color));

        // Dark mode is deliberately left alone. The theme already sets it, and it is what
        // tints the Mica backdrop: setting it from a misread colour turns the whole window
        // light behind light-on-dark text, which is unreadable rather than merely wrong.
    }

    /// <summary>
    /// Rounds the window's corners. Windows 11 rounds an ordinary window itself, but this one
    /// draws its own caption and has no DWM frame left to round, so it has to ask.
    /// </summary>
    public static void Round(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
    }

    /// <summary>DWM takes a Win32 COLORREF: blue in the high byte, not red.</summary>
    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static void Set(IntPtr hwnd, uint attribute, int value)
    {
        try { DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
        catch (DllNotFoundException) { /* no dwmapi: nothing to paint */ }
    }

    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_CAPTION_COLOR = 35;
    private const uint DWMWA_TEXT_COLOR = 36;

    /// <summary>What Windows 11 gives an ordinary window; 3 is the tighter radius.</summary>
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
}
