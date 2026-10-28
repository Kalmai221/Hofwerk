using System.Text.Json;
using System.Text.Json.Serialization;
using Hofwerk.Core.Presets;

namespace Hofwerk.Core;

/// <summary>App-wide preferences, kept next to the recent files (%APPDATA%\Hofwerk on Windows, ~/.config/Hofwerk on Linux).</summary>
public sealed class AppSettings
{
    [JsonIgnore] string _path = DefaultPath;

    /// <summary>
    /// The folder with the settings and the recent files list. Normally %APPDATA%\Hofwerk (~/.config/Hofwerk on Linux); starting Hofwerk with
    /// HOFWERK_DATA_DIR set to a folder uses that folder instead, which is how pictures and tests run without touching the real ones.
    /// </summary>
    static string DataFolder => Environment.GetEnvironmentVariable("HOFWERK_DATA_DIR") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hofwerk");

    static string DefaultPath => Path.Combine(DataFolder, "settings.json");

    /// <summary>The string preset whose slot names the editor shows.</summary>
    public string ActivePresetId { get; set; } = PresetLibrary.DefaultId;

    /// <summary>The OMSI folder chosen in the installer or on Home. Empty means "find it automatically".</summary>
    public string? OmsiFolder { get; set; }

    /// <summary>The language of the app: "auto" (empty means the same) follows the language Windows or the desktop is set to, otherwise a code such as "en" or "de".</summary>
    public string? Language { get; set; }

    /// <summary>"System" (follow Windows or the desktop), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Windows only: start the compatibility version (Hofwerk drawn with Avalonia) instead of the standard one (WinUI), for computers
    /// where the standard one does not work. Chosen from the error dialog, and undone from the compatibility version's App settings.
    /// </summary>
    public bool PreferCompatibilityVersion { get; set; }

    /// <summary>Save the open file by itself a little after the last change.</summary>
    public bool AutoSave { get; set; }

    /// <summary>How many seconds after the last change autosave saves (5, 30 or 120).</summary>
    public int AutoSaveSeconds { get; set; } = 30;

    /// <summary>Look for a newer version when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Also offer pre-releases (tags like V0.2-dev1). Empty means "automatic": on while the running version is itself a
    /// pre-release, off otherwise. Once the user flips the switch it is their choice.
    /// </summary>
    public bool? AllowPreReleases { get; set; }

    /// <summary>Where these settings are kept.</summary>
    [JsonIgnore]
    public string FilePath => _path;

    /// <summary>The recent files list lives next to the settings file, so settings kept somewhere else (a test) keep their own list too.</summary>
    [JsonIgnore]
    public string RecentFilesPath => Path.Combine(Path.GetDirectoryName(_path)!, "recent.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        AppSettings? s = null;
        try { s = JsonSerializer.Deserialize(File.ReadAllText(path), HofwerkJson.Default.AppSettings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        s ??= new AppSettings();
        s._path = path;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, HofwerkJson.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that doesn't stick is not worth interrupting the user for.
        }
    }
}
