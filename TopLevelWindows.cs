using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WorktreeHelper;

/// <summary>
/// The visible top-level windows of a program, by title, and a way to bring one forward:
/// what telling a worktree's open window apart comes down to, for the programs that offer no
/// better way to ask.
/// </summary>
internal static class TopLevelWindows
{
    /// <summary>Visible top-level windows belonging to a process of any of these names.</summary>
    public static List<(IntPtr Hwnd, string Title)> Of(params string[] processNames)
    {
        var found = new List<(IntPtr, string)>();
        var pids = ProcessIds(processNames);
        if (pids.Count == 0) return found;

        // Enumerated rather than read off each process: every VS Code window, for one, is
        // owned by the Electron main process, so Process.MainWindowHandle would only ever
        // report the first of them.
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

    /// <summary>Restores the window if it is minimised, and brings it to the front.</summary>
    public static bool Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        // Allowed to steal focus here: this app is the foreground process, having just been
        // clicked.
        return SetForegroundWindow(hwnd);
    }

    private static HashSet<uint> ProcessIds(string[] names)
    {
        var pids = new HashSet<uint>();
        foreach (var name in names)
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
