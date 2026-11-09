using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WorktreeHelper;

/// <summary>
/// The environment variables as Windows has them now, which everything this app starts is
/// given in place of the app's own.
/// </summary>
/// <remarks>
/// A process keeps the environment it was started with. Without this, a program opened from
/// here got the variables as they stood when the app itself started, so one set since, in
/// System Properties or with setx, never reached VS Code or Visual Studio until the app was
/// restarted. Explorer, by contrast, reloads them when Windows announces a change, which is why
/// the same program opened from the Start menu did see it.
///
/// Built by Windows itself, the way Explorer builds its own: the machine's variables and the
/// user's, expanded, with the two PATHs joined. It takes about a millisecond, against tens for
/// starting git, so it is built anew for every start rather than kept and refreshed, and a
/// change made straight in the registry, which announces nothing, is seen as well.
///
/// What the app's own environment had and Windows does not, from a console it was started in
/// for instance, is deliberately not passed on.
/// </remarks>
internal static class FreshEnvironment
{
    /// <summary>The variables as Windows has them now, by name, ignoring case.</summary>
    /// <remarks>Where Windows cannot build them, the app's own, so a start still has some.</remarks>
    public static Dictionary<string, string> Read()
    {
        try
        {
            return FromWindows();
        }
        catch (Win32Exception)
        {
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                own[(string)entry.Key] = (string?)entry.Value ?? "";
            return own;
        }
    }

    /// <summary>One variable as Windows has it now, or null where it is not set.</summary>
    public static string? Get(string name) => Read().TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>
    /// Gives <paramref name="psi"/> the variables as Windows has them now, in place of the app's,
    /// and finds its program on the PATH among them.
    /// </summary>
    /// <remarks>
    /// The program has to be found here: Windows looks for a bare name such as "git" on the
    /// starting process's own PATH, not on the one handed to the new process, so a tool whose
    /// folder was added since would not be found. Variables a caller sets after this are kept.
    /// </remarks>
    public static void ApplyTo(ProcessStartInfo psi)
    {
        if (psi.UseShellExecute)
            throw new InvalidOperationException("An environment can only be handed over with UseShellExecute off.");

        var variables = Read();
        psi.Environment.Clear();
        foreach (var (name, value) in variables)
            psi.Environment[name] = value;
        psi.FileName = Resolve(psi.FileName, variables);
    }

    /// <summary>
    /// The full path of <paramref name="file"/> where it is a bare program name, looked for the
    /// way Windows looks: in the system folders, then along PATH; otherwise <paramref name="file"/>
    /// unchanged, for Windows to look for, or fail to find, itself.
    /// </summary>
    /// <remarks>
    /// The app's own folder and the current folder, which Windows would try first, are left
    /// out: nothing this app runs lives in either, and a git.exe left in one should not be the
    /// git that runs.
    /// </remarks>
    internal static string Resolve(string file, IReadOnlyDictionary<string, string> variables)
    {
        if (file.Length == 0 || file.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0)
            return file;

        // As Windows does for a program named without one; PATHEXT is the console's business.
        var name = Path.HasExtension(file) ? file : file + ".exe";

        var folders = new List<string>
        {
            Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        };
        if (variables.TryGetValue("PATH", out var path))
            folders.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return file;
    }

    private static Dictionary<string, string> FromWindows()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, out var token))
            throw new Win32Exception();
        try
        {
            // Not inheriting is the point: the app's own variables are what is left out.
            if (!CreateEnvironmentBlock(out var block, token, inherit: false))
                throw new Win32Exception();
            try
            {
                return Parse(block);
            }
            finally
            {
                DestroyEnvironmentBlock(block);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// Reads a block of NAME=value strings, each ending in a null and the whole in a second.
    /// </summary>
    private static Dictionary<string, string> Parse(IntPtr block)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = block; ;)
        {
            var entry = Marshal.PtrToStringUni(at);
            if (string.IsNullOrEmpty(entry)) break;
            // From the second character: Windows keeps hidden entries such as "=C:=C:\" too.
            var equals = entry.IndexOf('=', 1);
            if (equals > 0) variables[entry[..equals]] = entry[(equals + 1)..];
            at += (entry.Length + 1) * sizeof(char);
        }
        return variables;
    }

    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
