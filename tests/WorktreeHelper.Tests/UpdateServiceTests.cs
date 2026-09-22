using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class ParseTagTests
{
    [Theory]
    [InlineData("v1.3.0", "1.3.0")]
    [InlineData("V1.3.0", "1.3.0")]
    [InlineData("1.3.0", "1.3.0")]
    [InlineData("  v2.0.1  ", "2.0.1")]
    public void Reads_the_shapes_a_release_tag_comes_in(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), UpdateService.ParseTag(tag));

    [Fact]
    public void A_two_part_tag_matches_a_three_part_version()
    {
        // "1.3" and a file version of "1.3.0.0" are the same release; Version would otherwise
        // treat the absent parts as -1 and call them different.
        Assert.Equal(UpdateService.ParseTag("1.3.0.0"), UpdateService.ParseTag("v1.3"));
        Assert.Equal(UpdateService.ParseTag("1.3"), UpdateService.ParseTag("1.3.0"));
    }

    [Fact]
    public void A_suffix_is_ignored() => Assert.Equal(new Version(1, 3, 0), UpdateService.ParseTag("v1.3.0-beta.2"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    public void Anything_that_is_not_a_version_is_refused(string? tag) => Assert.Null(UpdateService.ParseTag(tag));

    [Fact]
    public void Versions_order_the_way_the_check_relies_on()
    {
        Assert.True(UpdateService.ParseTag("v1.3.0") > UpdateService.ParseTag("v1.2.0"));
        Assert.True(UpdateService.ParseTag("v1.10.0") > UpdateService.ParseTag("v1.9.0"));
        Assert.False(UpdateService.ParseTag("v1.2.0") > UpdateService.ParseTag("v1.2.0"));
    }
}

public class SummariseNotesTests
{
    [Fact]
    public void Keeps_the_lines_and_drops_the_page_furniture()
    {
        var summary = UpdateService.Summarise("""
            ## What is new

            - **It updates itself.** A button appears when a release is newer.
            - A `PR` button opens the pull request for that branch.

            ---
            """);

        Assert.Equal(
            "- It updates itself. A button appears when a release is newer." + "\n" +
            "- A PR button opens the pull request for that branch.",
            summary);
    }

    [Fact]
    public void Stops_after_a_few_lines_and_says_there_is_more()
    {
        var many = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"- line {i}"));
        var summary = UpdateService.Summarise(many, maxLines: 3);

        Assert.Equal("- line 1\n- line 2\n- line 3\n…", summary);
    }

    [Fact]
    public void Stops_before_a_tooltip_becomes_a_wall()
    {
        var summary = UpdateService.Summarise(string.Join("\n", Enumerable.Repeat(new string('x', 80), 20)), maxChars: 200);

        Assert.True(summary.Length <= 250, $"summary was {summary.Length} characters");
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void Ignores_a_byte_order_mark_at_the_front()
    {
        // What GitHub returns for a release whose notes were written from PowerShell.
        var summary = UpdateService.Summarise("\uFEFF## What is new\n\n- **It updates itself.** A button appears.");

        Assert.Equal("- It updates itself. A button appears.", summary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("## Only a heading")]
    [InlineData("\uFEFF## Only a heading")]
    public void Notes_with_nothing_in_them_add_nothing(string? body)
        => Assert.Equal("", UpdateService.Summarise(body));
}

public class ParseReleaseTests
{
    /// <summary>Trimmed to the fields that matter, from the real releases/latest response.</summary>
    private static string Release(string tag = "v1.3.0", bool draft = false, bool prerelease = false,
                                  string asset = "WorkTreeHelper-V1.3.zip") => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/owner/repo/releases/tag/{{tag}}",
          "assets": [
            { "name": "{{asset}}", "browser_download_url": "https://github.com/owner/repo/releases/download/{{tag}}/{{asset}}" }
          ]
        }
        """;

    [Fact]
    public void Reads_the_version_the_zip_and_where_to_get_it()
    {
        var release = UpdateService.ParseRelease(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 3, 0), release!.Version);
        Assert.Equal("v1.3.0", release.Name);
        Assert.Equal("WorkTreeHelper-V1.3.zip", release.AssetName);
        Assert.EndsWith("WorkTreeHelper-V1.3.zip", release.AssetUrl);
        Assert.Contains("releases/tag/v1.3.0", release.PageUrl);
    }

    [Fact]
    public void A_draft_is_not_offered() => Assert.Null(UpdateService.ParseRelease(Release(draft: true)));

    [Fact]
    public void A_prerelease_is_not_offered() => Assert.Null(UpdateService.ParseRelease(Release(prerelease: true)));

    [Fact]
    public void A_release_with_no_zip_is_not_offered()
        => Assert.Null(UpdateService.ParseRelease(Release(asset: "notes.txt")));

    [Fact]
    public void A_release_whose_tag_is_not_a_version_is_not_offered()
        => Assert.Null(UpdateService.ParseRelease(Release(tag: "nightly")));

    [Fact]
    public void The_zip_is_picked_out_of_whatever_else_is_attached()
    {
        var release = UpdateService.ParseRelease("""
            {
              "tag_name": "v2.0.0", "name": "v2.0.0", "draft": false, "prerelease": false,
              "html_url": "https://example.invalid",
              "assets": [
                { "name": "notes.txt", "browser_download_url": "https://example.invalid/notes.txt" },
                { "name": "App-V2.0.zip", "browser_download_url": "https://example.invalid/App-V2.0.zip" }
              ]
            }
            """);

        Assert.Equal("App-V2.0.zip", release!.AssetName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "message": "Not Found" }""")]
    public void Anything_unexpected_answers_no_update(string json)
        => Assert.Null(UpdateService.ParseRelease(json));

    [Fact]
    public void The_running_version_is_known()
    {
        // Whatever the test host reports, the check needs something to compare against.
        Assert.NotNull(UpdateService.Current);
        Assert.True(UpdateService.Current >= new Version(0, 0, 0));
    }
}
