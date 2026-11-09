using System.Runtime.InteropServices;
using System.Windows;

namespace WorktreeHelper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Before anything is started: every git the app runs inherits this.
        QuietChildFailures();

        base.OnStartup(e);

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        // Shown here rather than through StartupUri so nothing is created before the
        // single-instance check has had its say.
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }

    /// <summary>
    /// Keeps Windows from putting up its own box when a program the app starts cannot.
    /// </summary>
    /// <remarks>
    /// git, started for a refresh, now and then fails before it has run at all - "The
    /// application was unable to start correctly (0xc0000142)" - which is Windows unable to set
    /// a console program up, usually with a great many processes running. The app copes: that
    /// git call fails, and the row keeps what it showed. But Windows also raises a box, which
    /// sits there until it is clicked. A child inherits its parent's error mode, and with
    /// SEM_FAILCRITICALERRORS that box is not raised; measured with a program built to fail the
    /// same way, the box came up with the mode at 0 and not with this set. The app's own errors
    /// are unaffected: it reports those itself.
    /// </remarks>
    private static void QuietChildFailures()
    {
        try { SetErrorMode(GetErrorMode() | SEM_FAILCRITICALERRORS); }
        catch (EntryPointNotFoundException) { /* nothing to set */ }
    }

    private const uint SEM_FAILCRITICALERRORS = 0x0001;

    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] private static extern uint GetErrorMode();
}
