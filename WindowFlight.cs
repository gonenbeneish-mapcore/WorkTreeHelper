using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace WorktreeHelper;

/// <summary>A rectangle in physical screen pixels, laid out as Win32's RECT.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ScreenRect
{
    public int Left, Top, Right, Bottom;

    public ScreenRect(int left, int top, int right, int bottom)
        => (Left, Top, Right, Bottom) = (left, top, right, bottom);

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool IsEmpty => Width <= 0 || Height <= 0;
    public readonly double CenterX => (Left + Right) / 2.0;
    public readonly double CenterY => (Top + Bottom) / 2.0;

    public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom})";
}

/// <summary>
/// A picture of the window flying between where it stands and where it is put away to: its
/// taskbar button, or its icon in the notification area.
/// </summary>
/// <remarks>
/// <para>
/// Windows draws the minimise animation itself and gives an application no say in where it
/// goes; on this taskbar it was measured heading for the middle rather than for the button.
/// Hiding to the tray has no animation at all. So for the trip the window's own transitions
/// are switched off and this draws one instead — the approach of ReDrag's Windows Animations
/// and Abdullah Masood's macOS Minimize Animation mods for Windhawk, cut down to one window
/// that is our own: a PrintWindow snapshot, in a layered, click-through, topmost window of
/// its own, redrawn smaller and further along once per composition frame.
/// </para>
/// <para>
/// What makes it smooth rather than merely moving: the picture is prepared once — rounded,
/// antialiased corners and a soft shadow drawn in as transparency, then halved and halved
/// again — so each frame is only a bilinear resample from the copy just larger than it needs,
/// handed to the compositor whole. Every frame is drawn for the moment it will be on screen,
/// one refresh ahead, and the curve eases in and out so no frame has to jump.
/// </para>
/// <para>
/// All rectangles are physical screen pixels: the app is per-monitor DPI aware, so window
/// rectangles, UI Automation's and the notification area's all agree.
/// </para>
/// </remarks>
internal static class WindowFlight
{
    /// <summary>A little longer than Windows' own minimise, which is gone before it is seen.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Whether animations are wanted at all. Windows' own minimise follows the first, and
    /// Settings' "Animation effects" switch turns off the second along with it.
    /// </summary>
    public static bool Wanted => SystemParameters.MinimizeAnimation && SystemParameters.ClientAreaAnimation;

    // ---- the path (pure, and tested) -------------------------------------------------

    /// <summary>
    /// How small the picture gets at the far end: about an icon's width, centred on the target,
    /// keeping the window's proportions so it reads as the window shrinking rather than a box.
    /// </summary>
    internal static ScreenRect EndRect(ScreenRect window, ScreenRect target)
    {
        var width = Math.Max(8, (int)Math.Round(Math.Min(target.Width, target.Height) * 0.8));
        var height = Math.Max(4, (int)Math.Round(width * (double)window.Height / Math.Max(1, window.Width)));
        var left = (int)Math.Round(target.CenterX - width / 2.0);
        var top = (int)Math.Round(target.CenterY - height / 2.0);
        return new ScreenRect(left, top, left + width, top + height);
    }

    /// <summary>
    /// How far along the path at time <paramref name="t"/>: slow away, fastest midway, slow
    /// in. A sine rather than a cube: at its fastest it moves about one and a half times its
    /// average, where a cubed ease-in reached three times it in the last frames and seemed to
    /// jump into the button.
    /// </summary>
    internal static double Progress(double t) => 0.5 - 0.5 * Math.Cos(Math.PI * Math.Clamp(t, 0, 1));

    /// <summary>
    /// Where the picture's window part is, its scale against the window, and how opaque it is,
    /// a fraction <paramref name="t"/> of the way through. It thins only over the last stretch
    /// into the target — or, coming back, the first stretch out of it.
    /// </summary>
    internal static (ScreenRect Rect, double Scale, byte Alpha) Frame(ScreenRect window, ScreenRect target, double t, bool away)
    {
        var end = EndRect(window, target);
        // p is how far from the window towards the target.
        var p = away ? Progress(t) : 1 - Progress(t);
        var width = window.Width + (end.Width - window.Width) * p;
        var height = window.Height + (end.Height - window.Height) * p;
        var centerX = window.CenterX + (end.CenterX - window.CenterX) * p;
        var centerY = window.CenterY + (end.CenterY - window.CenterY) * p;
        var left = (int)Math.Round(centerX - width / 2);
        var top = (int)Math.Round(centerY - height / 2);
        var rect = new ScreenRect(left, top, left + (int)Math.Round(width), top + (int)Math.Round(height));

        var fade = Math.Clamp((p - 0.65) / 0.35, 0, 1);
        var alpha = (byte)Math.Round(255 * (1 - 0.9 * fade * fade));
        return (rect, width / Math.Max(1, window.Width), alpha);
    }

    /// <summary>How long a window takes to open out, and to close back in.</summary>
    public static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// A window opening out of the button that asked for it, or closing back into it. It keeps
    /// its own proportions the whole way, at an icon's width to start with, so it reads as the
    /// window growing rather than a box stretching. Opening, it comes forward fast, grows a
    /// touch past its size and settles back — about three per cent, enough to feel sprung
    /// without wobbling — and is solid within the first third. Closing, it gathers speed back
    /// into the button and thins out over the last stretch, as the minimise does.
    /// </summary>
    internal static (ScreenRect Rect, double Scale, byte Alpha) PopFrame(ScreenRect window, ScreenRect origin, double t, bool opening)
    {
        t = Math.Clamp(t, 0, 1);
        var start = StartSize(window);
        double size, move, alpha;
        if (opening)
        {
            size = EaseOutBack(t);
            move = 1 - Math.Pow(1 - t, 3);
            alpha = Math.Clamp(t / 0.3, 0, 1);
        }
        else
        {
            size = move = 1 - t * t;
            alpha = 1 - Math.Clamp((t - 0.55) / 0.45, 0, 1);
        }

        var width = start.Width + (window.Width - start.Width) * size;
        var height = width * window.Height / window.Width;
        var centerX = origin.CenterX + (window.CenterX - origin.CenterX) * move;
        var centerY = origin.CenterY + (window.CenterY - origin.CenterY) * move;
        var left = (int)Math.Round(centerX - width / 2);
        var top = (int)Math.Round(centerY - height / 2);
        var rect = new ScreenRect(left, top, left + (int)Math.Round(width), top + (int)Math.Round(height));
        return (rect, width / window.Width, (byte)Math.Round(255 * alpha));
    }

    /// <summary>How big the window is at the button: an icon's width, its own shape.</summary>
    private static (double Width, double Height) StartSize(ScreenRect window)
    {
        var width = Math.Max(24, window.Width * 0.08);
        return (width, width * window.Height / Math.Max(1, window.Width));
    }

    /// <summary>
    /// Ease out with a small overshoot: past 1 by about three per cent near two thirds of the
    /// way, then back. The usual curve's constant is 1.70158, a tenth too far for a window.
    /// </summary>
    internal static double EaseOutBack(double t)
    {
        const double c1 = 0.9, c3 = c1 + 1;
        var u = t - 1;
        return 1 + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>
    /// Where the ghost for a pop has to reach: the window grown by its overshoot, and the
    /// button it comes out of.
    /// </summary>
    internal static ScreenRect PopBounds(ScreenRect window, ScreenRect origin)
    {
        var grow = (int)Math.Ceiling(Math.Max(window.Width, window.Height) * 0.03) + 2;
        return new ScreenRect(
            Math.Min(window.Left - grow, origin.Left), Math.Min(window.Top - grow, origin.Top),
            Math.Max(window.Right + grow, origin.Right), Math.Max(window.Bottom + grow, origin.Bottom));
    }

    /// <summary>
    /// A window opening or closing where it stands, the way Windows 11's own do: from a
    /// little smaller and clear to its full size and solid, settling as it arrives; closing,
    /// the same the other way, gathering speed as it goes. Faded in ahead of its growing, so
    /// it is readable well before it has finished.
    /// </summary>
    internal static (ScreenRect Rect, double Scale, byte Alpha) ZoomFrame(ScreenRect window, double t, bool opening)
    {
        t = Math.Clamp(t, 0, 1);
        // q is how far the window has come into being: 0 small and clear, 1 itself.
        var q = opening ? Math.Sin(t * Math.PI / 2) : Math.Cos(t * Math.PI / 2);
        var scale = 0.94 + 0.06 * q;
        var width = window.Width * scale;
        var height = window.Height * scale;
        var left = (int)Math.Round(window.CenterX - width / 2);
        var top = (int)Math.Round(window.CenterY - height / 2);
        var rect = new ScreenRect(left, top, left + (int)Math.Round(width), top + (int)Math.Round(height));
        var alpha = (byte)Math.Round(255 * Math.Clamp(opening ? q / 0.7 : q, 0, 1));
        return (rect, scale, alpha);
    }

    // ---- the picture (pure, and tested) -----------------------------------------------

    /// <summary>
    /// Premultiplied BGRA pixels, as UpdateLayeredWindow takes them: one int each, alpha in
    /// the top byte.
    /// </summary>
    internal sealed record Image(int Width, int Height, int[] Pixels);

    /// <summary>How the picture is framed: corners and shadow, in physical pixels.</summary>
    internal readonly record struct Look(double Radius, double ShadowBlur, double ShadowDrop, double ShadowAlpha)
    {
        /// <summary>Windows 11's own window corners and a shadow about as soft as its own.</summary>
        public static Look For(uint dpi)
        {
            var s = (dpi == 0 ? 96 : dpi) / 96.0;
            return new Look(Radius: 8 * s, ShadowBlur: 14 * s, ShadowDrop: 3 * s, ShadowAlpha: 0.32);
        }

        /// <summary>The space round the window the shadow needs.</summary>
        public int Margin => (int)Math.Ceiling(ShadowBlur + ShadowDrop);
    }

    /// <summary>
    /// The window's pixels with its corners rounded off and antialiased, sitting on a soft
    /// shadow, in a canvas <see cref="Look.Margin"/> larger on every side. Drawn by distance to
    /// a rounded rectangle, so it needs no blur pass and every edge is smooth.
    /// </summary>
    internal static Image Compose(int[] window, int width, int height, Look look)
    {
        var margin = look.Margin;
        var w = width + 2 * margin;
        var h = height + 2 * margin;
        var pixels = new int[w * h];
        var radius = Math.Min(look.Radius, Math.Min(width, height) / 2.0);

        for (var y = 0; y < h; y++)
        {
            var py = y + 0.5 - margin;
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var px = x + 0.5 - margin;
                double cover;
                // Most of the picture is plainly inside: no corner and no edge to work out.
                var inside = px >= 0.5 && px <= width - 0.5 && py >= 0.5 && py <= height - 0.5
                    && !((px < radius || px > width - radius) && (py < radius || py > height - radius));
                cover = inside ? 1 : Math.Clamp(0.5 - RoundedRectDistance(px, py, width, height, radius), 0, 1);

                var shadow = 0.0;
                if (cover < 1)
                {
                    var d = RoundedRectDistance(px, py - look.ShadowDrop, width, height, radius);
                    shadow = look.ShadowAlpha * (1 - SmoothStep(-look.ShadowBlur * 0.25, look.ShadowBlur, d));
                }

                if (cover <= 0)
                {
                    pixels[row + x] = (int)Math.Round(shadow * 255) << 24;
                    continue;
                }

                var sx = Math.Clamp(x - margin, 0, width - 1);
                var sy = Math.Clamp(y - margin, 0, height - 1);
                var c = window[sy * width + sx];
                if (cover >= 1)
                {
                    pixels[row + x] = c | unchecked((int)0xFF000000);
                    continue;
                }

                // Window over shadow, premultiplied: the colour scaled by its coverage, the
                // alpha the two together.
                var alpha = cover + shadow * (1 - cover);
                var r = (int)Math.Round(((c >> 16) & 0xFF) * cover);
                var g = (int)Math.Round(((c >> 8) & 0xFF) * cover);
                var b = (int)Math.Round((c & 0xFF) * cover);
                pixels[row + x] = ((int)Math.Round(alpha * 255) << 24) | (r << 16) | (g << 8) | b;
            }
        }
        return new Image(w, h, pixels);
    }

    /// <summary>Signed distance from a point to a rounded rectangle at the origin: negative inside.</summary>
    internal static double RoundedRectDistance(double px, double py, double width, double height, double radius)
    {
        var qx = Math.Abs(px - width / 2) - (width / 2 - radius);
        var qy = Math.Abs(py - height / 2) - (height / 2 - radius);
        var outside = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2));
        var inside = Math.Min(Math.Max(qx, qy), 0);
        return outside + inside - radius;
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The picture at half the size, each pixel the average of the four it covers.</summary>
    internal static Image Half(Image source)
    {
        var w = Math.Max(1, source.Width / 2);
        var h = Math.Max(1, source.Height / 2);
        var pixels = new int[w * h];
        var src = source.Pixels;
        var stride = source.Width;
        for (var y = 0; y < h; y++)
        {
            var r0 = 2 * y * stride;
            var r1 = Math.Min(2 * y + 1, source.Height - 1) * stride;
            for (var x = 0; x < w; x++)
            {
                var x0 = 2 * x;
                var x1 = Math.Min(2 * x + 1, source.Width - 1);
                pixels[y * w + x] = Average(src[r0 + x0], src[r0 + x1], src[r1 + x0], src[r1 + x1]);
            }
        }
        return new Image(w, h, pixels);
    }

    private static int Average(int a, int b, int c, int d)
    {
        var result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var sum = ((a >> shift) & 0xFF) + ((b >> shift) & 0xFF) + ((c >> shift) & 0xFF) + ((d >> shift) & 0xFF);
            result |= ((sum + 2) >> 2) << shift;
        }
        return result;
    }

    /// <summary>
    /// The picture, then halved again and again: each frame is drawn from the copy just larger
    /// than it, which a bilinear sample can shrink without the shimmer of skipping pixels.
    /// </summary>
    internal static List<Image> Halvings(Image full)
    {
        var levels = new List<Image> { full };
        while (levels[^1].Width > 24 && levels[^1].Height > 12) levels.Add(Half(levels[^1]));
        return levels;
    }

    /// <summary>
    /// Draws <paramref name="source"/> scaled to <paramref name="width"/> by
    /// <paramref name="height"/> into the top-left of <paramref name="target"/>, a canvas
    /// <paramref name="stride"/> pixels wide. Bilinear, in 8-bit fixed point.
    /// </summary>
    internal static void Resample(Image source, int[] target, int stride, int width, int height)
    {
        var src = source.Pixels;
        var sw = source.Width;
        var sh = source.Height;
        if (width == sw && height == sh)
        {
            for (var y = 0; y < height; y++) Array.Copy(src, y * sw, target, y * stride, sw);
            return;
        }

        var stepX = (double)sw / width;
        var stepY = (double)sh / height;
        var xs0 = new int[width];
        var xs1 = new int[width];
        var xw = new int[width];
        for (var x = 0; x < width; x++)
        {
            var fx = Math.Max(0, (x + 0.5) * stepX - 0.5);
            var ix = Math.Min((int)fx, sw - 1);
            xs0[x] = ix;
            xs1[x] = Math.Min(ix + 1, sw - 1);
            xw[x] = (int)((fx - ix) * 256);
        }

        // A near-full-size frame is most of a megapixel: split by rows across a few cores, so
        // the first frames, the largest, are as quick as the rest.
        if ((long)width * height > 120_000)
        {
            var bands = Math.Clamp(Environment.ProcessorCount, 1, 4);
            Parallel.For(0, bands, new ParallelOptions { MaxDegreeOfParallelism = bands },
                band => ResampleRows(src, sw, sh, target, stride, width, stepY, xs0, xs1, xw,
                    height * band / bands, height * (band + 1) / bands));
        }
        else
        {
            ResampleRows(src, sw, sh, target, stride, width, stepY, xs0, xs1, xw, 0, height);
        }
    }

    private static void ResampleRows(int[] src, int sw, int sh, int[] target, int stride, int width, double stepY,
        int[] xs0, int[] xs1, int[] xw, int fromRow, int toRow)
    {
        for (var y = fromRow; y < toRow; y++)
        {
            var fy = Math.Max(0, (y + 0.5) * stepY - 0.5);
            var iy = Math.Min((int)fy, sh - 1);
            var row0 = iy * sw;
            var row1 = Math.Min(iy + 1, sh - 1) * sw;
            var wy = (int)((fy - iy) * 256);
            var outRow = y * stride;
            for (var x = 0; x < width; x++)
            {
                var wx = xw[x];
                var a = src[row0 + xs0[x]];
                var b = src[row0 + xs1[x]];
                var c = src[row1 + xs0[x]];
                var d = src[row1 + xs1[x]];
                var result = 0;
                for (var shift = 0; shift < 32; shift += 8)
                {
                    var top = (((a >> shift) & 0xFF) << 8) + ((((b >> shift) & 0xFF) - ((a >> shift) & 0xFF)) * wx);
                    var bottom = (((c >> shift) & 0xFF) << 8) + ((((d >> shift) & 0xFF) - ((c >> shift) & 0xFF)) * wx);
                    var value = ((top << 8) + (bottom - top) * wy + 32768) >> 16;
                    result |= (value & 0xFF) << shift;
                }
                target[outRow + x] = result;
            }
        }
    }

    // ---- the window's own transitions ---------------------------------------------------

    /// <summary>
    /// Switches Windows' own minimise, restore, show and hide animations for the window off or
    /// back on. Off while the flight stands in for them, or they would play as well.
    /// </summary>
    public static void SetSystemTransitions(IntPtr hwnd, bool on)
    {
        var value = on ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
    }

    /// <summary>
    /// Keeps the window off the screen while it is, as far as everything else is concerned,
    /// shown: so it can be restored, laid out and painted underneath the picture flying in,
    /// and appear exactly as the picture lands.
    /// </summary>
    public static void Cloak(IntPtr hwnd, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref value, sizeof(int));
    }

    // ---- the snapshot ---------------------------------------------------------------------

    /// <summary>The window as it last looked, kept for the flight back.</summary>
    public sealed class Snapshot
    {
        internal Snapshot(ScreenRect bounds, uint dpi, int[] pixels) => (Bounds, Dpi, Pixels) = (bounds, dpi, pixels);

        /// <summary>Where the window was when it was taken.</summary>
        public ScreenRect Bounds { get; }
        internal uint Dpi { get; }
        internal int[] Pixels { get; }

        /// <summary>Prepared on the first flight and kept for the flight back.</summary>
        internal List<Image>? Levels { get; set; }
        internal int Margin { get; set; }
    }

    /// <summary>
    /// The window drawn by itself, so nothing on top of it gets into the picture. Null if
    /// there is nothing to take, and then there is no flight: Windows does what it always did.
    /// </summary>
    public static Snapshot? Capture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds) || bounds.IsEmpty) return null;
        var (width, height) = (bounds.Width, bounds.Height);

        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        ReleaseDC(IntPtr.Zero, screen);
        var bitmap = CreateTopDownDib(dc, width, height, out var bits);
        if (dc == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            return null;
        }

        var old = SelectObject(dc, bitmap);
        try
        {
            if (!PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)) return null;
            GdiFlush();
            var pixels = new int[width * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            // A window with a frame of its own, as Options has, reaches past what is drawn:
            // its rectangle takes in an invisible border that would come out as a dark rim.
            // What DWM actually shows is the extended frame; the picture is cut to that.
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var shown, Marshal.SizeOf<ScreenRect>()) == 0
                && !shown.IsEmpty && shown.Left >= bounds.Left && shown.Top >= bounds.Top
                && shown.Right <= bounds.Right && shown.Bottom <= bounds.Bottom
                && (shown.Width != width || shown.Height != height))
            {
                var cropped = new int[shown.Width * shown.Height];
                for (var y = 0; y < shown.Height; y++)
                    Array.Copy(pixels, (shown.Top - bounds.Top + y) * width + (shown.Left - bounds.Left), cropped, y * shown.Width, shown.Width);
                return new Snapshot(shown, GetDpiForWindow(hwnd), cropped);
            }
            return new Snapshot(bounds, GetDpiForWindow(hwnd), pixels);
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    private static IntPtr CreateTopDownDib(IntPtr dc, int width, int height, out IntPtr bits)
    {
        var info = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down, so row 0 is the top
            biPlanes = 1,
            biBitCount = 32,
        };
        return CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
    }

    // ---- the flight -------------------------------------------------------------------------

    /// <summary>One trip of the picture, on a thread of its own.</summary>
    public sealed class Flight
    {
        private readonly TaskCompletionSource _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();

        /// <summary>The picture is on screen, over the window: the window can now go.</summary>
        public Task Shown => _shown.Task;

        /// <summary>The picture has reached the end and is holding its last frame.</summary>
        public Task Arrived => _arrived.Task;

        /// <summary>
        /// Takes the picture away. Coming back, only once the window is showing beneath it,
        /// so there is no frame with neither.
        /// </summary>
        public void Release() => _release.Set();

        /// <summary>How long each frame took to draw, for judging smoothness.</summary>
        public List<double> FrameTimes { get; } = new();

        /// <summary>When each frame went up, from the first.</summary>
        public List<double> FrameAt { get; } = new();

        internal void Run(Snapshot shot, Func<double, (ScreenRect Rect, double Scale, byte Alpha)> path, TimeSpan duration, string label)
        {
            var ghost = IntPtr.Zero;
            var frameDc = IntPtr.Zero;
            var frameBitmap = IntPtr.Zero;
            var oldFrame = IntPtr.Zero;
            try
            {
                var window = shot.Bounds;
                if (shot.Levels is null)
                {
                    var look = Look.For(shot.Dpi);
                    shot.Levels = Halvings(Compose(shot.Pixels, window.Width, window.Height, look));
                    shot.Margin = look.Margin;
                }
                var levels = shot.Levels;
                var full = levels[0];
                var margin = shot.Margin;

                // The built-in Static class, so there is nothing to register: a layered window
                // updated through UpdateLayeredWindow never paints itself anyway.
                ghost = CreateWindowEx(
                    WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    "Static", null, WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (ghost == IntPtr.Zero) return;

                var screen = GetDC(IntPtr.Zero);
                frameDc = CreateCompatibleDC(screen);
                ReleaseDC(IntPtr.Zero, screen);
                frameBitmap = CreateTopDownDib(frameDc, full.Width, full.Height, out var frameBits);
                if (frameBitmap == IntPtr.Zero) return;
                oldFrame = SelectObject(frameDc, frameBitmap);
                var canvas = new int[full.Width * full.Height];

                var clock = new Stopwatch();
                var lookAhead = 0.0;
                var lastFlush = 0.0;
                var first = true;
                while (true)
                {
                    // Drawn for when it will be seen: the next refresh, a frame from now.
                    var t = first ? 0 : (clock.Elapsed.TotalMilliseconds + lookAhead) / duration.TotalMilliseconds;
                    var drawStart = Stopwatch.GetTimestamp();
                    var (rect, scale, alpha) = path(t);

                    var width = Math.Max(1, (int)Math.Round(full.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(full.Height * scale));
                    var level = levels[0];
                    foreach (var candidate in levels)
                    {
                        if (candidate.Width < width || candidate.Height < height) break;
                        level = candidate;
                    }
                    Resample(level, canvas, full.Width, width, height);
                    Marshal.Copy(canvas, 0, frameBits, full.Width * height);
                    GdiFlush();

                    var offset = (int)Math.Round(margin * scale);
                    var position = new POINT { X = rect.Left - offset, Y = rect.Top - offset };
                    var size = new SIZE { cx = width, cy = height };
                    var origin = new POINT();
                    var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
                    UpdateLayeredWindow(ghost, IntPtr.Zero, ref position, ref size, frameDc, ref origin, 0, ref blend, ULW_ALPHA);
                    FrameTimes.Add(Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds);
                    FrameAt.Add(clock.Elapsed.TotalMilliseconds);

                    if (first)
                    {
                        ShowWindow(ghost, SW_SHOWNOACTIVATE);
                        first = false;
                        _shown.TrySetResult();
                        clock.Start();
                    }
                    if (t >= 1) break;

                    Pump();
                    // One frame per composition pass, so the picture moves as smoothly as DWM can.
                    if (DwmFlush() != 0) Thread.Sleep(8);
                    var now = clock.Elapsed.TotalMilliseconds;
                    lookAhead = Math.Clamp(now - lastFlush, 4, 34);
                    lastFlush = now;
                }

                _arrived.TrySetResult();
                // Hold the last frame until told; never longer than a moment, whatever happens.
                while (!_release.Wait(10)) { Pump(); if (clock.Elapsed > duration + TimeSpan.FromSeconds(1)) break; }
            }
            finally
            {
                _shown.TrySetResult();
                _arrived.TrySetResult();
                if (oldFrame != IntPtr.Zero) SelectObject(frameDc, oldFrame);
                if (frameBitmap != IntPtr.Zero) DeleteObject(frameBitmap);
                if (frameDc != IntPtr.Zero) DeleteDC(frameDc);
                if (ghost != IntPtr.Zero) DestroyWindow(ghost);
                if (Environment.GetEnvironmentVariable("WORKTREEHELPER_FLIGHTLOG") is { Length: > 0 } log)
                    try
                    {
                        File.AppendAllText(log, $"{label} draw: {string.Join(" ", FrameTimes.Select(f => f.ToString("0.0")))}\n"
                            + $"{label} at:   {string.Join(" ", FrameAt.Select(f => f.ToString("0")))}\n");
                    }
                    catch (IOException) { }
            }
        }

        internal void RunLive(IntPtr source, bool opening, TimeSpan duration, ScreenRect? origin)
        {
            var ghost = IntPtr.Zero;
            var thumbnail = IntPtr.Zero;
            var clock = new Stopwatch();
            try
            {
                if (!GetWindowRect(source, out var window) || window.IsEmpty) return;
                // The ghost spans the whole trip, so the picture never runs off its edge.
                var bounds = origin is { } from ? PopBounds(window, from) : window;
                Func<double, (ScreenRect Rect, double Scale, byte Alpha)> path = origin is { } o
                    ? t => PopFrame(window, o, t, opening)
                    : t => ZoomFrame(window, t, opening);

                // No surface of its own at all: all there is to see is the thumbnail on it.
                ghost = CreateWindowEx(
                    WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    "Static", null, WS_POPUP, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (ghost == IntPtr.Zero || DwmRegisterThumbnail(ghost, source, out thumbnail) != 0) return;

                var lookAhead = 0.0;
                var lastFlush = 0.0;
                var first = true;
                while (true)
                {
                    var t = first ? 0 : (clock.Elapsed.TotalMilliseconds + lookAhead) / duration.TotalMilliseconds;
                    var drawStart = Stopwatch.GetTimestamp();
                    var (rect, _, alpha) = path(t);
                    var properties = new DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
                        rcDestination = new ScreenRect(rect.Left - bounds.Left, rect.Top - bounds.Top,
                            rect.Right - bounds.Left, rect.Bottom - bounds.Top),
                        opacity = alpha,
                        fVisible = true,
                        fSourceClientAreaOnly = false,
                    };
                    DwmUpdateThumbnailProperties(thumbnail, ref properties);
                    FrameTimes.Add(Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds);
                    FrameAt.Add(clock.Elapsed.TotalMilliseconds);

                    if (first)
                    {
                        ShowWindow(ghost, SW_SHOWNOACTIVATE);
                        first = false;
                        _shown.TrySetResult();
                        clock.Start();
                    }
                    if (t >= 1) break;

                    Pump();
                    if (DwmFlush() != 0) Thread.Sleep(8);
                    var now = clock.Elapsed.TotalMilliseconds;
                    lookAhead = Math.Clamp(now - lastFlush, 4, 34);
                    lastFlush = now;
                }

                _arrived.TrySetResult();
                while (!_release.Wait(10)) { Pump(); if (clock.Elapsed > duration + TimeSpan.FromSeconds(1)) break; }
            }
            finally
            {
                _shown.TrySetResult();
                _arrived.TrySetResult();
                if (thumbnail != IntPtr.Zero) DwmUnregisterThumbnail(thumbnail);
                if (ghost != IntPtr.Zero) DestroyWindow(ghost);
                if (Environment.GetEnvironmentVariable("WORKTREEHELPER_FLIGHTLOG") is { Length: > 0 } log)
                    try
                    {
                        var label = opening ? "open (live)" : "close (live)";
                        File.AppendAllText(log, $"{label} draw: {string.Join(" ", FrameTimes.Select(f => f.ToString("0.0")))}\n"
                            + $"{label} at:   {string.Join(" ", FrameAt.Select(f => f.ToString("0")))}\n");
                    }
                    catch (IOException) { }
            }
        }

        private static void Pump()
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
    }

    /// <summary>
    /// Starts the picture flying from the window to the target (<paramref name="away"/>) or
    /// back from the target to where the window was.
    /// </summary>
    public static Flight Fly(Snapshot shot, ScreenRect target, bool away)
        => Start(shot, t => Frame(shot.Bounds, target, t, away), Duration, away ? "away" : "back");

    /// <summary>
    /// Opens or closes a window where it stands by having the compositor draw it, live and
    /// scaled, in a window of our own: a DWM thumbnail, the way Alt+Tab and Task View draw
    /// windows. Nothing is copied or uploaded a frame, so a window as tall as Options moves as
    /// smoothly as a small one — the picture path, which hands the compositor a megapixel each
    /// frame, managed half the frame rate for it. The window must be shown, and may be cloaked:
    /// the compositor keeps drawing a cloaked window, which is how Task View shows the windows
    /// of other desktops.
    /// </summary>
    /// <param name="origin">
    /// The button it opens out of and closes back into; without one it grows in place.
    /// </param>
    public static Flight ZoomLive(IntPtr source, bool opening, ScreenRect? origin = null)
    {
        var flight = new Flight();
        var duration = opening ? OpenDuration : CloseDuration;
        var thread = new Thread(() => flight.RunLive(source, opening, duration, origin))
        {
            IsBackground = true,
            Name = "Window zoom",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        return flight;
    }

    private static Flight Start(Snapshot shot, Func<double, (ScreenRect, double, byte)> path, TimeSpan duration, string label)
    {
        var flight = new Flight();
        var thread = new Thread(() => flight.Run(shot, path, duration, label))
        {
            IsBackground = true,
            Name = "Window flight",
            // Ahead of everything else the app is doing, which is little while it flies.
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        return flight;
    }

    // ---- Win32 ----------------------------------------------------------------------------

    private const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const uint DWMWA_CLOAK = 13;
    private const uint PW_RENDERFULLCONTENT = 2;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOPMOST = 0x00000008,
        WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNOACTIVATE = 4;
    private const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    private const int ULW_ALPHA = 2;
    private const uint PM_REMOVE = 1;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out ScreenRect value, int size);
    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8,
        DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public ScreenRect rcDestination;
        public ScreenRect rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out ScreenRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size,
        IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
}
