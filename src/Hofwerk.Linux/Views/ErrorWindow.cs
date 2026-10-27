using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hofwerk.Core;
using Hofwerk.Core.Errors;

namespace Hofwerk.Linux.Views;

/// <summary>The window shown when Hofwerk ran into an error it did not expect (see <see cref="ErrorHandler"/>).</summary>
public sealed class ErrorWindow : Window
{
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public ErrorWindow(ErrorReport report, string? currentFile)
    {
        Title = "Something went wrong";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MinHeight = 300;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var restart = new Button { Content = "Restart Hofwerk" };
        AutomationProperties.SetName(restart, "Restart Hofwerk");
        restart.Click += (_, _) =>
        {
            if (ErrorActions.Restart()) ErrorHandler.Exit();
            else Say("Hofwerk could not be started again. Please start it yourself.");
        };

        var send = new Button { Content = "Report a bug" };
        AutomationProperties.SetName(send, "Report a bug");
        send.Click += (_, _) =>
        {
            if (!ErrorActions.OpenInBrowser(report.IssueUrl()))
                Say("The browser could not be opened. The report is in the box below; you can copy it into an issue at " + Hofwerk.Core.Model.HofHeader.Url + "/issues.");
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(restart);

        // On Windows the compatibility version offers the way back to the standard version (the other way round is in the WinUI app).
        var ui = WindowsUi.Current;
        if (OperatingSystem.IsWindows() && ui.IsCompat && System.IO.File.Exists(ui.StandardExe))
        {
            var standard = new Button { Content = "Try the standard version" };
            AutomationProperties.SetName(standard, "Try a different version");
            standard.Click += (_, _) =>
            {
                if (ui.SwitchToStandard(AppSettings.Load(), currentFile)) ErrorHandler.Exit();
                else Say("The standard version could not be started.");
            };
            buttons.Children.Add(standard);
        }
        buttons.Children.Add(send);

        var details = new TextBox
        {
            Text = report.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"),
            FontSize = 12,
            MaxHeight = 230,
        };
        var expander = new Expander { Header = "What a bug report contains", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch };

        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        close.Click += (_, _) => Close();

        var layout = new StackPanel { Spacing = 12, Margin = new Thickness(24, 20, 24, 18) };
        layout.Children.Add(new TextBlock { Text = "Something went wrong", FontSize = 20, FontWeight = FontWeight.SemiBold });
        layout.Children.Add(new TextBlock { Text = report.Summary, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        layout.Children.Add(new TextBlock
        {
            Text = "Hofwerk ran into a problem it did not expect. You can restart it" + (buttons.Children.Count > 2 ? ", try the standard version," : "")
                   + " or report the bug so it can be fixed.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        layout.Children.Add(buttons);
        layout.Children.Add(_status);
        layout.Children.Add(expander);
        layout.Children.Add(new TextBlock
        {
            Text = "Your name is taken out of file paths. Nothing is sent until you press Submit on the GitHub page.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            FontSize = 12,
        });
        layout.Children.Add(close);
        Content = layout;

        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Close();
        };
    }

    void Say(string text)
    {
        _status.Text = text;
        _status.IsVisible = true;
    }
}
