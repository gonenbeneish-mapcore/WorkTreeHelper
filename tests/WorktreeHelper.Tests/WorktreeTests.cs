using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class WorktreeTakeOverTests
{
    private static Worktree Shown(string branch) => new()
    {
        Path = @"C:\repo",
        Branch = branch,
        Head = "aaaaaaa",
        HasVisualStudio = true,
        Status = new WorktreeStatus(3, 1, 0, "origin/" + branch),
        IsOpenInVsCode = true,
        IsOpenInGitExtensions = true,
        IsSolutionOpen = true,
        PullRequest = new PullRequest(7, "A change", "https://github.com/owner/repo/pull/7", false),
    };

    [Fact]
    public void A_worktree_with_a_new_commit_keeps_what_its_row_showed()
    {
        // Git now reports another head for the same folder: the row is replaced, and until the
        // refresh has looked again it should look as it did, not start bare.
        var old = Shown("feature/x");
        var fresh = new Worktree { Path = old.Path, Branch = "feature/x", Head = "bbbbbbb" };

        fresh.TakeOverFrom(old);

        Assert.True(fresh.HasVisualStudio);
        Assert.Equal(old.Status, fresh.Status);
        Assert.True(fresh.IsOpenInVsCode);
        Assert.True(fresh.IsOpenInGitExtensions);
        Assert.True(fresh.IsSolutionOpen);
        Assert.Same(old.PullRequest, fresh.PullRequest);
    }

    [Fact]
    public void Another_branch_checked_out_does_not_inherit_the_pull_request()
    {
        var old = Shown("feature/x");
        var fresh = new Worktree { Path = old.Path, Branch = "feature/y", Head = "ccccccc" };

        fresh.TakeOverFrom(old);

        Assert.Null(fresh.PullRequest);
        Assert.True(fresh.HasVisualStudio); // the folder's, whatever the branch
    }
}
