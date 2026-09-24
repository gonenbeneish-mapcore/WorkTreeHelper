namespace WorktreeHelper;

/// <summary>
/// Finds Git Extensions windows by the worktree they browse, so its button can bring back the
/// one already open instead of starting another.
/// </summary>
/// <remarks>
/// By title, as for VS Code: Git Extensions has no way to be asked what it has open. Its title
/// is "folder (branch) - Git Extensions", with " &lt; outer" after the folder for a repository
/// that sits inside another one's folder, so the folder's leaf name is what is matched. Two
/// folders sharing a leaf name are therefore indistinguishable, as they are for VS Code.
/// </remarks>
internal static class GitExtensionsWindows
{
    private const string Process = "GitExtensions";
    private const string Suffix = " - Git Extensions";

    /// <summary>The folder names the open Git Extensions windows are browsing.</summary>
    public static HashSet<string> OpenFolderNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, title) in TopLevelWindows.Of(Process))
            if (FolderIn(title) is { } name) names.Add(name);
        return names;
    }

    /// <summary>Brings the Git Extensions window browsing <paramref name="folder"/> to the front.</summary>
    /// <returns>False if no such window was found, and the caller should start one.</returns>
    public static bool TryFocus(string folder)
    {
        var leaf = VsCodeWindows.LeafName(folder);
        foreach (var (hwnd, title) in TopLevelWindows.Of(Process))
            if (string.Equals(FolderIn(title), leaf, StringComparison.OrdinalIgnoreCase))
                return TopLevelWindows.Focus(hwnd);
        return false;
    }

    /// <summary>
    /// The folder a Git Extensions title names, or null for a window that browses none: its
    /// dashboard, or a dialog of its own.
    /// </summary>
    public static string? FolderIn(string title)
    {
        if (!title.EndsWith(Suffix, StringComparison.Ordinal)) return null;
        var text = title[..^Suffix.Length].TrimEnd();

        // The branch, in brackets at the end. The last pair, since a folder's own name may
        // have brackets in it.
        if (text.EndsWith(')') && text.LastIndexOf(" (", StringComparison.Ordinal) is var open and > 0)
            text = text[..open];

        // The repository this one sits inside, after " < ".
        if (text.IndexOf(" < ", StringComparison.Ordinal) is var outer and > 0)
            text = text[..outer];

        text = text.Trim();
        return text.Length > 0 ? text : null;
    }
}
