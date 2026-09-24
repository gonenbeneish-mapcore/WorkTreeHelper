using System.IO;

namespace WorktreeHelper;

/// <summary>
/// Finds VS Code windows by the folder they have open, so a worktree that is already
/// open can be focused instead of opened again.
/// </summary>
/// <remarks>
/// VS Code exposes no way to ask "which folders are open", so this matches window
/// titles. The title is user-configurable via <c>window.title</c> — the default is
/// "file - folder - Visual Studio Code", but a custom template such as
/// "${activeRepositoryBranchName} - ${folderName}" is just as common — so every
/// " - "-separated segment is compared rather than assuming a fixed position.
/// The match is therefore on the folder's leaf name: two folders that share a leaf
/// name (a worktree and an unrelated repo of the same name) are indistinguishable.
/// </remarks>
internal static class VsCodeWindows
{
    private static readonly string[] Processes = ["Code", "Code - Insiders"];

    /// <summary>Title segments of every open VS Code window; a worktree is "open" if its name is one.</summary>
    public static HashSet<string> OpenFolderNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, title) in TopLevelWindows.Of(Processes))
            foreach (var segment in Segments(title))
                names.Add(segment);
        return names;
    }

    /// <summary>Brings the VS Code window holding <paramref name="folder"/> to the front.</summary>
    /// <returns>False if no such window was found — the caller should launch VS Code instead.</returns>
    public static bool TryFocus(string folder)
    {
        var leaf = LeafName(folder);
        foreach (var (hwnd, title) in TopLevelWindows.Of(Processes))
            foreach (var segment in Segments(title))
                if (string.Equals(segment, leaf, StringComparison.OrdinalIgnoreCase))
                    return TopLevelWindows.Focus(hwnd);
        return false;
    }

    public static string LeafName(string folder) => Path.GetFileName(folder.TrimEnd('\\', '/'));

    private static IEnumerable<string> Segments(string title)
    {
        foreach (var raw in title.Split(" - ", StringSplitOptions.RemoveEmptyEntries))
        {
            // Strip the dirty-editor marker and the elevation suffix VS Code appends.
            var s = raw.Trim().TrimStart('●', '*').Trim();
            var admin = s.IndexOf(" [Administrator]", StringComparison.OrdinalIgnoreCase);
            if (admin >= 0) s = s[..admin];
            if (s.Length > 0) yield return s;
        }
    }
}
