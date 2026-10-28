using Hofwerk.Localization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>
/// Puts the open file into every vehicle folder that is ticked. The ticked folders are the file's own export list (HOF Suite's
/// [HOF_SUITE_DIRECTORIES]) and are saved in the file, so next time they are ticked already.
/// </summary>
public sealed class ExportWindow : Window
{
    readonly ExportViewModel _model;
    readonly MainViewModel _main;
    readonly StackPanel _list = new();
    readonly TextBlock _count = new() { Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    readonly SelectableTextBlock _report = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBox _search = new() { PlaceholderText = "Search folders" };
    readonly Button _all = new() { Content = "Tick all shown" };
    readonly Button _none = new() { Content = "Clear all shown" };
    readonly CheckBox _backup = new() { Content = "Keep a copy (.bak) of files that are replaced", IsChecked = true };
    readonly Button _export = new() { Content = "Export", IsDefault = true };
    readonly Button _cancel = new() { Content = "Cancel", IsCancel = true };

    public ExportWindow(MainViewModel main, ExportViewModel model)
    {
        _main = main;
        _model = model;
        Title = "Export to several folders";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AutomationProperties.SetName(_search, "Search folders");
        AutomationProperties.SetName(_report, "Export result");

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "Export to several folders", FontSize = 20, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Text = Localizer.F("Put {0} into every folder you tick, so every bus that uses it has the latest version. The ticked folders are saved in the file, like HOF Suite does, so next time they are ticked already.", model.FileName),
        });
        body.Children.Add(_search);
        body.Children.Add(new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4),
            Child = new ScrollViewer { Content = _list, MaxHeight = 300 },
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_all);
        row.Children.Add(_none);
        row.Children.Add(_count);
        body.Children.Add(row);
        body.Children.Add(_backup);
        body.Children.Add(_bar);
        body.Children.Add(_report);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_export);

        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = body });
        Content = layout;

        _cancel.Click += (_, _) => Close(false);
        _search.TextChanged += (_, _) => model.Filter = _search.Text ?? "";
        _all.Click += (_, _) => { model.SelectAllShown(true); Rebuild(); };
        _none.Click += (_, _) => { model.SelectAllShown(false); Rebuild(); };
        _backup.IsCheckedChanged += (_, _) => model.BackupReplaced = _backup.IsChecked == true;
        _export.Click += async (_, _) =>
        {
            if (model.IsDone) { Close(true); return; }     // the button now reads Close
            await _main.ExportAsync(model);
        };

        model.Shown.CollectionChanged += (_, _) => Rebuild();
        model.PropertyChanged += OnModelChanged;
        Rebuild();
        Refresh();
    }

    void Rebuild()
    {
        _list.Children.Clear();
        foreach (var item in _model.Shown)
        {
            var box = new CheckBox { Content = item.Exists ? item.Name : $"{item.Name}  ({item.Note})", IsChecked = item.IsSelected, MinHeight = 28 };
            if (!item.Exists) box.Opacity = 0.6;
            box.IsCheckedChanged += (_, _) => item.IsSelected = box.IsChecked == true;
            AutomationProperties.SetName(box, item.Name);
            _list.Children.Add(box);
        }
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    void Refresh()
    {
        _count.Text = _model.SelectionText;
        _bar.IsVisible = _model.IsRunning;
        _bar.Value = _model.Progress;
        _report.Text = _model.Report;
        _export.IsEnabled = _model.CanExport || _model.IsDone;
        _export.Content = Localizer.T(_model.IsDone ? "Close" : "Export");
        _cancel.IsVisible = !_model.IsDone;
        _search.IsEnabled = _all.IsEnabled = _none.IsEnabled = _backup.IsEnabled = !_model.IsRunning && !_model.IsDone;
    }
}
