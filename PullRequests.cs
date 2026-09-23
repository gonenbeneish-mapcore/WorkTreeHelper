using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
/// answers anonymously; a private one needs a token, which is looked for in the places a
/// developer machine already keeps one — the usual environment variables, the GitHub CLI if it
/// happens to be there, and finally git's own credential helper, asked in a way that cannot
/// pop a prompt. Where none of that yields anything, no branch has a pull request as far as
/// this app is concerned: no buttons, and nothing said.
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
            var remote = await RunAsync("git", "config --get remote.origin.url", repoPath, ct).ConfigureAwait(false);
            if (ParseRemote(remote) is not { } repo) return Empty();

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/pulls?state=open&per_page=100");

            if (await FindTokenAsync(repoPath, ct).ConfigureAwait(false) is { Length: > 0 } token)
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

    /// <summary>
    /// A token for the API, from wherever this machine already keeps one. Null when it keeps
    /// none, which still leaves public repositories answerable.
    /// </summary>
    private static async Task<string?> FindTokenAsync(string repoPath, CancellationToken ct)
    {
        foreach (var name in new[] { "GH_TOKEN", "GITHUB_TOKEN" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } fromEnvironment)
                return fromEnvironment;

        // Only if it is installed; this is not a dependency, just a place to look.
        if ((await RunAsync("gh", "auth token", repoPath, ct).ConfigureAwait(false)).Trim() is { Length: > 0 } fromCli)
            return fromCli;

        return await FromCredentialHelperAsync(repoPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks git for the credential it already holds for github.com.
    /// </summary>
    /// <remarks>
    /// Interactivity is turned off twice over — the config switch and the environment variable
    /// — because a credential helper with nothing stored would otherwise put a sign-in dialog
    /// on screen, which is not a thing a background check should ever do.
    /// </remarks>
    private static async Task<string?> FromCredentialHelperAsync(string repoPath, CancellationToken ct)
    {
        var output = await RunAsync(
            "git", "-c credential.interactive=false credential fill", repoPath, ct,
            input: "protocol=https\nhost=github.com\n\n",
            environment: ("GIT_TERMINAL_PROMPT", "0")).ConfigureAwait(false);

        foreach (var line in output.Split('\n'))
            if (line.StartsWith("password=", StringComparison.Ordinal))
                return line["password=".Length..].Trim();

        return null;
    }

    /// <summary>Runs a command and returns its output, or "" if it is missing or fails.</summary>
    private static async Task<string> RunAsync(
        string file, string arguments, string workingDirectory, CancellationToken ct,
        string? input = null, (string Name, string Value)? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (environment is { } variable) psi.Environment[variable.Name] = variable.Value;

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new Win32Exception($"{file} did not start.");
        }
        catch (Win32Exception)
        {
            // Not installed. That is an answer, not a failure.
            return "";
        }

        using (proc)
        using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { } }))
        {
            if (input is not null)
            {
                await proc.StandardInput.WriteAsync(input).ConfigureAwait(false);
                proc.StandardInput.Close();
            }

            var output = proc.StandardOutput.ReadToEndAsync(ct);
            var error = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            await error.ConfigureAwait(false);

            return proc.ExitCode == 0 ? await output.ConfigureAwait(false) : "";
        }
    }
}
