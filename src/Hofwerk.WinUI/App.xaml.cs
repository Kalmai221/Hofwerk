using Microsoft.UI.Xaml;

namespace Hofwerk.WinUI;

public partial class App : Application
{
    Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => CrashLog.Write("Unhandled exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) CrashLog.Write("Unhandled exception (domain)", ex);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // "Open with Hofwerk" and double-clicking a .hof pass the file as the first argument.
        string? file = Environment.GetCommandLineArgs().Skip(1)
            .FirstOrDefault(a => a.EndsWith(".hof", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        _window = new MainWindow(file);
        _window.Activate();
    }
}
