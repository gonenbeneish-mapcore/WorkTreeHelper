using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WorktreeHelper;

/// <summary>
/// Where the taskbar drew the window's button, for the minimise flight to aim at.
/// </summary>
/// <remarks>
/// Windows has no API for this; the taskbar's UI Automation tree is the one place it is
/// written down, which is how the Windhawk animation mods find it too. On Windows 11 each
/// button that stands for a single window says which in its AutomationId — "Window: 0x…",
/// the handle in hex — so the match is exact rather than by a title that changes. A button
/// standing for a group of windows says "Appid: …" instead, and is matched by name.
/// Measured at about 25 ms a lookup once warm, 120 ms the first time.
/// </remarks>
internal static class TaskbarButton
{
    /// <summary>The AutomationId the taskbar gives a single window's button.</summary>
    internal static string IdFor(IntPtr hwnd) => $"Window: 0x{hwnd.ToInt64():x}";

    /// <summary>Whether a button is the window's: by its handle, or its group's by name.</summary>
    internal static bool IsFor(string? automationId, string? className, string? name, IntPtr hwnd, string appName)
    {
        if (automationId == IdFor(hwnd)) return true;
        // Only taskbar buttons, never a notification-area icon carrying the same name.
        return automationId?.StartsWith("Appid:", StringComparison.Ordinal) == true
            && className?.Contains("TaskListButton", StringComparison.Ordinal) == true
            && name?.StartsWith(appName, StringComparison.Ordinal) == true;
    }

    private static readonly object Gate = new();
    private static ScreenRect? _last;
    private static IntPtr _lastFor;

    /// <summary>
    /// The button, looked up now if that is quick enough, otherwise where it was last seen.
    /// Null when it cannot be found at all, and then Windows animates the minimise itself.
    /// </summary>
    public static async Task<ScreenRect?> FindAsync(IntPtr hwnd, string appName, TimeSpan wait)
    {
        var lookup = Task.Run(() => Find(hwnd, appName));
        if (await Task.WhenAny(lookup, Task.Delay(wait)) == lookup && lookup.Result is { } found) return found;
        return Remembered(hwnd);
    }

    /// <summary>Where the button was last seen, without asking again.</summary>
    public static ScreenRect? Remembered(IntPtr hwnd)
    {
        lock (Gate) return _lastFor == hwnd ? _last : null;
    }

    /// <summary>Looks the button up in the background, so the next flight has it to hand.</summary>
    public static void Refresh(IntPtr hwnd, string appName) => _ = Task.Run(() => Find(hwnd, appName));

    private static ScreenRect? Find(IntPtr hwnd, string appName)
    {
        try
        {
            var home = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            ScreenRect? best = null;
            foreach (var bar in Taskbars())
            {
                var onHome = MonitorFromWindow(bar, MONITOR_DEFAULTTONEAREST) == home;
                var buttons = AutomationElement.FromHandle(bar).FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement button in buttons)
                {
                    var current = button.Current;
                    if (!IsFor(current.AutomationId, current.ClassName, current.Name, hwnd, appName)) continue;
                    var r = current.BoundingRectangle;
                    if (r.IsEmpty || r.Width <= 0) continue;
                    var rect = new ScreenRect((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
                    // With buttons on every taskbar, the one on the window's own screen.
                    if (onHome) { best = rect; break; }
                    best ??= rect;
                }
                if (best is not null && onHome) break;
            }

            lock (Gate)
            {
                _last = best;
                _lastFor = hwnd;
            }
            return best;
        }
        catch (Exception)
        {
            // Explorer restarting, a taskbar being rebuilt: there is just no target this time.
            return null;
        }
    }

    /// <summary>The main taskbar and those on other screens.</summary>
    private static IEnumerable<IntPtr> Taskbars()
    {
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var other = IntPtr.Zero;
        while ((other = FindWindowEx(IntPtr.Zero, other, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return other;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
