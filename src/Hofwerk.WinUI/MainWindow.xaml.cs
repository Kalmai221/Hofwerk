using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hofwerk.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        Title = ViewModel.WindowTitle;

        // The title bar is ours; the window icon is what the taskbar and Alt+Tab show.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Hofwerk.ico"));
        Root.ActualThemeChanged += (_, _) => ApplyCaptionButtonColors();
        Root.Loaded += (_, _) => ApplyCaptionButtonColors();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WindowTitle)) Title = ViewModel.WindowTitle;
        };
        Nav.SelectedItem = Nav.MenuItems[0];
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
    public static string FolderText(string? folder) => folder ?? "OMSI was not found automatically. Use “Open a .hof file” instead.";
    public static double D(int value) => value;
    public static Visibility NotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    void OnNavChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        HomePanel.Visibility = Show(tag == "home");
        PresetsPanel.Visibility = Show(tag == "presets");
        SettingsPanel.Visibility = Show(tag == "settings");
        DestinationsPanel.Visibility = Show(tag == "destinations");
        BusStopsPanel.Visibility = Show(tag == "busstops");
        RoutesPanel.Visibility = Show(tag == "routes");
        ProblemsPanel.Visibility = Show(tag == "problems");
    }

    static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

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
