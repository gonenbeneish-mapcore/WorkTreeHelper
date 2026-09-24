using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WorktreeHelper;

/// <summary>An open pull request, and where to read it.</summary>
public sealed record PullRequest(int Number, string Title, string Url, bool IsDraft);

/// <summary>A repository on GitHub, as named by a remote URL.</summary>
public sealed record GitHubRepo(string Owner, string Name);

/// <summary>
/// Finds the open pull requests of a repository, so a worktree whose branch has one can offer
/// a way to it.
/// </summary>
/// <remarks>
/// Asked of the GitHub API directly, over the owner and name taken from the remote, so this
/// works for any repository on github.com and needs nothing installed. A public repository
/// answers anonymously; a private one needs a token, which <see cref="GitHubToken"/> looks for.
/// Where it finds none, no branch has a pull request as far as this app is concerned: no
/// buttons, and nothing said.
/// </remarks>
internal static class PullRequests
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{UpdateService.Repository}/{UpdateService.Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Open pull requests by the branch each one comes from.</summary>
    /// <returns>
    /// Empty for a repository that is not on GitHub; null when it is but could not be asked,
    /// so a caller can keep what an earlier answer said.
    /// </returns>
    public static async Task<Dictionary<string, PullRequest>?> OpenByBranchAsync(string repoPath, CancellationToken ct = default)
    {
        try
        {
            var remote = await Commands.RunAsync("git", "config --get remote.origin.url", repoPath, ct).ConfigureAwait(false);
            if (ParseRemote(remote) is not { } repo) return Empty();

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/pulls?state=open&per_page=100");

            if (await GitHubToken.FindAsync(repoPath, ct).ConfigureAwait(false) is { Length: > 0 } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            return Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Not reachable, not permitted: no answer this time.
            return null;
        }
    }

    /// <summary>
    /// Reads the owner and name out of a remote URL, in the forms git writes them:
    /// https://github.com/owner/repo(.git), git@github.com:owner/repo(.git), ssh://…
    /// </summary>
    /// <returns>Null for anything that is not a github.com repository.</returns>
    public static GitHubRepo? ParseRemote(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var text = url.Trim();

        // scp-like syntax has no scheme for Uri to read.
        const string scp = "git@github.com:";
        if (text.StartsWith(scp, StringComparison.OrdinalIgnoreCase))
            return Split(text[scp.Length..]);

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)) return null;

        return Split(uri.AbsolutePath);
    }

    private static GitHubRepo? Split(string path)
    {
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var name = parts[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        return name.Length == 0 ? null : new GitHubRepo(parts[0], name);
    }

    /// <summary>Reads the pulls endpoint into a lookup by the branch each request comes from.</summary>
    public static Dictionary<string, PullRequest> Parse(string json)
    {
        var found = Empty();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return found;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("head", out var head)) continue;
                if (Text(head, "ref") is not { Length: > 0 } branch) continue;
                if (Text(item, "html_url") is not { Length: > 0 } url) continue;
                if (!item.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number) continue;

                // Newest first, so the first one wins where a branch somehow has two.
                found.TryAdd(branch, new PullRequest(
                    number.GetInt32(),
                    Text(item, "title") ?? "",
                    url,
                    item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True));
            }
        }
        catch (JsonException)
        {
            // Something other than the list we asked for.
        }
        return found;
    }

    /// <summary>Branch names are case-sensitive to git, but not worth a mismatch over here.</summary>
    private static Dictionary<string, PullRequest> Empty() => new(StringComparer.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
