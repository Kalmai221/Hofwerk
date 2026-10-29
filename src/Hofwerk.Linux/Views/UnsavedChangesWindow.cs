using Hofwerk.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hofwerk.Linux.Views;

public enum UnsavedAnswer { Cancel, Save, DontSave }

/// <summary>Shown when the window is closed, or another file is opened, with changes that are not saved.</summary>
public sealed class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow(string fileName, bool closing = true)
    {
        Title = "Save your changes?";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "Save", IsDefault = true };
        var dontSave = new Button { Content = "Don't save" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AutomationProperties.SetName(save, "Save");
        AutomationProperties.SetName(dontSave, "Don't save");
        AutomationProperties.SetName(cancel, "Cancel");
        save.Click += (_, _) => Close(UnsavedAnswer.Save);
        dontSave.Click += (_, _) => Close(UnsavedAnswer.DontSave);
        cancel.Click += (_, _) => Close(UnsavedAnswer.Cancel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(dontSave);
        buttons.Children.Add(save);

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 18), Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Save your changes?", FontSize = 20, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = closing
                ? Localizer.F("{0} has changes that are not saved. If you close without saving, they are lost.", fileName)
                : Localizer.F("{0} has changes that are not saved. If you go on without saving, they are lost.", fileName),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(buttons);
        Content = body;
    }
}
