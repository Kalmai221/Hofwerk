using System.Diagnostics;
using System.Runtime.Versioning;

namespace Hofwerk.Installer.Engine;

/// <summary>Per-user install under ~/.local: an applications-menu entry, an icon, a "hofwerk" command and an uninstaller.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class LinuxInstaller : PlatformInstaller
{
    public override string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hofwerk");

    public override string ProgramName => "hofwerk";
    public override string UninstallerName => "uninstall";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string DataHome => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string MenuEntry => Path.Combine(DataHome, "applications", "hofwerk.desktop");
    static string IconFile => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", "hofwerk.png");
    static string CommandLink => Path.Combine(Home, ".local", "bin", "hofwerk");
    static string DesktopEntry => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Hofwerk.desktop");

    // The folder ends up inside a quoted Exec= line, so characters that would break out of it are refused.
    protected override void ValidateDirectory(string directory)
    {
        if (directory.IndexOfAny(['"', '`', '$', '\\', '\n', '\r']) >= 0)
            throw new InstallerException("Please choose a folder whose path has no quotes, backslashes, backticks or $ signs.");
    }

    public override InstallRequest KeepExistingOptions(string directory, string? version) =>
        new(directory, File.Exists(DesktopEntry), false, Version: version);

    protected override void MakeExecutable(string path) =>
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

    /// <summary>The .desktop file that puts Hofwerk in the applications menu.</summary>
    public static string BuildDesktopEntry(string directory) => $"""
        [Desktop Entry]
        Type=Application
        Name=Hofwerk
        GenericName=OMSI depot editor
        Comment=Edit OMSI 2 .hof files: destinations, bus stops and routes
        Exec="{Path.Combine(directory, "hofwerk")}" %f
        Icon=hofwerk
        Terminal=false
        Categories=Utility;Game;
        Keywords=OMSI;bus;hof;depot;
        StartupWMClass=hofwerk
        Actions=Uninstall;

        [Desktop Action Uninstall]
        Name=Uninstall Hofwerk
        Exec="{Path.Combine(directory, "uninstall")}" --uninstall
        """ + "\n";

    protected override void Register(InstallRequest request, string directory, long sizeInBytes)
    {
        string entry = BuildDesktopEntry(directory);
        WriteFile(MenuEntry, entry, executable: false);

        string iconSource = Path.Combine(directory, "hofwerk.png");
        if (File.Exists(iconSource))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconFile)!);
            File.Copy(iconSource, IconFile, overwrite: true);
        }

        // A "hofwerk" command for the terminal, if ~/.local/bin is somewhere the user can use.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CommandLink)!);
            if (File.Exists(CommandLink) || Directory.ResolveLinkTarget(CommandLink, false) is not null) File.Delete(CommandLink);
            File.CreateSymbolicLink(CommandLink, Path.Combine(directory, ProgramName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (request.DesktopShortcut && Directory.Exists(Path.GetDirectoryName(DesktopEntry)))
            WriteFile(DesktopEntry, entry, executable: true);

        RunQuietly("update-desktop-database", Path.GetDirectoryName(MenuEntry)!);
        RunQuietly("gtk-update-icon-cache", "-f", "-t", Path.Combine(DataHome, "icons", "hicolor"));
    }

    protected override void Unregister(string directory)
    {
        DeleteIfExists(MenuEntry);
        DeleteIfExists(IconFile);
        DeleteIfExists(DesktopEntry);

        // Only remove the command link if it is ours.
        try
        {
            if (Directory.ResolveLinkTarget(CommandLink, false)?.FullName.StartsWith(directory, StringComparison.Ordinal) == true)
                File.Delete(CommandLink);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        RunQuietly("update-desktop-database", Path.GetDirectoryName(MenuEntry)!);
    }

    public override void Launch(string directory) =>
        Process.Start(new ProcessStartInfo(Path.Combine(directory, ProgramName)) { WorkingDirectory = directory, UseShellExecute = false });

    static void WriteFile(string path, string content, bool executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (executable)
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
    }

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    // Cache updaters are a nicety; a missing tool is not an error.
    static void RunQuietly(string tool, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            p?.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
