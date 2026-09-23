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
}
