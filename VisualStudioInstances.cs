using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace WorktreeHelper;

/// <summary>
/// Finds running Visual Studio instances and the solution each has open, so a worktree that
/// is already open can be focused instead of generated again.
/// </summary>
/// <remarks>
/// Window titles cannot answer this the way they do for VS Code: every worktree of a
/// repository generates a solution of the same name, so they all produce the same caption.
/// Visual Studio registers its automation object in the Running Object Table instead, and
/// that object knows the solution's full path and the window to bring forward.
///
/// Two things it cannot see: an instance running elevated while this app is not, because the
/// two integrity levels do not share a Running Object Table; and an instance too busy to
/// answer, which is skipped for that pass rather than waited on.
/// </remarks>
internal static class VisualStudioInstances
{
    private const string DteMoniker = "VisualStudio.DTE";

    /// <summary>The solution open in every instance that answers, and its main window.</summary>
    public static List<(string Solution, IntPtr Hwnd)> Open()
    {
        var found = new List<(string, IntPtr)>();

        if (GetRunningObjectTable(0, out var table) != 0 || table is null) return found;
        if (CreateBindCtx(0, out var context) != 0 || context is null) return found;

        try
        {
            table.EnumRunning(out var monikers);
            monikers.Reset();

            var one = new IMoniker[1];
            while (monikers.Next(1, one, IntPtr.Zero) == 0)
            {
                if (one[0] is null) continue;
                try
                {
                    one[0].GetDisplayName(context, null, out var name);
                    if (name is null || !name.Contains(DteMoniker, StringComparison.Ordinal)) continue;

                    table.GetObject(one[0], out var dte);
                    if (dte is null) continue;

                    // Late-bound rather than referencing the DTE interop assemblies, which
                    // would tie the app to one Visual Studio version.
                    if (Read(dte, "Solution") is not { } solution) continue;
                    if (Read(solution, "FullName") is not string { Length: > 0 } path) continue;

                    var hwnd = IntPtr.Zero;
                    if (Read(dte, "MainWindow") is { } window && Read(window, "HWnd") is { } handle)
                        hwnd = new IntPtr(Convert.ToInt64(handle));

                    found.Add((path, hwnd));
                }
                catch (COMException)
                {
                    // Busy, or shutting down as we asked. Skip it this time round.
                }
                catch (Exception)
                {
                    // Anything else about one instance is not worth failing the others for.
                }
            }
        }
        catch (COMException)
        {
            // No table to read: treat it as nothing open.
        }

        return found;
    }

    /// <summary>Solution paths only, for deciding which rows to mark.</summary>
    public static List<string> OpenSolutions() => Open().Select(i => i.Solution).ToList();

    /// <summary>Brings the instance holding <paramref name="worktree"/>'s solution to the front.</summary>
    /// <returns>False if no instance has it open — the caller should generate and launch instead.</returns>
    public static bool TryFocus(string worktree)
    {
        foreach (var (solution, hwnd) in Open())
        {
            if (hwnd == IntPtr.Zero || !LinkPaths.IsUnder(solution, worktree)) continue;
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            // Allowed to steal focus here: we are the foreground process, having just been
            // clicked.
            return SetForegroundWindow(hwnd);
        }
        return false;
    }

    private static object? Read(object target, string property)
    {
        try
        {
            return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
        }
        catch (MissingMemberException)
        {
            return null;
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
