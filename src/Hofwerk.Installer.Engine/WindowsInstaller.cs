using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Hofwerk.Installer.Engine;

/// <summary>Per-user install: no administrator rights, shows up under "Installed apps", Start menu and optionally the desktop.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstaller : PlatformInstaller
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hofwerk";
    const string ProgId = "Hofwerk.hof";

    public override string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hofwerk");

    public override string ProgramName => "Hofwerk.exe";
    public override string UninstallerName => "Uninstall.exe";
    public override bool SupportsFileAssociation => true;

    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hofwerk.lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Hofwerk.lnk");

    public override string? FindInstalledDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if (key?.GetValue("InstallLocation") is string dir && IsInstalled(dir)) return dir;
        return base.FindInstalledDirectory();
    }

    protected override void Register(InstallRequest request, string directory, long sizeInBytes)
    {
        string exe = Path.Combine(directory, ProgramName);

        CreateShortcut(StartMenuLink, exe, directory);
        if (request.DesktopShortcut) CreateShortcut(DesktopLink, exe, directory);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", AppInfo.Name);
            key.SetValue("DisplayVersion", AppInfo.Version);
            key.SetValue("Publisher", AppInfo.Publisher);
            key.SetValue("URLInfoAbout", AppInfo.Url);
            key.SetValue("InstallLocation", directory);
            key.SetValue("DisplayIcon", exe);
            key.SetValue("UninstallString", $"\"{Path.Combine(directory, UninstallerName)}\" --uninstall");
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeInBytes / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // "Open with": Hofwerk is offered for .hof files without taking over whatever the user has set as default.
        if (request.FileAssociation)
        {
            using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.hof\OpenWithProgids"))
                ext.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                prog.SetValue("", "OMSI HOF file");
            using (var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
                icon.SetValue("", $"\"{exe}\",0");
            using (var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
            NotifyAssociationsChanged();
        }
    }

    protected override void Unregister(string directory)
    {
        DeleteIfExists(StartMenuLink);
        DeleteIfExists(DesktopLink);

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        using (var ext = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.hof\OpenWithProgids", writable: true))
            ext?.DeleteValue(ProgId, throwOnMissingValue: false);
        NotifyAssociationsChanged();
    }

    // The running uninstaller cannot delete itself, so a hidden helper removes it a moment after this process exits.
    protected override void FinishSelfRemoval(string directory)
    {
        string self = Path.Combine(directory, UninstallerName);
        string manifest = Path.Combine(directory, ManifestName);
        string script = $"/c ping -n 3 127.0.0.1 >nul & del /f /q \"{self}\" \"{manifest}\" & rmdir \"{directory}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", script) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    public override void Launch(string directory) =>
        Process.Start(new ProcessStartInfo(Path.Combine(directory, ProgramName)) { WorkingDirectory = directory, UseShellExecute = true });

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    // Shortcuts are created through the Windows scripting host, which every Windows install has. It is called by name
    // (IDispatch) rather than through `dynamic`, so the installer can be trimmed without losing this.
    static void CreateShortcut(string linkPath, string target, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? link = null;
        try
        {
            link = Call(shell, "CreateShortcut", BindingFlags.InvokeMethod, linkPath)!;
            Call(link, "TargetPath", BindingFlags.SetProperty, target);
            Call(link, "WorkingDirectory", BindingFlags.SetProperty, workingDirectory);
            Call(link, "IconLocation", BindingFlags.SetProperty, target + ",0");
            Call(link, "Description", BindingFlags.SetProperty, "Edit OMSI .hof files");
            Call(link, "Save", BindingFlags.InvokeMethod);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    static object? Call(object target, string name, BindingFlags flags, params object[] args) =>
        target.GetType().InvokeMember(name, flags, binder: null, target, args);

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    static void NotifyAssociationsChanged() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
}
