using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace WorktreeHelper;

/// <summary>
/// Finds the Android Studio windows that have a worktree's Android project open, so its
/// button can bring one back rather than open the project again.
/// </summary>
/// <remarks>
/// A window's title is "project – file", the project being named after its folder. Every
/// worktree's Android project has the same folder name, so the title alone cannot say which
/// worktree a window belongs to. It can when two projects of the same name are open at once:
/// Android Studio then writes each one's path after its name, in brackets, as it does for
/// every project under "always show full paths". Otherwise the path comes from Android
/// Studio's own list of recent projects, which marks the ones it has open, and only from
/// entries opened since it started, since one killed rather than closed leaves its projects
/// marked open.
/// </remarks>
internal static class AndroidStudioWindows
{
    private const string ProcessName = "studio64";

    private static readonly string[] Separators = [" – ", " - "];

    /// <summary>The project windows of Android Studio, each with the folder it has open.</summary>
    public static List<(IntPtr Hwnd, string Folder)> OpenProjects()
    {
        var windows = TopLevelWindows.Of(ProcessName);
        if (windows.Count == 0) return [];

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var recent = RecentProjectsFile() is { } file ? OpenedIn(Read(file), home) : [];
        return Resolve(windows, recent, StartedAt(), home);
    }

    /// <summary>Brings the Android Studio window that has <paramref name="project"/> open to the front.</summary>
    /// <returns>False if no such window was found, and the caller should open the project.</returns>
    public static bool TryFocus(string project)
    {
        foreach (var (hwnd, folder) in OpenProjects())
            if (LinkPaths.SameFolder(folder, project))
                return TopLevelWindows.Focus(hwnd);
        return false;
    }

    /// <summary>
    /// The folder each project window has open: the one its title names, or else the project
    /// of that name the recent list says is open, the last one used where it says several are.
    /// </summary>
    /// <param name="since">When Android Studio started, as Unix milliseconds.</param>
    public static List<(IntPtr Hwnd, string Folder)> Resolve(
        IEnumerable<(IntPtr Hwnd, string Title)> windows, IReadOnlyList<RecentProject> recent, long since, string home)
    {
        var found = new List<(IntPtr, string)>();
        foreach (var (hwnd, title) in windows)
        {
            if (ProjectIn(title, home) is not { } project) continue;
            var (name, folder) = project;
            folder ??= recent
                .Where(p => p.Opened >= since && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .MaxBy(p => p.Activated)?.Folder;
            if (folder is not null) found.Add((hwnd, folder));
        }
        return found;
    }

    /// <summary>
    /// The project a window's title names, and its folder where the title gives that too; null
    /// for a title with nothing in it.
    /// </summary>
    /// <remarks>
    /// The project comes before the first " – ", the file being edited after it; a bracket
    /// after the file is the module that file belongs to, not a path. The search for the
    /// separator skips over brackets, since a path may have one in it. A title without a
    /// project, such as the welcome screen's or a dialog's, names a project that is never
    /// found open.
    /// </remarks>
    public static (string Name, string? Folder)? ProjectIn(string title, string home)
    {
        var cut = title.Length;
        var depth = 0;
        for (var i = 0; i < title.Length && cut == title.Length; i++)
        {
            if (title[i] == '[') depth++;
            else if (title[i] == ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && Separators.Any(s => string.CompareOrdinal(title, i, s, 0, s.Length) == 0)) cut = i;
        }

        var head = title[..cut].Trim();
        string? folder = null;
        if (head.EndsWith(']') && head.IndexOf('[') is var open and >= 0)
        {
            var path = head[(open + 1)..^1].Trim();
            if (path == "~" || path.StartsWith("~\\") || path.StartsWith("~/"))
                folder = home + path[1..];
            else if (Path.IsPathRooted(path))
                folder = path;
            if (folder is not null) head = head[..open].Trim();
        }

        // A project named after its folder's full path shows only the path.
        var name = head.Length > 0 ? head : folder is null ? "" : LeafName(folder);
        return name.Length > 0 ? (name, folder) : null;
    }

    /// <summary>A project in Android Studio's recent list that it says is open.</summary>
    /// <param name="Opened">When it was opened or last brought forward, as Unix milliseconds.</param>
    /// <param name="Activated">When it was last brought forward, to choose among several.</param>
    public sealed record RecentProject(string Folder, string Name, long Opened, long Activated);

    /// <summary>The projects a recentProjects.xml marks open.</summary>
    /// <remarks>
    /// Each is an entry whose key is the project's folder, with $USER_HOME$ for the user's
    /// own, and whose RecentProjectMetaInfo carries opened="true" and the title its window
    /// last had, from which the name is read.
    /// </remarks>
    public static List<RecentProject> OpenedIn(string? xml, string home)
    {
        var found = new List<RecentProject>();
        if (string.IsNullOrEmpty(xml)) return found;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return found; }

        foreach (var entry in doc.Descendants("entry"))
        {
            var info = entry.Descendants("RecentProjectMetaInfo").FirstOrDefault();
            if (info is null || (string?)info.Attribute("opened") != "true") continue;
            if ((string?)entry.Attribute("key") is not { Length: > 0 } key) continue;

            var folder = key.Replace("$USER_HOME$", home).Replace('/', '\\');
            var name = (string?)info.Attribute("frameTitle") is { } frameTitle && ProjectIn(frameTitle, home) is { } titled
                ? titled.Name
                : LeafName(folder);
            var activated = Timestamp(info, "activationTimestamp");
            found.Add(new RecentProject(folder, name, Math.Max(activated, Timestamp(info, "projectOpenTimestamp")), activated));
        }
        return found;
    }

    private static long Timestamp(XElement info, string option)
        => info.Elements("option").FirstOrDefault(o => (string?)o.Attribute("name") == option)?.Attribute("value") is { } value
           && long.TryParse(value.Value, out var ms) ? ms : 0;

    /// <summary>
    /// The recent-projects file of the Android Studio last used: each version keeps its own
    /// settings folder, and the one written to last belongs to the one running.
    /// </summary>
    private static string? RecentProjectsFile()
    {
        try
        {
            var google = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Google");
            if (!Directory.Exists(google)) return null;
            return Directory.EnumerateDirectories(google, "AndroidStudio*")
                .Select(dir => new FileInfo(Path.Combine(dir, "options", "recentProjects.xml")))
                .Where(f => f.Exists)
                .MaxBy(f => f.LastWriteTimeUtc)?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string? Read(string file)
    {
        // Android Studio may be writing it this moment, and shares it for reading meanwhile.
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>When the running Android Studio started, as Unix milliseconds; 0 if it cannot be told.</summary>
    private static long StartedAt()
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(ProcessName); }
        catch { return 0; }

        long earliest = long.MaxValue;
        foreach (var p in procs)
        {
            try { earliest = Math.Min(earliest, new DateTimeOffset(p.StartTime).ToUnixTimeMilliseconds()); }
            catch { /* exited, or not ours to ask */ }
            p.Dispose();
        }
        return earliest == long.MaxValue ? 0 : earliest;
    }

    private static string LeafName(string folder) => Path.GetFileName(folder.TrimEnd('\\', '/'));
}
