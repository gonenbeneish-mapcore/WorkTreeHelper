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
/// repository generates a solution of the same name, so they all produce the same caption —
/// and a custom title template can leave the folder's name out altogether. Visual Studio
/// registers its automation object in the Running Object Table instead, and that object names
/// what it has open: the solution file, or the folder. That one string carries both the
/// worktree and the mode (see <see cref="VisualStudioInstance"/>).
///
/// Two things it cannot see: an instance running elevated while this app is not, because the
/// two integrity levels do not share a Running Object Table; and an instance too busy to
/// answer, which is skipped for that pass rather than waited on.
/// </remarks>
internal static class VisualStudioInstances
{
    private const string DteMoniker = "VisualStudio.DTE";

    /// <summary>Every running instance that names something it has open.</summary>
    public static List<VisualStudioInstance> Open() => Open(out _);

    /// <summary>Every running instance that names something it has open.</summary>
    /// <param name="complete">
    /// False when some instance could not be asked this time, being busy: then what it has
    /// open is not known, rather than known to be nothing, and a caller should not take its
    /// absence from the list as its window having closed.
    /// </param>
    public static List<VisualStudioInstance> Open(out bool complete)
    {
        var found = new List<VisualStudioInstance>();
        complete = true;

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

                    // Nothing can be done with an instance that has no window to raise.
                    if (hwnd == IntPtr.Zero) continue;

                    found.Add(new VisualStudioInstance(path, hwnd));
                }
                catch (COMException)
                {
                    // Busy, or shutting down as we asked. Skip it this time round, and say
                    // the list is short of it.
                    complete = false;
                }
                catch (Exception)
                {
                    // Anything else about one instance is not worth failing the others for.
                    complete = false;
                }
            }
        }
        catch (COMException)
        {
            // The table could not be read through: what is open is not known.
            complete = false;
        }

        return found;
    }

    /// <summary>The instances that have <paramref name="worktree"/> open, in either mode.</summary>
    public static List<VisualStudioInstance> Holding(string worktree)
        => Open().Where(i => i.Holds(worktree)).ToList();

    /// <summary>The instance that has <paramref name="worktree"/> open the given way, if any.</summary>
    public static VisualStudioInstance? Holding(string worktree, VisualStudioMode mode)
        => Holding(worktree).FirstOrDefault(i => i.Mode == mode);

    /// <summary>Brings one instance to the front.</summary>
    /// <returns>False if its window has gone since it was found.</returns>
    public static bool Focus(VisualStudioInstance instance)
    {
        if (instance.Hwnd == IntPtr.Zero) return false;
        if (IsIconic(instance.Hwnd)) ShowWindow(instance.Hwnd, SW_RESTORE);
        // Allowed to steal focus here: we are the foreground process, having just been clicked.
        return SetForegroundWindow(instance.Hwnd);
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
