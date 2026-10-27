using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Hofwerk.Installer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            Vm.ExitRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            await Vm.CheckForNewerVersionAsync();
        };
    }

    InstallerViewModel Vm => (InstallerViewModel)DataContext!;

    async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to install Hofwerk",
            AllowMultiple = false,
        });

        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
            Vm.UseBrowsedFolder(path);
    }

    async void OnChooseOmsi(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select your OMSI 2 folder",
            AllowMultiple = false,
        });

        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
            Vm.LocationChosen(path);
    }

    void OnLaunch(object? sender, RoutedEventArgs e)
    {
        Vm.LaunchApp();
        Close();
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
}
