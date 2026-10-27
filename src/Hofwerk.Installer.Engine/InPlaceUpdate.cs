namespace Hofwerk.Installer.Engine;

/// <summary>
/// Replaces the installed app with a newer packed one while the app itself is running, with no other program involved.
/// A running program's files cannot be overwritten on Windows, but they can be renamed. So every new file is first written
/// next to its old one, then the old file is renamed aside and the new one takes its place. If anything goes wrong the old
/// files are put back. The renamed old files are deleted straight away, or at the next start when Windows still holds them.
/// </summary>
public static class InPlaceUpdate
{
    const string NewSuffix = ".hwnew";
    const string OldSuffix = ".hwold";

    /// <summary>True when <paramref name="directory"/> is a Hofwerk install (it has the install manifest) that can be updated this way.</summary>
    public static bool CanUpdate(string directory) => PlatformInstaller.IsInstalled(directory);

    public static void Apply(PlatformInstaller platform, string packFile, string directory, string version, IProgress<double>? progress = null)
    {
        string dir = Path.GetFullPath(directory);
        if (!PlatformInstaller.IsInstalled(dir))
            throw new InstallerException("Hofwerk is not installed in this folder, so it cannot be updated here.");

        var staged = new List<(string Relative, string Destination, string Temp)>();
        try
        {
            // 1. Write every new file next to where it will go. Nothing in use is touched yet.
            using var pack = File.OpenRead(packFile);
            long total = 1, done = 0;
            PayloadPack.Read(pack,
                entries => total = Math.Max(1, entries.Sum(e => e.Length)),
                (entry, content) =>
                {
                    string relative = entry.Path.Replace('\\', '/');
                    string destination = Path.GetFullPath(Path.Combine(dir, relative));
                    string check = Path.GetRelativePath(dir, destination);
                    if (check.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(check))
                        throw new InstallerException($"The update is damaged ({entry.Path} points outside the install folder).");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    string temp = destination + NewSuffix;
                    using (var file = File.Create(temp)) content.CopyTo(file);
                    staged.Add((relative, destination, temp));

                    done += entry.Length;
                    progress?.Report(0.80 * done / total);
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DeleteAll(staged.Select(s => s.Temp));
            throw new InstallerException($"The update could not be unpacked: {ex.Message}", ex);
        }
        catch
        {
            DeleteAll(staged.Select(s => s.Temp));
            throw;
        }

        // 2. Swap. The old file is renamed aside (allowed even while it runs), then the new one is moved in.
        var swapped = new List<(string Destination, string? Old)>();
        var retired = new List<string>();
        string manifestPath = Path.Combine(dir, PlatformInstaller.ManifestName);
        try
        {
            foreach (var (_, destination, temp) in staged)
            {
                string? old = null;
                if (File.Exists(destination))
                {
                    old = destination + OldSuffix;
                    File.Move(destination, old, overwrite: true);
                }
                File.Move(temp, destination);
                swapped.Add((destination, old));
                if (old is not null) retired.Add(old);
            }

            // Files the new version no longer has are moved aside too.
            var keep = staged.Select(s => s.Relative).Append(platform.UninstallerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in File.ReadAllLines(manifestPath).Where(l => l.Length > 0 && !l.EndsWith(OldSuffix, StringComparison.Ordinal)))
            {
                if (keep.Contains(relative.Replace('\\', '/'))) continue;
                string path = Path.GetFullPath(Path.Combine(dir, relative));
                if (Path.GetRelativePath(dir, path).StartsWith("..", StringComparison.Ordinal) || !File.Exists(path)) continue;
                string old = path + OldSuffix;
                File.Move(path, old, overwrite: true);
                swapped.Add((path, old));
                retired.Add(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rollback(swapped);
            DeleteAll(staged.Select(s => s.Temp));
            throw new InstallerException($"The update could not be installed and Hofwerk was left as it was: {ex.Message}", ex);
        }
        progress?.Report(0.90);

        // 3. The old files can go. Windows may still hold the running program's files; those are deleted at the next start.
        var stillThere = new List<string>();
        foreach (string old in retired)
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { stillThere.Add(Path.GetRelativePath(dir, old).Replace('\\', '/')); }
        }

        var manifest = staged.Select(s => s.Relative).Append(platform.UninstallerName).Concat(stillThere).Distinct().ToList();
        File.WriteAllLines(manifestPath, manifest);

        // The version shown in Installed apps (best effort: the files are already in place).
        try
        {
            long size = staged.Sum(s => new FileInfo(s.Destination).Length);
            platform.RefreshRegistration(dir, version, size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        progress?.Report(1.0);
    }

    /// <summary>Deletes files an earlier update could not remove because the old version was still running.</summary>
    public static void CleanLeftovers(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string suffix in new[] { OldSuffix, NewSuffix })
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*" + suffix, SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static void Rollback(List<(string Destination, string? Old)> swapped)
    {
        for (int i = swapped.Count - 1; i >= 0; i--)
        {
            var (destination, old) = swapped[i];
            try
            {
                if (old is null) continue;
                File.Move(old, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static void DeleteAll(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
