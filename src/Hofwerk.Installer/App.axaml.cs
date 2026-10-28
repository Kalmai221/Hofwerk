using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Hofwerk.Installer;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The window is translated once it has loaded, and again whenever the language is changed in the drop-down.
            Avalonia.Controls.Control.LoadedEvent.AddClassHandler<Avalonia.Controls.Window>((w, _) => Hofwerk.Localization.AvaloniaTranslator.Apply(w));
            Hofwerk.Localization.Localizer.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var open in desktop.Windows) Hofwerk.Localization.AvaloniaTranslator.Apply(open);
            });

            desktop.MainWindow = new MainWindow
            {
                DataContext = new InstallerViewModel(desktop.Args ?? []),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
