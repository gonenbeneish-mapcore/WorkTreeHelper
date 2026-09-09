using System.Diagnostics;
using System.IO;

namespace WorktreeHelper;

/// <summary>Opens a folder in external tools.</summary>
public static class Launcher
{
    public static void OpenInVsCode(string folder)
    {
        var exe = FindVsCode();
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

    public static void OpenInExplorer(string folder)
        => Start("explorer.exe", Quote(folder), folder);

    private static string? FindVsCode()
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
