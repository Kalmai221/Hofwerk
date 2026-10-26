using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
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
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().First(i => i.Tag as string == "home");

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Window width changes how the pages are laid out; the content area width is what matters, not the window's.
        SizeChanged += (_, _) => ApplyAdaptiveLayout();
        Opened += (_, _) => ApplyAdaptiveLayout();
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    void OnNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        string tag = (Nav.SelectedItem as ListBoxItem)?.Tag as string ?? "home";
        if (tag == "header") return; // section labels in the sidebar are not pages
        if (tag == "settings") Vm.RefreshSettingsData(); // lists and folder checks are read when the page is shown
        if (tag == "routes") Vm.RefreshRouteData();      // destination list and unknown-stop flags are read when shown
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
            "presets" => ("String Presets", "Names for the string slots, from the people who made your buses"),
            "settings" => ("Settings", "What applies to this whole depot file"),
            "destinations" => ("Destinations", "What the bus display shows for each destination code"),
            "busstops" => ("Bus stops", "Names and announcement strings"),
            "routes" => ("Routes", "Trips with their destination and list of stops"),
            _ => ("Problems", "Things that looked wrong while reading the file"),
        };
    }

    // Double-clicking a defined bus stop adds it to the selected route.
    void OnPickerDoubleTapped(object? sender, TappedEventArgs e) => Vm.AddPickedStopCommand.Execute(null);

    // Below ~1020 px the stop lists stack, below ~760 px the lists and their details stack; the sidebar gets a little narrower.
    void ApplyAdaptiveLayout()
    {
        double width = Bounds.Width;
        bool narrow = width < 1050;     // sidebar + content: master/detail no longer fit side by side
        bool tight = width < 1300;

        RootGrid.ColumnDefinitions[0].Width = new GridLength(narrow ? 200 : 232);

        foreach (var page in new[] { DestinationsPanel, BusStopsPanel, RoutesPanel })
        {
            page.ColumnDefinitions.Clear();
            page.RowDefinitions.Clear();
            if (narrow)
            {
                page.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                page.RowDefinitions.Add(new RowDefinition(new GridLength(220)));
                page.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                page.RowSpacing = 12;
            }
            else
            {
                page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(320)));
                page.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                page.RowSpacing = 0;
            }
            for (int i = 0; i < page.Children.Count; i++)
            {
                Grid.SetColumn(page.Children[i], narrow ? 0 : i);
                Grid.SetRow(page.Children[i], narrow ? i : 0);
            }
        }

        // The route fields: two rows of two when there is not room for four across.
        RouteFields.ColumnDefinitions.Clear();
        RouteFields.RowDefinitions.Clear();
        int cols = tight ? 2 : 4;
        for (int c = 0; c < cols; c++) RouteFields.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        int rows = tight ? 4 : 2;
        for (int r = 0; r < rows; r++) RouteFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < RouteFields.Children.Count; i += 2)
        {
            int field = i / 2; // caption + input pairs
            int col = tight ? field % 2 : field, row = tight ? field / 2 * 2 : 0;
            Grid.SetColumn(RouteFields.Children[i], col); Grid.SetRow(RouteFields.Children[i], row);
            Grid.SetColumn(RouteFields.Children[i + 1], col); Grid.SetRow(RouteFields.Children[i + 1], row + 1);
        }

        // All stops | buttons | Route stops side by side, or stacked when narrow.
        StopsGrid.ColumnDefinitions.Clear();
        StopsGrid.RowDefinitions.Clear();
        if (tight)
        {
            StopsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int r = 0; r < 3; r++) StopsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(AllStopsBox, 0); Grid.SetRow(AllStopsBox, 0);
            Grid.SetColumn(StopButtons, 0); Grid.SetRow(StopButtons, 1);
            Grid.SetColumn(RouteStopsBox, 0); Grid.SetRow(RouteStopsBox, 2);
            StopButtons.Orientation = Avalonia.Layout.Orientation.Horizontal;
        }
        else
        {
            StopsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            StopsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            StopsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            StopsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(AllStopsBox, 0); Grid.SetRow(AllStopsBox, 0);
            Grid.SetColumn(StopButtons, 1); Grid.SetRow(StopButtons, 0);
            Grid.SetColumn(RouteStopsBox, 2); Grid.SetRow(RouteStopsBox, 0);
            StopButtons.Orientation = Avalonia.Layout.Orientation.Vertical;
        }
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

    public void ShowSettings() => SelectSection("settings");

    void SelectSection(string tag) =>
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().First(i => i.Tag as string == tag);

    // A folder is entered, a file is opened.
    void OnPickOmsiItem(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: OmsiItem item } list) return;

        list.SelectedItem = null;
        if (Vm.ActivateOmsiItem(item) is { } file) OpenAndShow(file);
    }

    // Lets the user point Hofwerk at their OMSI folder (also what the installer's "Where is OMSI 2?" step sets).
    async void OnChangeOmsi(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select your OMSI 2 folder",
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path) Vm.TrySetOmsiFolder(path);
    }

    // The Recent files / OMSI library tabs on Home.
    void OnHomeRecent(object? sender, RoutedEventArgs e) => Vm.HomeTab = 0;
    void OnHomeLibrary(object? sender, RoutedEventArgs e) => Vm.HomeTab = 1;

    // A missing folder is created where the user says: that depends on the bus pack, so it is not guessed.
    async void OnCreateFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not FolderRow row) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Where should “{row.Name}” be created?",
            AllowMultiple = false,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } parent) Vm.CreateFolder(row, parent);
    }

    void OnContinue(object? sender, RoutedEventArgs e)
    {
        if (Vm.LatestRecent is { } latest) OpenAndShow(latest.Path);
    }

    void OnChangePreset(object? sender, RoutedEventArgs e) => SelectSection("presets");

    // Dropping a .hof file anywhere on the window opens it.
    void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault(f => f.Name.EndsWith(".hof", StringComparison.OrdinalIgnoreCase));
        if (file?.TryGetLocalPath() is { } path) OpenAndShow(path);
        else Vm.Status = "That is not a .hof file.";
    }

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
