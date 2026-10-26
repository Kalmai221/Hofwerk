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

    public static void SaveOmsiFolder(string folder, string? path = null)
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

        settings["OmsiFolder"] = folder;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
