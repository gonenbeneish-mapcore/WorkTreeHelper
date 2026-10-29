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
        HasAndroidStudio = true,
        IsOpenInAndroidStudio = true,
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
        Assert.True(fresh.HasAndroidStudio);
        Assert.True(fresh.IsOpenInAndroidStudio);
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

public class PullRequestSignalTests
{
    private static Worktree With(PullRequest? pr) => new() { Path = @"C:\repo", Branch = "feature/x", PullRequest = pr };

    private static PullRequest Open(PullRequestChecks checks = PullRequestChecks.None, PullRequestReview review = PullRequestReview.None, bool draft = false)
        => new(7, "A change", "https://github.com/owner/repo/pull/7", draft) { Checks = checks, Review = review };

    [Theory]
    [InlineData(PullRequestChecks.Failing, PullRequestReview.Required, PullRequestSignal.Failing)]
    [InlineData(PullRequestChecks.Failing, PullRequestReview.Approved, PullRequestSignal.Failing)]
    [InlineData(PullRequestChecks.Passing, PullRequestReview.ChangesRequested, PullRequestSignal.Failing)]
    [InlineData(PullRequestChecks.Pending, PullRequestReview.Required, PullRequestSignal.Running)]
    [InlineData(PullRequestChecks.Pending, PullRequestReview.Approved, PullRequestSignal.Running)]
    [InlineData(PullRequestChecks.Passing, PullRequestReview.Approved, PullRequestSignal.Approved)]
    [InlineData(PullRequestChecks.None, PullRequestReview.Approved, PullRequestSignal.Approved)]
    [InlineData(PullRequestChecks.Passing, PullRequestReview.Required, PullRequestSignal.None)]
    [InlineData(PullRequestChecks.None, PullRequestReview.None, PullRequestSignal.None)]
    public void Marks_only_what_calls_for_something(PullRequestChecks checks, PullRequestReview review, PullRequestSignal expected)
        => Assert.Equal(expected, With(Open(checks, review)).PullRequestSignal);

    [Theory]
    [InlineData(PullRequestChecks.Passing, PullRequestReview.Approved, PullRequestSignal.Unresolved)]
    [InlineData(PullRequestChecks.Pending, PullRequestReview.Required, PullRequestSignal.Unresolved)]
    [InlineData(PullRequestChecks.Failing, PullRequestReview.Required, PullRequestSignal.Failing)]
    public void Unresolved_comments_come_after_failing_and_before_the_rest(PullRequestChecks checks, PullRequestReview review, PullRequestSignal expected)
        => Assert.Equal(expected, With(Open(checks, review) with { UnresolvedThreads = 2 }).PullRequestSignal);

    [Fact]
    public void The_tooltip_counts_the_unresolved_conversations()
        => Assert.Contains("2 unresolved conversations", With(Open() with { UnresolvedThreads = 2 }).PullRequestTooltip);

    [Fact]
    public void An_approved_draft_is_not_marked_approved()
        => Assert.Equal(PullRequestSignal.None, With(Open(PullRequestChecks.Passing, PullRequestReview.Approved, draft: true)).PullRequestSignal);

    [Fact]
    public void A_merged_request_marks_the_worktree_merged()
    {
        var worktree = With(new PullRequest(9, "Done", "https://github.com/owner/repo/pull/9", false) { MergedAt = DateTimeOffset.Now, ClosedAt = DateTimeOffset.Now });

        Assert.True(worktree.IsMerged);
        Assert.True(worktree.HasNotes);
        Assert.Equal(PullRequestSignal.Merged, worktree.PullRequestSignal);
        Assert.Contains("#9", worktree.MergedTooltip);
        Assert.Contains("Merged on", worktree.PullRequestTooltip);
    }

    [Fact]
    public void A_request_closed_without_merging_is_closed_and_not_merged()
    {
        var worktree = With(new PullRequest(8, "Abandoned", "https://github.com/owner/repo/pull/8", false) { ClosedAt = DateTimeOffset.Now });

        Assert.Equal(PullRequestSignal.Closed, worktree.PullRequestSignal);
        Assert.False(worktree.IsMerged);
        Assert.Contains("Closed without merging on", worktree.PullRequestTooltip);
    }

    [Fact]
    public void An_open_request_is_not_merged()
    {
        var worktree = With(Open());
        Assert.False(worktree.IsMerged);
        Assert.False(worktree.HasNotes);
        Assert.Equal("", worktree.MergedTooltip);
    }

    [Fact]
    public void The_tooltip_says_how_it_stands_in_words()
    {
        var tooltip = With(Open(PullRequestChecks.Failing, PullRequestReview.ChangesRequested, draft: true)).PullRequestTooltip;

        Assert.StartsWith("#7 (draft)  A change", tooltip);
        Assert.Contains("Checks failing", tooltip);
        Assert.Contains("Changes requested", tooltip);
    }

    [Fact]
    public void The_same_request_again_is_no_change()
    {
        var worktree = With(Open(PullRequestChecks.Passing));
        var raised = new List<string?>();
        worktree.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        worktree.PullRequest = Open(PullRequestChecks.Passing);

        Assert.Empty(raised);
    }
}

public class FinishedPullRequestTests
{
    private static PullRequest Merged() => new(9, "Done", "https://github.com/o/r/pull/9", false) { MergedAt = DateTimeOffset.Now, ClosedAt = DateTimeOffset.Now };

    [Fact]
    public void A_merged_request_is_not_carried_to_a_new_commit()
    {
        // GitHub may not be there to ask again; the new commit is newer than what was merged
        // either way, so the row must not go on saying nothing is.
        var old = new Worktree { Path = @"C:\repo", Branch = "feature/x", Head = "aaaaaaa", PullRequest = Merged() };
        var fresh = new Worktree { Path = old.Path, Branch = "feature/x", Head = "bbbbbbb" };

        fresh.TakeOverFrom(old);

        Assert.Null(fresh.PullRequest);
        Assert.False(fresh.IsMerged);
    }

    [Fact]
    public void A_merged_request_stays_at_the_same_commit()
    {
        var old = new Worktree { Path = @"C:\repo", Branch = "feature/x", Head = "aaaaaaa", IsLocked = false, PullRequest = Merged() };
        var fresh = new Worktree { Path = old.Path, Branch = "feature/x", Head = "aaaaaaa", IsLocked = true };

        fresh.TakeOverFrom(old);

        Assert.True(fresh.IsMerged);
    }

    [Fact]
    public void A_merged_worktree_with_uncommitted_work_does_not_say_it_can_go()
    {
        var worktree = new Worktree { Path = @"C:\repo", Branch = "feature/x", PullRequest = Merged(), Status = new WorktreeStatus(3, 0, 0) };

        Assert.DoesNotContain("can be reused or removed", worktree.MergedTooltip);
        Assert.Contains("3 paths here are uncommitted", worktree.MergedTooltip);
    }

    [Fact]
    public void A_clean_merged_worktree_says_it_can_go()
        => Assert.Contains("can be reused or removed",
            new Worktree { Path = @"C:\repo", Branch = "feature/x", PullRequest = Merged(), Status = new WorktreeStatus(0, 0, 0) }.MergedTooltip);
}
