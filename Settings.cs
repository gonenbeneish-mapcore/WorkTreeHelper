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

    /// <summary>Where the user last left the window; null until they move it.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WorktreeHelper", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
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
