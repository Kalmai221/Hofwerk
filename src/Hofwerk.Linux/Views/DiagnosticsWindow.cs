using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Hofwerk.ViewModels;

namespace Hofwerk.Linux.Views;

/// <summary>What Hofwerk knows about itself, and a few helpers for looking into a problem. Closes with true to ask for a simulated error.</summary>
public sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(MainViewModel vm)
    {
        Title = "Diagnostics";
        Width = 760;
        Height = 560;
        MinWidth = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox
        {
            Text = vm.DiagnosticsReport("Avalonia"),
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"),
            FontSize = 12,
        };
        AutomationProperties.SetName(box, "Diagnostics report");

        var copy = new Button { Content = "Copy" };
        var folder = new Button { Content = "Open settings folder" };
        var crash = new Button { Content = "Open crash log" };
        var clear = new Button { Content = "Clear recent files" };
        var simulate = new Button { Content = "Simulate an error" };
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true };

        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(box.Text ?? "");
            copy.Content = "Copied";
        };
        folder.Click += (_, _) => Open(vm.SettingsFolder);
        crash.Click += (_, _) =>
        {
            if (System.IO.File.Exists(vm.CrashLogFile)) Open(vm.CrashLogFile);
            else crash.Content = "No crash log";
        };
        clear.Click += (_, _) => { vm.ClearRecentFiles(); clear.Content = "Cleared"; };
        simulate.Click += (_, _) => Close(true);
        close.Click += (_, _) => Close(false);

        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var button in new[] { copy, folder, crash, clear, simulate }) row.Children.Add(button);

        var closeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        closeRow.Children.Add(close);

        var bottom = new StackPanel();
        bottom.Children.Add(row);
        bottom.Children.Add(closeRow);

        // the heading on top, the buttons at the bottom, and the report fills everything between them
        var heading = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock { Text = "Diagnostics", FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "What Hofwerk knows about itself. Your user name is taken out of the paths, so this can be pasted into a bug report.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
            },
        };
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.VerticalAlignment = VerticalAlignment.Stretch;
        box.TextWrapping = TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 16), LastChildFill = true };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(heading);
        layout.Children.Add(bottom);
        layout.Children.Add(box);
        Content = layout;
    }

    static void Open(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
