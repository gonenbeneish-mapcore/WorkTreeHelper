using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WorktreeHelper;

/// <summary>Runs the small command-line tools the GitHub lookups lean on: git, and gh.</summary>
internal static class Commands
{
    /// <summary>Runs a command and returns its output, or "" if it is missing or fails.</summary>
    public static async Task<string> RunAsync(
        string file, string arguments, string workingDirectory, CancellationToken ct,
        string? input = null, (string Name, string Value)? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (environment is { } variable) psi.Environment[variable.Name] = variable.Value;

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new Win32Exception($"{file} did not start.");
        }
        catch (Win32Exception)
        {
            // Not installed. That is an answer, not a failure.
            return "";
        }

        using (proc)
        using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { } }))
        {
            if (input is not null)
            {
                await proc.StandardInput.WriteAsync(input).ConfigureAwait(false);
                proc.StandardInput.Close();
            }

            var output = proc.StandardOutput.ReadToEndAsync(ct);
            var error = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            await error.ConfigureAwait(false);

            return proc.ExitCode == 0 ? await output.ConfigureAwait(false) : "";
        }
    }
}
