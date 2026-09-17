using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class VisualStudioInstanceTests
{
    // The two strings a real Visual Studio 18.0 reported through DTE.Solution.FullName, one
    // instance per mode, both on the same worktree. This is the whole basis of the mode rule.
    private const string SolutionMode = @"C:\git\mapcore2\VS\MapCore.slnx";
    private const string FolderMode = @"C:\git\mapcore2";
    private const string Worktree = @"C:\git\mapcore2";

    private static VisualStudioInstance Instance(string opened) => new(opened, new IntPtr(1));

    [Fact]
    public void A_solution_file_is_the_solution_mode()
        => Assert.Equal(VisualStudioMode.Solution, Instance(SolutionMode).Mode);

    [Fact]
    public void The_worktree_itself_is_the_folder_mode()
        => Assert.Equal(VisualStudioMode.Folder, Instance(FolderMode).Mode);

    [Theory]
    [InlineData(@"C:\git\repo\Thing.sln")]
    [InlineData(@"C:\git\repo\Thing.slnx")]
    [InlineData(@"C:\git\repo\Thing.slnf")]
    [InlineData(@"C:\git\repo\THING.SLN")]
    public void Every_solution_extension_counts_as_a_solution(string opened)
        => Assert.Equal(VisualStudioMode.Solution, Instance(opened).Mode);

    [Fact]
    public void A_folder_whose_name_looks_like_a_file_is_still_a_folder()
        => Assert.Equal(VisualStudioMode.Folder, Instance(@"C:\git\my.project").Mode);

    [Fact]
    public void Both_modes_belong_to_the_worktree_they_were_opened_from()
    {
        Assert.True(Instance(SolutionMode).Holds(Worktree));
        Assert.True(Instance(FolderMode).Holds(Worktree));
    }

    [Fact]
    public void A_sibling_worktree_whose_name_starts_the_same_is_not_held()
    {
        // The case the single VS button used to get wrong: mapcore2 is not inside mapcore.
        Assert.False(Instance(SolutionMode).Holds(@"C:\git\mapcore"));
        Assert.False(Instance(FolderMode).Holds(@"C:\git\mapcore"));
    }

    [Fact]
    public void An_instance_holding_nothing_belongs_to_no_worktree()
        => Assert.False(Instance("").Holds(Worktree));

    [Fact]
    public void The_label_names_what_is_open()
    {
        Assert.Equal("MapCore.slnx", Instance(SolutionMode).Label);
        Assert.Equal("mapcore2", Instance(FolderMode).Label);
        // A trailing separator must not leave the label empty.
        Assert.Equal("mapcore2", Instance(@"C:\git\mapcore2\").Label);
    }

    [Fact]
    public void Two_instances_on_one_worktree_are_told_apart()
    {
        var open = new[] { Instance(SolutionMode), Instance(FolderMode) };

        Assert.Single(open.Where(i => i.Holds(Worktree) && i.Mode == VisualStudioMode.Solution));
        Assert.Single(open.Where(i => i.Holds(Worktree) && i.Mode == VisualStudioMode.Folder));
    }
}
