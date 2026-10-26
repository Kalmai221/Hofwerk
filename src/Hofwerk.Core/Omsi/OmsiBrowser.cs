namespace Hofwerk.Core.Omsi;

/// <summary>One row of the OMSI library: a folder to step into, or a .hof file to open.</summary>
/// <param name="Path">For a folder, its path under Vehicles (using /); for a file, its full path on disk.</param>
public sealed record OmsiItem(bool IsFolder, string Name, string Detail, string Path);

/// <summary>
/// Lets the OMSI library be browsed like a file explorer: folders that contain .hof files come first, then the files
/// in the current folder. Only folders that lead to at least one .hof file are listed. Searching looks through everything.
/// </summary>
public sealed class OmsiBrowser(IReadOnlyList<HofFileEntry> files)
{
    // Folder paths use '/' inside this class, whatever the operating system uses.
    readonly List<(HofFileEntry File, string Dir)> _files = files.Select(f => (f, Normalize(f.Subtitle))).ToList();

    public int FileCount => _files.Count;

    /// <summary>The folders (with .hof file counts) and files directly inside <paramref name="folder"/>; "" is Vehicles itself.</summary>
    public IReadOnlyList<OmsiItem> Browse(string folder)
    {
        folder = Normalize(folder);
        string prefix = folder.Length == 0 ? "" : folder + "/";

        var subfolders = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var here = new List<OmsiItem>();

        foreach (var (file, dir) in _files)
        {
            if (string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase))
            {
                here.Add(new OmsiItem(false, file.Title, "", file.Path));
            }
            else if (dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = dir[prefix.Length..];
                int slash = rest.IndexOf('/');
                string name = slash < 0 ? rest : rest[..slash];
                subfolders[name] = subfolders.GetValueOrDefault(name) + 1;
            }
        }

        var result = new List<OmsiItem>(subfolders.Count + here.Count);
        foreach (var (name, count) in subfolders)
            result.Add(new OmsiItem(true, name, count == 1 ? "1 .hof file" : $"{count:N0} .hof files", prefix + name));

        here.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        result.AddRange(here);
        return result;
    }

    /// <summary>Files anywhere in the library whose name or folder contains the text, as a flat list.</summary>
    public IReadOnlyList<OmsiItem> Search(string text, int max = 300)
    {
        text = text.Trim();
        return _files
            .Where(x => x.File.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || x.Dir.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(max)
            .Select(x => new OmsiItem(false, x.File.Title, x.Dir.Length == 0 ? "Vehicles" : x.Dir.Replace("/", " › "), x.File.Path))
            .ToList();
    }

    public static string Parent(string folder)
    {
        folder = Normalize(folder);
        int slash = folder.LastIndexOf('/');
        return slash < 0 ? "" : folder[..slash];
    }

    /// <summary>"Vehicles › Citaro › Berlin" for the folder being shown.</summary>
    public static string Breadcrumb(string folder)
    {
        folder = Normalize(folder);
        return folder.Length == 0 ? "Vehicles" : "Vehicles › " + folder.Replace("/", " › ");
    }

    static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
