using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WorktreeHelper;

/// <summary>A pull request, open, merged or closed, and where to read it.</summary>
public sealed record PullRequest(int Number, string Title, string Url, bool IsDraft)
{
    /// <summary>What the checks on its latest commit came to.</summary>
    public PullRequestChecks Checks { get; init; }

    /// <summary>Where its review stands, as GitHub sums it up.</summary>
    public PullRequestReview Review { get; init; }

    /// <summary>Review conversations not yet resolved: comments on the code still waiting on someone.</summary>
    public int UnresolvedThreads { get; init; }

    /// <summary>When it was merged; null while it is open.</summary>
    public DateTimeOffset? MergedAt { get; init; }

    /// <summary>The commit its branch was at, which for a merged one is what was merged.</summary>
    public string HeadSha { get; init; } = "";

    /// <summary>When it was closed, merged or not; null while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    public bool IsMerged => MergedAt is not null;

    /// <summary>Closed without being merged.</summary>
    public bool IsClosed => ClosedAt is not null && MergedAt is null;

    /// <summary>When it was merged or closed; null while it is open.</summary>
    public DateTimeOffset? FinishedAt => MergedAt ?? ClosedAt;
}

/// <summary>The checks on a pull request's latest commit, taken together.</summary>
public enum PullRequestChecks { None, Pending, Passing, Failing }

/// <summary>A pull request's review decision. None where the repository asks for no review.</summary>
public enum PullRequestReview { None, Required, Approved, ChangesRequested }

/// <summary>A repository's pull requests by the branch each comes from.</summary>
/// <param name="Open">The open ones.</param>
/// <param name="Finished">
/// The latest of each branch's merged or closed ones; empty where GitHub was asked without a token.
/// </param>
public sealed record PullRequestsByBranch(Dictionary<string, PullRequest> Open, Dictionary<string, PullRequest> Finished);

/// <summary>What asking GitHub for the pull requests came to.</summary>
/// <param name="Found">The requests; null when GitHub could not be asked.</param>
/// <param name="Problem">
/// What went wrong, in words, when it was GitHub that failed: no answer in time, no connection, a
/// server error. Null otherwise, and null too when GitHub answered but refused, which for a
/// private repository without a token is the expected answer and not worth a word.
/// </param>
public sealed record PullRequestLookup(PullRequestsByBranch? Found, string? Problem = null);

/// <summary>A repository on GitHub, as named by a remote URL.</summary>
public sealed record GitHubRepo(string Owner, string Name);

/// <summary>
/// Finds the pull requests of a repository, so a worktree whose branch has one can offer a way
/// to it, say how it stands, and say when it has been merged or closed.
/// </summary>
/// <remarks>
/// Asked of the GitHub API directly, over the owner and name taken from the remote, so this
/// works for any repository on github.com and needs nothing installed. A private repository
/// needs a token, which <see cref="GitHubToken"/> looks for. With one, a single GraphQL query
/// brings, for the branches the worktrees are on, the open requests with their checks and
/// review, and the merged and closed ones. GraphQL
/// takes no anonymous calls, so without one the REST list of open requests is all there is: the
/// buttons, but no checks, reviews or merges. Where a private repository has no token, no branch
/// has a pull request as far as this app is concerned: no buttons, and nothing said.
/// </remarks>
internal static class PullRequests
{
    /// <summary>How long GitHub has to answer, which the problem it reports names.</summary>
    /// <remarks>Ahead of <see cref="Http"/>, which is made from it: static fields start in order.</remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Patience };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{UpdateService.Repository}/{UpdateService.Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>
    /// GraphQL turned the token away, so for the rest of the session the REST list is asked
    /// straight off, rather than asking both and waiting on both at every refresh.
    /// </summary>
    private static bool _graphQLRefused;

    /// <summary>The open, merged and closed pull requests of the given branches.</summary>
    /// <param name="branches">The branches the worktrees are on, the only ones asked about.</param>
    /// <returns>
    /// Empty for a repository that is not on GitHub. No requests when it is but could not be
    /// asked, so a caller can keep what an earlier answer said, and why where GitHub failed.
    /// </returns>
    public static async Task<PullRequestLookup> ByBranchAsync(string repoPath, IReadOnlyList<string> branches, CancellationToken ct = default)
    {
        try
        {
            var remote = await Commands.RunAsync("git", "config --get remote.origin.url", repoPath, ct).ConfigureAwait(false);
            if (ParseRemote(remote) is not { } repo || branches.Count == 0) return new(new(Empty(), Empty()));

            var token = await GitHubToken.FindAsync(repoPath, ct).ConfigureAwait(false);

            // A token the REST call takes, GraphQL may still turn away - a fine-grained one short
            // of a permission, say - and the buttons are worth having without the rest.
            if (token is { Length: > 0 } && !_graphQLRefused &&
                await AskGraphQLAsync(repo, branches, token, ct).ConfigureAwait(false) is { } full)
                return new(full);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/pulls?state=open&per_page=100");
            if (token is { Length: > 0 }) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(null, (int)response.StatusCode >= 500 ? $"GitHub answered with an error ({(int)response.StatusCode})" : null);

            return new(new(Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)), Empty()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer refresh took over. Only that: HttpClient ends a request that ran out of
            // time the same way, and taken for this it ended the whole refresh, counts and all.
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(null, $"GitHub did not answer within {Patience.TotalSeconds:0} seconds");
        }
        catch (HttpRequestException)
        {
            return new(null, "GitHub could not be reached");
        }
        catch (Exception)
        {
            // Something else about the answer: no requests this time, and nothing to say.
            return new(null);
        }
    }

    /// <summary>
    /// The query for <paramref name="count"/> branches: for each, its latest few requests open
    /// merged or closed, with what a row shows of them.
    /// </summary>
    /// <remarks>
    /// One lookup per branch rather than the repository's whole list. The checks are the costly
    /// part, and for two hundred requests - the open and the recently merged of a busy
    /// repository - GitHub gave up after ten seconds; for the few branches the worktrees are on
    /// it answers in one. The names go in as variables, never into the query's own text.
    /// </remarks>
    public static string QueryFor(int count)
    {
        var query = new StringBuilder("query($owner: String!, $name: String!");
        for (var i = 0; i < count; i++) query.Append($", $b{i}: String!");
        query.Append(") { repository(owner: $owner, name: $name) {");
        for (var i = 0; i < count; i++)
            query.Append($" b{i}: pullRequests(headRefName: $b{i}, states: [OPEN, MERGED, CLOSED], first: 5, orderBy: {{field: UPDATED_AT, direction: DESC}}) {{ nodes {{ ...shown }} }}");
        query.Append(" } } fragment shown on PullRequest { number title url isDraft state isCrossRepository headRefName headRefOid mergedAt closedAt reviewDecision");
        query.Append(" commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }");
        query.Append(" reviewThreads(first: 100) { nodes { isResolved } } }");
        return query.ToString();
    }

    /// <summary>
    /// Asks the GraphQL API; null for any answer that is not the repository's requests, for the
    /// REST list to be asked instead. No answer at all is thrown, as for the REST list.
    /// </summary>
    private static async Task<PullRequestsByBranch?> AskGraphQLAsync(GitHubRepo repo, IReadOnlyList<string> branches, string token, CancellationToken ct)
    {
        var variables = new Dictionary<string, string> { ["owner"] = repo.Owner, ["name"] = repo.Name };
        for (var i = 0; i < branches.Count; i++) variables[$"b{i}"] = branches[i];
        var body = JsonSerializer.Serialize(new { query = QueryFor(branches.Count), variables });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Turned away for good, not for now: a rate limit says so in its headers, and passes.
            _graphQLRefused |= response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden &&
                !(response.Headers.TryGetValues("x-ratelimit-remaining", out var left) && left.FirstOrDefault() == "0");
            return null;
        }
        return ParseGraphQL(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
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
                // From a fork: a branch of someone else's that only shares the name.
                if (RepoName(head) is { } from && item.TryGetProperty("base", out var target) &&
                    RepoName(target) is { } into && !from.Equals(into, StringComparison.OrdinalIgnoreCase)) continue;

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

    /// <summary>The "owner/name" of the repository a REST head or base belongs to, where it says.</summary>
    private static string? RepoName(JsonElement end) =>
        end.ValueKind == JsonValueKind.Object && end.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.Object
            ? Text(repo, "full_name")
            : null;

    /// <summary>Reads the GraphQL answer into the open and the finished requests by branch.</summary>
    /// <returns>Null for an answer without the repository in it: an error, or no access to it.</returns>
    public static PullRequestsByBranch? ParseGraphQL(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object)
                return null;

            // One list per branch asked about, each keyed by its alias; the requests in them
            // say which branch they come from, and whether they are open, merged or closed.
            var open = Empty();
            var finished = Empty();
            foreach (var list in repository.EnumerateObject())
                if (list.Value.ValueKind == JsonValueKind.Object &&
                    list.Value.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                    foreach (var node in nodes.EnumerateArray())
                        Read(node, open, finished);
            return new(open, finished);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Read(JsonElement node, Dictionary<string, PullRequest> open, Dictionary<string, PullRequest> finished)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        if (Text(node, "headRefName") is not { Length: > 0 } branch) return;
        if (Text(node, "url") is not { Length: > 0 } url) return;
        if (!node.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number) return;
        // From a fork: a branch of someone else's that only shares the name.
        if (node.TryGetProperty("isCrossRepository", out var fork) && fork.ValueKind == JsonValueKind.True) return;
        // Merged and closed go in one list: the later of the two is what became of the branch.
        var into = Text(node, "state") switch { "OPEN" => open, "MERGED" or "CLOSED" => finished, _ => null };
        if (into is null) return;

        var pr = new PullRequest(
            number.GetInt32(),
            Text(node, "title") ?? "",
            url,
            node.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True)
        {
            Checks = ChecksOf(node),
            UnresolvedThreads = UnresolvedIn(node),
            Review = Text(node, "reviewDecision") switch
            {
                "APPROVED" => PullRequestReview.Approved,
                "CHANGES_REQUESTED" => PullRequestReview.ChangesRequested,
                "REVIEW_REQUIRED" => PullRequestReview.Required,
                _ => PullRequestReview.None,
            },
            MergedAt = DateTimeOffset.TryParse(Text(node, "mergedAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var merged) ? merged : null,
            ClosedAt = DateTimeOffset.TryParse(Text(node, "closedAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var closed) ? closed : null,
            HeadSha = Text(node, "headRefOid") ?? "",
        };

        // Open ones come most recently updated first, and the first wins where a branch has two.
        // Finished ones go by when they finished: a comment on an old closed request updates it,
        // and must not put it ahead of the one merged after it.
        if (!into.TryGetValue(branch, out var earlier) || earlier.FinishedAt < pr.FinishedAt && into == finished)
            into[branch] = pr;
    }

    /// <summary>How many of the request's review conversations are still open, of the first hundred.</summary>
    private static int UnresolvedIn(JsonElement node)
    {
        if (!node.TryGetProperty("reviewThreads", out var threads) || threads.ValueKind != JsonValueKind.Object ||
            !threads.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            return 0;

        return nodes.EnumerateArray().Count(thread =>
            thread.ValueKind == JsonValueKind.Object &&
            thread.TryGetProperty("isResolved", out var resolved) && resolved.ValueKind == JsonValueKind.False);
    }

    /// <summary>The rollup of every check and status on the request's latest commit.</summary>
    private static PullRequestChecks ChecksOf(JsonElement node)
    {
        if (!node.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Object ||
            !commits.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array ||
            nodes.GetArrayLength() == 0)
            return PullRequestChecks.None;

        var last = nodes[nodes.GetArrayLength() - 1];
        if (last.ValueKind != JsonValueKind.Object ||
            !last.TryGetProperty("commit", out var commit) || commit.ValueKind != JsonValueKind.Object ||
            !commit.TryGetProperty("statusCheckRollup", out var rollup) || rollup.ValueKind != JsonValueKind.Object)
            return PullRequestChecks.None;

        return Text(rollup, "state") switch
        {
            "SUCCESS" => PullRequestChecks.Passing,
            "FAILURE" or "ERROR" => PullRequestChecks.Failing,
            "PENDING" or "EXPECTED" => PullRequestChecks.Pending,
            _ => PullRequestChecks.None,
        };
    }

    /// <summary>Branch names are case-sensitive to git, but not worth a mismatch over here.</summary>
    private static Dictionary<string, PullRequest> Empty() => new(StringComparer.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
