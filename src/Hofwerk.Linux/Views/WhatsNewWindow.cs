using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Hofwerk.Core.Updates;

namespace Hofwerk.Linux.Views;

/// <summary>The release notes of a version in a pop-up window, so the App settings page stays short.</summary>
public sealed class WhatsNewWindow : Window
{
    /// <param name="pageButton">Text of the button that opens <paramref name="pageUrl"/>; the button is left out when there is no web page.</param>
    public WhatsNewWindow(string title, string? subtitle, IReadOnlyList<ChangeGroup> groups, IDataTemplate groupTemplate, string? pageUrl, string pageButton)
    {
        Title = title;
        Width = 680;
        Height = 560;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var content = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 14, 0) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(subtitle))
            content.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ItemsControl { ItemsSource = groups, ItemTemplate = groupTemplate });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps)
        {
            var open = new Button { Content = pageButton };
            open.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
            buttons.Children.Add(open);
        }
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 14, 0, 0);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = content });
        Content = layout;
    }
}
