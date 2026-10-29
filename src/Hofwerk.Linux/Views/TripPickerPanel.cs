using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>
/// The trips of the chosen map as a list with ticks, and buttons to tick them all, none, or by the way they write their stops (as in
/// HOF Suite's import). Closed to begin with: most of the time every trip is wanted. Used by the New file and Import windows.
/// </summary>
internal static class TripPickerPanel
{
    public static Expander Create(TripSelection picker)
    {
        var summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        var all = new Button { Content = "Select all", Command = picker.SelectAllCommand };
        var none = new Button { Content = "Select none", Command = picker.SelectNoneCommand };
        var links = new Button { Content = "Only trips with station links", Command = picker.SelectStationLinksCommand };
        var plain = new Button { Content = "Only trips with stop names", Command = picker.SelectPlainCommand };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in new[] { all, none, links, plain }) buttons.Children.Add(button);

        var list = new ListBox
        {
            ItemsSource = picker.Trips,
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            MaxHeight = 220,
            ItemTemplate = new FuncDataTemplate<TripChoice>((trip, _) =>
            {
                if (trip is null) return new TextBlock();
                var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
                check.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, new Avalonia.Data.Binding { Path = nameof(TripChoice.IsSelected), Mode = Avalonia.Data.BindingMode.TwoWay });
                AutomationProperties.SetName(check, trip.Name);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = trip.Name, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = trip.Detail, FontSize = 12, Opacity = 0.6, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(check);
                row.Children.Add(text);
                return row;
            }),
        };
        // the ticks are in the check boxes; picking a row is not another way of choosing
        list.SelectionChanged += (_, _) => list.SelectedItems?.Clear();
        AutomationProperties.SetName(list, "Trips");

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(summary);
        panel.Children.Add(buttons);
        panel.Children.Add(list);

        var expander = new Expander { Header = "Choose which trips", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        void Refresh()
        {
            summary.Text = picker.Summary;
            links.IsVisible = plain.IsVisible = picker.HasBothKinds;
        }
        picker.Changed += Refresh;
        Refresh();
        return expander;
    }

}
