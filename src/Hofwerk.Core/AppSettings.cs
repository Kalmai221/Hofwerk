using System.Text.Json;
using System.Text.Json.Serialization;
using Hofwerk.Core.Presets;

namespace Hofwerk.Core;

/// <summary>App-wide preferences, kept next to the recent files (%APPDATA%\Hofwerk on Windows, ~/.config/Hofwerk on Linux).</summary>
public sealed class AppSettings
{
    [JsonIgnore] string _path = DefaultPath;

    static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hofwerk", "settings.json");

    /// <summary>The string preset whose slot names the editor shows.</summary>
    public string ActivePresetId { get; set; } = PresetLibrary.DefaultId;

    /// <summary>The OMSI folder chosen in the installer or on Home. Empty means "find it automatically".</summary>
    public string? OmsiFolder { get; set; }

    /// <summary>"System" (follow Windows or the desktop), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Look for a newer version when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Also offer pre-releases (tags like V0.2-dev1). Empty means "automatic": on while the running version is itself a
    /// pre-release, off otherwise. Once the user flips the switch it is their choice.
    /// </summary>
    public bool? AllowPreReleases { get; set; }

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
