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
