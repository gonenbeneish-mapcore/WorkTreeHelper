using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WorktreeHelper;

/// <summary>One point from the changelog, and how far it is indented under the one before.</summary>
public sealed record ChangePoint(string Text, int Level);

/// <summary>One version's section of the changelog.</summary>
public sealed record ReleaseNotes(Version Version, IReadOnlyList<ChangePoint> Points);

/// <summary>
/// What the first run of a new version shows: the changelog sections the user has not seen.
/// </summary>
/// <remarks>
/// Read from CHANGELOG.md, compiled into the exe, rather than written a second time for the
/// app: the changelog is already where each release is described, and a second copy would
/// be the one that went stale. Offline, too, which the GitHub release notes are not.
/// </remarks>
internal static partial class WhatsNew
{
    /// <summary>The changelog as it was when this copy was built.</summary>
    public static string ReadChangelog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Splits the changelog into its versions. A heading is "## v1.5.0", perhaps followed by
    /// " — unreleased"; under it, "- " starts a point, a deeper "- " starts one under it, and
    /// any other indented line carries on the point above, since the file is wrapped by hand.
    /// </summary>
    public static IReadOnlyList<ReleaseNotes> Parse(string changelog)
    {
        var sections = new List<ReleaseNotes>();
        Version? version = null;
        var points = new List<ChangePoint>();

        void Close()
        {
            if (version is not null) sections.Add(new ReleaseNotes(version, points.ToList()));
            points.Clear();
        }

        foreach (var raw in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                // ParseTag stops at the first thing that is not part of a number, which is what
                // takes the " — unreleased" off.
                version = UpdateService.ParseTag(line[3..].Trim());
                continue;
            }
            if (version is null || line.Length == 0) continue;

            var indent = line.Length - line.TrimStart().Length;
            var body = line.TrimStart();

            if (body.StartsWith("- ", StringComparison.Ordinal))
                points.Add(new ChangePoint(Plain(body[2..]), indent >= 2 ? 1 : 0));
            else if (indent > 0 && points.Count > 0)
                points[^1] = points[^1] with { Text = points[^1].Text + " " + Plain(body) };
        }
        Close();

        return sections;
    }

    /// <summary>
    /// The sections to show: everything newer than the last version the user saw, up to the
    /// one running, newest first. With nothing seen before - an upgrade from before this was
    /// recorded - only the running version's, because there is no telling where they came from.
    /// </summary>
    public static IReadOnlyList<ReleaseNotes> Since(IReadOnlyList<ReleaseNotes> all, Version? lastSeen, Version current)
        => all.Where(n => n.Version <= current && (lastSeen is null ? n.Version == current : n.Version > lastSeen))
              .OrderByDescending(n => n.Version)
              .ToList();

    /// <summary>The markdown a page renders and a TextBlock would print: bold, code, links.</summary>
    private static string Plain(string text)
        => Link().Replace(text, "$1").Replace("**", "").Replace("`", "");

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();
}
