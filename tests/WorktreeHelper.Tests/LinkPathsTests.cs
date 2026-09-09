using System.Diagnostics;
using System.IO;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class LinkPathsTests
{
    [Fact]
    public void A_folder_reached_without_crossing_a_link_needs_no_rewriting()
        => Assert.Null(LinkPaths.For(Path.GetTempPath()));

    [Fact]
    public void A_path_that_does_not_exist_is_not_a_link()
        => Assert.Null(LinkPaths.For(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void The_same_folder_under_two_names_is_recognised()
    {
        using var tree = LinkedTree.Create();
        if (tree is null) return; // links cannot be made here; nothing to assert

        Assert.True(LinkPaths.SameFolder(tree.Link, tree.Target));
        Assert.False(LinkPaths.SameFolder(tree.Link, Path.GetTempPath()));
    }

    [Fact]
    public void Git_paths_come_back_under_the_name_the_user_chose()
    {
        using var tree = LinkedTree.Create();
        if (tree is null) return;

        var map = LinkPaths.For(tree.Link);
        Assert.NotNull(map);
        Assert.Equal(tree.Link, map!.Prefer(tree.Target), ignoreCase: true);
    }

    [Fact]
    public void A_sibling_of_the_chosen_link_is_rewritten_too()
    {
        // The layout this exists for: C:\git\repo -> D:\git\repo, with the repository's
        // other worktrees sitting beside it as C:\git\repo2 -> D:\git\repo2.
        using var tree = LinkedTree.Create(sibling: true);
        if (tree is null) return;

        var map = LinkPaths.For(tree.Link);
        Assert.NotNull(map);
        Assert.Equal(tree.SiblingLink, map!.Prefer(tree.SiblingTarget), ignoreCase: true);
    }

    [Fact]
    public void A_path_outside_the_link_is_left_alone()
    {
        using var tree = LinkedTree.Create();
        if (tree is null) return;

        var map = LinkPaths.For(tree.Link);
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere");
        Assert.Equal(outside, map!.Prefer(outside));
    }

    [Fact]
    public void A_name_the_link_cannot_reach_keeps_the_path_git_gave()
    {
        using var tree = LinkedTree.Create();
        if (tree is null) return;

        // Same prefix, but no link stands where the rewrite would point.
        var unmapped = Path.Combine(Path.GetDirectoryName(tree.Target)!, "not-linked");
        Directory.CreateDirectory(unmapped);

        var map = LinkPaths.For(tree.Link);
        Assert.Equal(unmapped, map!.Prefer(unmapped));
    }

    [Fact]
    public void A_folder_contains_itself()
        => Assert.True(LinkPaths.IsUnder(Path.GetTempPath(), Path.GetTempPath()));

    [Fact]
    public void A_solution_inside_a_worktree_belongs_to_it()
    {
        using var tree = LinkedTree.Create();
        if (tree is null) return;

        var solution = Path.Combine(tree.Target, "VS", "MapCore.slnx");
        Assert.True(LinkPaths.IsUnder(solution, tree.Target));
        // ...and still does when the two are named through different links.
        Assert.True(LinkPaths.IsUnder(solution, tree.Link));
        Assert.True(LinkPaths.IsUnder(Path.Combine(tree.Link, "VS", "MapCore.slnx"), tree.Target));
    }

    [Fact]
    public void A_sibling_worktree_whose_name_starts_the_same_is_not_inside()
    {
        // The case that makes this a prefix test and not a StartsWith: repo2 is not in repo,
        // and both generate a solution of the same name.
        using var tree = LinkedTree.Create(sibling: true);
        if (tree is null) return;

        Assert.False(LinkPaths.IsUnder(Path.Combine(tree.SiblingTarget, "VS", "MapCore.slnx"), tree.Target));
        Assert.False(LinkPaths.IsUnder(tree.SiblingTarget, tree.Target));
    }

    [Fact]
    public void Enumerating_visual_studio_instances_does_not_throw()
    {
        // Exercises the Running Object Table interop. With no Visual Studio running the list
        // is empty; a wrong signature or a missing bind context would show up here.
        Assert.NotNull(VisualStudioInstances.Open());
    }

    /// <summary>
    /// A physical folder plus a junction pointing at it, laid out as
    /// root\logical\repo -> root\physical\repo.
    /// </summary>
    /// <remarks>
    /// Junctions rather than symbolic links: creating a symbolic link needs Developer Mode
    /// or elevation, a junction needs neither. Create returns null where even that is not
    /// allowed, and those tests then assert nothing rather than failing on the environment.
    /// </remarks>
    private sealed class LinkedTree : IDisposable
    {
        public required string Root { get; init; }
        public required string Target { get; init; }
        public required string Link { get; init; }
        public string SiblingTarget => Target + "2";
        public string SiblingLink => Link + "2";

        public static LinkedTree? Create(bool sibling = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "wth-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(root, "physical", "repo");
            var link = Path.Combine(root, "logical", "repo");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);

            var tree = new LinkedTree { Root = root, Target = target, Link = link };
            if (!Junction(link, target)) { tree.Dispose(); return null; }

            if (sibling)
            {
                Directory.CreateDirectory(tree.SiblingTarget);
                if (!Junction(tree.SiblingLink, tree.SiblingTarget)) { tree.Dispose(); return null; }
            }
            return tree;
        }

        private static bool Junction(string link, string target)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (p is null) return false;
                p.WaitForExit(10_000);
                return p.ExitCode == 0 && Directory.Exists(link);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            // Delete the junctions first: removing them must not follow through to the
            // folders they point at.
            foreach (var link in new[] { Link, Link + "2" })
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
