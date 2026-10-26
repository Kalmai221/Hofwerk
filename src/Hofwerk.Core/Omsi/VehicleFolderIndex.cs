namespace Hofwerk.Core.Omsi;

/// <summary>
/// Knows every folder under OMSI's Vehicles folder by name, so the Settings page can say whether the announcement,
/// roll sign and side plate folders named in a .hof file exist. Where those folders live depends on the bus pack
/// (Vehicles\Announcements, Vehicles\SP_CustomDisplays, …), so a folder counts as found when one with that name exists anywhere.
/// </summary>
public sealed class VehicleFolderIndex
{
    public static VehicleFolderIndex Empty { get; } = new(new Dictionary<string, List<string>>());

    readonly Dictionary<string, List<string>> _byName;

    VehicleFolderIndex(Dictionary<string, List<string>> byName) => _byName = byName;

    public int Count => _byName.Values.Sum(l => l.Count);

    public static VehicleFolderIndex Build(string omsiFolder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? vehicles = Directory.Exists(omsiFolder)
            ? Directory.EnumerateDirectories(omsiFolder, "Vehicles", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()
            : null;
        if (vehicles is null) return Empty;

        foreach (string dir in Directory.EnumerateDirectories(vehicles, "*", options))
        {
            string name = Path.GetFileName(dir);
            if (!byName.TryGetValue(name, out var list)) byName[name] = list = [];
            list.Add(Path.GetRelativePath(vehicles, Path.GetDirectoryName(dir)!));
        }
        return new VehicleFolderIndex(byName);
    }

    /// <summary>Where a folder with this name is, relative to Vehicles (for example "Announcements"), or null if there is none.</summary>
    public string? Find(string name)
    {
        name = name.Trim();
        if (!IsValidFolderName(name)) return null;
        return _byName.TryGetValue(name, out var parents) && parents.Count > 0 ? parents[0] : null;
    }

    /// <summary>Folder names are a single path segment without characters Windows or Linux would reject.</summary>
    static readonly char[] WindowsInvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static bool IsValidFolderName(string name)
    {
        name = name.Trim();
        return name.Length > 0
               && name is not ("." or "..")
               && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && name.IndexOfAny(WindowsInvalidNameChars) < 0 // Linux allows these, but OMSI folders have to work on Windows too
               && !name.EndsWith('.');
    }

    /// <summary>Creates <paramref name="name"/> inside <paramref name="parent"/> and returns its full path. Does nothing if it exists.</summary>
    public static string CreateFolder(string parent, string name)
    {
        name = name.Trim();
        if (!IsValidFolderName(name)) throw new IOException($"“{name}” is not a valid folder name.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException($"The folder {parent} does not exist.");

        string path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
