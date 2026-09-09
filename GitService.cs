using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WorktreeHelper;

public static class GitService
{
    /// <summary>Runs `git worktree list --porcelain` in <paramref name="repoPath"/> and parses the result.</summary>
    public static async Task<IReadOnlyList<Worktree>> ListWorktreesAsync(string repoPath, CancellationToken ct = default)
    {
        // ConfigureAwait(false): parsing walks the disk to map link targets back to the
        // names the user chose, which has no business running on the UI thread.
        var output = await RunGitAsync(repoPath, "worktree list --porcelain", ct).ConfigureAwait(false);
        // git reports link targets; show the paths under the folder the user actually picked.
        return Parse(output, LinkPaths.For(repoPath));
    }

    /// <summary>
    /// Reads the working-tree state of one worktree: how many paths differ from HEAD, and
    /// how far its branch has drifted from its upstream.
    /// </summary>
    /// <returns>Null when the state could not be read; the row then simply shows nothing.</returns>
    public static async Task<WorktreeStatus?> ReadStatusAsync(string worktreePath, CancellationToken ct = default)
    {
        try
        {
            var output = await RunGitAsync(worktreePath, "status --porcelain=v2 --branch", ct).ConfigureAwait(false);
            return ParseStatus(output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses `git status --porcelain=v2 --branch` output.</summary>
    public static WorktreeStatus ParseStatus(string porcelain)
    {
        int changes = 0, ahead = 0, behind = 0;
        string? upstream = null;

        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '#')
            {
                const string up = "# branch.upstream ";
                if (line.StartsWith(up, StringComparison.Ordinal))
                {
                    upstream = line[up.Length..].Trim();
                    continue;
                }

                // "# branch.ab +2 -1" — absent entirely when there is no upstream.
                const string ab = "# branch.ab ";
                if (!line.StartsWith(ab, StringComparison.Ordinal)) continue;
                foreach (var field in line[ab.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (field.Length < 2 || !int.TryParse(field[1..], out var n)) continue;
                    if (field[0] == '+') ahead = n;
                    else if (field[0] == '-') behind = n;
                }
                continue;
            }

            // Everything else is one path: changed (1/2), unmerged (u) or untracked (?).
            changes++;
        }

        return new WorktreeStatus(changes, ahead, behind, upstream);
    }

    /// <summary>True if the folder is inside a git working tree.</summary>
    /// <exception cref="GitNotFoundException">git is not installed, or not on PATH.</exception>
    public static async Task<bool> IsGitRepoAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var r = await RunGitAsync(path, "rev-parse --is-inside-work-tree", ct).ConfigureAwait(false);
            return r.Trim() == "true";
        }
        catch (GitNotFoundException)
        {
            // Not something the chosen folder can be blamed for: let the caller say so.
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<Worktree> Parse(string porcelain, LinkPaths? links = null)
    {
        var result = new List<Worktree>();
        string? path = null, head = null, branch = null;
        bool detached = false, bare = false, locked = false, prunable = false;
        bool first = true;

        void Flush()
        {
            if (path is null) return;
            result.Add(new Worktree
            {
                Path = NormalizePath(path, links),
                Head = head ?? "",
                Branch = branch ?? "",
                IsDetached = detached,
                IsBare = bare,
                IsLocked = locked,
                IsPrunable = prunable,
                IsMain = first,
            });
            first = false;
            path = head = branch = null;
            detached = bare = locked = prunable = false;
        }

        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush(); // safety if blank lines are missing
                path = line["worktree ".Length..];
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                branch = line["branch ".Length..];
                const string prefix = "refs/heads/";
                if (branch.StartsWith(prefix, StringComparison.Ordinal)) branch = branch[prefix.Length..];
            }
            else if (line == "detached") detached = true;
            else if (line == "bare") bare = true;
            else if (line.StartsWith("locked", StringComparison.Ordinal)) locked = true;
            else if (line.StartsWith("prunable", StringComparison.Ordinal)) prunable = true;
        }
        Flush();
        return result;
    }

    private static string NormalizePath(string p, LinkPaths? links)
    {
        // git prints forward slashes on Windows; show native separators.
        p = p.Replace('/', Path.DirectorySeparatorChar);
        try { p = Path.GetFullPath(p); } catch { return p; }
        return links?.Prefer(p) ?? p;
    }

    private static async Task<string> RunGitAsync(string workingDir, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Make git print paths without quoting/escaping non-ASCII characters.
        psi.Environment["LC_ALL"] = "C.UTF-8";

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new Win32Exception("git did not start.");
        }
        catch (Win32Exception ex)
        {
            throw new GitNotFoundException(ex);
        }

        using (proc)
        // Cancelling abandons the await but leaves git running, so end it with the wait.
        using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ } }))
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"git exited with code {proc.ExitCode}" : stderr.Trim());
            return stdout;
        }
    }
}

/// <summary>How far one worktree has drifted, for the marker on its row.</summary>
/// <param name="Changes">Paths that differ from HEAD, untracked files included.</param>
/// <param name="Ahead">Commits the branch has that its upstream does not.</param>
/// <param name="Behind">Commits the upstream has that the branch does not.</param>
/// <param name="Upstream">The upstream branch, e.g. "origin/main"; null when there is none.</param>
public readonly record struct WorktreeStatus(int Changes, int Ahead, int Behind, string? Upstream = null);

/// <summary>git could not be started at all, so nothing about the repository is known.</summary>
public sealed class GitNotFoundException(Exception inner)
    : Exception("Could not run git. Install Git for Windows and make sure it is on PATH.", inner);
