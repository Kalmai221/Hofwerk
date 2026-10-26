using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

public partial class MainWindow : Window
{
    static readonly IReadOnlyList<FilePickerFileType> HofFiles =
        [new("OMSI HOF files") { Patterns = ["*.hof"] }, FilePickerFileTypes.All];

    public MainWindow()
    {
        InitializeComponent();
        Nav.SelectedIndex = 0;
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    void OnNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        string tag = (Nav.SelectedItem as ListBoxItem)?.Tag as string ?? "home";
        HomePanel.IsVisible = tag == "home";
        PresetsPanel.IsVisible = tag == "presets";
        SettingsPanel.IsVisible = tag == "settings";
        DestinationsPanel.IsVisible = tag == "destinations";
        BusStopsPanel.IsVisible = tag == "busstops";
        RoutesPanel.IsVisible = tag == "routes";
        ProblemsPanel.IsVisible = tag == "problems";

        (SectionTitle.Text, SectionHint.Text) = tag switch
        {
            "home" => ("Home", "Pick up where you left off, or open something from your OMSI install"),
            "presets" => ("Presets", "Names for the string slots, from the people who made your buses"),
            "settings" => ("Settings", "Depot name, service trip and string counts"),
            "destinations" => ("Destinations", "What the bus display shows for each destination code"),
            "busstops" => ("Bus stops", "Names and announcement strings"),
            "routes" => ("Routes", "Trips with their destination and list of stops"),
            _ => ("Problems", "Things that looked wrong while reading the file"),
        };
    }

    async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open HOF file",
            AllowMultiple = false,
            FileTypeFilter = HofFiles,
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            OpenAndShow(path);
    }

    // Clicking an entry on the Home screen opens it; the selection is cleared so the same entry can be clicked again.
    void OnPickEntry(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: HofFileEntry entry } list)
        {
            list.SelectedItem = null;
            OpenAndShow(entry.Path);
        }
    }

    void SelectSection(string tag) =>
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().First(i => i.Tag as string == tag);

    void OnUsePreset(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PresetViewModel preset) Vm.UsePreset(preset);
    }

    // Preset links come from files anyone can contribute, so only plain web links are opened.
    void OnPresetLink(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PresetViewModel { HasUrl: true } preset
            && Uri.TryCreate(preset.Url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    void OnOpenPresetsFolder(object? sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Vm.UserPresetsFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Vm.UserPresetsFolder) { UseShellExecute = true });
    }

    void OnRefreshPresets(object? sender, RoutedEventArgs e) => Vm.RefreshPresets();

    void OpenAndShow(string path)
    {
        if (Vm.Open(path)) SelectSection("settings");
    }

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Vm.FilePath is { } path) await Guarded(() => Vm.Save(path), "save");
    }

    async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save HOF file",
            DefaultExtension = "hof",
            SuggestedFileName = System.IO.Path.GetFileName(Vm.FilePath),
            FileTypeChoices = HofFiles,
        });
        if (file?.TryGetLocalPath() is { } path)
            await Guarded(() => Vm.Save(path), "save");
    }

    // File problems (locked file, no permission) are reported in the status bar instead of crashing the app.
    Task Guarded(Action action, string verb)
    {
        try { action(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Vm.Status = $"Could not {verb} the file: {ex.Message}";
        }
        return Task.CompletedTask;
    }
}
