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
        CompatCard.IsVisible = OperatingSystem.IsWindows() && Hofwerk.Core.Errors.WindowsUi.Current.IsCompat;
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().First(i => i.Tag as string == "home");

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Window width changes how the pages are laid out; the content area width is what matters, not the window's.
        SizeChanged += (_, _) => ApplyAdaptiveLayout();
        Opened += async (_, _) =>
        {
            ApplyAdaptiveLayout();

            // Start Hofwerk with HOFWERK_SIMULATE_ERROR=1 to see the error window without a real bug.
            if (Environment.GetEnvironmentVariable("HOFWERK_SIMULATE_ERROR") == "1")
                Avalonia.Threading.Dispatcher.UIThread.Post(() => throw new InvalidOperationException("Simulated error (HOFWERK_SIMULATE_ERROR is set)"));

            await Vm.CheckForUpdatesOnStartAsync();
        };
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    bool _syncingNav;

    // The sidebar is two lists (the pages, and App settings at the bottom); only one of them has a selected item.
    void OnNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav || Nav.SelectedItem is not ListBoxItem item) return;
        _syncingNav = true;
        NavFooter.SelectedItem = null;
        _syncingNav = false;
        ShowPage(item.Tag as string ?? "home");
    }

    void OnFooterNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav || NavFooter.SelectedItem is not ListBoxItem item) return;
        _syncingNav = true;
        Nav.SelectedItem = null;
        _syncingNav = false;
        ShowPage(item.Tag as string ?? "appsettings");
    }

    void ShowPage(string tag)
    {
        if (tag == "header") return; // section labels and lines in the sidebar are not pages
        if (tag == "settings") Vm.RefreshSettingsData(); // lists and folder checks are read when the page is shown
        if (tag == "routes") Vm.RefreshRouteData();      // destination list and unknown-stop flags are read when shown
        HomePanel.IsVisible = tag == "home";
        AppSettingsPanel.IsVisible = tag == "appsettings";
        SettingsPanel.IsVisible = tag == "settings";
        DestinationsPanel.IsVisible = tag == "destinations";
        BusStopsPanel.IsVisible = tag == "busstops";
        RoutesPanel.IsVisible = tag == "routes";
        ProblemsPanel.IsVisible = tag == "problems";

        // The page that was just shown eases in.
        Control? shown = tag switch
        {
            "home" => HomePanel, "appsettings" => AppSettingsPanel, "settings" => SettingsPanel, "destinations" => DestinationsPanel,
            "busstops" => BusStopsPanel, "routes" => RoutesPanel, "problems" => ProblemsPanel, _ => null,
        };
        if (shown is not null) Hofwerk.Linux.Motion.PageEnter(shown);

        (SectionTitle.Text, SectionHint.Text) = tag switch
        {
            "home" => ("Home", "Pick up where you left off, or open something from your OMSI install"),
            "appsettings" => ("App settings", "Preferences for Hofwerk itself"),
            "settings" => ("Settings", "What applies to this whole depot file"),
            "destinations" => ("Destinations", "What the bus display shows for each destination code"),
            "busstops" => ("Bus stops", "Names and announcement strings"),
            "routes" => ("Routes", "Trips with their destination and list of stops"),
            _ => ("Problems", "Things that looked wrong while reading the file"),
        };
    }

    // ---- What's new: a pop-up, so App settings stays short -------------------------------------------------------

    void OnShowInstalledNotes(object? sender, RoutedEventArgs e) =>
        ShowNotes(Vm.InstalledNotesTitle, Vm.InstalledNotesSubtitle, Vm.InstalledNotes, MainViewModel.ChangelogUrl, "Full changelog");

    void OnShowUpdateNotes(object? sender, RoutedEventArgs e)
    {
        // A release without notes of its own just opens its page.
        if (Vm.ShowUpdateNotes) ShowNotes(Vm.UpdateNotesTitle, null, Vm.UpdateNotes, Vm.UpdatePageUrl, "Open on GitHub");
        else Vm.OpenReleasePageCommand.Execute(null);
    }

    void ShowNotes(string title, string? subtitle, IReadOnlyList<Hofwerk.Core.Updates.ChangeGroup> groups, string? pageUrl, string pageButton)
    {
        var template = (Avalonia.Controls.Templates.IDataTemplate)this.FindResource("ChangeGroupTemplate")!;
        _ = new WhatsNewWindow(title, subtitle, groups, template, pageUrl, pageButton).ShowDialog(this);
    }

    // Windows only: from the compatibility version back to the standard one, for good.
    void OnUseStandardVersion(object? sender, RoutedEventArgs e)
    {
        string? file = Vm.FilePath is { } path && !Vm.IsDirty && System.IO.File.Exists(path) ? path : null;
        if (Hofwerk.Core.Errors.WindowsUi.Current.SwitchToStandard(Hofwerk.Core.AppSettings.Load(), file)) ErrorHandler.Exit();
        else Vm.Status = "The standard version could not be started.";
    }

    void OnOpenGitHub(object? sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Kalmai221/Hofwerk") { UseShellExecute = true });

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

    // File dialogs start in OMSI's Vehicles folder when it is known, otherwise where the system would put them.
    Task<IStorageFolder?> VehiclesStart() => StartAt(Vm.VehiclesFolder);

    async Task<IStorageFolder?> StartAt(string? folder) =>
        folder is { Length: > 0 } && System.IO.Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;

    // A new, empty depot: pick where to put it, and it opens ready to edit.
    async void OnNew(object? sender, RoutedEventArgs e)
    {
        // First what to start with (empty, or from an OMSI map), then where to save it.
        var model = Vm.StartNewFile();
        string? maps = Vm.OmsiFolder is { } omsi ? System.IO.Path.Combine(omsi, "Maps") : null;
        if (!await new NewFileWindow(model, maps).ShowDialog<bool>(this)) return;

        var plan = model.ToPlan();
        IStorageFolder? start = await VehiclesStart();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create a new HOF file",
            DefaultExtension = "hof",
            SuggestedFileName = model.SuggestedFileName,
            SuggestedStartLocation = start,
            FileTypeChoices = HofFiles,
        });
        if (file?.TryGetLocalPath() is { } path && Vm.CreateNew(path, plan.Map, plan.Options)) ShowSettings();
    }

    async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open HOF file",
            AllowMultiple = false,
            SuggestedStartLocation = await VehiclesStart(),
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
            SuggestedStartLocation = await StartAt(Vm.OmsiFolder),
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
            SuggestedStartLocation = await VehiclesStart(),
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } parent) Vm.CreateFolder(row, parent);
    }

    void OnContinue(object? sender, RoutedEventArgs e)
    {
        if (Vm.LatestRecent is { } latest) OpenAndShow(latest.Path);
    }

    // Dropping a .hof file anywhere on the window opens it.
    void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault(f => f.Name.EndsWith(".hof", StringComparison.OrdinalIgnoreCase));
        if (file?.TryGetLocalPath() is { } path) OpenAndShow(path);
        else Vm.Status = "That is not a .hof file.";
    }

    // Preset links come from files anyone can contribute, so only plain web links are opened.
    void OnActivePresetLink(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedPreset is { HasUrl: true } preset
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
            // Saving a copy lands next to the file that is open.
            SuggestedStartLocation = await StartAt(Vm.FilePath is { } open ? System.IO.Path.GetDirectoryName(open) : null) ?? await VehiclesStart(),
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
