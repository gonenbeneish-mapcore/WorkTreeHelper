using System.ComponentModel;

namespace WorktreeHelper;

public sealed class Worktree : INotifyPropertyChanged
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public string Branch { get; init; } = "";
    public string Head { get; init; } = "";
    public bool IsMain { get; init; }
    public bool IsDetached { get; init; }
    public bool IsBare { get; init; }
    public bool IsLocked { get; init; }
    public bool IsPrunable { get; init; }

    /// <summary>
    /// True when git reports for <paramref name="other"/> exactly what this row already
    /// shows, so the row on screen can stay and keep the markers it has been given.
    /// </summary>
    public bool Matches(Worktree other) =>
        Branch == other.Branch && Head == other.Head &&
        IsMain == other.IsMain && IsDetached == other.IsDetached && IsBare == other.IsBare &&
        IsLocked == other.IsLocked && IsPrunable == other.IsPrunable;

    /// <summary>
    /// Takes over what the row it replaces had found out, for a worktree git now reports
    /// differently: a new commit, a pull, another branch checked out.
    /// </summary>
    /// <remarks>
    /// The refresh that made this row goes on to work all of it out again. Until it does, the
    /// row shows what the old one showed, rather than starting bare - no Visual Studio button,
    /// no counts, no marks - and putting each back as it is found, which moved the window's
    /// size about for a worktree that had only gained a commit. The pull request stays only
    /// on the same branch: it belongs to the branch, not the folder. A merged or closed one
    /// stays only at the same commit too: it counts while nothing is newer than it, and a new
    /// commit is, whether or not GitHub can be asked again to say so.
    /// </remarks>
    public void TakeOverFrom(Worktree old)
    {
        HasVisualStudio = old.HasVisualStudio;
        Status = old.Status;
        IsOpenInVsCode = old.IsOpenInVsCode;
        IsOpenInGitExtensions = old.IsOpenInGitExtensions;
        IsSolutionOpen = old.IsSolutionOpen;
        IsFolderOpen = old.IsFolderOpen;
        HasAndroidStudio = old.HasAndroidStudio;
        IsOpenInAndroidStudio = old.IsOpenInAndroidStudio;
        if (Branch == old.Branch && (old.PullRequest is { FinishedAt: null } || Head == old.Head))
            PullRequest = old.PullRequest;
    }

    /// <summary>Branch shown in the list: branch name, or "detached @ abc1234".</summary>
    public string DisplayBranch =>
        IsBare ? "(bare)" :
        IsDetached ? $"detached @ {ShortHead}" :
        Branch;

    public string ShortHead => Head.Length > 7 ? Head[..7] : Head;

    public string Badges
    {
        get
        {
            // Not "main": which worktree git calls the main one changes nothing about what
            // can be done with it, and it was on screen every time the app opened.
            var parts = new List<string>();
            if (IsLocked) parts.Add("locked");
            if (IsPrunable) parts.Add("prunable");
            return string.Join(" · ", parts);
        }
    }
    public bool HasBadges => Badges.Length > 0;

    private WorktreeStatus? _status;

    /// <summary>
    /// Working-tree state, filled in after the list appears because it costs one git call
    /// per worktree. Null until it first arrives; a later read that fails leaves it as it was.
    /// </summary>
    public WorktreeStatus? Status
    {
        get => _status;
        set
        {
            if (Nullable.Equals(_status, value)) return;
            _status = value;
            Raise(nameof(Status));
            Raise(nameof(IsDirty));
            Raise(nameof(StatusSummary));
            Raise(nameof(StatusTooltip));
            Raise(nameof(HasStatus));
            Raise(nameof(HasNotes));
            Raise(nameof(MergedTooltip));
        }
    }

    /// <summary>Something in this worktree differs from HEAD.</summary>
    public bool IsDirty => Status is { Changes: > 0 };

    /// <summary>
    /// The row's right-hand note: what is uncommitted and how far the branch has drifted.
    /// Empty for a worktree that is clean and level with its upstream, so a quiet row
    /// really does mean there is nothing to come back to.
    /// </summary>
    public string StatusSummary
    {
        get
        {
            if (Status is not { } s) return "";
            var parts = new List<string>();
            // Counts only: the dot beside them says the first one is uncommitted work, and
            // the arrows say which way the branch has drifted.
            if (s.Changes > 0) parts.Add(s.Changes.ToString());
            if (s.Ahead > 0) parts.Add($"↑{s.Ahead}");
            if (s.Behind > 0) parts.Add($"↓{s.Behind}");
            return string.Join(" ", parts);
        }
    }
    public bool HasStatus => StatusSummary.Length > 0;

    /// <summary>Anything to show beside the branch: the counts, or the merged tag.</summary>
    public bool HasNotes => HasStatus || IsMerged;

    /// <summary>
    /// What the counts beside the branch actually mean, in words, naming the upstream they
    /// are measured against.
    /// </summary>
    public string StatusTooltip
    {
        get
        {
            if (Status is not { } s) return "";

            var lines = new List<string>();
            if (s.Changes > 0)
                lines.Add($"{Plural(s.Changes, "path")} uncommitted, untracked files included");

            var upstream = s.Upstream is { Length: > 0 } named ? named : "its upstream";
            if (s.Ahead > 0) lines.Add($"{Plural(s.Ahead, "commit")} not yet pushed to {upstream}");
            if (s.Behind > 0) lines.Add($"{Plural(s.Behind, "commit")} on {upstream} not yet pulled in");

            return string.Join("\n", lines);
        }
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private PullRequest? _pullRequest;

    /// <summary>
    /// The pull request this worktree's branch has: the open one, or else the latest merged or
    /// closed one, but only while nothing in the worktree is newer than it. Filled in per refresh
    /// from one query for the whole repository.
    /// </summary>
    public PullRequest? PullRequest
    {
        get => _pullRequest;
        set
        {
            // Compared by value: each refresh brings a new record of the same request.
            if (Equals(_pullRequest, value)) return;
            _pullRequest = value;
            Raise(nameof(PullRequest));
            Raise(nameof(HasPullRequest));
            Raise(nameof(PullRequestTooltip));
            Raise(nameof(PullRequestSignal));
            Raise(nameof(IsMerged));
            Raise(nameof(MergedTooltip));
            Raise(nameof(HasNotes));
        }
    }

    public bool HasPullRequest => PullRequest is not null;

    /// <summary>
    /// The branch went in, and nothing here has been committed since: the worktree is free to
    /// be reused or removed.
    /// </summary>
    public bool IsMerged => PullRequest is { IsMerged: true };

    /// <summary>What the mark on the pull request button says, if anything.</summary>
    /// <remarks>
    /// Merged, or closed without it; failing checks or changes asked for; comments not yet
    /// resolved; checks not finished; approved with nothing failing. A request still in review is quiet, like a row with
    /// nothing waiting in it. A draft is approved for nothing: it cannot be merged as it is.
    /// </remarks>
    public PullRequestSignal PullRequestSignal => PullRequest switch
    {
        null => PullRequestSignal.None,
        { IsMerged: true } => PullRequestSignal.Merged,
        { IsClosed: true } => PullRequestSignal.Closed,
        { Checks: PullRequestChecks.Failing } or { Review: PullRequestReview.ChangesRequested } => PullRequestSignal.Failing,
        { UnresolvedThreads: > 0 } => PullRequestSignal.Unresolved,
        { Checks: PullRequestChecks.Pending } => PullRequestSignal.Running,
        { IsDraft: false, Review: PullRequestReview.Approved } => PullRequestSignal.Approved,
        _ => PullRequestSignal.None,
    };

    /// <summary>The pull request's number and title, and how it stands, in words.</summary>
    public string PullRequestTooltip
    {
        get
        {
            if (PullRequest is not { } pr) return "";

            var lines = new List<string> { $"#{pr.Number}{(pr.IsDraft ? " (draft)" : "")}  {pr.Title}" };
            if (pr.MergedAt is { } merged)
            {
                lines.Add($"Merged on {Day(merged)}");
            }
            else if (pr.ClosedAt is { } closed)
            {
                lines.Add($"Closed without merging on {Day(closed)}");
            }
            else
            {
                switch (pr.Checks)
                {
                    case PullRequestChecks.Failing: lines.Add("Checks failing"); break;
                    case PullRequestChecks.Pending: lines.Add("Checks still running"); break;
                    case PullRequestChecks.Passing: lines.Add("Checks passing"); break;
                }
                // Not "waiting for review": every open request is, until it is one of these.
                switch (pr.Review)
                {
                    case PullRequestReview.ChangesRequested: lines.Add("Changes requested"); break;
                    case PullRequestReview.Approved: lines.Add("Approved"); break;
                }
                if (pr.UnresolvedThreads > 0)
                    lines.Add($"{Plural(pr.UnresolvedThreads, "unresolved conversation")}");
            }
            lines.Add("Click to open it in the browser");
            return string.Join("\n", lines);
        }
    }

    /// <summary>What the "merged" tag beside the branch means, in words.</summary>
    /// <remarks>
    /// Commits are all that "merged" looks at. Files changed and not committed are still only
    /// here, so for a worktree that has them it says so rather than that it can go.
    /// </remarks>
    public string MergedTooltip => PullRequest is { MergedAt: { } merged } pr
        ? $"Merged in pull request #{pr.Number} on {Day(merged)}, and nothing committed here since.\n" +
          (Status is { Changes: > 0 } s
              ? $"But {Plural(s.Changes, "path")} here {(s.Changes == 1 ? "is" : "are")} uncommitted, and would be lost with the worktree."
              : "This worktree can be reused or removed.")
        : "";

    /// <summary>A date the same way whatever the machine's language, since the rest of the text is English.</summary>
    private static string Day(DateTimeOffset when) =>
        when.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private bool _hasVisualStudio;

    /// <summary>
    /// This worktree carries the Visual Studio script, so its row offers a button for it.
    /// Filled in per refresh rather than looked up per binding, which would touch the disk
    /// every time the row drew.
    /// </summary>
    public bool HasVisualStudio
    {
        get => _hasVisualStudio;
        set
        {
            if (_hasVisualStudio == value) return;
            _hasVisualStudio = value;
            Raise(nameof(HasVisualStudio));
            Raise(nameof(ShowVisualStudioChooser));
        }
    }

    private bool _isOpenInVsCode;

    /// <summary>
    /// A VS Code window appears to have this worktree open, so its button focuses that
    /// window instead of launching. Re-checked on refresh and whenever the app is
    /// activated, so it is a snapshot, not a live subscription.
    /// </summary>
    public bool IsOpenInVsCode
    {
        get => _isOpenInVsCode;
        set
        {
            if (_isOpenInVsCode == value) return;
            _isOpenInVsCode = value;
            Raise(nameof(IsOpenInVsCode));
        }
    }

    private bool _isOpenInGitExtensions;

    /// <summary>
    /// A Git Extensions window is browsing this worktree, so its button brings that window
    /// back instead of starting another. A snapshot, like <see cref="IsOpenInVsCode"/>.
    /// </summary>
    public bool IsOpenInGitExtensions
    {
        get => _isOpenInGitExtensions;
        set
        {
            if (_isOpenInGitExtensions == value) return;
            _isOpenInGitExtensions = value;
            Raise(nameof(IsOpenInGitExtensions));
        }
    }

    private bool _hasAndroidStudio;

    /// <summary>
    /// This worktree has the Android project, so its row offers to open it in Android Studio.
    /// Filled in per refresh, like <see cref="HasVisualStudio"/>.
    /// </summary>
    public bool HasAndroidStudio
    {
        get => _hasAndroidStudio;
        set
        {
            if (_hasAndroidStudio == value) return;
            _hasAndroidStudio = value;
            Raise(nameof(HasAndroidStudio));
        }
    }

    private bool _isOpenInAndroidStudio;

    /// <summary>
    /// An Android Studio window has this worktree's Android project open, so its button brings
    /// that window back. A snapshot, like <see cref="IsOpenInVsCode"/>.
    /// </summary>
    public bool IsOpenInAndroidStudio
    {
        get => _isOpenInAndroidStudio;
        set
        {
            if (_isOpenInAndroidStudio == value) return;
            _isOpenInAndroidStudio = value;
            Raise(nameof(IsOpenInAndroidStudio));
        }
    }

    private bool _isSolutionOpen;
    private bool _isFolderOpen;

    /// <summary>
    /// An instance has this worktree's generated solution open, so the solution button focuses
    /// that window instead of generating it again.
    /// </summary>
    public bool IsSolutionOpen
    {
        get => _isSolutionOpen;
        set
        {
            if (_isSolutionOpen == value) return;
            _isSolutionOpen = value;
            Raise(nameof(IsSolutionOpen));
            RaiseVisualStudio();
        }
    }

    /// <summary>An instance has this worktree open as a folder — the CMake-configured builds.</summary>
    public bool IsFolderOpen
    {
        get => _isFolderOpen;
        set
        {
            if (_isFolderOpen == value) return;
            _isFolderOpen = value;
            Raise(nameof(IsFolderOpen));
            RaiseVisualStudio();
        }
    }

    private bool _allowSecondVisualStudio = true;

    /// <summary>
    /// Whether this row may offer the other kind of Visual Studio once one is open. Copied
    /// from the setting onto every row, so the buttons can be worked out here.
    /// </summary>
    public bool AllowSecondVisualStudio
    {
        get => _allowSecondVisualStudio;
        set
        {
            if (_allowSecondVisualStudio == value) return;
            _allowSecondVisualStudio = value;
            Raise(nameof(AllowSecondVisualStudio));
            RaiseVisualStudio();
        }
    }

    /// <summary>
    /// The solution button. With a second instance allowed it stands beside the folder one
    /// once either is open, focusing the solution or starting it; without, it only appears
    /// when the solution is what is open, and then all it does is focus it.
    /// </summary>
    public bool ShowSolutionButton => AllowSecondVisualStudio ? IsOpenInVisualStudio : IsSolutionOpen;

    /// <summary>The folder button, by the same rule as <see cref="ShowSolutionButton"/>.</summary>
    public bool ShowFolderButton => AllowSecondVisualStudio ? IsOpenInVisualStudio : IsFolderOpen;

    private void RaiseVisualStudio()
    {
        Raise(nameof(IsOpenInVisualStudio));
        Raise(nameof(ShowVisualStudioChooser));
        Raise(nameof(ShowSolutionButton));
        Raise(nameof(ShowFolderButton));
    }

    /// <summary>
    /// Either way is open, which is what splits one button into two: until something is open
    /// there is a choice to offer, and afterwards each mode has a button of its own.
    /// </summary>
    public bool IsOpenInVisualStudio => IsSolutionOpen || IsFolderOpen;

    /// <summary>The single button that asks which way in, shown only while neither is open.</summary>
    public bool ShowVisualStudioChooser => HasVisualStudio && !IsOpenInVisualStudio;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The mark on a row's pull request button.</summary>
/// <remarks>Hollow while it can still change, filled once it is settled.</remarks>
public enum PullRequestSignal
{
    /// <summary>Nothing to show, or no pull request.</summary>
    None,
    /// <summary>Checks failing, or changes asked for: a hollow red cross, it can still be put right.</summary>
    Failing,
    /// <summary>Closed without being merged: a filled red cross.</summary>
    Closed,
    /// <summary>Review conversations not yet resolved: an amber question mark.</summary>
    Unresolved,
    /// <summary>Its checks have not finished: a clock.</summary>
    Running,
    /// <summary>Approved, nothing failing, not merged yet: a hollow green tick.</summary>
    Approved,
    /// <summary>It has gone in: a filled green tick.</summary>
    Merged,
}
