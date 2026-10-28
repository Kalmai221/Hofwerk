using Hofwerk.Localization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>Asks what is in a strings file the user brought (it usually has no preset.json): name, how it is written, what it is for.</summary>
public sealed class PresetImportWindow : Window
{
    readonly PresetImportViewModel _model;
    readonly StackPanel _forPanel = new() { Spacing = 4 };
    readonly StackPanel _firstPanel = new() { Spacing = 4 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly Button _import = new() { Content = "Import", IsDefault = true };

    public PresetImportWindow(PresetImportViewModel model)
    {
        _model = model;
        Title = "Import a strings file";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var name = Labelled("Name of the preset", new TextBox { Text = model.Name }, "Preset name", out var nameBox);
        var author = Labelled("Who made the names (optional)", new TextBox { Text = model.Author }, "Author", out var authorBox);
        var description = Labelled("What it is for (optional)", new TextBox { Text = model.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 }, "Description", out var descriptionBox);

        var layout = new ComboBox { ItemsSource = PresetImportViewModel.LayoutChoices, SelectedIndex = model.LayoutIndex, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(layout, "How the names are written");
        var layoutPanel = new StackPanel { Spacing = 4 };
        layoutPanel.Children.Add(new TextBlock { Text = "How are the names written in your file?" });
        layoutPanel.Children.Add(layout);

        var destinations = new CheckBox { Content = "Destinations", IsChecked = model.ForDestinations };
        var busStops = new CheckBox { Content = "Bus stops", IsChecked = model.ForBusStops };
        _forPanel.Children.Add(new TextBlock { Text = "What are the names for?" });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        row.Children.Add(destinations);
        row.Children.Add(busStops);
        _forPanel.Children.Add(row);

        var first = new NumericUpDown { Minimum = 0, Maximum = 99, Value = model.FirstSlot, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Left, Width = 140 };
        AutomationProperties.SetName(first, "First slot");
        _firstPanel.Children.Add(new TextBlock { Text = "Slot of the first line (OMSI counts from 0)" });
        _firstPanel.Children.Add(first);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        _import.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_import);

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 14, 0) };
        body.Children.Add(new TextBlock { Text = "Import a strings file", FontSize = 20, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = Localizer.F("File: {0}", model.FileName), Opacity = 0.7 });
        foreach (Control part in new Control[] { name, author, description, layoutPanel, _forPanel, _firstPanel, _summary }) body.Children.Add(part);

        var layoutRoot = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layoutRoot.Children.Add(buttons);
        layoutRoot.Children.Add(new ScrollViewer { Content = body });
        Content = layoutRoot;

        nameBox.TextChanged += (_, _) => model.Name = nameBox.Text ?? "";
        authorBox.TextChanged += (_, _) => model.Author = authorBox.Text ?? "";
        descriptionBox.TextChanged += (_, _) => model.Description = descriptionBox.Text ?? "";
        layout.SelectionChanged += (_, _) => model.LayoutIndex = Math.Max(0, layout.SelectedIndex);
        destinations.IsCheckedChanged += (_, _) => model.ForDestinations = destinations.IsChecked == true;
        busStops.IsCheckedChanged += (_, _) => model.ForBusStops = busStops.IsChecked == true;
        first.ValueChanged += (_, _) => model.FirstSlot = (int)(first.Value ?? 0);

        model.PropertyChanged += OnModelChanged;
        Refresh();
    }

    static StackPanel Labelled(string label, TextBox box, string automationName, out TextBox field)
    {
        field = box;
        AutomationProperties.SetName(box, automationName);
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        return panel;
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    void Refresh()
    {
        _forPanel.IsVisible = _model.AsksWhatFor;
        _firstPanel.IsVisible = _model.AsksFirstSlot;
        _summary.Text = _model.Summary;
        _import.IsEnabled = _model.CanImport;
    }
}
