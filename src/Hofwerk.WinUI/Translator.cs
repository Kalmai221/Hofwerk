using System.Runtime.CompilerServices;
using Hofwerk.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Hofwerk.WinUI;

/// <summary>
/// Shows the fixed texts of the window in the language in use. The layout file is written in English; every text that has a translation
/// is swapped for it, and swapped back or on to another language when the language changes, because the English original of each text
/// is remembered. Texts that the program sets itself (names of files, numbers, messages) are not touched; those are translated where
/// they are made, with <see cref="Localizer.T"/>.
/// </summary>
internal static class Translator
{
    sealed record Seen(string Original, string Applied);

    // The English original of each text is kept on the element itself (an attached property), not in a table keyed by the C# object:
    // WinUI can give out a new C# object for the same Run each time it is asked, and the table would lose what it remembered, so a
    // text translated once could never be taken back to English or on to another language.
    static readonly DependencyProperty OriginalsProperty =
        DependencyProperty.RegisterAttached("Originals", typeof(string), typeof(Translator), new PropertyMetadata(""));

    const char RecordEnd = '', FieldEnd = '';

    static Dictionary<string, Seen> ReadMemory(DependencyObject owner)
    {
        var memory = new Dictionary<string, Seen>();
        foreach (string record in ((string)owner.GetValue(OriginalsProperty)).Split(RecordEnd, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split(FieldEnd);
            if (fields.Length == 3) memory[fields[0]] = new Seen(fields[1], fields[2]);
        }
        return memory;
    }

    static void WriteMemory(DependencyObject owner, Dictionary<string, Seen> memory) =>
        owner.SetValue(OriginalsProperty, string.Concat(memory.Select(p => $"{p.Key}{FieldEnd}{p.Value.Original}{FieldEnd}{p.Value.Applied}{RecordEnd}")));

    /// <summary>Translates everything now in the window.</summary>
    public static void Apply(DependencyObject root)
    {
        // a language written from right to left turns the whole window (or dialog) around
        if (root is FrameworkElement element)
            element.FlowDirection = Localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Visit(root, new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>Shows a dialog built in code, in the language in use.</summary>
    public static Windows.Foundation.IAsyncOperation<ContentDialogResult> ShowTranslated(this ContentDialog dialog)
    {
        Apply(dialog);
        return dialog.ShowAsync();
    }

    static string Translate(object owner, string property, string current)
    {
        var target = (DependencyObject)owner;
        var memory = ReadMemory(target);
        // A text that is not the one set last time was changed by the program since, so it is a new English original.
        string original = memory.TryGetValue(property, out var seen) && seen.Applied == current ? seen.Original : current;
        string result = Localizer.T(original);
        memory[property] = new Seen(original, result);
        WriteMemory(target, memory);
        return result;
    }

    static void Set(object owner, string property, string? current, Action<string> assign)
    {
        if (string.IsNullOrEmpty(current)) return;
        string result = Translate(owner, property, current);
        if (result != current) assign(result);
    }

    static void SetObject(object owner, string property, object? current, Action<object> assign)
    {
        if (current is string text) Set(owner, property, text, value => assign(value));
    }

    // Every element is visited once: it can be reached as a child, as the content of a control and through the visual tree, and visiting
    // it each time would double the work at every level of nesting.
    static void Visit(DependencyObject node, HashSet<DependencyObject> seen)
    {
        if (!seen.Add(node)) return;

        switch (node)
        {
            case TextBlock block:
                if (block.Inlines.Count > 1 || (block.Inlines.Count == 1 && block.Inlines[0] is not Run))
                    foreach (var inline in block.Inlines) VisitInline(inline);
                else Set(block, "Text", block.Text, value => block.Text = value);
                break;
            case TextBox box:
                SetObject(box, "Header", box.Header, value => box.Header = value);
                Set(box, "PlaceholderText", box.PlaceholderText, value => box.PlaceholderText = value);
                break;
            case ComboBox combo:
                SetObject(combo, "Header", combo.Header, value => combo.Header = value);
                Set(combo, "PlaceholderText", combo.PlaceholderText, value => combo.PlaceholderText = value);
                break;
            case NumberBox number:
                SetObject(number, "Header", number.Header, value => number.Header = value);
                break;
            case ToggleSwitch toggle:
                SetObject(toggle, "Header", toggle.Header, value => toggle.Header = value);
                SetObject(toggle, "OnContent", toggle.OnContent, value => toggle.OnContent = value);
                SetObject(toggle, "OffContent", toggle.OffContent, value => toggle.OffContent = value);
                break;
            case Expander expander:
                SetObject(expander, "Header", expander.Header, value => expander.Header = value);
                break;
            case AppBarButton appButton:
                Set(appButton, "Label", appButton.Label, value => appButton.Label = value);
                break;
            case AppBarToggleButton appToggle:
                Set(appToggle, "Label", appToggle.Label, value => appToggle.Label = value);
                break;
            case RadioButtons radios:
                SetObject(radios, "Header", radios.Header, value => radios.Header = value);
                break;
            case ContentDialog dialog:
                SetObject(dialog, "Title", dialog.Title, value => dialog.Title = value);
                Set(dialog, "PrimaryButtonText", dialog.PrimaryButtonText, value => dialog.PrimaryButtonText = value);
                Set(dialog, "SecondaryButtonText", dialog.SecondaryButtonText, value => dialog.SecondaryButtonText = value);
                Set(dialog, "CloseButtonText", dialog.CloseButtonText, value => dialog.CloseButtonText = value);
                if (dialog.Content is DependencyObject body) Visit(body, seen);
                break;
            case ContentControl content:
                SetObject(content, "Content", content.Content, value => content.Content = value);
                if (content.Content is DependencyObject inner) Visit(inner, seen);
                break;
        }

        if (node is FrameworkElement element)
        {
            SetObject(element, "ToolTip", ToolTipService.GetToolTip(element), value => ToolTipService.SetToolTip(element, value));
            Set(element, "AutomationName", AutomationProperties.GetName(element), value => AutomationProperties.SetName(element, value));
        }

        // elements that hold others as items instead of as children
        if (node is CommandBar bar)
        {
            foreach (var command in bar.PrimaryCommands) if (command is DependencyObject d) Visit(d, seen);
            foreach (var command in bar.SecondaryCommands) if (command is DependencyObject d) Visit(d, seen);
        }
        if (node is NavigationView nav)
        {
            foreach (var item in nav.MenuItems) if (item is DependencyObject d) Visit(d, seen);
            foreach (var item in nav.FooterMenuItems) if (item is DependencyObject d) Visit(d, seen);
        }

        // the items of a menu that opens from a button
        if (node is Button { Flyout: MenuFlyout flyout })
            foreach (var item in flyout.Items) VisitMenuItem(item);

        // children of elements that are not on the screen yet (a dialog built in code)
        if (node is Panel panel) foreach (var child in panel.Children) Visit(child, seen);
        if (node is Border border && border.Child is { } borderChild) Visit(borderChild, seen);

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(node, i), seen);
    }

    static void VisitMenuItem(MenuFlyoutItemBase item)
    {
        switch (item)
        {
            case MenuFlyoutSubItem sub:
                Set(sub, "Text", sub.Text, value => sub.Text = value);
                foreach (var child in sub.Items) VisitMenuItem(child);
                break;
            case MenuFlyoutItem entry:
                Set(entry, "Text", entry.Text, value => entry.Text = value);
                break;
        }
    }

    static void VisitInline(Inline inline)
    {
        switch (inline)
        {
            case Run run:
                Set(run, "Text", run.Text, value => run.Text = value);
                break;
            case Span span:
                foreach (var child in span.Inlines) VisitInline(child);
                break;
        }
    }
}
