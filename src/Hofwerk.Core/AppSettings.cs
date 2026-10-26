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

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        AppSettings? s = null;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)); }
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
            File.WriteAllText(_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that doesn't stick is not worth interrupting the user for.
        }
    }
}
