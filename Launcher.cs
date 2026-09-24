using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WorktreeHelper;

/// <summary>Opens a folder in external tools.</summary>
public static class Launcher
{
    /// <summary>Opens the folder in VS Code, with <paramref name="exe"/> where one was located.</summary>
    public static void OpenInVsCode(string folder, string? exe = null)
    {
        exe ??= FindVsCode();
        if (exe is not null)
        {
            Start(exe, Quote(folder), folder);
            return;
        }
        // Fall back to the `code` shim on PATH (code.cmd) via cmd, hidden.
        Start("cmd.exe", $"/c code {Quote(folder)}", folder, hidden: true);
    }

    /// <summary>
    /// The script a repository can carry to generate its Visual Studio solution and open it.
    /// Repositories that have no such script never show the button.
    /// </summary>
    public const string VisualStudioScript = "CreateVS-2026.bat";

    /// <summary>True if <paramref name="folder"/> holds the Visual Studio script.</summary>
    public static bool HasVisualStudioScript(string folder)
    {
        try
        {
            return File.Exists(Path.Combine(folder, VisualStudioScript));
        }
        catch
        {
            // A malformed or unreachable path is simply a folder without the script.
            return false;
        }
    }

    /// <summary>Runs the worktree's Visual Studio script, which opens the IDE when it finishes.</summary>
    /// <remarks>
    /// In a console window the user can see, deliberately: the script takes minutes, prints
    /// its progress, and pauses for input when credentials are missing. It also builds its
    /// output folder relative to the working directory, so that has to be the worktree.
    /// </remarks>
    public static void OpenInVisualStudio(string folder)
        => Start("cmd.exe", $"/c {Quote(Path.Combine(folder, VisualStudioScript))}", folder);

    public static void OpenTerminal(string folder)
    {
        // Prefer Windows Terminal; its App Execution Alias (wt.exe) is normally on PATH.
        try
        {
            Start("wt.exe", $"-d {Quote(folder)}", folder);
            return;
        }
        catch (System.ComponentModel.Win32Exception) { /* not installed */ }

        Start("powershell.exe", "-NoLogo", folder);
    }

    /// <summary>
    /// Opens the worktree folder itself in Visual Studio, which is how the builds driven from
    /// CMake rather than from a generated solution are worked on.
    /// </summary>
    public static void OpenFolderInVisualStudio(string folder, string? devenv = null)
    {
        devenv ??= FindVisualStudio()
                   ?? throw new FileNotFoundException("Could not find Visual Studio on this machine.");
        Start(devenv, Quote(folder), folder);
    }

    /// <summary>Asks the Visual Studio Installer where the newest installation is.</summary>
    /// <remarks>
    /// Through vswhere, which ships with the installer and is the supported way to find one.
    /// Guessing at a path under Program Files finds only the edition guessed at.
    /// </remarks>
    public static string? FindVisualStudio()
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return null;

        try
        {
            using var proc = Process.Start(new ProcessStartInfo(vswhere, "-latest -property installationPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return null;

            var root = proc.StandardOutput.ReadLine()?.Trim();
            proc.WaitForExit(10_000);
            if (string.IsNullOrEmpty(root)) return null;

            var devenv = Path.Combine(root, "Common7", "IDE", "devenv.exe");
            return File.Exists(devenv) ? devenv : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Opens the worktree in Git Extensions' browse window, the one with the history.</summary>
    public static void OpenInGitExtensions(string exe, string folder)
        => Start(exe, $"browse {Quote(folder)}", folder);

    /// <summary>Where Git Extensions is installed, or null where it is not.</summary>
    /// <remarks>
    /// The installer records its folder in the registry, per user or per machine, and that is
    /// asked first. The usual install folders and PATH, where its gitex.cmd sits beside the
    /// exe, catch a copy that was unzipped rather than installed.
    /// </remarks>
    public static string? FindGitExtensions()
    {
        const string exeName = "GitExtensions.exe";
        var candidates = new List<string>();

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"Software\GitExtensions");
                if (key?.GetValue("InstallDir") is string dir && dir.Length > 0)
                    candidates.Add(Path.Combine(dir, exeName));
            }
            catch { /* unreadable key: look elsewhere */ }
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitExtensions", exeName));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GitExtensions", exeName));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitExtensions", exeName));

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return c;
            }
            catch { /* malformed InstallDir */ }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var exe = Path.Combine(dir, exeName);
                if (File.Exists(exe)) return exe;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>Hands a URL to whatever the user browses with.</summary>
    public static void OpenInBrowser(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void OpenInExplorer(string folder)
        => Start("explorer.exe", Quote(folder), folder);

    public static string? FindVsCode()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Search PATH for Code.exe (e.g. custom install location)
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                // PATH usually holds "...\Microsoft VS Code\bin"; the exe is one level up.
                var exe = Path.Combine(dir, "Code.exe");
                if (File.Exists(exe)) return exe;
                var parent = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\')) ?? dir, "Code.exe");
                if (File.Exists(parent)) return parent;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    private static void Start(string file, string args, string workingDir, bool hidden = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = !hidden,
            CreateNoWindow = hidden,
        };
        Process.Start(psi);
    }

    private static string Quote(string s) => $"\"{s}\"";
}
