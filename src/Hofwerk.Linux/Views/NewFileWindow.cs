using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>The New file dialog: start with an empty depot, or take the bus stops, destinations and routes from an OMSI map.</summary>
public sealed class NewFileWindow : Window
{
    readonly NewFileViewModel _model;
    readonly StackPanel _mapPanel = new() { Spacing = 10, IsVisible = false };
    readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.75 };
    readonly ComboBox _maps = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a map" };
    readonly Button _continue = new() { Content = "Choose where to save…", IsDefault = true };

    public NewFileWindow(NewFileViewModel model, string? mapsFolder)
    {
        _model = model;
        Title = "New .hof file";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MinHeight = 260;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var empty = new RadioButton { Content = "An empty depot", GroupName = "start", IsChecked = true };
        var fromMap = new RadioButton { Content = "Bus stops, destinations and routes from an OMSI map", GroupName = "start" };
        AutomationProperties.SetName(fromMap, "From an OMSI map");

        _maps.ItemsSource = model.Maps;
        _maps.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Hofwerk.Core.Omsi.OmsiMap>((m, _) => new TextBlock { Text = m?.Name });
        AutomationProperties.SetName(_maps, "Map");
        var browse = new Button { Content = "Choose another map folder…" };

        var busStops = new CheckBox { Content = "Bus stops", IsChecked = true };
        var destinations = new CheckBox { Content = "Destinations", IsChecked = true };
        var routes = new CheckBox { Content = "Routes, with their stops", IsChecked = true };

        _mapPanel.Children.Add(_maps);
        _mapPanel.Children.Add(browse);
        _mapPanel.Children.Add(_summary);
        _mapPanel.Children.Add(new TextBlock { Text = "Take from the map", Margin = new Thickness(0, 4, 0, 0) });
        _mapPanel.Children.Add(busStops);
        _mapPanel.Children.Add(destinations);
        _mapPanel.Children.Add(routes);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        _continue.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_continue);

        var layout = new StackPanel { Spacing = 10, Margin = new Thickness(24, 20, 24, 18) };
        layout.Children.Add(new TextBlock { Text = "New .hof file", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        layout.Children.Add(empty);
        layout.Children.Add(fromMap);
        layout.Children.Add(_mapPanel);
        layout.Children.Add(buttons);
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
                Title = "Choose an OMSI map folder",
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

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    void Refresh()
    {
        _mapPanel.IsVisible = _model.FromMap;
        _summary.Text = _model.Summary;
        _continue.IsEnabled = _model.CanContinue;
    }
}
