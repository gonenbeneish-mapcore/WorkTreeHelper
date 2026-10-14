using System.IO;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class VisualStudioScriptTests
{
    [Fact]
    public void A_folder_without_the_script_does_not_offer_the_button()
    {
        using var folder = new TempFolder();
        Assert.False(Launcher.HasVisualStudioScript(folder.Path));
    }

    [Fact]
    public void A_folder_holding_the_script_offers_it()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, Launcher.VisualStudioScript), "@echo off");
        Assert.True(Launcher.HasVisualStudioScript(folder.Path));
    }

    [Fact]
    public void Another_batch_file_is_not_mistaken_for_it()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "PackRelease.bat"), "@echo off");
        Assert.False(Launcher.HasVisualStudioScript(folder.Path));
    }

    [Fact]
    public void A_folder_that_is_not_there_offers_nothing()
        => Assert.False(Launcher.HasVisualStudioScript(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))));

    internal sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wth-vs-" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}

public class AndroidProjectTests
{
    private const string Project = @"MapCore\Android\Test\McAndroidTester";

    private static VisualStudioScriptTests.TempFolder WorktreeWith(string project, string gradleFile)
    {
        var worktree = new VisualStudioScriptTests.TempFolder();
        var folder = Path.Combine(worktree.Path, project);
        Directory.CreateDirectory(folder);
        if (gradleFile.Length > 0) File.WriteAllText(Path.Combine(folder, gradleFile), "");
        return worktree;
    }

    [Theory]
    [InlineData("settings.gradle")]
    [InlineData("settings.gradle.kts")]
    [InlineData("build.gradle")]
    [InlineData("build.gradle.kts")]
    public void A_folder_with_a_Gradle_build_is_an_Android_project(string gradleFile)
    {
        using var worktree = WorktreeWith(Project, gradleFile);
        Assert.True(Launcher.HasAndroidProject(worktree.Path, Project));
    }

    [Fact]
    public void A_folder_with_no_Gradle_build_is_not_one()
    {
        using var worktree = WorktreeWith(Project, "");
        Assert.False(Launcher.HasAndroidProject(worktree.Path, Project));
    }

    [Fact]
    public void An_empty_path_is_the_worktree_itself_and_counts_only_with_a_Gradle_build()
    {
        using var bare = new VisualStudioScriptTests.TempFolder();
        Assert.False(Launcher.HasAndroidProject(bare.Path, ""));

        using var gradle = WorktreeWith("", "settings.gradle");
        Assert.True(Launcher.HasAndroidProject(gradle.Path, ""));
    }

    [Fact]
    public void A_full_path_is_no_worktree_s_project()
    {
        using var worktree = WorktreeWith(Project, "settings.gradle");
        Assert.False(Launcher.HasAndroidProject(worktree.Path, Path.Combine(worktree.Path, Project)));
    }

    [Theory]
    [InlineData(@"MapCore\Android\Test\McAndroidTester", @"MapCore\Android\Test\McAndroidTester")]
    [InlineData(@"  MapCore/Android/Test/McAndroidTester/ ", @"MapCore\Android\Test\McAndroidTester")]
    [InlineData(@"\Android\App\", @"Android\App")]
    [InlineData(@"""MapCore\Android""", @"MapCore\Android")]
    [InlineData("", "")]
    [InlineData(null, "")]
    // A full path from inside a worktree, as pasted from Explorer.
    [InlineData(@"D:\git\mapcore2\MapCore\Android\Test\McAndroidTester", @"MapCore\Android\Test\McAndroidTester")]
    [InlineData(@"D:\git\mapcore2", "")]
    // From somewhere else altogether: kept, and so matches no worktree.
    [InlineData(@"E:\elsewhere\App", @"E:\elsewhere\App")]
    public void The_folder_is_kept_relative_to_the_worktree(string? typed, string kept)
        => Assert.Equal(kept, Launcher.RelativeAndroidProject(typed, [@"D:\git\mapcore", @"D:\git\mapcore2"]));
}
