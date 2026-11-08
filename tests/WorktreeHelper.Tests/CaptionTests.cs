using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace WorktreeHelper.Tests;

/// <summary>
/// The window draws its own caption, so the buttons every other window gets for free are only
/// there if MainWindow.xaml puts them there. Read from the source rather than by building the
/// window, which would take a tray icon and the single-instance lock with it.
/// </summary>
public class CaptionTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XDocument MainWindowXaml([CallerFilePath] string here = "")
        => XDocument.Load(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "MainWindow.xaml"));

    private static XElement ButtonNamed(XDocument xaml, string name)
        => xaml.Descendants(Wpf + "Button").Single(b => (string?)b.Attribute("AutomationProperties.Name") == name);

    [Fact]
    public void There_is_a_minimize_button_and_it_minimizes()
    {
        var minimize = ButtonNamed(MainWindowXaml(), "Minimize");
        Assert.Equal("Minimize_Click", (string?)minimize.Attribute("Click"));
    }

    [Fact]
    public void The_minimize_button_is_only_there_in_the_taskbar()
    {
        // In the tray, Close puts the window away and there is no taskbar button to go to.
        var visibility = (string?)ButtonNamed(MainWindowXaml(), "Minimize").Attribute("Visibility");
        Assert.NotNull(visibility);
        Assert.Contains("Binding LivesInTaskbar", visibility);
        Assert.DoesNotContain(".Not", visibility);
    }

    [Fact]
    public void The_minimize_button_sits_just_before_close()
    {
        var xaml = MainWindowXaml();
        var minimize = ButtonNamed(xaml, "Minimize");
        Assert.Same(ButtonNamed(xaml, "Close"), minimize.ElementsAfterSelf().FirstOrDefault());
    }

    [Fact]
    public void The_window_keeps_its_minimize_box()
    {
        // Without WS_MINIMIZEBOX the shell refuses to minimise the window at all: neither the
        // button above nor a click on the taskbar button would put it down.
        var resizeMode = (string?)MainWindowXaml().Root!.Attribute("ResizeMode");
        Assert.NotEqual("NoResize", resizeMode);
    }
}
