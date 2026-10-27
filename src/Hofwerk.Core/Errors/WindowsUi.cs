using System.Diagnostics;
using Hofwerk.Core.Updates;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Core.Errors;

/// <summary>
/// On Windows Hofwerk comes in two versions that look the same: the standard one (drawn by WinUI) and a compatibility version (the Linux
/// version's Avalonia, drawn another way) for computers where the standard one does not work. The compatibility version is not part
/// of the installer; it is downloaded the first time it is needed and lives in the "compat" folder of the install.
/// </summary>
public sealed class WindowsUi
{
    public const string CompatFolderName = "compat";

    /// <summary>The copy that is running.</summary>
    public static WindowsUi Current { get; } = new(AppContext.BaseDirectory);

    public WindowsUi(string baseDirectory)
    {
        string trimmed = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(trimmed);

        if (parent is not null && string.Equals(Path.GetFileName(trimmed), CompatFolderName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(parent, "Hofwerk.exe")))
        {
            IsCompat = true;
            InstallFolder = parent;
        }
        else InstallFolder = trimmed;
    }

    /// <summary>True when this is the compatibility version, running from the "compat" folder.</summary>
    public bool IsCompat { get; }

    /// <summary>The folder with Hofwerk.exe, the uninstaller and the install manifest.</summary>
    public string InstallFolder { get; }

    public string StandardExe => Path.Combine(InstallFolder, "Hofwerk.exe");
    public string CompatFolder => Path.Combine(InstallFolder, CompatFolderName);
    public string CompatExe => Path.Combine(CompatFolder, "hofwerk.exe");
    public bool CompatInstalled => File.Exists(CompatExe);

    /// <summary>
    /// Switching is offered on Windows for an installed copy (it needs the install folder to put the compatibility version in, and the
    /// version number of a release to download it for).
    /// </summary>
    public bool CanSwitch => OperatingSystem.IsWindows() && PlatformInstaller.IsInstalled(InstallFolder);

    /// <summary>Downloads the compatibility version of the version that is running and unpacks it into the "compat" folder.</summary>
    public async Task InstallCompatAsync(UpdateChecker checker, AppVersion version, string tempFolder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var asset = await checker.FindCompatAsync(version, ct).ConfigureAwait(false)
                    ?? throw new IOException($"There is no compatibility version of Hofwerk {version} to download.");

        string pack = await checker.DownloadPackAsync(asset, tempFolder, progress, ct).ConfigureAwait(false);
        try { await Task.Run(() => InPlaceUpdate.ExtractFolder(pack, InstallFolder, CompatFolderName), ct).ConfigureAwait(false); }
        finally { try { File.Delete(pack); } catch (IOException) { } }
    }

    /// <summary>Starts the other version of Hofwerk (with the open file, if any). The caller closes this copy.</summary>
    public static bool Launch(string exe, string? file = null)
    {
        if (!File.Exists(exe)) return false;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            if (!string.IsNullOrEmpty(file)) start.ArgumentList.Add(file);
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>Remembers the choice, so Hofwerk.exe starts the compatibility version from now on, and starts it.</summary>
    public bool SwitchToCompat(AppSettings settings, string? file = null)
    {
        if (!CompatInstalled) return false;
        settings.PreferCompatibilityVersion = true;
        settings.Save();
        return Launch(CompatExe, file);
    }

    /// <summary>Goes back to the standard version for good (from the compatibility version).</summary>
    public bool SwitchToStandard(AppSettings settings, string? file = null)
    {
        settings.PreferCompatibilityVersion = false;
        settings.Save();
        return Launch(StandardExe, file);
    }
}
