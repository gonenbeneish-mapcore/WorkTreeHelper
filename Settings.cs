using System.IO;
using System.Text.Json;

namespace WorktreeHelper;

public sealed class Settings
{
    public string? LastRepoPath { get; set; }
    public bool Pinned { get; set; }

    /// <summary>
    /// Whether the window takes a taskbar button, and with it an Alt+Tab entry, as well as the
    /// tray icon. Off by default: this app is tray-resident, and a button that is there whether
    /// the window is shown or not is the thing that design avoids.
    /// </summary>
    public bool ShowInTaskbar { get; set; }

    /// <summary>
    /// Whether the taskbar-or-tray question has been put once already. Kept separately from the
    /// answer, so choosing "tray only" is remembered as a decision rather than looking like a
    /// question never asked.
    /// </summary>
    public bool AskedAboutTaskbar { get; set; }

    /// <summary>
    /// Whether a worktree may have both kinds of Visual Studio open at once, the generated
    /// solution and the folder. On by default, which is how it always behaved: once one is
    /// open the row offers the other. Off, the row only ever offers to focus what is open.
    /// </summary>
    /// <remarks>
    /// The initialiser is what an older settings file gets, since it has no such key and the
    /// deserialiser leaves an absent property at whatever the constructor made it.
    /// </remarks>
    public bool AllowSecondVisualStudio { get; set; } = true;

    /// <summary>
    /// Whether each kind of button keeps a column of its own down the list, leaving a gap
    /// in a row that has no such button. Off by default: rows pack their buttons together,
    /// which is as small as the window can be.
    /// </summary>
    public bool AlignColumns { get; set; }

    /// <summary>
    /// The version whose what's-new was last closed, so the next version knows what to tell.
    /// Null in a file from before this existed, which is taken as "not told about this one".
    /// </summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>
    /// There was no settings file at all: the app has never run here. Not saved, since the
    /// save is what makes it untrue.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNew { get; private init; }

    /// <summary>Where the user last left the window; null until they move it.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WorktreeHelper", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        // Only a missing file means a first run. A corrupt one means the app has been here
        // before, so it is not the moment to hold back what is new.
        if (!File.Exists(FilePath)) return new Settings { IsNew = true };
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
        }
        catch { /* corrupt settings: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* non-fatal */ }
    }
}
