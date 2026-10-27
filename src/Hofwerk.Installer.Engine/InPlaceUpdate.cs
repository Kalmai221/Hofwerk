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

    /// <returns>
    /// Null when everything went well. Otherwise the error that stopped the Start menu and Installed apps entries from being refreshed:
    /// the new files are in place and the update is complete, but the caller should write it to the log.
    /// </returns>
    public static Exception? Apply(PlatformInstaller platform, string packFile, string directory, string version, IProgress<double>? progress = null)
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

        // The version shown in Installed apps and the Start menu shortcut. This is best effort: the files are already in place, so
        // whatever goes wrong here (a locked shortcut, a switched-off Windows component) is reported but does not undo the update.
        Exception? registrationError = null;
        try
        {
            long size = staged.Sum(s => new FileInfo(s.Destination).Length);
            platform.RefreshRegistration(dir, version, size);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            registrationError = ex;
        }

        progress?.Report(1.0);
        return registrationError;
    }

    /// <summary>
    /// Unpacks a packed app into a sub-folder of the installed copy (the compatibility version goes to "compat"). The files are added to
    /// the install manifest, so the uninstaller removes them, and a later update of the main app removes them too (it is a new
    /// pack without them), which is right because a leftover compatibility version would be of the old release.
    /// </summary>
    public static void ExtractFolder(string packFile, string installFolder, string subfolder)
    {
        string dir = Path.GetFullPath(installFolder);
        if (!PlatformInstaller.IsInstalled(dir))
            throw new InstallerException("Hofwerk is not installed in this folder, so nothing can be added to it.");

        string target = Path.Combine(dir, subfolder);
        try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException($"The old compatibility version could not be removed (is it still running?): {ex.Message}", ex);
        }

        var written = new List<string>();
        try
        {
            using var pack = File.OpenRead(packFile);
            PayloadPack.Read(pack, _ => { }, (entry, content) =>
            {
                string relative = entry.Path.Replace('\\', '/');
                string destination = Path.GetFullPath(Path.Combine(target, relative));
                string check = Path.GetRelativePath(target, destination);
                if (check.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(check))
                    throw new InstallerException($"The download is damaged ({entry.Path} points outside its folder).");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var file = File.Create(destination)) content.CopyTo(file);
                written.Add(subfolder + "/" + relative);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); } catch (IOException) { }
            throw new InstallerException($"The compatibility version could not be installed: {ex.Message}", ex);
        }

        string manifest = Path.Combine(dir, PlatformInstaller.ManifestName);
        var lines = File.ReadAllLines(manifest).Where(l => l.Length > 0 && !l.StartsWith(subfolder + "/", StringComparison.OrdinalIgnoreCase)).ToList();
        lines.AddRange(written);
        File.WriteAllLines(manifest, lines);
    }

    /// <summary>True while files of an earlier update (the old version's, set aside) are still in the folder.</summary>
    public static bool HasLeftovers(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            return new[] { OldSuffix, NewSuffix }.Any(suffix => Directory.EnumerateFiles(directory, "*" + suffix, SearchOption.AllDirectories).Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
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
