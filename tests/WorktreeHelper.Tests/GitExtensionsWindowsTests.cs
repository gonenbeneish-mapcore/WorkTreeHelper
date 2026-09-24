using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class GitExtensionsWindowsTests
{
    [Theory]
    // The shape Git Extensions gives a worktree it is browsing.
    [InlineData("mapcore2 (feature/arrow-line-endings) - Git Extensions", "mapcore2")]
    // A repository inside another one's folder names the outer one after " < ".
    [InlineData("duck-bath < WorkTreeHelper (fix/sinks-in-the-bath) - Git Extensions", "duck-bath")]
    // A folder whose own name has brackets keeps them; only the last pair is the branch.
    [InlineData("tools (old) (main) - Git Extensions", "tools (old)")]
    // No branch shown, as for a repository with no commits yet.
    [InlineData("scratch - Git Extensions", "scratch")]
    public void The_folder_is_read_out_of_the_title(string title, string folder)
        => Assert.Equal(folder, GitExtensionsWindows.FolderIn(title));

    [Theory]
    // The dashboard, before a repository is open, and a window of some other program.
    [InlineData("Git Extensions")]
    [InlineData("mapcore2 - Visual Studio Code")]
    [InlineData("")]
    public void A_window_browsing_no_worktree_names_none(string title)
        => Assert.Null(GitExtensionsWindows.FolderIn(title));
}
