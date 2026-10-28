using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Hofwerk.Localization;

/// <summary>A language Hofwerk can be shown in: its code ("de") and its name in its own language ("Deutsch").</summary>
public sealed record Language(string Code, string NativeName);

/// <summary>One line of a language drop-down: "auto" (follow the system) or a language code, with the name to show.</summary>
public sealed record LanguageChoice(string Id, string Label);

/// <summary>
/// The texts of Hofwerk in the chosen language. The English text itself is the key: <c>Localizer.T("Save as")</c> gives "Speichern unter"
/// in German, and gives "Save as" for a language that has no translation for it yet, so a missing translation is never a blank or an error.
/// Every language is one file in <c>Languages/</c> (<c>de.json</c>); adding a file adds the language everywhere, including the installer.
/// </summary>
public static class Localizer
{
    public const string English = "en";
    public const string Automatic = "auto";

    static readonly Lazy<IReadOnlyList<Language>> Available = new(LoadLanguageList);
    static Dictionary<string, string> _texts = [];
    static string _current = English;

    // A language that applies only to the code that is running (and what it starts), for tests: they can use German without
    // changing the language of other tests that run at the same time.
    sealed record Scope(string Code, Dictionary<string, string> Texts);
    static readonly AsyncLocal<Scope?> Scoped = new();

    /// <summary>Every language, English first and the others by name.</summary>
    public static IReadOnlyList<Language> Languages => Available.Value;

    /// <summary>The code of the language in use. English until <see cref="Use"/> is called, so nothing changes language by accident (tests, tools).</summary>
    public static string Current => Scoped.Value?.Code ?? _current;

    /// <summary>Raised after the language in use changed, so windows can show their texts again.</summary>
    public static event Action? Changed;

    /// <summary>
    /// The best language for a culture: its own code if there is a file for it, otherwise English. Regions do not matter
    /// (de-AT and de-CH get "de"). Without a culture, the language Windows or the desktop is set to is used.
    /// </summary>
    public static string Detect(CultureInfo? culture = null)
    {
        string code = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.ToLowerInvariant();
        return Languages.Any(l => l.Code == code) ? code : English;
    }

    /// <summary>The code a setting stands for: nothing, empty or "auto" means the system language, a known code is itself, anything else is English.</summary>
    public static string Resolve(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals(Automatic, StringComparison.OrdinalIgnoreCase)) return Detect();
        string code = setting.Trim().ToLowerInvariant();
        return Languages.Any(l => l.Code == code) ? code : English;
    }

    /// <summary>Switches to the language a setting stands for (see <see cref="Resolve"/>). Returns the code now in use.</summary>
    public static string Use(string? setting)
    {
        string code = Resolve(setting);
        bool changed = code != _current;
        _texts = code == English ? [] : LoadTexts(code);
        _current = code;
        if (changed) Changed?.Invoke();
        return code;
    }

    /// <summary>The lines of a language drop-down: "System default (Deutsch)" first, then every language by its own name.</summary>
    public static List<LanguageChoice> Choices()
    {
        var choices = new List<LanguageChoice> { new(Automatic, F("System default ({0})", NameOf(Detect()))) };
        choices.AddRange(Languages.Select(l => new LanguageChoice(l.Code, l.NativeName)));
        return choices;
    }

    public static string NameOf(string code) => Languages.FirstOrDefault(l => l.Code == code)?.NativeName ?? code;

    /// <summary>Uses a language for the code that is running, until the returned value is disposed. For tests and tools.</summary>
    public static IDisposable Override(string code)
    {
        var before = Scoped.Value;
        code = Resolve(code);
        Scoped.Value = new Scope(code, code == English ? [] : LoadTexts(code));
        return new Restore(() => Scoped.Value = before);
    }

    sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    /// <summary>The text in the language in use; the English text itself when there is no translation.</summary>
    public static string T(string english)
    {
        var texts = Scoped.Value?.Texts ?? _texts;
        return texts.TryGetValue(english, out string? translated) && translated.Length > 0 ? translated : english;
    }

    /// <summary>A text with {0}, {1} ... filled in, translated first: <c>F("Loaded {0} destinations", n)</c>.</summary>
    public static string F(string english, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, T(english), arguments);

    /// <summary>"1 bus stop" or "12 bus stops": the two English texts are the keys, each with {0} for the number.</summary>
    public static string Count(int number, string one, string many) => F(number == 1 ? one : many, number);

    // ---- the language files ----------------------------------------------------------------------------------------

    static readonly Assembly Home = typeof(Localizer).Assembly;
    const string Prefix = "Languages/";

    static IReadOnlyList<Language> LoadLanguageList()
    {
        var list = new List<Language> { new(English, "English") };
        foreach (string resource in Home.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            string code = Path.GetFileNameWithoutExtension(resource);
            if (code == English) continue;
            try
            {
                using var document = JsonDocument.Parse(Read(resource));
                string name = document.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } text ? text : code;
                list.Add(new Language(code, name));
            }
            catch (JsonException) { /* a broken file is skipped instead of breaking the app */ }
        }
        return [list[0], .. list.Skip(1).OrderBy(l => l.NativeName, StringComparer.CurrentCultureIgnoreCase)];
    }

    static Dictionary<string, string> LoadTexts(string code)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string? resource = Home.GetManifestResourceNames().FirstOrDefault(n => n == $"{Prefix}{code}.json");
            if (resource is null) return texts;
            using var document = JsonDocument.Parse(Read(resource));
            if (document.RootElement.TryGetProperty("strings", out var strings))
                foreach (var entry in strings.EnumerateObject())
                    if (entry.Value.GetString() is { } value) texts[entry.Name] = value;
        }
        catch (JsonException) { /* the texts that were read stay; the rest is English */ }
        return texts;
    }

    static string Read(string resource)
    {
        using var stream = Home.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>The texts of a language as a list (for the check that every language file is complete). Empty for English.</summary>
    public static IReadOnlyDictionary<string, string> TextsOf(string code) => code == English ? new Dictionary<string, string>() : LoadTexts(code);
}
