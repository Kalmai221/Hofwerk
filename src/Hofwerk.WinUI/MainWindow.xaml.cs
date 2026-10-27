using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hofwerk.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow(string? openPath = null)
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        Title = ViewModel.WindowTitle;

        // The title bar is ours; the window icon is what the taskbar and Alt+Tab show.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Hofwerk.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.PreferredMinimumWidth = 760; presenter.PreferredMinimumHeight = 520; }
        Root.ActualThemeChanged += (_, _) => ApplyCaptionButtonColors();
        // After an update download the installer is running; close so it can replace the files.
        ViewModel.ExitRequested += () => DispatcherQueue.TryEnqueue(() => Application.Current.Exit());
        Root.Loaded += async (_, _) =>
        {
            ApplyCaptionButtonColors();

            // A file passed on the command line is opened once the window is up; WinUI crashes if navigation changes earlier.
            if (openPath is not null)
            {
                string path = openPath;
                openPath = null;
                OpenAndShow(path);
            }

            await ViewModel.CheckForUpdatesOnStartAsync();
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WindowTitle)) Title = ViewModel.WindowTitle;
        };
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        HomeTabs.SelectedItem = HomeTabs.Items[Math.Clamp(ViewModel.HomeTab, 0, HomeTabs.Items.Count - 1)];
    }

    // Minimize/maximize/close glyphs must contrast with the Mica background, which changes with light/dark mode.
    void ApplyCaptionButtonColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var fg = dark ? Colors.White : Colors.Black;
        var dim = dark ? Color.FromArgb(255, 150, 150, 150) : Color.FromArgb(255, 120, 120, 120);
        var hover = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
        var pressed = dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0);

        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = dim;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = fg;
    }

    // Helpers for x:Bind function bindings.
    public static bool HasFile(string? path) => path is not null;
    public static Visibility HasText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    public static string FolderText(string? folder) => folder ?? "OMSI was not found automatically. Use “Open a .hof file” instead.";
    public static double D(int value) => value;

    // The dot in the sidebar file card: green when everything is saved, amber when there are unsaved changes.
    public static Brush StateBrush(bool unsaved) =>
        (Brush)Application.Current.Resources[unsaved ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush"];
    public static Visibility NotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility IsNull(object? value) => value is null ? Visibility.Visible : Visibility.Collapsed;
    public static bool NotEmpty(string? text) => !string.IsNullOrEmpty(text);
    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    void OnNavChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        if (tag == "settings") ViewModel.RefreshSettingsData(); // lists and folder checks are read when the page is shown
        if (tag == "routes") ViewModel.RefreshRouteData();       // destination list and "stop not defined" checks
        HomePanel.Visibility = Show(tag == "home");
        PresetsPanel.Visibility = Show(tag == "presets");
        SettingsPanel.Visibility = Show(tag == "settings");
        DestinationsPanel.Visibility = Show(tag == "destinations");
        BusStopsPanel.Visibility = Show(tag == "busstops");
        RoutesPanel.Visibility = Show(tag == "routes");
        ProblemsPanel.Visibility = Show(tag == "problems");
    }

    static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    // ---- Layout that follows the size of the window ------------------------------------------------------------

    void OnPageHostSizeChanged(object sender, SizeChangedEventArgs e) => ApplyAdaptiveLayout(e.NewSize.Width);

    /// <summary>
    /// Rearranges the pages for the width of the content area (not of the window, which also holds the sidebar):
    /// list and details side by side when there is room, one above the other when there is not.
    /// </summary>
    void ApplyAdaptiveLayout(double width)
    {
        bool narrow = width < 760;     // list above the details
        bool tight = width < 1020;     // the two stop lists of a route above each other

        PageHost.Padding = width < 620 ? new Thickness(12, 8, 12, 12) : new Thickness(24, 8, 24, 16);

        StackMasterDetail(DestinationsPanel, DestListPane, DestDetailPane, narrow);
        StackMasterDetail(BusStopsPanel, StopListPane, StopDetailPane, narrow);
        StackMasterDetail(RoutesPanel, RouteListPane, RouteDetailPane, narrow);
        // The empty-state message of each page sits in the details column; keep it with the details.
        foreach (var page in new[] { DestinationsPanel, BusStopsPanel, RoutesPanel })
            foreach (var hint in page.Children.OfType<TextBlock>())
            {
                Grid.SetRow(hint, narrow ? 1 : 0);
                Grid.SetColumn(hint, narrow ? 0 : 1);
                Grid.SetColumnSpan(hint, narrow ? 2 : 1);
            }

        // Route stops: all stops | buttons | route stops, or the same top to bottom. The lists have a height of their own
        // and the details scroll, so a short window never squeezes a list down to one row.
        double hostHeight = Math.Max(0, PageHost.ActualHeight - PageHost.Padding.Top - PageHost.Padding.Bottom);
        double detailHeight = narrow ? Math.Max(hostHeight - 200 - 12, 360) : hostHeight;
        double listHeight = tight ? 260 : Math.Max(260, detailHeight - 118 - 12 - 200);   // minus the route fields and the card around the lists
        AllStopsList.Height = listHeight;
        RouteStopsList.Height = listHeight;

        var editor = RouteStopsEditor;
        editor.RowDefinitions[0].Height = GridLength.Auto;
        editor.RowDefinitions[1].Height = tight ? GridLength.Auto : new GridLength(0);
        editor.RowDefinitions[2].Height = tight ? GridLength.Auto : new GridLength(0);
        editor.ColumnDefinitions[1].Width = tight ? new GridLength(0) : GridLength.Auto;
        editor.ColumnDefinitions[2].Width = tight ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(StopButtons, tight ? 1 : 0);
        Grid.SetColumn(StopButtons, tight ? 0 : 1);
        Grid.SetRow(RouteStopsPane, tight ? 2 : 0);
        Grid.SetColumn(RouteStopsPane, tight ? 0 : 2);
        StopButtons.Orientation = tight ? Orientation.Horizontal : Orientation.Vertical;
        StopButtons.HorizontalAlignment = tight ? HorizontalAlignment.Left : HorizontalAlignment.Center;

        // Route details: four fields in a row, or two rows of two.
        bool compactHeader = width < 900;
        PlaceField(RouteFieldCode, compactHeader ? 0 : 0, compactHeader ? 0 : 0, compactHeader ? 3 : 1);
        PlaceField(RouteFieldLine, compactHeader ? 0 : 0, compactHeader ? 3 : 1, compactHeader ? 3 : 1);
        PlaceField(RouteFieldDestination, compactHeader ? 1 : 0, compactHeader ? 0 : 2, compactHeader ? 3 : 2);
        PlaceField(RouteFieldName, compactHeader ? 1 : 0, compactHeader ? 3 : 4, compactHeader ? 3 : 2);

        HeroButtons.Orientation = width < 560 ? Orientation.Vertical : Orientation.Horizontal;
    }

    static void StackMasterDetail(Grid page, FrameworkElement list, FrameworkElement detail, bool stacked)
    {
        page.RowDefinitions[0].Height = stacked ? new GridLength(200) : new GridLength(1, GridUnitType.Star);
        page.RowDefinitions[1].Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        Grid.SetRow(list, 0); Grid.SetColumn(list, 0); Grid.SetColumnSpan(list, stacked ? 2 : 1);
        Grid.SetRow(detail, stacked ? 1 : 0); Grid.SetColumn(detail, stacked ? 0 : 1); Grid.SetColumnSpan(detail, stacked ? 2 : 1);
    }

    static void PlaceField(FrameworkElement field, int row, int column, int span)
    {
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);
        Grid.SetColumnSpan(field, span);
    }

    // Double-clicking a bus stop in the "All stops" list adds it to the route.
    void OnPickerDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is BusStopViewModel stop) ViewModel.AddStopToRoute(stop);
    }

    void OnTerminusCount(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        ViewModel.TerminusStringCount = double.IsNaN(args.NewValue) ? 0 : (int)args.NewValue;

    void OnBusStopCount(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        ViewModel.BusStopStringCount = double.IsNaN(args.NewValue) ? 0 : (int)args.NewValue;

    async void OnOpen(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".hof");
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) OpenAndShow(file.Path);
    }

    void OnPickEntry(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HofFileEntry entry) OpenAndShow(entry.Path);
    }

    void OpenAndShow(string path)
    {
        if (ViewModel.Open(path)) SelectSection("settings");
    }

    void SelectSection(string tag) =>
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag as string == tag);

    // A folder is entered, a file is opened.
    void OnPickOmsiItem(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OmsiItem item && ViewModel.ActivateOmsiItem(item) is { } file) OpenAndShow(file);
    }

    // Lets the user point Hofwerk at their OMSI folder (also what the installer's "Where is OMSI 2?" step sets).
    async void OnChangeOmsi(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) ViewModel.TrySetOmsiFolder(folder.Path);
    }

    // The Recent files / OMSI library tabs on Home.
    void OnHomeTab(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0) ViewModel.HomeTab = index;
    }

    void OnShowLibrary(object sender, RoutedEventArgs e) => HomeTabs.SelectedItem = HomeTabs.Items[1];

    // IBIS codes are two digits: letters are refused as they are typed.
    void OnDigitsOnly(TextBox sender, TextBoxBeforeTextChangingEventArgs args) =>
        args.Cancel = args.NewText.Any(c => !char.IsDigit(c));

    // A missing folder is created where the user says: that depends on the bus pack, so it is not guessed.
    async void OnCreateFolder(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderRow row) return;

        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var parent = await picker.PickSingleFolderAsync();
        if (parent is not null) ViewModel.CreateFolder(row, parent.Path);
    }

    void OnContinue(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LatestRecent is { } latest) OpenAndShow(latest.Path);
    }

    void OnChangePreset(object sender, RoutedEventArgs e) => SelectSection("presets");

    // Dropping a .hof file anywhere on the window opens it.
    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open in Hofwerk";
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault(f => f.FileType.Equals(".hof", StringComparison.OrdinalIgnoreCase));
        if (file is not null) OpenAndShow(file.Path);
        else ViewModel.Status = "That is not a .hof file.";
    }

    void OnUsePreset(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PresetViewModel preset) ViewModel.UsePreset(preset);
    }

    // Preset links come from files anyone can contribute, so only plain web links are opened.
    void OnPresetLink(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PresetViewModel { HasUrl: true } preset
            && Uri.TryCreate(preset.Url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    void OnOpenPresetsFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ViewModel.UserPresetsFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ViewModel.UserPresetsFolder) { UseShellExecute = true });
    }

    void OnRefreshPresets(object sender, RoutedEventArgs e) => ViewModel.RefreshPresets();

    void OnSave(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FilePath is { } path) Guarded(() => ViewModel.Save(path), "save");
    }

    async void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(ViewModel.FilePath) };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("OMSI HOF file", [".hof"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null) Guarded(() => ViewModel.Save(file.Path), "save");
    }

    // File problems (locked file, no permission) go to the status bar instead of crashing the app.
    void Guarded(Action action, string verb)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.Status = $"Could not {verb} the file: {ex.Message}";
        }
    }
}
