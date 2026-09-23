using System.Runtime.InteropServices;

namespace WorktreeHelper;

/// <summary>
/// Keeps one copy of the app running. A second launch hands its request to the copy
/// already in the tray and exits, instead of adding a second tray icon and a second
/// writer of the same settings file.
/// </summary>
internal static class SingleInstance
{
    /// <summary>Broadcast by a second launch; the running copy answers by showing itself.</summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("WorktreeHelper.Show");

    private static Mutex? _held;

    /// <summary>True if this process is the first one, false if the app is already running.</summary>
    public static bool TryAcquire()
    {
        // Local\ scopes the name to the logon session, so two signed-in users each get one.
        _held = new Mutex(initiallyOwned: true, @"Local\WorktreeHelper.SingleInstance", out var isFirst);
        if (isFirst) return true;

        _held.Dispose();
        _held = null;
        return false;
    }

    /// <summary>Asks the copy that is already running to show its window.</summary>
    /// <remarks>
    /// A broadcast rather than a direct post: there is no telling which of the running
    /// copy's windows would be found by looking, but its main window is an ordinary
    /// top-level window and receives broadcasts even while hidden.
    /// </remarks>
    public static void SignalExistingInstance()
        => PostMessage(HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);

    public static void Release()
    {
        if (_held is null) return;
        try { _held.ReleaseMutex(); } catch { /* never acquired */ }
        _held.Dispose();
        _held = null;
    }

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
