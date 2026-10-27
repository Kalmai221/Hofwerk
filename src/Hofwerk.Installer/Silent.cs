using System.Runtime.InteropServices;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Installer;

/// <summary>
/// Windowless mode:  Setup --silent [--uninstall] [--dir PATH] [--omsi-folder PATH] [--desktop-shortcut] [--file-association] [--remove-user-data]
/// Exit code 0 means success.
/// </summary>
static class Silent
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    public static int Run(string[] args)
    {
        // The installer is a windowed program on Windows; borrow the console it was started from so output is visible.
        if (OperatingSystem.IsWindows()) AttachConsole(-1);

        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);
        string? Value(string flag)
        {
            int i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var platform = PlatformInstaller.Create();
        string exe = Environment.ProcessPath ?? "";
        var progress = new Progress<InstallProgress>(p => Console.WriteLine($"[{p.Fraction * 100,3:0}%] {p.Message}"));

        try
        {
            bool hasPayload = PayloadFile.HasPayload(exe);
            bool canInstall = hasPayload || WebPayload.TryRead(exe) is not null; // the online installer downloads the app
            if (Has("--uninstall") || !canInstall)
            {
                string dir = Value("--dir") ?? (canInstall ? platform.FindInstalledDirectory() : Path.GetDirectoryName(exe)) ?? platform.DefaultDirectory;
                platform.UninstallAsync(dir, Has("--remove-user-data"), progress).GetAwaiter().GetResult();
                Console.WriteLine("Hofwerk was removed.");
                return 0;
            }

            string target = Value("--dir") ?? platform.FindInstalledDirectory() ?? platform.DefaultDirectory;
            var request = new InstallRequest(target, Has("--desktop-shortcut"), Has("--file-association") && platform.SupportsFileAssociation,
                OmsiFolder: Value("--omsi-folder"));
            var download = new Progress<double>(d => Console.WriteLine($"[{d * 100,3:0}%] Downloading Hofwerk"));
            using var payload = WebPayload.OpenInstallPayloadAsync(exe, download).GetAwaiter().GetResult()!;
            platform.InstallAsync(request, payload, exe, progress).GetAwaiter().GetResult();
            Console.WriteLine($"Hofwerk {AppInfo.Version} was installed to {Path.GetFullPath(target)}");
            return 0;
        }
        catch (InstallerException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            SetupLog.Write("Unexpected error (silent)", ex);
            Console.Error.WriteLine($"Unexpected error: {ex.Message} (details in {SetupLog.Path_})");
            return 2;
        }
    }
}
