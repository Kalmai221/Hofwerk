namespace Hofwerk.Core.Omsi;

public enum ExportOutcome
{
    /// <summary>The file was put in the folder (new, or replacing a different one).</summary>
    Written,
    /// <summary>The folder already had exactly this file.</summary>
    Unchanged,
    /// <summary>Nothing was done, and the reason says why (the folder does not exist, it is the file's own folder ...).</summary>
    Skipped,
    Failed,
}

public sealed record ExportResult(string Folder, string? Path, ExportOutcome Outcome, string Message);

public sealed record ExportOptions(bool BackupReplaced = true);

/// <summary>
/// HOF Suite keeps a list of vehicle folders inside each .hof file ([HOF_SUITE_DIRECTORIES] ... [HOF_SUITE_DIRECTORIES_END]): the bus
/// packs that should all have this file. Exporting puts the file into each of them, so one edit reaches every bus that uses it.
/// </summary>
public static class HofExporter
{
    /// <summary>The vehicle folders of an OMSI install that can take a .hof: those with a bus, a trailer or a .hof in them.</summary>
    public static IReadOnlyList<string> FindVehicleFolders(string vehiclesFolder)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(vehiclesFolder)) return [];

        var options = new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        foreach (string dir in Directory.EnumerateDirectories(vehiclesFolder))
        {
            if (HoldsVehicle(dir, options)) found.Add(Path.GetFileName(dir));
            else
                foreach (string inner in Directory.EnumerateDirectories(dir, "*", options))   // packs that keep their buses one folder down
                    if (HoldsVehicle(inner, options)) found.Add(Path.GetFileName(inner));
        }
        return [.. found];
    }

    static bool HoldsVehicle(string dir, EnumerationOptions options) =>
        new[] { "*.bus", "*.ovh", "*.hof" }.Any(pattern => Directory.EnumerateFiles(dir, pattern, options).Any());

    /// <summary>
    /// The folder a name in a file stands for: <c>Vehicles\name</c>, or a folder of that name one level further down. Null when there is
    /// no such folder, and for names that are not a single plain folder name (a name from a file is never allowed to point elsewhere).
    /// </summary>
    public static string? ResolveFolder(string vehiclesFolder, string name)
    {
        name = name.Trim();
        if (!VehicleFolderIndex.IsValidFolderName(name) || !Directory.Exists(vehiclesFolder)) return null;

        string direct = Path.Combine(vehiclesFolder, name);
        if (Directory.Exists(direct)) return direct;

        var options = new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        foreach (string dir in Directory.EnumerateDirectories(vehiclesFolder))
            if (Directory.EnumerateDirectories(dir, name, options).FirstOrDefault() is { } inner) return inner;
        return null;
    }

    /// <summary>Copies <paramref name="sourceFile"/> (already saved) into each named folder under the same file name.</summary>
    public static List<ExportResult> Export(string sourceFile, string vehiclesFolder, IEnumerable<string> folderNames,
        ExportOptions? options = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        options ??= new ExportOptions();
        var names = folderNames.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new List<ExportResult>();
        string fileName = Path.GetFileName(sourceFile);
        string? ownFolder = Path.GetDirectoryName(Path.GetFullPath(sourceFile))?.TrimEnd(Path.DirectorySeparatorChar);

        for (int i = 0; i < names.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(ExportOne(sourceFile, fileName, ownFolder, vehiclesFolder, names[i], options));
            progress?.Report((i + 1) / (double)names.Count);
        }
        if (names.Count == 0) progress?.Report(1);
        return results;
    }

    static ExportResult ExportOne(string source, string fileName, string? ownFolder, string vehiclesFolder, string name, ExportOptions options)
    {
        string? folder = ResolveFolder(vehiclesFolder, name);
        if (folder is null) return new ExportResult(name, null, ExportOutcome.Skipped, "There is no folder with this name in Vehicles.");

        if (string.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), ownFolder, StringComparison.OrdinalIgnoreCase))
            return new ExportResult(name, null, ExportOutcome.Skipped, "The file is already saved in this folder.");

        string target = Path.Combine(folder, fileName);
        try
        {
            byte[] content = File.ReadAllBytes(source);
            bool exists = File.Exists(target);
            if (exists && File.ReadAllBytes(target).AsSpan().SequenceEqual(content))
                return new ExportResult(name, target, ExportOutcome.Unchanged, "Already has this file.");

            if (exists && options.BackupReplaced) File.Copy(target, target + ".bak", overwrite: true);

            // Written beside it first, so a full disk or a crash never leaves half a file where a good one was.
            string temp = target + ".hwtmp";
            File.WriteAllBytes(temp, content);
            File.Move(temp, target, overwrite: true);
            return new ExportResult(name, target, ExportOutcome.Written, exists ? "Replaced the file that was there." : "Added the file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExportResult(name, target, ExportOutcome.Failed, ex.Message);
        }
    }
}
