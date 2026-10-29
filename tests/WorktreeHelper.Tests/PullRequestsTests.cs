using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class ParseRemoteTests
{
    [Theory]
    [InlineData("https://github.com/MapCoreGroup/mapcore.git")]
    [InlineData("https://github.com/MapCoreGroup/mapcore")]
    [InlineData("https://github.com/MapCoreGroup/mapcore/")]
    [InlineData("git@github.com:MapCoreGroup/mapcore.git")]
    [InlineData("git@github.com:MapCoreGroup/mapcore")]
    [InlineData("ssh://git@github.com/MapCoreGroup/mapcore.git")]
    [InlineData("  https://github.com/MapCoreGroup/mapcore.git\n")]
    public void Reads_the_forms_git_writes_a_github_remote_in(string url)
    {
        var repo = PullRequests.ParseRemote(url);

        Assert.NotNull(repo);
        Assert.Equal("MapCoreGroup", repo!.Owner);
        Assert.Equal("mapcore", repo.Name);
    }

    [Fact]
    public void A_name_that_merely_contains_git_keeps_all_of_it()
        => Assert.Equal("gitignore", PullRequests.ParseRemote("https://github.com/owner/gitignore.git")!.Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://gitlab.com/owner/repo.git")]
    [InlineData("git@bitbucket.org:owner/repo.git")]
    [InlineData("https://github.enterprise.local/owner/repo.git")]
    [InlineData("https://github.com/owner")]
    [InlineData("D:/git/somewhere")]
    public void Anything_that_is_not_a_github_repository_is_refused(string? url)
        => Assert.Null(PullRequests.ParseRemote(url));
}

public class ParsePullRequestsTests
{
    /// <summary>Trimmed to the fields that matter, from the real /pulls response.</summary>
    private const string TwoOpen = """
        [
          {
            "number": 628, "title": "Web-Tester - Object World tree context menus", "draft": true,
            "html_url": "https://github.com/MapCoreGroup/mapcore/pull/628",
            "head": { "ref": "feature/web-tester-object-world-tree-menu" }
          },
          {
            "number": 623, "title": "fix(mapterrain): carry the flag through the WCS layer", "draft": false,
            "html_url": "https://github.com/MapCoreGroup/mapcore/pull/623",
            "head": { "ref": "bug/fix-wcs-params" }
          }
        ]
        """;

    [Fact]
    public void Keys_each_request_by_the_branch_it_comes_from()
    {
        var open = PullRequests.Parse(TwoOpen);

        Assert.Equal(2, open.Count);
        Assert.Equal(623, open["bug/fix-wcs-params"].Number);
        Assert.Equal("https://github.com/MapCoreGroup/mapcore/pull/623", open["bug/fix-wcs-params"].Url);
        Assert.False(open["bug/fix-wcs-params"].IsDraft);
    }

    [Fact]
    public void A_draft_is_listed_and_says_so()
        => Assert.True(PullRequests.Parse(TwoOpen)["feature/web-tester-object-world-tree-menu"].IsDraft);

    [Fact]
    public void A_branch_with_no_request_is_simply_absent()
        => Assert.False(PullRequests.Parse(TwoOpen).ContainsKey("feature/ogre-upgrade13"));

    [Fact]
    public void An_entry_missing_what_a_button_needs_is_skipped()
    {
        var open = PullRequests.Parse("""
            [
              { "number": 1, "title": "no head", "html_url": "https://example.invalid/1" },
              { "title": "no number", "html_url": "https://example.invalid/2", "head": { "ref": "a" } },
              { "number": 3, "title": "no url", "head": { "ref": "b" } },
              { "number": 4, "title": "complete", "html_url": "https://example.invalid/4", "head": { "ref": "c" } }
            ]
            """);

        Assert.Single(open);
        Assert.Equal(4, open["c"].Number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "message": "Not Found" }""")]
    public void Anything_unexpected_answers_no_pull_requests(string json)
        => Assert.Empty(PullRequests.Parse(json));
}

public class ParseGraphQLTests
{
    /// <summary>Trimmed from a real answer to the query, for MapCoreGroup/mapcore: one list per branch.</summary>
    private const string Answer = """
        {"data":{"repository":{
          "b0":{"nodes":[
            {"number":688,"title":"Cloned terrain keeps its flag","url":"https://github.com/MapCoreGroup/mapcore/pull/688","isDraft":false,"state":"OPEN",
             "headRefName":"bug/cloned-terrain-display-items-flag","headRefOid":"e990cb137f1e28d37fee5dd7c9fca70f6746d3f4","mergedAt":null,
             "reviewDecision":"REVIEW_REQUIRED","commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"FAILURE"}}}]},
             "reviewThreads":{"nodes":[{"isResolved":true},{"isResolved":false},{"isResolved":false}]}}]},
          "b1":{"nodes":[
            {"number":745,"title":"Line clipping margin","url":"https://github.com/MapCoreGroup/mapcore/pull/745","isDraft":true,"state":"OPEN",
             "headRefName":"feature/line-clipping-margin","headRefOid":"dc9f23ded4507fe64efb3713d53f5ac888b42efa","mergedAt":null,
             "reviewDecision":"APPROVED","commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}}]},
          "b2":{"nodes":[
            {"number":746,"title":"No checks at all","url":"https://github.com/MapCoreGroup/mapcore/pull/746","isDraft":false,"state":"OPEN",
             "headRefName":"feature/quiet","headRefOid":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","mergedAt":null,
             "reviewDecision":null,"commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}]},
          "b3":{"nodes":[
            {"number":733,"title":"Dispatch the auto VM","url":"https://github.com/MapCoreGroup/mapcore/pull/733","isDraft":false,"state":"MERGED",
             "headRefName":"ci/dispatch-auto-vm","headRefOid":"e32ffc1559574989a11a9f903ba9e1faf6192e4f","mergedAt":"2026-10-28T11:15:59Z",
             "reviewDecision":"APPROVED","commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}},
            {"number":700,"title":"An earlier go, merged","url":"https://github.com/MapCoreGroup/mapcore/pull/700","isDraft":false,"state":"MERGED",
             "headRefName":"ci/dispatch-auto-vm","headRefOid":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","mergedAt":"2026-09-01T10:00:00Z",
             "reviewDecision":"APPROVED","commits":{"nodes":[]}}]},
          "b4":{"nodes":[
            {"number":650,"title":"Closed without merging","url":"https://github.com/MapCoreGroup/mapcore/pull/650","isDraft":false,"state":"CLOSED",
             "headRefName":"feature/abandoned","headRefOid":"cccccccccccccccccccccccccccccccccccccccc","mergedAt":null,"closedAt":"2026-10-20T08:00:00Z",
             "reviewDecision":null,"commits":{"nodes":[]}}]}
        }}}
        """;

    [Fact]
    public void Reads_the_checks_and_the_review_of_each_open_request()
    {
        var found = PullRequests.ParseGraphQL(Answer)!;

        Assert.Equal(3, found.Open.Count);
        var failing = found.Open["bug/cloned-terrain-display-items-flag"];
        Assert.Equal(PullRequestChecks.Failing, failing.Checks);
        Assert.Equal(2, failing.UnresolvedThreads);
        Assert.Equal(PullRequestReview.Required, failing.Review);
        Assert.False(failing.IsMerged);

        var approved = found.Open["feature/line-clipping-margin"];
        Assert.True(approved.IsDraft);
        Assert.Equal(PullRequestChecks.Passing, approved.Checks);
        Assert.Equal(PullRequestReview.Approved, approved.Review);

        var quiet = found.Open["feature/quiet"];
        Assert.Equal(PullRequestChecks.None, quiet.Checks);
        Assert.Equal(0, quiet.UnresolvedThreads);
        Assert.Equal(PullRequestReview.None, quiet.Review);
    }

    [Fact]
    public void A_request_closed_without_merging_is_finished_and_says_so()
    {
        var found = PullRequests.ParseGraphQL(Answer)!;
        Assert.False(found.Open.ContainsKey("feature/abandoned"));

        var closed = found.Finished["feature/abandoned"];
        Assert.True(closed.IsClosed);
        Assert.False(closed.IsMerged);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), closed.ClosedAt);
    }

    [Fact]
    public void Of_two_finished_requests_on_one_branch_the_latest_counts()
        => Assert.Equal(733, PullRequests.ParseGraphQL(Answer)!.Finished["ci/dispatch-auto-vm"].Number);

    [Fact]
    public void The_branch_names_go_in_as_variables_not_into_the_query()
    {
        var query = PullRequests.QueryFor(3);
        Assert.Contains("$b0: String!, $b1: String!, $b2: String!", query);
        Assert.Contains("b2: pullRequests(headRefName: $b2, states: [OPEN, MERGED, CLOSED]", query);
        Assert.DoesNotContain("$b3", query);
    }

    [Fact]
    public void A_merged_request_says_when_and_what_went_in()
    {
        var merged = PullRequests.ParseGraphQL(Answer)!.Finished["ci/dispatch-auto-vm"];

        Assert.True(merged.IsMerged);
        Assert.False(merged.IsClosed);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 11, 15, 59, TimeSpan.Zero), merged.MergedAt);
        Assert.Equal("e32ffc1559574989a11a9f903ba9e1faf6192e4f", merged.HeadSha);
        Assert.Equal("https://github.com/MapCoreGroup/mapcore/pull/733", merged.Url);
    }

    [Theory]
    [InlineData("PENDING", PullRequestChecks.Pending)]
    [InlineData("EXPECTED", PullRequestChecks.Pending)]
    [InlineData("ERROR", PullRequestChecks.Failing)]
    [InlineData("SOMETHING_NEW", PullRequestChecks.None)]
    public void Every_rollup_state_has_a_reading(string state, PullRequestChecks expected)
    {
        var json = Answer.Replace("\"state\":\"FAILURE\"", $"\"state\":\"{state}\"");
        Assert.Equal(expected, PullRequests.ParseGraphQL(json)!.Open["bug/cloned-terrain-display-items-flag"].Checks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "message": "Bad credentials" }""")]
    [InlineData("""{ "data": { "repository": null }, "errors": [ { "type": "NOT_FOUND" } ] }""")]
    public void An_answer_without_the_repository_is_no_answer(string json)
        => Assert.Null(PullRequests.ParseGraphQL(json));
}

public class ReviewFixTests
{
    private static string Quoted(string? text) => text is null ? "null" : "\"" + text + "\"";

    private static string Node(int number, string state, string branch, string? finishedAt = null, bool fork = false) =>
        "{\"number\":" + number + ",\"title\":\"t\",\"url\":\"https://github.com/o/r/pull/" + number + "\",\"isDraft\":false," +
        "\"state\":\"" + state + "\",\"isCrossRepository\":" + (fork ? "true" : "false") + "," +
        "\"headRefName\":\"" + branch + "\",\"headRefOid\":\"" + new string('a', 40) + "\"," +
        "\"mergedAt\":" + Quoted(state == "MERGED" ? finishedAt : null) + ",\"closedAt\":" + Quoted(finishedAt) + "," +
        "\"reviewDecision\":null,\"commits\":{\"nodes\":[]}}";

    private static string Answer(params string[] nodes) =>
        "{\"data\":{\"repository\":{\"b0\":{\"nodes\":[" + string.Join(",", nodes) + "]}}}}";

    [Fact]
    public void A_request_from_a_fork_with_the_same_branch_name_is_left_out()
    {
        var found = PullRequests.ParseGraphQL(Answer(Node(1, "OPEN", "main", fork: true), Node(2, "CLOSED", "main", "2026-10-01T00:00:00Z", fork: true)))!;
        Assert.Empty(found.Open);
        Assert.Empty(found.Finished);
    }

    [Fact]
    public void The_request_that_finished_last_counts_not_the_one_touched_last()
    {
        // Most recently updated first: the old closed one was commented on after the merge.
        var found = PullRequests.ParseGraphQL(Answer(
            Node(10, "CLOSED", "feature/x", "2026-09-01T00:00:00Z"),
            Node(20, "MERGED", "feature/x", "2026-10-01T00:00:00Z")))!;

        Assert.Equal(20, found.Finished["feature/x"].Number);
    }

    [Fact]
    public void The_REST_list_leaves_out_a_fork_request_too()
    {
        var open = PullRequests.Parse("""
            [
              { "number": 1, "title": "from a fork", "html_url": "https://github.com/o/r/pull/1",
                "head": { "ref": "main", "repo": { "full_name": "someone/r" } }, "base": { "repo": { "full_name": "o/r" } } },
              { "number": 2, "title": "from here", "html_url": "https://github.com/o/r/pull/2",
                "head": { "ref": "feature/x", "repo": { "full_name": "o/r" } }, "base": { "repo": { "full_name": "o/r" } } }
            ]
            """);

        Assert.False(open.ContainsKey("main"));
        Assert.Equal(2, open["feature/x"].Number);
    }
}
