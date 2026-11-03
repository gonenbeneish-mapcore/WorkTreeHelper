using Microsoft.Win32;

namespace WorktreeHelper;

/// <summary>
/// Whether Windows starts the app when the user signs in: an entry under the user's own Run
/// key, which needs no administrator and goes no further than this user.
/// </summary>
/// <remarks>
/// The registry is the setting, not the settings file. Task Manager's Startup apps page can
/// turn the entry off behind the app's back, and does it with a value of its own under
/// StartupApproved rather than by touching Run, so both are read to say whether it is on, and
/// turning it on clears what Task Manager left.
/// </remarks>
internal sealed class StartWithWindows(string runKey, string approvedKey, string name = StartWithWindows.Name)
{
    /// <summary>The value's name, which is what Task Manager lists the entry as.</summary>
    public const string Name = "Worktree Helper";

    /// <summary>The user's own entries, the ones Windows reads at sign-in.</summary>
    public static StartWithWindows ForUser { get; } = new(
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");

    /// <summary>
    /// The command the entry runs: this exe, quoted, and nothing more. Started at sign-in it
    /// starts as it does any other time, its window up, in the tray or on the taskbar.
    /// </summary>
    public static string CommandFor(string exe) => $"\"{exe}\"";

    /// <summary>On: there is an entry, and Task Manager has not turned it off.</summary>
    public bool IsOn
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(runKey);
                if (run?.GetValue(name) is not string { Length: > 0 }) return false;

                // Task Manager writes 12 bytes, the first of them odd when the entry is off.
                using var approved = Registry.CurrentUser.OpenSubKey(approvedKey);
                return approved?.GetValue(name) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>What the entry runs; null when there is none.</summary>
    public string? Command
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(runKey);
                return run?.GetValue(name) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Adds the entry for <paramref name="exe"/>, or takes it away.</summary>
    /// <returns>False when the registry would not have it.</returns>
    public bool Set(bool on, string exe)
    {
        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(runKey))
            {
                if (on) run.SetValue(name, CommandFor(exe));
                else run.DeleteValue(name, throwOnMissingValue: false);
            }

            // Task Manager's say goes with the entry: gone, it would turn a new entry off at
            // once; left behind once the entry is, it is litter.
            using (var approved = Registry.CurrentUser.OpenSubKey(approvedKey, writable: true))
                approved?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Points an entry that is on at <paramref name="exe"/>, for an app that has been moved
    /// since it was made. Left alone when it is off: that is the user's to change.
    /// </summary>
    public void Follow(string exe)
    {
        if (IsOn && Command != CommandFor(exe)) Set(true, exe);
    }
}
