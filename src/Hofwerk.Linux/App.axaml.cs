using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hofwerk.ViewModels;
using Hofwerk.Linux.Views;

namespace Hofwerk.Linux;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Errors nobody expected: written to crash.log, then a window offers to restart, try the standard version, or report the bug.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            ErrorHandler.Handle(e.Exception, "Unhandled exception");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ErrorHandler.HandleFatal(ex, "Unhandled exception (the program is closing)");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ErrorHandler.Handle(e.Exception, "Background task");
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            // The theme chosen in App settings ("System" follows the desktop).
            void SetTheme(string id) => RequestedThemeVariant = id switch
            {
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
            SetTheme(vm.ThemeId);
            vm.ThemeChanged += id =>
            {
                if (window.Content is Avalonia.Controls.Control content) Motion.ThemeChange(content, () => SetTheme(id));
                else SetTheme(id);
            };

            // After an update download the installer is running; close so it can replace the files.
            vm.ExitRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());

            // "Open with Hofwerk" and double-clicking a .hof pass the file as the first argument.
            string? file = desktop.Args?.FirstOrDefault(a => a.EndsWith(".hof", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
            if (file is not null && vm.Open(file))
                window.Opened += (_, _) => window.ShowSettings();
        }

        base.OnFrameworkInitializationCompleted();
    }
}