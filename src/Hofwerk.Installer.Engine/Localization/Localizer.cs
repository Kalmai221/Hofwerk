using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Hofwerk.Localization;

/// <summary>
/// A language Hofwerk can be shown in: its code ("de", or "pt-BR" for a regional one) and its name in its own language ("Deutsch").
/// <see cref="RightToLeft"/> turns the windows around for languages written from right to left. <see cref="Plural"/> names how the
/// language counts (see <c>Languages/README.md</c>); the default is one and many, as in English and German.
/// </summary>
public sealed record Language(string Code, string NativeName, bool RightToLeft = false, string Plural = "one-other");

/// <summary>One line of a language drop-down: "auto" (follow the system) or a language code, with the name to show.</summary>
public sealed record LanguageChoice(string Id, string Label);

/// <summary>
/// The texts of Hofwerk in the chosen language. The English text itself is the key: <c>Localizer.T("Save as")</c> gives "Speichern unter"
/// in German, and gives "Save as" for a language that has no translation for it yet, so a missing translation is never a blank or an error.
/// Every language is one file in <c>Languages/</c> (<c>de.json</c>, <c>pt-BR.json</c>); adding a file adds the language everywhere,
/// including the installer. A folder named <c>Languages</c> next to the program, or the one named in the environment variable
/// <c>HOFWERK_LANGUAGES_DIR</c>, can hold more (or newer) language files, so a translation can be tried without building Hofwerk.
/// </summary>
public static class Localizer
{
    public const string English = "en";
    public const string Automatic = "auto";

    /// <summary>A language file read: who it is, its texts, and the texts that have a form for each count (see <see cref="Count"/>).</summary>
    sealed record Pack(Language Language, Dictionary<string, string> Texts, Dictionary<string, string[]> Forms);

    static readonly object Gate = new();
    static readonly List<string> Folders = StartFolders();
    static Dictionary<string, Pack>? _packs;
    static Pack? _pack;               // the language in use (null: English)
    static string _current = English;

    // A language that applies only to the code that is running (and what it starts), for tests: they can use German without
    // changing the language of other tests that run at the same time.
    sealed record Scope(string Code, Pack? Pack);
    static readonly AsyncLocal<Scope?> Scoped = new();

    static Pack? PackInUse => Scoped.Value is { } scope ? scope.Pack : _pack;

    /// <summary>Every language, English first and the others by name.</summary>
    public static IReadOnlyList<Language> Languages
    {
        get
        {
            var all = Packs;
            return [new Language(English, "English"), .. all.Values.Select(p => p.Language).OrderBy(l => l.NativeName, StringComparer.CurrentCultureIgnoreCase)];
        }
    }

    /// <summary>The code of the language in use. English until <see cref="Use"/> is called, so nothing changes language by accident (tests, tools).</summary>
    public static string Current => Scoped.Value?.Code ?? _current;

    /// <summary>True when the language in use is written from right to left, so the windows should be turned around.</summary>
    public static bool IsRightToLeft => PackInUse?.Language.RightToLeft ?? false;

    /// <summary>Raised after the language in use changed, so windows can show their texts again.</summary>
    public static event Action? Changed;

    // ---- which language -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The best language for a culture. A regional file is used for its region ("pt-BR" for Brazil), a general one for every region
    /// (de.json for de-AT and de-CH), and a language with only regional files gets the first of them (pt-PT asks for pt-BR when
    /// that is all there is). A language Hofwerk has no file for gets English. Without a culture, the language Windows or the
    /// desktop is set to is used.
    /// </summary>
    public static string Detect(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var packs = Packs;

        // the culture itself, then what it belongs to: zh-Hans-CN, zh-Hans, zh
        for (var c = culture; c is not null && c.Name.Length > 0; c = c.Parent)
            if (Find(packs, c.Name) is { } exact) return exact;

        string language = culture.TwoLetterISOLanguageName;
        if (Find(packs, language) is { } general) return general;
        if (Find(packs, culture.ThreeLetterISOLanguageName) is { } three) return three;

        // only regional files of this language (pt-BR, pt-PT): the first is closer than English
        return packs.Keys.Where(code => string.Equals(LanguagePart(code), language, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
               ?? English;
    }

    static string? Find(Dictionary<string, Pack> packs, string code) =>
        string.Equals(code, English, StringComparison.OrdinalIgnoreCase) ? English
        : packs.Keys.FirstOrDefault(k => string.Equals(k, code, StringComparison.OrdinalIgnoreCase));

    static string LanguagePart(string code) => code.Split('-', '_')[0];

    /// <summary>The code a setting stands for: nothing, empty or "auto" means the system language, a known code is itself (in any letter case), anything else is English.</summary>
    public static string Resolve(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals(Automatic, StringComparison.OrdinalIgnoreCase)) return Detect();
        return Find(Packs, setting.Trim()) ?? English;
    }

    /// <summary>Switches to the language a setting stands for (see <see cref="Resolve"/>). Returns the code now in use.</summary>
    public static string Use(string? setting)
    {
        string code = Resolve(setting);
        bool changed = code != _current;
        _pack = code == English ? null : Packs.GetValueOrDefault(code);
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

    public static string NameOf(string code) => Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.NativeName ?? code;

    /// <summary>Uses a language for the code that is running, until the returned value is disposed. For tests and tools.</summary>
    public static IDisposable Override(string code)
    {
        var before = Scoped.Value;
        code = Resolve(code);
        Scoped.Value = new Scope(code, code == English ? null : Packs.GetValueOrDefault(code));
        return new Restore(() => Scoped.Value = before);
    }

    sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    // ---- the texts ---------------------------------------------------------------------------------------------------------

    /// <summary>The text in the language in use; the English text itself when there is no translation.</summary>
    public static string T(string english) =>
        PackInUse is { } pack && pack.Texts.TryGetValue(english, out string? translated) && translated.Length > 0 ? translated : english;

    /// <summary>A text with {0}, {1} ... filled in, translated first: <c>F("Loaded {0} destinations", n)</c>.</summary>
    public static string F(string english, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, T(english), arguments);

    /// <summary>
    /// "1 bus stop" or "12 bus stops": the two English texts are the keys, each with {0} for the number. A language that counts in more
    /// than two ways (Russian, Polish ...) gives a list of texts under the first key instead, and its file says which rule picks one.
    /// </summary>
    public static string Count(int number, string one, string many)
    {
        if (PackInUse is { } pack && pack.Forms.TryGetValue(one, out var forms) && forms.Length > 0)
        {
            int index = Math.Min(PluralIndex(pack.Language.Plural, number), forms.Length - 1);
            return string.Format(CultureInfo.CurrentCulture, forms[index], number);
        }
        return F(number == 1 ? one : many, number);
    }

    /// <summary>
    /// Which of a text's forms goes with a number, by the rule a language file names:
    /// <c>one-other</c> (the default: 1, otherwise), <c>french</c> (0 and 1, otherwise), <c>none</c> (one form),
    /// <c>slavic</c> (1 21 31 / 2-4 / the rest: Russian, Ukrainian) and <c>polish</c> (1 / 2-4 / the rest).
    /// </summary>
    public static int PluralIndex(string? rule, int number)
    {
        int n = Math.Abs(number), last = n % 10, lastTwo = n % 100;
        bool few = last is >= 2 and <= 4 && lastTwo is < 12 or > 14;
        return (rule ?? "one-other").ToLowerInvariant() switch
        {
            "none" => 0,
            "french" => n < 2 ? 0 : 1,
            "slavic" => last == 1 && lastTwo != 11 ? 0 : few ? 1 : 2,
            "polish" => n == 1 ? 0 : few ? 1 : 2,
            _ => n == 1 ? 0 : 1,
        };
    }

    /// <summary>The texts of a language as a list (for the check that every language file is complete). Empty for English.</summary>
    public static IReadOnlyDictionary<string, string> TextsOf(string code) =>
        Find(Packs, code) is { } found && found != English ? Packs[found].Texts : new Dictionary<string, string>();

    // ---- the language files ----------------------------------------------------------------------------------------------

    static readonly Assembly Home = typeof(Localizer).Assembly;
    const string Prefix = "Languages/";

    /// <summary>Also reads the language files in a folder (a folder's file replaces a built-in one with the same code). For tests and translators.</summary>
    public static void AddLanguageFolder(string folder)
    {
        lock (Gate)
        {
            if (!Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) Folders.Add(folder);
            _packs = null;
        }
    }

    /// <summary>
    /// Also reads the language files in a folder, but only for the code that is running (and what it starts), until the returned value
    /// is disposed. For tests, which must not add languages for the tests that run at the same time.
    /// </summary>
    public static IDisposable UseLanguageFolder(string folder)
    {
        var before = ExtraFolders.Value;
        ExtraFolders.Value = [.. before ?? [], folder];
        return new Restore(() => ExtraFolders.Value = before);
    }

    static readonly AsyncLocal<string[]?> ExtraFolders = new();

    static List<string> StartFolders()
    {
        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable("HOFWERK_LANGUAGES_DIR") is { Length: > 0 } fromEnvironment) folders.Add(fromEnvironment);
        folders.Add(Path.Combine(AppContext.BaseDirectory, "Languages"));
        return folders;
    }

    static Dictionary<string, Pack> Packs
    {
        get
        {
            Dictionary<string, Pack> all;
            lock (Gate) all = _packs ??= LoadPacks();
            if (ExtraFolders.Value is not { Length: > 0 } extra) return all;

            var merged = new Dictionary<string, Pack>(all, StringComparer.OrdinalIgnoreCase);
            foreach (string folder in extra) AddFolder(merged, folder);
            return merged;
        }
    }

    static Dictionary<string, Pack> LoadPacks()
    {
        var packs = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);

        foreach (string resource in Home.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
            Add(packs, Path.GetFileNameWithoutExtension(resource), () => ReadResource(resource));

        foreach (string folder in Folders) AddFolder(packs, folder);
        return packs;
    }

    static void AddFolder(Dictionary<string, Pack> packs, string folder)
    {
        if (!Directory.Exists(folder)) return;
        string[] files;
        try { files = Directory.GetFiles(folder, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (string file in files) Add(packs, Path.GetFileNameWithoutExtension(file), () => File.ReadAllText(file, System.Text.Encoding.UTF8));
    }

    static void Add(Dictionary<string, Pack> packs, string code, Func<string> read)
    {
        if (string.Equals(code, English, StringComparison.OrdinalIgnoreCase) || code.Length == 0) return;
        try
        {
            using var document = JsonDocument.Parse(read());
            var root = document.RootElement;
            string name = root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } text ? text : code;
            bool rtl = root.TryGetProperty("rightToLeft", out var r) && r.ValueKind == JsonValueKind.True;
            string plural = root.TryGetProperty("plural", out var p) && p.GetString() is { Length: > 0 } rule ? rule : "one-other";

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var forms = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (root.TryGetProperty("strings", out var strings))
                foreach (var entry in strings.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String) texts[entry.Name] = entry.Value.GetString()!;
                    else if (entry.Value.ValueKind == JsonValueKind.Array)
                    {
                        string[] list = entry.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray();
                        if (list.Length == 0) continue;
                        forms[entry.Name] = list;
                        texts[entry.Name] = list[0];
                    }
                }
            packs[code] = new Pack(new Language(code, name, rtl, plural), texts, forms);
        }
        catch (JsonException) { /* a broken file is skipped instead of breaking the app */ }
        catch (IOException) { }
    }

    static string ReadResource(string resource)
    {
        using var stream = Home.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
