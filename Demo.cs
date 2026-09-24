namespace WorktreeHelper;

/// <summary>
/// The dressing the README's pictures are taken in, switched on by WORKTREEHELPER_DEMO=1.
/// </summary>
/// <remarks>
/// The demo repository tools\make-demo-repo.ps1 builds has a local folder for its remote, so
/// no branch of it can have a pull request, and nothing has any of its worktrees open. For
/// the picture to show what those look like, this gives duck-quack a pull request, and marks
/// windows open on duck-bath and duck-quack. The names are that script's. The update check
/// is left out too, so no notice of a newer release lands in the picture.
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

    /// <summary>The pull request the picture shows, on the branch with the most work in it.</summary>
    public static PullRequest? PullRequestFor(Worktree worktree)
        => worktree.Name is "duck-quack" ? new PullRequest(12, "Teach it to quack", "https://github.com/", false) : null;
}
