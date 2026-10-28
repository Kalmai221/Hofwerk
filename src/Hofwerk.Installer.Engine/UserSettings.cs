using System.Text.Json.Nodes;

namespace Hofwerk.Installer.Engine;

/// <summary>
/// Writes the choices made in the installer into the same settings file Hofwerk itself reads
/// (%APPDATA%\Hofwerk\settings.json on Windows, ~/.config/Hofwerk/settings.json on Linux).
/// The file is edited, not replaced, so settings Hofwerk saved earlier (such as the chosen preset) survive.
/// </summary>
public static class UserSettings
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name, "settings.json");

    public static void SaveOmsiFolder(string folder, string? path = null) => Save("OmsiFolder", folder, path);

    /// <summary>The language saved the last time (by the installer or in App settings), or null when there is none.</summary>
    public static string? ReadLanguage(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject settings && settings["Language"]?.GetValue<string>() is { Length: > 0 } language)
                return language;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
        return null;
    }

    /// <summary>The language chosen in the installer ("auto" follows the system language, otherwise a code such as "de").</summary>
    public static void SaveLanguage(string language, string? path = null) => Save("Language", language, path);

    static void Save(string name, string value, string? path)
    {
        path ??= DefaultPath;

        JsonObject settings = new();
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
                settings = existing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // An unreadable settings file is replaced rather than blocking the install.
        }

        settings[name] = value;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
