using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace WorktreeHelper;

/// <summary>A release on GitHub, what it says about itself, and where its download lives.</summary>
public sealed record ReleaseInfo(
    Version Version, string Name, string Notes, string AssetName, string AssetUrl, string PageUrl);

/// <summary>
/// Looks for a newer release on GitHub and installs it over this copy of the app.
/// </summary>
/// <remarks>
/// The app ships as a loose exe rather than through an installer, so updating is: download
/// the release zip, take the exe out of it, rename the running one aside and put the new one
/// in its place. Windows allows a running exe to be renamed but not overwritten, which is
/// what makes that order the one that works.
/// </remarks>
internal static class UpdateService
{
    public const string Owner = "gonenbeneish-mapcore";
    public const string Repository = "WorkTreeHelper";

    private static readonly Uri LatestRelease =
        new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    /// <summary>The version this copy reports, normalised for comparison.</summary>
    public static Version Current { get; } = ReadCurrentVersion();

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub refuses anonymous API calls that do not name themselves.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repository}/{Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>
    /// The latest release if it is newer than this copy, or null — including when there is no
    /// network, which is not worth telling anyone about.
    /// </summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(LatestRelease, ct).ConfigureAwait(false);
            var release = ParseRelease(json);
            return release is not null && release.Version > Current ? release : null;
        }
        catch (Exception)
        {
            // Offline, rate-limited, or the shape changed: there is simply no update today.
            return null;
        }
    }

    /// <summary>Reads what the releases API returned. Null if it names no version or no zip.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // A draft or a pre-release is not something to push at anyone automatically.
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

            if (ParseTag(Text(root, "tag_name")) is not { } version) return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                if (name is null || url is null) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                var title = Text(root, "name") is { Length: > 0 } given ? given : Text(root, "tag_name") ?? version.ToString();
                return new ReleaseInfo(
                    version, title, Summarise(Text(root, "body")), name, url, Text(root, "html_url") ?? "");
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>
    /// Cuts release notes down to something a tooltip can hold: the first few lines, with the
    /// markdown that only makes sense on a page taken off.
    /// </summary>
    public static string Summarise(string? notes, int maxLines = 7, int maxChars = 500)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";

        var kept = new List<string>();
        var length = 0;
        var more = false;

        foreach (var raw in notes.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // Headings and rules are page furniture; bullets read fine as they are.
            if (line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal)) continue;
            line = line.Replace("**", "").Replace("`", "");

            if (kept.Count == maxLines || length + line.Length > maxChars)
            {
                more = true;
                break;
            }

            kept.Add(line);
            length += line.Length;
        }

        if (kept.Count == 0) return "";
        return string.Join("\n", kept) + (more ? "\n…" : "");
    }

    private static bool IsTrue(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads "v1.3.0", "1.3" and the like. Null for anything that is not a version.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V')) text = text[1..];

        // Stop at the first thing that is not part of a number, so "1.3.0-beta" reads as 1.3.0.
        var end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.')) end++;
        text = text[..end].Trim('.');

        return Version.TryParse(text, out var version) ? Normalise(version) : null;
    }

    /// <summary>
    /// Three components, so a tag of "1.3" and a file version of "1.3.0.0" compare equal:
    /// Version treats an absent part as -1, which would otherwise make them differ.
    /// </summary>
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>
    /// Fetches the release and unpacks the exe out of it, into the temp folder.
    /// </summary>
    /// <returns>The path of the new exe, verified to be the version the release claims.</returns>
    public static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"{Repository}-update");
        if (Directory.Exists(folder)) TryDelete(folder);
        Directory.CreateDirectory(folder);

        var zip = Path.Combine(folder, release.AssetName);
        await using (var download = await Http.GetStreamAsync(release.AssetUrl, ct).ConfigureAwait(false))
        await using (var file = File.Create(zip))
            await download.CopyToAsync(file, ct).ConfigureAwait(false);

        var unpacked = Path.Combine(folder, "unpacked");
        ZipFile.ExtractToDirectory(zip, unpacked);

        var exe = Directory.EnumerateFiles(unpacked, "*.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidOperationException($"{release.AssetName} holds no .exe.");

        // Refuse anything that does not say it is what was asked for, rather than installing it.
        var found = ParseTag(FileVersionInfo.GetVersionInfo(exe).ProductVersion);
        if (found != release.Version)
            throw new InvalidOperationException(
                $"{Path.GetFileName(exe)} reports version {found?.ToString() ?? "nothing"}, but the release is {release.Version}.");

        return exe;
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> where this app is running from, keeping the old one
    /// beside it until the next start.
    /// </summary>
    /// <remarks>
    /// The running exe cannot be overwritten, but it can be renamed, which leaves its path
    /// free to write to. If the copy fails the rename is undone, so a failed update leaves
    /// the app exactly as it was.
    /// </remarks>
    public static void Apply(string newExe)
    {
        if (Environment.ProcessPath is not { Length: > 0 } target)
            throw new InvalidOperationException("Cannot tell where this app is running from.");

        var aside = target + OldSuffix;
        TryDeleteFile(aside);

        File.Move(target, aside);
        try
        {
            File.Copy(newExe, target);
        }
        catch
        {
            File.Move(aside, target);
            throw;
        }
    }

    /// <summary>Clears away the copy an earlier update renamed aside. Best effort.</summary>
    public static void CleanUpPreviousVersion()
    {
        if (Environment.ProcessPath is { Length: > 0 } target) TryDeleteFile(target + OldSuffix);
    }

    private const string OldSuffix = ".old";

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* still in use; next time */ }
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* a stale download; harmless */ }
    }

    private static Version ReadCurrentVersion()
    {
        try
        {
            if (Environment.ProcessPath is { Length: > 0 } exe &&
                ParseTag(FileVersionInfo.GetVersionInfo(exe).ProductVersion) is { } fromFile)
                return fromFile;
        }
        catch
        {
            // Fall back to what the assembly says.
        }

        var assembly = Assembly.GetExecutingAssembly().GetName().Version;
        return assembly is null ? new Version(0, 0, 0) : Normalise(assembly);
    }
}
