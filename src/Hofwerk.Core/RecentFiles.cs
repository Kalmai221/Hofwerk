using System.Text.Json;

namespace Hofwerk.Core;

/// <summary>Remembers the last few opened files in the user's app-data folder (%APPDATA% on Windows, ~/.config on Linux).</summary>
public sealed class RecentFiles(string? storePath = null)
{
    public const int Max = 8;

    readonly string _path = storePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hofwerk", "recent.json");

    /// <summary>Saved files that still exist, newest first.</summary>
    public List<string> Load()
    {
        try
        {
            var list = JsonSerializer.Deserialize(File.ReadAllText(_path), HofwerkJson.Default.ListString) ?? [];
            return list.Where(File.Exists).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public List<string> Add(string file)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, file);
        if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, HofwerkJson.Default.ListString));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the recent list is not worth interrupting the user for.
        }
        return list;
    }
}
