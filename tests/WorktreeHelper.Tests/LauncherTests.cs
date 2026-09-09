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

    private sealed class TempFolder : IDisposable
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
