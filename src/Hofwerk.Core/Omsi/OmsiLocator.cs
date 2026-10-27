using System.Text.RegularExpressions;

namespace Hofwerk.Core.Omsi;

/// <summary>A .hof file found on disk. <see cref="Subtitle"/> is the folder it lives in, shown under the file name.</summary>
public sealed record HofFileEntry(string Path, string Title, string Subtitle);

/// <summary>Finds the OMSI install through Steam on Windows and Linux (including Flatpak/Snap Steam and extra library drives).</summary>
public static partial class OmsiLocator
{
    static readonly EnumerationOptions IgnoreCase = new() { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };

    static readonly EnumerationOptions AllHofFiles = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    /// <summary>Every OMSI install found, most likely first: Steam libraries, then a few plain install locations on Windows.</summary>
    public static IReadOnlyList<string> FindInstallFolders()
    {
        var found = new List<string>();
        void Add(string? dir)
        {
            if (dir is null || !LooksLikeOmsi(dir)) return;
            dir = PathCasing.Normalize(dir);    // Steam and the registry write paths in lowercase
            if (!found.Contains(dir, StringComparer.OrdinalIgnoreCase)) found.Add(dir);
        }

        foreach (string library in SteamLibraries())
        {
            string? common = Child(Child(library, "steamapps"), "common");
            if (common is null) continue;
            foreach (string name in new[] { "OMSI 2", "OMSI" })
                Add(Child(common, name));
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string name in new[] { "OMSI 2", "OMSI" })
            {
                Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), name));
                Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name));
            }
        }
        return found;
    }

    /// <summary>
    /// True when the folder looks like an OMSI install: it has a Vehicles folder, plus either a Maps folder or the game's
    /// executable. Used to confirm a detected folder and to warn about a folder picked by hand.
    /// </summary>
    public static bool LooksLikeOmsi(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        if (Child(folder, "Vehicles") is null) return false;
        return Child(folder, "Maps") is not null || Child(folder, "Omsi.exe", file: true) is not null;
    }

    public static List<HofFileEntry> FindHofFiles(string omsiFolder)
    {
        var result = new List<HofFileEntry>();
        if (Child(omsiFolder, "Vehicles") is not { } vehicles) return result;

        foreach (string file in Directory.EnumerateFiles(vehicles, "*.hof", AllHofFiles))
        {
            string folder = Path.GetRelativePath(vehicles, Path.GetDirectoryName(file)!);
            result.Add(new HofFileEntry(file, Path.GetFileName(file), folder == "." ? "" : folder));
        }
        result.Sort((a, b) =>
        {
            int c = string.Compare(a.Subtitle, b.Subtitle, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    /// <summary>Extracts every "path" value from Steam's libraryfolders.vdf.</summary>
    public static List<string> ParseLibraryPaths(string vdf) =>
        VdfPath().Matches(vdf).Select(m => m.Groups["p"].Value.Replace(@"\\", @"\")).ToList();

    [GeneratedRegex("\"path\"\\s+\"(?<p>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();

    static IEnumerable<string> SteamLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in SteamRoots())
        {
            if (seen.Add(root)) yield return root;

            string? vdf = Child(Child(root, "steamapps"), "libraryfolders.vdf", file: true);
            if (vdf is null) continue;

            string text;
            try { text = File.ReadAllText(vdf); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (string lib in ParseLibraryPaths(text))
                if (seen.Add(lib)) yield return lib;
        }
    }

    static IEnumerable<string> SteamRoots()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            roots.Add(RegistrySteamPath() ?? "");
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.AddRange(
            [
                Path.Combine(home, ".local/share/Steam"),
                Path.Combine(home, ".steam/steam"),
                Path.Combine(home, ".steam/root"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
                Path.Combine(home, "snap/steam/common/.local/share/Steam"),
            ]);
        }
        return roots.Where(r => r.Length > 0 && Directory.Exists(r)).Select(Path.GetFullPath);
    }

    static string? RegistrySteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Case-insensitive child lookup, because Linux file systems are case-sensitive and Steam folders vary.</summary>
    static string? Child(string? parent, string name, bool file = false)
    {
        if (parent is null || !Directory.Exists(parent)) return null;
        try
        {
            return (file ? Directory.EnumerateFiles(parent, name, IgnoreCase) : Directory.EnumerateDirectories(parent, name, IgnoreCase))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
