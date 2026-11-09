using System.Diagnostics;
using System.IO;
using WorktreeHelper;
using Xunit;

namespace WorktreeHelper.Tests;

public class FreshEnvironmentTests
{
    [Fact]
    public void Windows_own_variables_are_there()
    {
        var variables = FreshEnvironment.Read();
        Assert.False(string.IsNullOrEmpty(variables.GetValueOrDefault("PATH")));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.Windows), variables["SystemRoot"], ignoreCase: true);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), variables["USERPROFILE"], ignoreCase: true);
    }

    [Fact]
    public void Names_are_matched_ignoring_case()
        => Assert.Equal(FreshEnvironment.Read()["PATH"], FreshEnvironment.Read()["Path"]);

    [Fact]
    public void A_variable_only_this_process_has_is_left_out()
    {
        var name = OnlyHere();
        try
        {
            Assert.False(FreshEnvironment.Read().ContainsKey(name));
            Assert.Null(FreshEnvironment.Get(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task A_started_program_does_not_see_this_process_own_variables()
    {
        var name = OnlyHere();
        try
        {
            var output = await Commands.RunAsync("cmd.exe", $"/d /c echo %{name}%", Path.GetTempPath(), CancellationToken.None);
            // cmd leaves an unset variable as it was written.
            Assert.Equal($"%{name}%", output.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task A_variable_the_caller_sets_afterwards_is_kept()
    {
        var output = await Commands.RunAsync("cmd.exe", "/d /c echo %WTH_SET_BY_CALLER%", Path.GetTempPath(), CancellationToken.None,
            environment: ("WTH_SET_BY_CALLER", "kept"));
        Assert.Equal("kept", output.Trim());
    }

    [Fact]
    public void Applying_replaces_the_environment_and_finds_the_program()
    {
        var name = OnlyHere();
        try
        {
            var psi = new ProcessStartInfo("cmd") { UseShellExecute = false };
            FreshEnvironment.ApplyTo(psi);
            Assert.False(psi.Environment.ContainsKey(name));
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), psi.FileName, ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Applying_to_a_shell_start_is_refused()
        => Assert.Throws<InvalidOperationException>(() => FreshEnvironment.ApplyTo(new ProcessStartInfo("cmd") { UseShellExecute = true }));

    [Fact]
    public void A_program_is_found_on_the_given_PATH_not_this_process_own()
    {
        using var folder = new VisualStudioScriptTests.TempFolder();
        var tool = Path.Combine(folder.Path, "wth-tool.exe");
        File.WriteAllText(tool, "");

        var found = FreshEnvironment.Resolve("wth-tool", new Dictionary<string, string> { ["PATH"] = @"C:\nowhere;" + folder.Path });
        Assert.Equal(tool, found);
    }

    [Fact]
    public void A_PATH_entry_in_quotes_is_still_searched()
    {
        using var folder = new VisualStudioScriptTests.TempFolder();
        var tool = Path.Combine(folder.Path, "wth-tool.exe");
        File.WriteAllText(tool, "");

        Assert.Equal(tool, FreshEnvironment.Resolve("wth-tool.exe", new Dictionary<string, string> { ["PATH"] = $"\"{folder.Path}\"" }));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Tool\tool.exe")]
    [InlineData(@"tools\tool.exe")]
    [InlineData("no-such-tool-anywhere")]
    public void A_path_or_a_program_not_found_is_left_as_it_was(string file)
        => Assert.Equal(file, FreshEnvironment.Resolve(file, new Dictionary<string, string> { ["PATH"] = @"C:\nowhere" }));

    /// <summary>Sets a variable in this process alone, and returns its name.</summary>
    private static string OnlyHere()
    {
        var name = "WTH_ONLY_HERE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "1");
        return name;
    }
}
