using System.IO;
using System.Windows.Media;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class AppIconThemeTests
{
    [Theory]
    [InlineData(32, 32, 32, true)]      // the dark taskbar
    [InlineData(0, 0, 0, true)]
    [InlineData(243, 243, 243, false)]  // the light one
    [InlineData(255, 255, 255, false)]
    public void Picks_the_disc_that_is_the_opposite_of_the_background(byte r, byte g, byte b, bool dark)
        => Assert.Equal(dark, AppIcon.IsDark(Color.FromRgb(r, g, b)));

    [Fact]
    public void Green_is_read_by_what_the_eye_makes_of_it_rather_than_by_its_parts()
    {
        // 0,128,0 is half of one channel but reads bright, because green carries most of
        // the luminance. A naive average would call this dark and pick the wrong disc.
        Assert.False(AppIcon.IsDark(Color.FromRgb(0, 180, 0)));
        Assert.True(AppIcon.IsDark(Color.FromRgb(20, 83, 45)));
    }
}

public class AppIconFrameTests
{
    /// <summary>The icons live at the root of the repository, above the test assembly.</summary>
    private static string? FindIcon(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Theory]
    [InlineData("app.ico")]
    [InlineData("app-dark.ico")]
    public void Every_size_the_shell_asks_for_is_in_the_file(string name)
    {
        if (FindIcon(name) is not { } path) return;   // not a checkout: nothing to say
        var ico = File.ReadAllBytes(path);

        // What the tray, the title bar and the Alt+Tab list ask for.
        foreach (var size in new[] { 16, 20, 24, 32, 48, 64, 256 })
        {
            var frame = AppIcon.Frame(ico, size);
            Assert.Equal(size, frame.Width);
            Assert.True(frame.Length > 0, $"{name} has no bytes for its {size}px frame");
            Assert.True(frame.Offset + frame.Length <= ico.Length,
                $"{name}'s {size}px frame runs past the end of the file");
        }
    }

    [Fact]
    public void A_size_that_is_not_in_the_file_gets_the_nearest_one()
    {
        if (FindIcon("app.ico") is not { } path) return;
        var ico = File.ReadAllBytes(path);

        Assert.Equal(20, AppIcon.Frame(ico, 18).Width);    // between 16 and 20, nearer 20
        Assert.Equal(48, AppIcon.Frame(ico, 40).Width);    // a 150% display's tray size
        Assert.Equal(256, AppIcon.Frame(ico, 512).Width);  // larger than anything in it
    }
}
