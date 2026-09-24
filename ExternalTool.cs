using System.ComponentModel;
using System.IO;

namespace WorktreeHelper;

/// <summary>
/// A program a row's button hands the worktree to, and whether that button is up: it is when
/// the user wants it and the program is on this computer.
/// </summary>
/// <remarks>
/// Wanted is on by default, and stays on while the program is missing, so a button whose
/// program is installed later simply appears. Options shows <see cref="Shown"/> rather than
/// <see cref="Wanted"/>, so a button that cannot appear is not ticked as if it could.
/// </remarks>
public sealed class ExternalTool(string name, string exeName, Func<string?> find) : INotifyPropertyChanged
{
    /// <summary>What the program is called, for the user.</summary>
    public string Name => name;

    /// <summary>The file to look for when the user goes looking for it.</summary>
    public string ExeName => exeName;

    private string? _path;

    /// <summary>The program's exe, or null where it was not found. Set by <see cref="Look"/>'s result.</summary>
    public string? Path
    {
        get => _path;
        set
        {
            if (string.Equals(_path, value, StringComparison.OrdinalIgnoreCase)) return;
            _path = value;
            Raise(nameof(Path));
            Raise(nameof(Found));
            Raise(nameof(Shown));
        }
    }

    public bool Found => Path is not null;

    private System.Windows.Media.ImageSource? _icon;

    /// <summary>The program's own icon for its button, or null to keep the button's own mark.</summary>
    public System.Windows.Media.ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            Raise(nameof(Icon));
        }
    }

    private bool _wanted = true;

    /// <summary>The user wants this button, whether or not the program is here to back it.</summary>
    public bool Wanted
    {
        get => _wanted;
        set
        {
            if (_wanted == value) return;
            _wanted = value;
            Raise(nameof(Wanted));
            Raise(nameof(Shown));
        }
    }

    public bool Shown => Wanted && Found;

    private string? _chosen;

    /// <summary>
    /// An exe the user pointed at themselves, for a program installed where it could not be
    /// found. Looked at first, for as long as it is there.
    /// </summary>
    public string? Chosen
    {
        get => _chosen;
        set
        {
            if (_chosen == value) return;
            _chosen = value;
            Raise(nameof(Chosen));
        }
    }

    /// <summary>
    /// Where the program is now: the user's own choice, then where it was last found, then a
    /// search. Touches nothing on screen, so it can run off the UI thread.
    /// </summary>
    /// <remarks>
    /// Where it was last found comes before the search because the search may be slow: for
    /// Visual Studio it starts a process. A program that has since gone is searched for again.
    /// </remarks>
    public string? Look() => Existing(Chosen) ?? Existing(Path) ?? find();

    /// <summary>The user found the program themselves, and so wants its button.</summary>
    public void Choose(string exe)
    {
        Chosen = exe;
        Path = exe;
        Wanted = true;
    }

    private static string? Existing(string? path)
    {
        try
        {
            return path is { Length: > 0 } && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
