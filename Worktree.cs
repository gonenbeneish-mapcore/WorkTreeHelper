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
    /// per worktree. Null until it arrives, and after a read that failed.
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
    /// The open pull request this worktree's branch has, if it has one. Filled in per refresh
    /// from one query for the whole repository.
    /// </summary>
    public PullRequest? PullRequest
    {
        get => _pullRequest;
        set
        {
            if (ReferenceEquals(_pullRequest, value)) return;
            _pullRequest = value;
            Raise(nameof(PullRequest));
            Raise(nameof(HasPullRequest));
            Raise(nameof(PullRequestTooltip));
        }
    }

    public bool HasPullRequest => PullRequest is not null;

    /// <summary>The pull request's number and title, and whether it is still a draft.</summary>
    public string PullRequestTooltip => PullRequest is not { } pr
        ? ""
        : $"#{pr.Number}{(pr.IsDraft ? " (draft)" : "")}  {pr.Title}\nClick to open it in the browser";

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
            Raise(nameof(IsOpenInVisualStudio));
            Raise(nameof(ShowVisualStudioChooser));
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
            Raise(nameof(IsOpenInVisualStudio));
            Raise(nameof(ShowVisualStudioChooser));
        }
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
