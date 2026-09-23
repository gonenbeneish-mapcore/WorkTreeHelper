using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorktreeHelper;

/// <summary>
/// The app's icon, in the colourway that suits the Windows theme: a dark disc on a light
/// taskbar, a pale one on a dark taskbar.
/// </summary>
/// <remarks>
/// Two files rather than one, because an icon cannot be told which background it will be
/// drawn on. The window icon and the notification-area icon are handed over by the app and
/// can be swapped when the theme changes; the icon compiled into the exe, which Explorer
/// and SmartScreen show, cannot, which is why the darker disc is the one that goes there.
/// </remarks>
internal static class AppIcon
{
    private const string Light = "app.ico";       // dark disc, for a light theme
    private const string Dark = "app-dark.ico";   // pale disc, for a dark theme

    /// <summary>
    /// Whether Windows is in dark mode, read from the theme the app is already wearing.
    /// </summary>
    /// <remarks>
    /// The Fluent theme follows the system setting (ThemeMode="System"), so its background
    /// brush is the same answer the registry would give, without reaching for the registry
    /// or for an undocumented uxtheme ordinal. It is also the answer that matters: the icon
    /// should match the app it belongs to.
    /// </remarks>
    public static bool IsDark(FrameworkElement element)
        => element.TryFindResource("ApplicationBackgroundBrush") is SolidColorBrush { Color: var c }
           && IsDark(c);

    /// <summary>Whether a background is dark enough to want the pale disc on it.</summary>
    internal static bool IsDark(Color background)
        => (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255 < 0.5;

    private static string FileFor(FrameworkElement element) => IsDark(element) ? Dark : Light;

    /// <summary>The icon for the window's title bar and its taskbar button.</summary>
    public static ImageSource Image(FrameworkElement element)
        => BitmapFrame.Create(Uri(FileFor(element)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <summary>
    /// An HICON at the size the notification area asks for. The caller owns it and must
    /// destroy it; <see cref="TrayIcon"/> does.
    /// </summary>
    public static IntPtr CreateHandle(FrameworkElement element, int cx, int cy)
        => FromIcoBytes(Read(FileFor(element)), cx, cy);

    private static Uri Uri(string file) => new($"pack://application:,,,/{file}", UriKind.Absolute);

    private static byte[] Read(string file)
    {
        var stream = Application.GetResourceStream(Uri(file))?.Stream
                     ?? throw new FileNotFoundException($"{file} is not a resource of this assembly.");
        using (stream)
        {
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }

    /// <summary>
    /// The frame of a .ico closest to the width asked for: its own width, and where its
    /// bytes are. Width zero in the file means 256, which is as large as the format can say.
    /// </summary>
    internal static (int Width, int Offset, int Length) Frame(byte[] ico, int cx)
    {
        var count = BitConverter.ToUInt16(ico, 4);
        var best = -1;
        var bestWidth = 0;
        var bestDistance = int.MaxValue;

        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var width = ico[entry] == 0 ? 256 : ico[entry];
            var distance = Math.Abs(width - cx);
            // A tie goes to the larger frame: shrinking a frame looks better than stretching
            // one, and at 18px the shell is equally far from the 16 and the 20.
            if (distance > bestDistance || (distance == bestDistance && width <= bestWidth)) continue;
            bestDistance = distance;
            bestWidth = width;
            best = entry;
        }

        return best < 0
            ? (0, 0, 0)
            : (bestWidth, BitConverter.ToInt32(ico, best + 12), BitConverter.ToInt32(ico, best + 8));
    }

    /// <summary>
    /// Turns one frame of a .ico into an HICON, which is what Shell_NotifyIcon wants.
    /// </summary>
    /// <remarks>
    /// By hand because the alternative is System.Drawing.Common, a whole package for one
    /// call, and because the file is this app's own: tools\make-icon.ps1 writes it. The
    /// layout is ICONDIR, then one ICONDIRENTRY per frame, then the frames.
    /// </remarks>
    private static IntPtr FromIcoBytes(byte[] ico, int cx, int cy)
    {
        var (_, offset, length) = Frame(ico, cx);
        if (length == 0) return IntPtr.Zero;

        var frame = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.Copy(ico, offset, frame, length);
            // 0x00030000 is the icon format version the API expects; true means an icon
            // rather than a cursor.
            return CreateIconFromResourceEx(frame, (uint)length, true, 0x00030000, cx, cy, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(frame);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        IntPtr bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version,
        int cx, int cy, uint flags);
}
