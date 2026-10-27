using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>Add the bus stops, destinations and routes of an OMSI map, or of one timetable file, to the open file.</summary>
public sealed class ImportWindow : Window
{
    readonly ImportViewModel _model;
    readonly StackPanel _mapPanel = new() { Spacing = 10 };
    readonly StackPanel _tripPanel = new() { Spacing = 10, IsVisible = false };
    readonly ListBox _maps = new() { SelectionMode = SelectionMode.Single, MaxHeight = 280 };
    readonly ProgressBar _reading = new() { IsIndeterminate = true, IsVisible = false };
    readonly TextBlock _tripName = new() { Opacity = 0.8, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly Button _import = new() { Content = "Import", IsDefault = true };

    public ImportWindow(ImportViewModel model, string? mapsFolder)
    {
        _model = model;
        Title = "Import into this file";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        MinHeight = 260;
        MaxHeight = 720;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var fromMap = new RadioButton { Content = "From an OMSI map", GroupName = "source", IsChecked = true };
        var fromTrip = new RadioButton { Content = "One timetable file (.ttp)", GroupName = "source" };
        AutomationProperties.SetName(fromMap, "From an OMSI map");
        AutomationProperties.SetName(fromTrip, "One timetable file");

        _maps.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel());
        _maps.ItemsSource = model.Maps;
        _maps.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Hofwerk.Core.Omsi.OmsiMap>((m, _) => NewFileWindow.MapTile(m));
        ScrollViewer.SetHorizontalScrollBarVisibility(_maps, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _maps.ContainerPrepared += (_, e) =>
        {
            if (e.Container.DataContext is Hofwerk.Core.Omsi.OmsiMap map) AutomationProperties.SetName(e.Container, map.Title);
        };
        AutomationProperties.SetName(_maps, "Map");
        var browseMap = new Button { Content = "Choose another map folder…" };
        _mapPanel.Children.Add(_maps);
        _mapPanel.Children.Add(browseMap);

        var chooseTrip = new Button { Content = "Choose a timetable file…" };
        _tripPanel.Children.Add(chooseTrip);
        _tripPanel.Children.Add(_tripName);

        var busStops = new CheckBox { Content = "Bus stops", IsChecked = true };
        var destinations = new CheckBox { Content = "Destinations", IsChecked = true };
        var routes = new CheckBox { Content = "Routes, with their stops", IsChecked = true };
        var partsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        partsRow.Children.Add(busStops);
        partsRow.Children.Add(destinations);
        partsRow.Children.Add(routes);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        _import.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_import);

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
        body.Children.Add(new TextBlock { Text = "Import into this file", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = "What is added goes in next to what the file already has. Nothing is added twice.", Opacity = 0.7 });
        body.Children.Add(fromMap);
        body.Children.Add(fromTrip);
        body.Children.Add(_mapPanel);
        body.Children.Add(_tripPanel);
        body.Children.Add(_reading);
        body.Children.Add(new TextBlock { Text = "Take", Margin = new Thickness(0, 4, 0, 0) });
        body.Children.Add(partsRow);
        body.Children.Add(_summary);

        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = body });
        Content = layout;

        fromMap.IsCheckedChanged += (_, _) => model.FromTrip = fromTrip.IsChecked == true;
        fromTrip.IsCheckedChanged += (_, _) => model.FromTrip = fromTrip.IsChecked == true;
        _maps.SelectionChanged += (_, _) => model.SelectedMap = _maps.SelectedItem as Hofwerk.Core.Omsi.OmsiMap;
        busStops.IsCheckedChanged += (_, _) => model.ImportBusStops = busStops.IsChecked == true;
        destinations.IsCheckedChanged += (_, _) => model.ImportDestinations = destinations.IsChecked == true;
        routes.IsCheckedChanged += (_, _) => model.ImportRoutes = routes.IsChecked == true;

        browseMap.Click += async (_, _) =>
        {
            IStorageFolder? start = mapsFolder is { Length: > 0 } && System.IO.Directory.Exists(mapsFolder) ? await StorageProvider.TryGetFolderFromPathAsync(mapsFolder) : null;
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose an OMSI map folder", AllowMultiple = false, SuggestedStartLocation = start });
            if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
            {
                model.AddMapFolder(path);
                fromMap.IsChecked = true;
                _maps.SelectedItem = model.SelectedMap;
            }
        };
        chooseTrip.Click += async (_, _) =>
        {
            IStorageFolder? start = mapsFolder is { Length: > 0 } && System.IO.Directory.Exists(mapsFolder) ? await StorageProvider.TryGetFolderFromPathAsync(mapsFolder) : null;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose an OMSI timetable file",
                AllowMultiple = false,
                SuggestedStartLocation = start,
                FileTypeFilter = [new FilePickerFileType("OMSI timetable (*.ttp)") { Patterns = ["*.ttp"] }, FilePickerFileTypes.All],
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is { } path) model.SetTripFile(path);
        };

        model.PropertyChanged += OnModelChanged;
        Refresh();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    void Refresh()
    {
        _mapPanel.IsVisible = !_model.FromTrip;
        _tripPanel.IsVisible = _model.FromTrip;
        _reading.IsVisible = _model.IsReading;
        _tripName.Text = _model.TripFile is { Length: > 0 } file ? System.IO.Path.GetFileName(file) : "";
        _summary.Text = _model.Summary;
        _import.IsEnabled = _model.CanImport;
    }
}
