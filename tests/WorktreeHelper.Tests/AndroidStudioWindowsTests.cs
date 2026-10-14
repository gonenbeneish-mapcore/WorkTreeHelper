using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class AndroidStudioWindowsTests
{
    private const string Home = @"C:\Users\someone";

    [Theory]
    // Only the project, as when it is the one open of that name.
    [InlineData("McAndroidTester \u2013 MainActivity.java", "McAndroidTester", null)]
    [InlineData("McAndroidTester", "McAndroidTester", null)]
    // A bracket after the file is the module the file belongs to, not a path.
    [InlineData("McAndroidTester \u2013 MainActivity.java [McAndroidTester.app.main]", "McAndroidTester", null)]
    // The file may be named by its whole path.
    [InlineData(@"McAndroidTester – C:\git\mapcore3\CMakeLists.txt", "McAndroidTester", null)]
    // Two projects of one name: each gets its path, the user's own folder written as ~.
    [InlineData(@"McAndroidTester [D:\git\mapcore2\MapCore\Android\Test\McAndroidTester] – build.gradle",
        "McAndroidTester", @"D:\git\mapcore2\MapCore\Android\Test\McAndroidTester")]
    [InlineData(@"McAndroidTester [~\AndroidStudioProjects\McAndroidTester] – build.gradle",
        "McAndroidTester", @"C:\Users\someone\AndroidStudioProjects\McAndroidTester")]
    // A path with the separator in it is not cut short.
    [InlineData(@"App [D:\a - b\App] - Main.kt", "App", @"D:\a - b\App")]
    public void The_project_is_read_out_of_the_title(string title, string name, string? folder)
    {
        var project = AndroidStudioWindows.ProjectIn(title, Home);

        Assert.NotNull(project);
        Assert.Equal(name, project.Value.Name);
        Assert.Equal(folder, project.Value.Folder);
    }

    [Fact]
    public void A_window_with_no_title_names_no_project()
        => Assert.Null(AndroidStudioWindows.ProjectIn("", Home));

    private const string Recent = """
        <application>
          <component name="RecentProjectsManager">
            <option name="additionalInfo">
              <map>
                <entry key="$USER_HOME$/AndroidStudioProjects/Other">
                  <value>
                    <RecentProjectMetaInfo frameTitle="Other – Main.kt" opened="true">
                      <option name="activationTimestamp" value="3000" />
                      <option name="projectOpenTimestamp" value="2000" />
                    </RecentProjectMetaInfo>
                  </value>
                </entry>
                <entry key="C:/git/mapcore/MapCore/Android/Test/McAndroidTester">
                  <value>
                    <RecentProjectMetaInfo frameTitle="McAndroidTester">
                      <option name="activationTimestamp" value="9000" />
                    </RecentProjectMetaInfo>
                  </value>
                </entry>
                <entry key="C:/git/mapcore3/MapCore/Android/Test/McAndroidTester">
                  <value>
                    <RecentProjectMetaInfo frameTitle="McAndroidTester – C:\git\mapcore3\CMakeLists.txt" opened="true">
                      <option name="activationTimestamp" value="5000" />
                      <option name="projectOpenTimestamp" value="4000" />
                    </RecentProjectMetaInfo>
                  </value>
                </entry>
              </map>
            </option>
          </component>
        </application>
        """;

    [Fact]
    public void Only_the_projects_marked_open_are_read_from_the_recent_list()
    {
        var open = AndroidStudioWindows.OpenedIn(Recent, Home);

        Assert.Equal(2, open.Count);
        Assert.Contains(open, p => p.Folder == @"C:\Users\someone\AndroidStudioProjects\Other" && p.Name == "Other");
        Assert.Contains(open, p => p.Folder == @"C:\git\mapcore3\MapCore\Android\Test\McAndroidTester"
                                   && p.Name == "McAndroidTester" && p.Opened == 5000 && p.Activated == 5000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<not xml")]
    public void A_recent_list_that_cannot_be_read_marks_nothing_open(string? xml)
        => Assert.Empty(AndroidStudioWindows.OpenedIn(xml, Home));

    [Fact]
    public void A_window_naming_only_its_project_takes_the_folder_the_recent_list_has_open()
    {
        var recent = AndroidStudioWindows.OpenedIn(Recent, Home);

        var found = AndroidStudioWindows.Resolve([(1, "McAndroidTester \u2013 build.gradle")], recent, since: 1000, Home);

        Assert.Equal([((IntPtr)1, @"C:\git\mapcore3\MapCore\Android\Test\McAndroidTester")], found);
    }

    [Fact]
    public void A_project_left_marked_open_by_an_earlier_run_is_not_taken_for_the_one_open_now()
    {
        // Android Studio started after the project was last opened or used: it was killed with
        // the project open, and this window is some other McAndroidTester not yet written down.
        var recent = AndroidStudioWindows.OpenedIn(Recent, Home);

        var found = AndroidStudioWindows.Resolve([(1, "McAndroidTester \u2013 build.gradle")], recent, since: 6000, Home);

        Assert.Empty(found);
    }

    [Fact]
    public void Of_several_marked_open_the_one_used_last_is_taken()
    {
        var recent = new List<AndroidStudioWindows.RecentProject>
        {
            new(@"D:\git\mapcore2\App", "App", 5000, 5000),
            new(@"D:\git\mapcore4\App", "App", 7000, 7000),
        };

        var found = AndroidStudioWindows.Resolve([(1, "App")], recent, since: 0, Home);

        Assert.Equal(@"D:\git\mapcore4\App", Assert.Single(found).Folder);
    }

    [Fact]
    public void A_window_whose_title_has_the_path_needs_no_recent_list()
    {
        var found = AndroidStudioWindows.Resolve(
            [(1, @"App [D:\git\mapcore2\App] – Main.kt"), (2, @"App [D:\git\mapcore4\App] – Main.kt")],
            [], since: 0, Home);

        Assert.Equal([((IntPtr)1, @"D:\git\mapcore2\App"), ((IntPtr)2, @"D:\git\mapcore4\App")], found);
    }

    [Fact]
    public void The_welcome_screen_and_dialogs_have_no_project()
    {
        var recent = AndroidStudioWindows.OpenedIn(Recent, Home);

        var found = AndroidStudioWindows.Resolve([(1, "Welcome to Android Studio"), (2, "Settings")], recent, since: 0, Home);

        Assert.Empty(found);
    }
}
