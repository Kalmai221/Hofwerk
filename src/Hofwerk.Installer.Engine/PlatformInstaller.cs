
namespace Hofwerk.Installer.Engine;

/// <summary>
/// Installs and removes Hofwerk for the current user. The shared part copies files and keeps a manifest of exactly
/// what it wrote, so uninstalling never touches anything else. The platform part registers the app with the OS.
/// </summary>
public abstract class PlatformInstaller
{
    public const string ManifestName = "install.manifest";

    public static PlatformInstaller Create() =>
        OperatingSystem.IsWindows() ? new WindowsInstaller() : new LinuxInstaller();

    public abstract string DefaultDirectory { get; }

    /// <summary>File name of the app inside the install folder.</summary>
    public abstract string ProgramName { get; }

    /// <summary>File name the uninstaller is saved as inside the install folder.</summary>
    public abstract string UninstallerName { get; }

    public virtual bool SupportsFileAssociation => false;

    public static bool IsInstalled(string directory) => File.Exists(Path.Combine(directory, ManifestName));

    /// <summary>The folder Hofwerk is installed in, if it is.</summary>
    public virtual string? FindInstalledDirectory() => IsInstalled(DefaultDirectory) ? DefaultDirectory : null;

    protected abstract void Register(InstallRequest request, string directory, long sizeInBytes);
    protected abstract void Unregister(string directory);

    /// <summary>Called when the uninstaller runs from inside the folder it is removing.</summary>
    protected virtual void FinishSelfRemoval(string directory) { }

    /// <summary>Where Hofwerk keeps its settings; overridable so tests do not touch the real file.</summary>
    protected virtual string SettingsPath => UserSettings.DefaultPath;

    protected virtual void ValidateDirectory(string directory) { }
    protected virtual void MakeExecutable(string path) { }

    public abstract void Launch(string directory);

    /// <summary>The options an update installs with: whatever is already set up stays as it is.</summary>
    public virtual InstallRequest KeepExistingOptions(string directory, string? version) => new(directory, false, false, Version: version);

    /// <summary>Updates what is registered with the system (shortcuts, Installed apps) after the app's files were replaced in place.</summary>
    public void RefreshRegistration(string directory, string version, long sizeInBytes) =>
        Register(KeepExistingOptions(directory, version), Path.GetFullPath(directory), sizeInBytes);

    public Task InstallAsync(InstallRequest request, Stream payload, string installerPath,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Install(request, payload, installerPath, progress, ct), ct);

    public Task UninstallAsync(string directory, bool removeUserData,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Uninstall(directory, removeUserData, progress, ct), ct);

    void Install(InstallRequest request, Stream payload, string installerPath, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Directory))
            throw new InstallerException("Choose a folder to install into.");

        string dir = Path.GetFullPath(request.Directory);
        CheckTargetFolder(dir);
        ValidateDirectory(dir);

        void Report(double fraction, string message) => progress?.Report(new InstallProgress(fraction, message));

        try
        {
            Report(0.02, "Getting ready");
            Directory.CreateDirectory(dir);
            RemoveOldVersion(dir);

            var written = new List<string>();
            long totalBytes = 1, doneBytes = 0;

            PayloadPack.Read(payload,
                entries => totalBytes = Math.Max(1, entries.Sum(e => e.Length)),
                (entry, content) =>
                {
                    ct.ThrowIfCancellationRequested();

                    string relative = entry.Path.Replace('\\', '/');
                    string destination = Path.GetFullPath(Path.Combine(dir, relative));
                    string check = Path.GetRelativePath(dir, destination);
                    if (check.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(check))
                        throw new InstallerException($"The installer package is damaged ({entry.Path} points outside the install folder).");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using (var file = File.Create(destination))
                        content.CopyTo(file);

                    written.Add(relative);
                    doneBytes += entry.Length;
                    Report(0.05 + 0.80 * doneBytes / totalBytes, $"Copying {Path.GetFileName(destination)}");
                });

            Report(0.88, "Adding the uninstaller");
            string uninstaller = Path.Combine(dir, UninstallerName);
            PayloadFile.CopyProgramOnly(installerPath, uninstaller);
            MakeExecutable(uninstaller);
            written.Add(UninstallerName);
            MakeExecutable(Path.Combine(dir, ProgramName));

            File.WriteAllLines(Path.Combine(dir, ManifestName), written);

            Report(0.93, "Setting up shortcuts");
            long size = written.Sum(f => new FileInfo(Path.Combine(dir, f)).Length);
            Register(request, dir, size);

            // Tell Hofwerk where OMSI is, so it does not have to guess. Left alone when the user skipped the question.
            if (!string.IsNullOrWhiteSpace(request.OmsiFolder))
                UserSettings.SaveOmsiFolder(Path.GetFullPath(request.OmsiFolder), SettingsPath);

            Report(1.0, "Done");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InstallerException($"Installing failed: {ex.Message}", ex);
        }
    }

    void Uninstall(string directory, bool removeUserData, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        void Report(double fraction, string message) => progress?.Report(new InstallProgress(fraction, message));

        string dir = Path.GetFullPath(directory);
        string manifest = Path.Combine(dir, ManifestName);
        if (!File.Exists(manifest))
            throw new InstallerException("Hofwerk does not seem to be installed in this folder, so there is nothing to remove.");

        try
        {
            Report(0.05, "Removing shortcuts and registrations");
            Unregister(dir);

            string[] files = File.ReadAllLines(manifest).Where(l => l.Length > 0).ToArray();
            string? self = Environment.ProcessPath is { } p ? Path.GetFullPath(p) : null;
            bool removesItself = false;

            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(Path.Combine(dir, files[i]));
                if (Path.GetRelativePath(dir, path).StartsWith("..", StringComparison.Ordinal)) continue; // never leave the folder

                if (string.Equals(path, self, StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
                    removesItself = true; // Windows cannot delete a running program; it is removed afterwards
                else if (File.Exists(path))
                    File.Delete(path);

                Report(0.10 + 0.75 * (i + 1) / files.Length, $"Removing {Path.GetFileName(path)}");
            }

            RemoveEmptyFolders(dir);

            if (removeUserData)
            {
                Report(0.92, "Removing your settings");
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
                if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
            }

            if (removesItself) FinishSelfRemoval(dir);
            else File.Delete(manifest);
            TryRemoveFolderIfEmpty(dir);

            Report(1.0, "Done");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException($"Removing failed: {ex.Message}", ex);
        }
    }

    /// <summary>Throws an InstallerException with a user-friendly message if Hofwerk cannot be installed into this folder.</summary>
    public void CheckTarget(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InstallerException("Choose a folder to install into.");
        string dir = Path.GetFullPath(directory);
        CheckTargetFolder(dir);
        ValidateDirectory(dir);
    }

    /// <summary>Only an empty folder or an existing Hofwerk install is accepted, so another program's files are never overwritten or later deleted.</summary>
    static void CheckTargetFolder(string dir)
    {
        if (Path.GetPathRoot(dir) == dir)
            throw new InstallerException("Please choose a folder, not a whole drive.");
        if (File.Exists(dir))
            throw new InstallerException("That path is a file. Choose a folder.");
        if (Directory.Exists(dir) && !IsInstalled(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new InstallerException("That folder is not empty. Choose an empty folder or a new one, for example a new folder named Hofwerk.");
    }

    // An update replaces the old version's files, including ones the new version no longer has.
    static void RemoveOldVersion(string dir)
    {
        string manifest = Path.Combine(dir, ManifestName);
        if (!File.Exists(manifest)) return;

        foreach (string relative in File.ReadAllLines(manifest).Where(l => l.Length > 0))
        {
            string path = Path.GetFullPath(Path.Combine(dir, relative));
            if (Path.GetRelativePath(dir, path).StartsWith("..", StringComparison.Ordinal)) continue;
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static void RemoveEmptyFolders(string dir)
    {
        foreach (string sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
    }

    protected static void TryRemoveFolderIfEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
