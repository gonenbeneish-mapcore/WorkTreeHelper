using Microsoft.Win32;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

/// <summary>
/// Against keys of the tests' own under the user's hive, laid out like Run and StartupApproved,
/// never the real ones: a test must not change what Windows starts at sign-in.
/// </summary>
public sealed class StartWithWindowsTests : IDisposable
{
    private const string Parent = @"Software\WorktreeHelper.Tests";
    private readonly string _root = Parent + @"\" + Guid.NewGuid().ToString("N");
    private string Run => _root + @"\Run";
    private string Approved => _root + @"\Approved";
    private StartWithWindows Entry => new(Run, Approved);

    private const string Exe = @"C:\Tools\Worktree Helper\WorktreeHelper.exe";

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false); } catch { }
        // The parent too, once the last test has gone: DeleteSubKey refuses one with keys left.
        try { Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false); } catch { }
    }

    /// <summary>What Task Manager writes when the entry is turned off, or back on.</summary>
    private void TaskManagerSays(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Approved);
        key.SetValue(StartWithWindows.Name, new byte[] { (byte)(on ? 2 : 3), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }

    [Fact]
    public void Off_until_there_is_an_entry()
        => Assert.False(Entry.IsOn);

    [Fact]
    public void Turned_on_it_runs_this_exe_quoted()
    {
        Assert.True(Entry.Set(true, Exe));

        Assert.True(Entry.IsOn);
        Assert.Equal($"\"{Exe}\"", Entry.Command);
    }

    [Fact]
    public void Turned_off_in_Task_Manager_it_is_off()
    {
        Entry.Set(true, Exe);
        TaskManagerSays(on: false);

        Assert.False(Entry.IsOn);
    }

    [Fact]
    public void Turned_back_on_in_Task_Manager_it_is_on()
    {
        Entry.Set(true, Exe);
        TaskManagerSays(on: true);

        Assert.True(Entry.IsOn);
    }

    [Fact]
    public void Turning_it_on_undoes_what_Task_Manager_turned_off()
    {
        Entry.Set(true, Exe);
        TaskManagerSays(on: false);

        Entry.Set(true, Exe);

        Assert.True(Entry.IsOn);
    }

    [Fact]
    public void Turning_it_off_leaves_nothing_behind()
    {
        Entry.Set(true, Exe);
        TaskManagerSays(on: false);

        Assert.True(Entry.Set(false, Exe));

        Assert.False(Entry.IsOn);
        Assert.Null(Entry.Command);
        using var approved = Registry.CurrentUser.OpenSubKey(Approved);
        Assert.Null(approved?.GetValue(StartWithWindows.Name));
    }

    [Fact]
    public void An_entry_that_is_on_follows_the_app_to_where_it_now_is()
    {
        Entry.Set(true, @"C:\Old\WorktreeHelper.exe");

        Entry.Follow(Exe);

        Assert.Equal(StartWithWindows.CommandFor(Exe), Entry.Command);
    }

    [Fact]
    public void An_entry_that_is_off_is_not_brought_back_by_following()
    {
        Entry.Set(true, @"C:\Old\WorktreeHelper.exe");
        TaskManagerSays(on: false);

        Entry.Follow(Exe);

        Assert.False(Entry.IsOn);
        Assert.Equal(StartWithWindows.CommandFor(@"C:\Old\WorktreeHelper.exe"), Entry.Command);
    }

    [Fact]
    public void Without_an_entry_following_makes_none()
    {
        Entry.Follow(Exe);

        Assert.Null(Entry.Command);
    }
}
