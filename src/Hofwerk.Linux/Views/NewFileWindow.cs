using Hofwerk.Localization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>The New file dialog: start with an empty depot, or take the bus stops, destinations and routes from an OMSI map.</summary>
public sealed class NewFileWindow : Window
{
    readonly NewFileViewModel _model;
    readonly StackPanel _mapPanel = new() { Spacing = 10, IsVisible = false };
    readonly ProgressBar _reading = new() { IsIndeterminate = true, IsVisible = false };   // while the map is being read
    readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.75 };
    // The maps as pictures with their names, like OMSI's own map list (and HOF Suite's): click one to choose it.
    readonly ListBox _maps = new() { SelectionMode = SelectionMode.Single, MaxHeight = 280 };
    readonly Button _continue = new() { Content = "Choose where to save…", IsDefault = true };
    readonly Expander _trips;

    public NewFileWindow(NewFileViewModel model, string? mapsFolder)
    {
        _model = model;
        _trips = TripPickerPanel.Create(model.TripPicker);
        Title = "New .hof file";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        MinHeight = 260;
        MaxHeight = 700;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var empty = new RadioButton { Content = "An empty depot", GroupName = "start", IsChecked = true };
        var fromMap = new RadioButton { Content = "Bus stops, destinations and routes from an OMSI map", GroupName = "start" };
        AutomationProperties.SetName(fromMap, "From an OMSI map");

        _maps.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel());
        _maps.ItemsSource = model.Maps;
        _maps.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Hofwerk.Core.Omsi.OmsiMap>((m, _) => MapTile(m));
        ScrollViewer.SetHorizontalScrollBarVisibility(_maps, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        // The list item is what a screen reader (and the tests) find, so it carries the map's name.
        _maps.ContainerPrepared += (_, e) =>
        {
            if (e.Container.DataContext is Hofwerk.Core.Omsi.OmsiMap map) AutomationProperties.SetName(e.Container, map.Title);
        };
        AutomationProperties.SetName(_maps, "Map");
        var browse = new Button { Content = "Choose another map folder…" };

        var busStops = new CheckBox { Content = "Bus stops", IsChecked = true };
        var destinations = new CheckBox { Content = "Destinations", IsChecked = true };
        var routes = new CheckBox { Content = "Routes, with their stops", IsChecked = true };

        _mapPanel.Children.Add(_maps);
        _mapPanel.Children.Add(browse);
        _mapPanel.Children.Add(_reading);
        _mapPanel.Children.Add(_summary);
        _mapPanel.Children.Add(new TextBlock { Text = "Take from the map", Margin = new Thickness(0, 4, 0, 0) });
        var partsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        partsRow.Children.Add(busStops);
        partsRow.Children.Add(destinations);
        partsRow.Children.Add(routes);
        _mapPanel.Children.Add(partsRow);
        _mapPanel.Children.Add(_trips);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        _continue.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_continue);

        // The buttons stay at the bottom; the rest scrolls when the window is not tall enough for it (a small screen).
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
        body.Children.Add(new TextBlock { Text = "New .hof file", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        body.Children.Add(empty);
        body.Children.Add(fromMap);
        body.Children.Add(_mapPanel);

        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = body });
        Content = layout;

        empty.IsCheckedChanged += (_, _) => model.FromMap = fromMap.IsChecked == true;
        fromMap.IsCheckedChanged += (_, _) => model.FromMap = fromMap.IsChecked == true;
        _maps.SelectionChanged += (_, _) => model.SelectedMap = _maps.SelectedItem as Hofwerk.Core.Omsi.OmsiMap;
        busStops.IsCheckedChanged += (_, _) => model.ImportBusStops = busStops.IsChecked == true;
        destinations.IsCheckedChanged += (_, _) => model.ImportDestinations = destinations.IsChecked == true;
        routes.IsCheckedChanged += (_, _) => model.ImportRoutes = routes.IsChecked == true;
        browse.Click += async (_, _) =>
        {
            IStorageFolder? start = mapsFolder is { Length: > 0 } && System.IO.Directory.Exists(mapsFolder) ? await StorageProvider.TryGetFolderFromPathAsync(mapsFolder) : null;
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Localizer.T("Choose an OMSI map folder"),
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
            {
                model.AddMapFolder(path);
                fromMap.IsChecked = true;
                _maps.SelectedItem = model.SelectedMap;
            }
        };

        model.PropertyChanged += OnModelChanged;
        Refresh();
    }

    // One map in the grid: its picture (picture.jpg) above its name. A map without a picture gets a plain tile.
    internal static Control MapTile(Hofwerk.Core.Omsi.OmsiMap? map)
    {
        var picture = new Border
        {
            Width = 200,
            Height = 125,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
        };
        picture.Child = LoadPicture(map?.PicturePath) is { } bitmap
            ? new Image { Source = bitmap, Stretch = Stretch.UniformToFill }
            : new TextBlock { Text = "\u2316", FontSize = 36, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var tile = new StackPanel { Spacing = 6, Width = 200, Margin = new Thickness(2) };
        tile.Children.Add(picture);
        tile.Children.Add(new TextBlock { Text = map?.Title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        return tile;
    }

    static Avalonia.Media.Imaging.Bitmap? LoadPicture(string? path)
    {
        if (path is null) return null;
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 400);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    void Refresh()
    {
        _mapPanel.IsVisible = _model.FromMap;
        _trips.IsVisible = _model.TripPicker.HasTrips;
        _summary.Text = _model.Summary;
        _reading.IsVisible = _model.IsReading;
        _continue.IsEnabled = _model.CanContinue;
    }
}
