using System.Text.Json;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class VisualStudioButtonTests
{
    private static Worktree Row(bool solution, bool folder, bool allowSecond) => new()
    {
        Path = @"C:\repo",
        HasVisualStudio = true,
        IsSolutionOpen = solution,
        IsFolderOpen = folder,
        AllowSecondVisualStudio = allowSecond,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nothing_open_asks_which_way_in_either_way(bool allowSecond)
    {
        var row = Row(solution: false, folder: false, allowSecond);
        Assert.True(row.ShowVisualStudioChooser);
        Assert.False(row.ShowSolutionButton);
        Assert.False(row.ShowFolderButton);
    }

    [Fact]
    public void With_a_second_allowed_one_open_offers_both()
    {
        var row = Row(solution: true, folder: false, allowSecond: true);
        Assert.False(row.ShowVisualStudioChooser);
        Assert.True(row.ShowSolutionButton);    // focuses it
        Assert.True(row.ShowFolderButton);      // starts the second instance
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Without_a_second_only_what_is_open_is_offered(bool solution, bool folder)
    {
        var row = Row(solution, folder, allowSecond: false);
        Assert.False(row.ShowVisualStudioChooser);
        Assert.Equal(solution, row.ShowSolutionButton);
        Assert.Equal(folder, row.ShowFolderButton);
    }

    [Fact]
    public void Both_open_elsewhere_are_both_shown_even_without_a_second()
    {
        // The option is about what the app offers to start. Two instances opened some other
        // way are still two windows to get back to, and hiding one would lose it.
        var row = Row(solution: true, folder: true, allowSecond: false);
        Assert.True(row.ShowSolutionButton);
        Assert.True(row.ShowFolderButton);
    }

    [Fact]
    public void Changing_the_option_tells_the_buttons()
    {
        var row = Row(solution: true, folder: false, allowSecond: true);
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.AllowSecondVisualStudio = false;

        Assert.Contains(nameof(Worktree.ShowFolderButton), raised);
        Assert.False(row.ShowFolderButton);
    }
}

public class SlotVisibilityTests
{
    private static System.Windows.Visibility Slot(params object[] values)
        => (System.Windows.Visibility)SlotVisibility.Instance.Convert(values, typeof(object), null!, null!);

    [Fact]
    public void A_button_the_row_wants_is_drawn()
        => Assert.Equal(System.Windows.Visibility.Visible, Slot(true, false));

    [Fact]
    public void Packed_a_missing_button_takes_no_room()
        => Assert.Equal(System.Windows.Visibility.Collapsed, Slot(false, false));

    [Fact]
    public void In_columns_a_missing_button_keeps_its_place()
        => Assert.Equal(System.Windows.Visibility.Hidden, Slot(false, true));

    [Fact]
    public void A_shared_slot_is_held_once_not_twice()
    {
        // The chooser and the solution button share a slot. With the solution button up, the
        // chooser must not hold a place beside it, or that row would be one button wider.
        Assert.Equal(System.Windows.Visibility.Collapsed, Slot(false, true, true));
        Assert.Equal(System.Windows.Visibility.Hidden, Slot(false, true, false));
    }
}

public class SettingsDefaultsTests
{
    [Fact]
    public void A_settings_file_from_before_the_options_keeps_the_old_behaviour()
    {
        // What 1.4 wrote: none of the new keys.
        const string old = """
            {
              "LastRepoPath": "C:\\git\\repo",
              "Pinned": false,
              "ShowInTaskbar": false,
              "AskedAboutTaskbar": true
            }
            """;

        var settings = JsonSerializer.Deserialize<Settings>(old)!;

        Assert.True(settings.AllowSecondVisualStudio);
        Assert.False(settings.AlignColumns);
        Assert.False(settings.ShowInTaskbar);   // the tray, still the default
    }

    [Fact]
    public void A_settings_file_from_before_the_button_choices_shows_every_button()
    {
        const string old = """{ "LastRepoPath": "C:\\git\\repo", "AlignColumns": true }""";

        var settings = JsonSerializer.Deserialize<Settings>(old)!;

        Assert.True(settings.ShowCodeButton);
        Assert.True(settings.ShowTerminalButton);
        Assert.True(settings.ShowExplorerButton);
        Assert.True(settings.ShowGitExtensionsButton);
        Assert.True(settings.ShowVisualStudioButtons);
        Assert.True(settings.ShowPullRequestButton);
        Assert.Null(settings.GitExtensionsPath);
    }
}

public class ExternalToolTests
{
    [Fact]
    public void A_program_that_is_not_found_leaves_its_button_down_but_still_wanted()
    {
        var tool = new ExternalTool("Tool", "tool.exe", () => null);
        tool.Path = tool.Look();

        Assert.False(tool.Found);
        Assert.True(tool.Wanted);   // so it appears by itself once installed
        Assert.False(tool.Shown);
    }

    [Fact]
    public void A_program_found_and_wanted_puts_its_button_up()
    {
        using var exe = new TempExe();
        var tool = new ExternalTool("Tool", "tool.exe", () => exe.Path);
        tool.Path = tool.Look();

        Assert.True(tool.Shown);

        tool.Wanted = false;
        Assert.False(tool.Shown);
    }

    [Fact]
    public void An_exe_the_user_chose_comes_before_a_search()
    {
        using var chosen = new TempExe();
        var searched = false;
        var tool = new ExternalTool("Tool", "tool.exe", () => { searched = true; return null; })
        {
            Wanted = false,
        };

        tool.Choose(chosen.Path);

        Assert.Equal(chosen.Path, tool.Look());
        Assert.False(searched);
        Assert.True(tool.Wanted);   // choosing it is asking for the button
        Assert.True(tool.Shown);
    }

    [Fact]
    public void A_chosen_exe_that_has_gone_is_searched_for_again()
    {
        string gone;
        using (var chosen = new TempExe()) gone = chosen.Path;
        var tool = new ExternalTool("Tool", "tool.exe", () => null) { Chosen = gone };

        Assert.Null(tool.Look());
    }

    [Fact]
    public void Being_found_tells_the_button()
    {
        using var exe = new TempExe();
        var tool = new ExternalTool("Tool", "tool.exe", () => exe.Path);
        var raised = new List<string?>();
        tool.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tool.Path = tool.Look();

        Assert.Contains(nameof(ExternalTool.Shown), raised);
    }

    private sealed class TempExe : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wth-tool-" + Guid.NewGuid().ToString("N") + ".exe");

        public TempExe() => System.IO.File.WriteAllText(Path, "");

        public void Dispose()
        {
            try { System.IO.File.Delete(Path); } catch { }
        }
    }
}
