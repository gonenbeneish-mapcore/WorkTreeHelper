using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class WhatsNewParseTests
{
    private const string Sample = """
        # Changelog

        ## v1.5.0 — unreleased

        - **Options**, behind a gear in the title bar: where the app lives,
          and whether the buttons are packed.
        - A `PR` button, see [the release](https://example.invalid/r).

        ## v1.4.2

        - One line.
          - Under it.
        """;

    [Fact]
    public void Reads_each_version_including_one_marked_unreleased()
    {
        var notes = WhatsNew.Parse(Sample);
        Assert.Equal([new Version(1, 5, 0), new Version(1, 4, 2)], notes.Select(n => n.Version));
    }

    [Fact]
    public void Joins_a_point_the_file_wrapped_by_hand()
    {
        var first = WhatsNew.Parse(Sample)[0].Points[0];
        Assert.Equal("Options, behind a gear in the title bar: where the app lives, and whether the buttons are packed.", first.Text);
    }

    [Fact]
    public void Takes_the_markdown_off()
        => Assert.Equal("A PR button, see the release.", WhatsNew.Parse(Sample)[0].Points[1].Text);

    [Fact]
    public void Keeps_a_point_under_another_as_its_own_indented_point()
    {
        var points = WhatsNew.Parse(Sample)[1].Points;
        Assert.Equal(2, points.Count);
        Assert.Equal(0, points[0].Level);
        Assert.Equal(new ChangePoint("Under it.", 1), points[1]);
    }
}

public class WhatsNewSinceTests
{
    private static readonly IReadOnlyList<ReleaseNotes> All =
    [
        new(new Version(1, 5, 0), []),
        new(new Version(1, 4, 2), []),
        new(new Version(1, 4, 1), []),
        new(new Version(1, 4, 0), []),
    ];

    private static Version[] Since(string? seen, string current)
        => WhatsNew.Since(All, seen is null ? null : Version.Parse(seen), Version.Parse(current))
                   .Select(n => n.Version).ToArray();

    [Fact]
    public void Everything_newer_than_what_was_seen_newest_first()
        => Assert.Equal([new Version(1, 5, 0), new Version(1, 4, 2)], Since("1.4.1", "1.5.0"));

    [Fact]
    public void Nothing_seen_before_shows_only_the_running_version()
        => Assert.Equal([new Version(1, 5, 0)], Since(null, "1.5.0"));

    [Fact]
    public void Nothing_once_the_running_version_has_been_seen()
        => Assert.Empty(Since("1.5.0", "1.5.0"));

    [Fact]
    public void Never_what_is_newer_than_the_copy_running()
        => Assert.Equal([new Version(1, 4, 2)], Since("1.4.1", "1.4.2"));
}

public class WhatsNewChangelogTests
{
    private static string? FindUp(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void The_version_being_built_has_notes_the_app_can_show()
    {
        // The fold is only as good as the changelog: a release built without a section for
        // its own version would open nothing, and say nothing about why.
        if (FindUp("WorktreeHelper.csproj") is not { } csproj || FindUp("CHANGELOG.md") is not { } changelog) return;

        var version = Version.Parse(Regex.Match(File.ReadAllText(csproj), "<Version>([^<]+)</Version>").Groups[1].Value);
        var section = WhatsNew.Parse(File.ReadAllText(changelog)).FirstOrDefault(n => n.Version == version);

        Assert.NotNull(section);
        Assert.NotEmpty(section.Points);
        Assert.All(section.Points, p => Assert.DoesNotContain("**", p.Text));
    }
}

public class LastSeenVersionTests
{
    [Fact]
    public void A_file_from_before_it_existed_has_seen_nothing()
        => Assert.Null(JsonSerializer.Deserialize<Settings>("""{ "AskedAboutTaskbar": true }""")!.LastSeenVersion);

    [Fact]
    public void Being_new_is_never_written_down()
        => Assert.DoesNotContain("IsNew", JsonSerializer.Serialize(new Settings()));
}
