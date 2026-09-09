using System.IO;

namespace WorktreeHelper;

/// <summary>
/// Keeps paths in the shape the user chose. A Windows symbolic link or junction gives one
/// folder two names — C:\git\repo can be a link to D:\git\repo — and git always reports the
/// target. When the user pointed the app at the link, this rewrites git's paths back through
/// it so the list, the tooltips and everything the buttons launch use the name they picked.
/// </summary>
public sealed class LinkPaths
{
    private static readonly char[] Separators = { '\\', '/' };

    private readonly string _physicalPrefix;
    private readonly string _logicalPrefix;

    private LinkPaths(string physicalPrefix, string logicalPrefix)
    {
        _physicalPrefix = physicalPrefix;
        _logicalPrefix = logicalPrefix;
    }

    /// <summary>
    /// Derives a rewrite from the folder the user picked, or null when that folder is
    /// reached without crossing a link and nothing needs rewriting.
    /// </summary>
    public static LinkPaths? For(string chosenFolder)
    {
        string logical, physical;
        try
        {
            logical = Path.GetFullPath(chosenFolder).TrimEnd(Separators);
            physical = ResolveFinal(logical).TrimEnd(Separators);
        }
        catch
        {
            return null;
        }
        if (Same(logical, physical)) return null;

        // Cancel the trailing segments the two names share, so a link that sits beside its
        // siblings (C:\git\a -> D:\git\a) also covers them (C:\git\b -> D:\git\b) — which is
        // exactly the layout that has other worktrees of the same repo under it.
        while (true)
        {
            var logicalName = Path.GetFileName(logical);
            if (logicalName.Length == 0 || !Same(logicalName, Path.GetFileName(physical))) break;
            if (Path.GetDirectoryName(logical) is not { Length: > 0 } logicalParent) break;
            if (Path.GetDirectoryName(physical) is not { Length: > 0 } physicalParent) break;
            logical = logicalParent.TrimEnd(Separators);
            physical = physicalParent.TrimEnd(Separators);
        }
        return new LinkPaths(physical, logical);
    }

    /// <summary>
    /// The name for <paramref name="physicalPath"/> under the link the user chose, or the
    /// path unchanged when there is no such name.
    /// </summary>
    public string Prefer(string physicalPath)
    {
        var full = Path.GetFullPath(physicalPath).TrimEnd(Separators);
        if (!Same(full, _physicalPrefix) &&
            !(full.Length > _physicalPrefix.Length &&
              full.AsSpan(0, _physicalPrefix.Length).Equals(_physicalPrefix, StringComparison.OrdinalIgnoreCase) &&
              Separators.Contains(full[_physicalPrefix.Length])))
            return physicalPath;

        var rest = full.Length > _physicalPrefix.Length ? full[(_physicalPrefix.Length + 1)..] : "";
        var candidate = rest.Length == 0 ? _logicalPrefix : Path.Combine(_logicalPrefix, rest);
        try
        {
            // Only trust the rewrite once it is confirmed to be another name for that folder.
            if (Directory.Exists(candidate) && Same(ResolveFinal(candidate).TrimEnd(Separators), full))
                return candidate;
        }
        catch { /* fall through to the path git gave us */ }
        return physicalPath;
    }

    /// <summary>True when both paths name the same folder, whatever links they cross.</summary>
    public static bool SameFolder(string a, string b)
    {
        try { return Same(ResolveFinal(a).TrimEnd(Separators), ResolveFinal(b).TrimEnd(Separators)); }
        catch { return false; }
    }

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="folder"/> or sits inside it, with
    /// links on both sides resolved first — the same folder reached by two names still counts.
    /// </summary>
    public static bool IsUnder(string path, string folder)
    {
        try
        {
            var inner = ResolveFinal(path).TrimEnd(Separators);
            var outer = ResolveFinal(folder).TrimEnd(Separators);

            if (Same(inner, outer)) return true;
            return inner.Length > outer.Length
                   && inner.AsSpan(0, outer.Length).Equals(outer, StringComparison.OrdinalIgnoreCase)
                   && Separators.Contains(inner[outer.Length]);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Follows every link along <paramref name="path"/>, not just its last segment.</summary>
    private static string ResolveFinal(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        foreach (var segment in full[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                // Null for anything that is not a link; throws on a broken or looping one.
                if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                    current = Path.GetFullPath(target.FullName);
            }
            catch { /* not resolvable: keep the name as written */ }
        }
        return current;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
