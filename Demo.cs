namespace WorktreeHelper;

/// <summary>
/// The dressing the README's pictures are taken in, switched on by WORKTREEHELPER_DEMO=1.
/// </summary>
/// <remarks>
/// The demo repository tools\make-demo-repo.ps1 builds has a local folder for its remote, so
/// no branch of it can have a pull request, and nothing has any of its worktrees open. For
/// the picture to show what those look like, this gives duck-quack a pull request whose checks
/// are failing, duck-bubbles an approved one and duck-float one that has been merged, and marks
/// windows open on duck-bath and duck-quack. The names are that script's. The update check is left out too, so no
/// notice of a newer release lands in the picture.
/// </remarks>
internal static class Demo
{
    public static bool IsOn { get; } = Environment.GetEnvironmentVariable("WORKTREEHELPER_DEMO") == "1";

    /// <summary>Marks the windows the picture shows as open.</summary>
    public static void MarkOpen(Worktree worktree)
    {
        if (worktree.Name is "duck-bath")
        {
            worktree.IsOpenInVsCode = true;
            worktree.IsOpenInGitExtensions = true;
        }
        if (worktree.Name is "duck-quack") worktree.IsOpenInVsCode = true;
    }

    /// <summary>
    /// The pull requests the picture shows: one waiting on its author, on the branch with the
    /// most work in it, one approved, and one merged, on the branch that is clean and level.
    /// </summary>
    public static PullRequest? PullRequestFor(Worktree worktree) => worktree.Name switch
    {
        "duck-bubbles" => new PullRequest(11, "More bubbles", "https://github.com/", false)
        {
            Checks = PullRequestChecks.Passing,
            Review = PullRequestReview.Approved,
        },
        "duck-quack" => new PullRequest(12, "Teach it to quack", "https://github.com/", false)
        {
            Checks = PullRequestChecks.Failing,
            Review = PullRequestReview.Required,
        },
        "duck-float" => new PullRequest(9, "Float the right way up", "https://github.com/", false)
        {
            // A day rather than a moment, so each refresh brings the same request, not a new one.
            MergedAt = DateTimeOffset.Now.Date.AddDays(-2),
        },
        _ => null,
    };
}
