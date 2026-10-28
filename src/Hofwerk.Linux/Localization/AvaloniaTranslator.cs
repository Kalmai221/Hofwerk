using Hofwerk.Localization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;

namespace Hofwerk.Localization;

/// <summary>
/// Shows the fixed texts of an Avalonia window (and the installer, which links this file) in the language in use. The layout files are
/// written in English; every text that has a translation is swapped for it, and swapped back or on to another language when the
/// language changes, because the English original of each text is remembered. Texts that the program sets itself (names of files,
/// numbers, messages) are not touched; those are translated where they are made, with <see cref="Localizer.T"/>.
/// </summary>
public static class AvaloniaTranslator
{
    sealed record Seen(string Original, string Applied);

    static readonly ConditionalWeakTable<object, Dictionary<string, Seen>> Memory = new();

    /// <summary>Translates everything now in the window.</summary>
    public static void Apply(Visual root)
    {
        Visit(root);
        foreach (var visual in root.GetVisualDescendants()) Visit(visual);
    }

    static string Translate(object owner, string property, string current)
    {
        var memory = Memory.GetOrCreateValue(owner);
        // A text that is not the one set last time was changed by the program since, so it is a new English original.
        string original = memory.TryGetValue(property, out var seen) && seen.Applied == current ? seen.Original : current;
        string result = Localizer.T(original);
        memory[property] = new Seen(original, result);
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

    static void Visit(Visual visual)
    {
        if (visual is not Control control) return;

        switch (control)
        {
            case Window window:
                Set(window, "Title", window.Title, value => window.Title = value);
                break;
            case TextBlock block:
                if (block.Inlines is { Count: > 0 } inlines)
                {
                    foreach (var inline in inlines)
                        if (inline is Run run) Set(run, "Text", run.Text, value => run.Text = value);
                }
                else Set(block, "Text", block.Text, value => block.Text = value);
                break;
            case TextBox box:
                Set(box, "PlaceholderText", box.PlaceholderText, value => box.PlaceholderText = value);
                break;
            case ComboBox combo:
                Set(combo, "PlaceholderText", combo.PlaceholderText, value => combo.PlaceholderText = value);
                break;
            case ToggleSwitch toggle:
                SetObject(toggle, "OnContent", toggle.OnContent, value => toggle.OnContent = value);
                SetObject(toggle, "OffContent", toggle.OffContent, value => toggle.OffContent = value);
                break;
            case Expander expander:
                SetObject(expander, "Header", expander.Header, value => expander.Header = value);
                break;
        }

        if (control is ContentControl content)
            SetObject(content, "Content", content.Content, value => content.Content = value);

        SetObject(control, "ToolTip", ToolTip.GetTip(control), value => ToolTip.SetTip(control, value));
        Set(control, "AutomationName", AutomationProperties.GetName(control), value => AutomationProperties.SetName(control, value));
    }
}
