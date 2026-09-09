using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WorktreeHelper;

/// <summary>
/// Finds VS Code windows by the folder they have open, so a worktree that is already
/// open can be focused instead of opened again.
/// </summary>
/// <remarks>
/// VS Code exposes no way to ask "which folders are open", so this matches window
/// titles. The title is user-configurable via <c>window.title</c> — the default is
/// "file - folder - Visual Studio Code", but a custom template such as
/// "${activeRepositoryBranchName} - ${folderName}" is just as common — so every
/// " - "-separated segment is compared rather than assuming a fixed position.
/// The match is therefore on the folder's leaf name: two folders that share a leaf
/// name (a worktree and an unrelated repo of the same name) are indistinguishable.
/// </remarks>
internal static class VsCodeWindows
{
    /// <summary>Title segments of every open VS Code window; a worktree is "open" if its name is one.</summary>
    public static HashSet<string> OpenFolderNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, title) in Enumerate())
            foreach (var segment in Segments(title))
                names.Add(segment);
        return names;
    }

    /// <summary>Brings the VS Code window holding <paramref name="folder"/> to the front.</summary>
    /// <returns>False if no such window was found — the caller should launch VS Code instead.</returns>
    public static bool TryFocus(string folder)
    {
        var leaf = LeafName(folder);
        foreach (var (hwnd, title) in Enumerate())
        {
            foreach (var segment in Segments(title))
            {
                if (!string.Equals(segment, leaf, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                // Allowed to steal focus here: we are the foreground process, having
                // just been clicked.
                return SetForegroundWindow(hwnd);
            }
        }
        return false;
    }

    public static string LeafName(string folder) => Path.GetFileName(folder.TrimEnd('\\', '/'));

    private static IEnumerable<string> Segments(string title)
    {
        foreach (var raw in title.Split(" - ", StringSplitOptions.RemoveEmptyEntries))
        {
            // Strip the dirty-editor marker and the elevation suffix VS Code appends.
            var s = raw.Trim().TrimStart('\u25cf', '*').Trim();
            var admin = s.IndexOf(" [Administrator]", StringComparison.OrdinalIgnoreCase);
            if (admin >= 0) s = s[..admin];
            if (s.Length > 0) yield return s;
        }
    }

    /// <summary>Visible top-level windows belonging to a VS Code process.</summary>
    private static List<(IntPtr Hwnd, string Title)> Enumerate()
    {
        var found = new List<(IntPtr, string)>();
        var pids = CodeProcessIds();
        if (pids.Count == 0) return found;

        // Every VS Code window is owned by the Electron main process, so Process
        // .MainWindowHandle would only ever report the first one.
        EnumWindowsProc callback = (hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains(pid)) return true;

            var title = GetTitle(hwnd);
            if (title.Length > 0) found.Add((hwnd, title));
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    private static HashSet<uint> CodeProcessIds()
    {
        var pids = new HashSet<uint>();
        foreach (var name in new[] { "Code", "Code - Insiders" })
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var p in procs)
            {
                try { pids.Add((uint)p.Id); } catch { /* exited */ }
                p.Dispose();
            }
        }
        return pids;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
}
