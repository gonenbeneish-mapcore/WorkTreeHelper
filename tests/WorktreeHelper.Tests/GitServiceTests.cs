using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class ParseWorktreeListTests
{
    private const string ThreeWorktrees = """
        worktree D:/git/mapcore
        HEAD 4ed7acfb4a0eccefee3ba1ac1546c8f9d1db0bbb
        branch refs/heads/feature/map-camera-navigator

        worktree D:/git/mapcore2
        HEAD 8d23620368e22ebc2e5a34158f29a168d7028631
        branch refs/heads/bug/arrow-calculations

        worktree D:/git/mapcore3
        HEAD 172fb6c2d5fce2d71fb3a9087d891cf0a0dcdf33
        detached

        """;

    [Fact]
    public void Reads_every_worktree()
    {
        var list = GitService.Parse(ThreeWorktrees);
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { "mapcore", "mapcore2", "mapcore3" }, list.Select(w => w.Name));
    }

    [Fact]
    public void Strips_the_refs_heads_prefix()
        => Assert.Equal("feature/map-camera-navigator", GitService.Parse(ThreeWorktrees)[0].Branch);

    [Fact]
    public void Marks_only_the_first_worktree_as_main()
        => Assert.Equal(new[] { true, false, false }, GitService.Parse(ThreeWorktrees).Select(w => w.IsMain));

    [Fact]
    public void Turns_forward_slashes_into_windows_separators()
        => Assert.Equal(@"D:\git\mapcore", GitService.Parse(ThreeWorktrees)[0].Path);

    [Fact]
    public void Shows_a_detached_head_as_its_short_hash()
    {
        var detached = GitService.Parse(ThreeWorktrees)[2];
        Assert.True(detached.IsDetached);
        Assert.Equal("detached @ 172fb6c", detached.DisplayBranch);
    }

    [Fact]
    public void Reads_bare_locked_and_prunable_flags()
    {
        var list = GitService.Parse("""
            worktree D:/git/bare
            bare

            worktree D:/git/wt
            HEAD 1111111111111111111111111111111111111111
            branch refs/heads/main
            locked reason goes here
            prunable gitdir file points to non-existent location

            """);

        Assert.True(list[0].IsBare);
        Assert.Equal("(bare)", list[0].DisplayBranch);
        // Being the main worktree is not worth a badge; only these two are.
        Assert.False(list[0].HasBadges);

        Assert.True(list[1].IsLocked);
        Assert.True(list[1].IsPrunable);
        Assert.Equal("locked · prunable", list[1].Badges);
    }

    [Fact]
    public void Handles_crlf_line_endings()
    {
        var list = GitService.Parse("worktree D:/git/a\r\nHEAD 2222222222222222222222222222222222222222\r\nbranch refs/heads/main\r\n\r\n");
        Assert.Single(list);
        Assert.Equal("main", list[0].Branch);
    }

    [Fact]
    public void Records_the_last_worktree_when_the_trailing_blank_line_is_missing()
    {
        var list = GitService.Parse("worktree D:/git/a\nHEAD 3333333333333333333333333333333333333333\nbranch refs/heads/main");
        Assert.Single(list);
    }

    [Fact]
    public void Returns_nothing_for_empty_output() => Assert.Empty(GitService.Parse(""));
}

public class ParseStatusTests
{
    [Fact]
    public void A_clean_worktree_level_with_upstream_has_nothing_to_report()
    {
        var status = GitService.ParseStatus("""
            # branch.oid 1111111111111111111111111111111111111111
            # branch.head main
            # branch.upstream origin/main
            # branch.ab +0 -0

            """);

        Assert.Equal(new WorktreeStatus(0, 0, 0, "origin/main"), status);
    }

    [Fact]
    public void Counts_changed_unmerged_and_untracked_paths()
    {
        var status = GitService.ParseStatus("""
            # branch.head main
            1 .M N... 100644 100644 100644 aaa bbb src/changed.cs
            2 R. N... 100644 100644 100644 ccc ddd R100 new.cs	old.cs
            u UU N... 100644 100644 100644 100644 eee fff ggg conflict.cs
            ? untracked.txt

            """);

        Assert.Equal(4, status.Changes);
        Assert.Equal(0, status.Ahead);
    }

    [Fact]
    public void Reads_the_upstream_name()
        => Assert.Equal("origin/feature", GitService.ParseStatus("# branch.upstream origin/feature\n").Upstream);

    [Fact]
    public void Reads_ahead_and_behind()
    {
        var status = GitService.ParseStatus("# branch.ab +2 -13\n");
        Assert.Equal(2, status.Ahead);
        Assert.Equal(13, status.Behind);
    }

    [Fact]
    public void A_branch_with_no_upstream_reports_no_drift()
    {
        // git omits branch.ab entirely rather than printing zeroes.
        var status = GitService.ParseStatus("# branch.head local-only\n# branch.upstream\n");
        Assert.Equal(0, status.Ahead);
        Assert.Equal(0, status.Behind);
        Assert.Equal(0, status.Changes);
    }

    [Fact]
    public void Header_lines_are_never_counted_as_changes()
        => Assert.Equal(0, GitService.ParseStatus("# branch.oid abc\n# branch.head main\n").Changes);
}
