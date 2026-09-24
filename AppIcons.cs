using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorktreeHelper;

/// <summary>The icons of the programs the row's buttons open, as Windows itself shows them.</summary>
/// <remarks>
/// Asked of the shell rather than pulled out of the exe, because the shell also knows the
/// icon of a packaged app such as Windows Terminal, whose exe cannot be read from outside,
/// and hands every icon back with its transparency intact.
/// </remarks>
internal static class AppIcons
{
    /// <summary>Pixels asked for: sharp at the 16 DIPs a button shows, up to 200% scaling.</summary>
    private const int Size = 32;

    /// <summary>The icon of an exe, or null where there is none to be had.</summary>
    public static ImageSource? OfFile(string? path) => path is { Length: > 0 } ? Of(path) : null;

    /// <summary>
    /// The terminal button's icon: Windows Terminal where it is installed, since that is what
    /// the button opens, and PowerShell, which it falls back to, where it is not.
    /// </summary>
    public static ImageSource? OfTerminal()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(alias))
            return Of(@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App") ?? Of(alias);

        return OfFile(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
    }

    public static ImageSource? OfExplorer()
        => OfFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));

    /// <summary>Asks the shell for the icon of anything it can name: a path, or an app.</summary>
    private static ImageSource? Of(string parsingName)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory);
            factory.GetImage(new SIZE { cx = Size, cy = Size }, SIIGBF_ICONONLY, out bitmap);
            return ToImage(bitmap);
        }
        catch
        {
            // No such file or app, or nothing the shell can draw for it: the button keeps
            // its own mark.
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    /// <summary>
    /// Copies the shell's bitmap out pixel by pixel. WPF's own conversion of an HBITMAP drops
    /// the alpha channel, which would put every icon on a black square.
    /// </summary>
    private static ImageSource? ToImage(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0) return null;
        int width = info.bmWidth, height = info.bmHeight;

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top row first, the order WPF wants
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        // The shell's alpha is already multiplied into the colour.
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private const int SIIGBF_ICONONLY = 0x4;

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits,
        ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
