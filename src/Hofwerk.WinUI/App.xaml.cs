using Hofwerk.Core;
using Hofwerk.Core.Errors;
using Microsoft.UI.Xaml;

namespace Hofwerk.WinUI;

public partial class App : Application
{
    Window? _window;

    public App()
    {
        InitializeComponent();

        // Errors nobody expected: written to crash.log, then a dialog offers to restart, try another version, or report the bug.
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            ErrorHandler.Handle(e.Exception, "Unhandled exception");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ErrorHandler.HandleFatal(ex, "Unhandled exception (the program is closing)");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ErrorHandler.Handle(e.Exception, "Background task");
        };
    }

    static void RunRegistrationSelfTest()
    {
        string result;
        try
        {
            var platform = Hofwerk.Installer.Engine.PlatformInstaller.Create();
            platform.RefreshRegistration(WindowsUi.Current.InstallFolder, "selftest", 1024);
            result = "ok";
        }
        catch (Exception ex)
        {
            result = "failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        if (Environment.GetEnvironmentVariable("HOFWERK_SELFTEST_OUT") is { Length: > 0 } output) File.WriteAllText(output, result);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] arguments = Environment.GetCommandLineArgs();

        // Used by installer/test-installer.ps1: runs the part of an update that registers Hofwerk with Windows, inside this (trimmed)
        // program, writes what happened to the file named by HOFWERK_SELFTEST_OUT, and closes. Never used by anyone else.
        if (Environment.GetEnvironmentVariable("HOFWERK_SELFTEST") == "registration")
        {
            RunRegistrationSelfTest();
            Exit();
            return;
        }

        // "Open with Hofwerk" and double-clicking a .hof pass the file as the first argument.
        string? file = arguments.Skip(1).FirstOrDefault(a => a.EndsWith(".hof", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        // Someone who chose "Try a different version" after an error gets the compatibility version every time. "--standard" starts this
        // one anyway, so a broken compatibility version can never lock anyone out.
        var ui = WindowsUi.Current;
        bool standard = arguments.Skip(1).Contains("--standard", StringComparer.OrdinalIgnoreCase);
        if (!standard && ui.CompatInstalled && AppSettings.Load().PreferCompatibilityVersion && WindowsUi.Launch(ui.CompatExe, file))
        {
            Exit();
            return;
        }

        try
        {
            _window = new MainWindow(file);
            _window.Activate();
        }
        catch (Exception ex)
        {
            ErrorHandler.HandleFatal(ex, "Starting Hofwerk");
            Exit();
        }
    }
}
