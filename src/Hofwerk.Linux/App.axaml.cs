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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

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