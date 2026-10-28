using Hofwerk.Localization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hofwerk.Core.Model;

namespace Hofwerk.Core.Presets;

/// <summary>How the names are written in the file the user brings.</summary>
public enum StringFileLayout
{
    /// <summary>The format OMSI add-ons and the original HOF Suite use ("string 0:", "[TerminusString]" ...): used as it is.</summary>
    OmsiStrings,
    /// <summary>One name per line with the slot number in front: "0 = IBIS 1", "7: ANNAX (side)".</summary>
    Numbered,
    /// <summary>One name per line, in slot order: the first line is the first slot.</summary>
    OnePerLine,
}

/// <summary>Which strings the names are for.</summary>
[Flags]
public enum SlotTargets { None = 0, Destinations = 1, BusStops = 2, Both = 3 }

/// <summary>What the user told Hofwerk about their file.</summary>
public sealed record PresetImportOptions(
    string Name,
    string Author = "",
    string Description = "",
    string Url = "",
    StringFileLayout Layout = StringFileLayout.OmsiStrings,
    SlotTargets Targets = SlotTargets.Both,
    int FirstSlot = 0);

/// <summary>A look at what a file would give, so the person can see whether Hofwerk understood it before importing.</summary>
public sealed record StringFilePreview(int Slots, IReadOnlyList<string> Sample);

public sealed class PresetImportException(string message) : Exception(message);

/// <summary>
/// Turns a strings file that has no preset.json (and is often not in OMSI's format at all) into a preset in the user's presets
/// folder. Hofwerk writes the preset.json and the strings file; the user only says what the names are and who they are for.
/// </summary>
public static class PresetImporter
{
    static readonly Regex NumberedLine = new(@"^\s*(\d+)\s*(?:[:=;\t]|-|–)\s*(.+?)\s*$", RegexOptions.Compiled);

    /// <summary>Reads a text file as UTF-8, UTF-16 (with its marker bytes) or, when it is neither, Windows-1252.</summary>
    public static string ReadText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        int skip = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) { return HofEncodings.Windows1252.GetString(bytes, skip, bytes.Length - skip); }
    }

    /// <summary>A first guess at the layout, which the person can change.</summary>
    public static StringFileLayout Detect(string text)
    {
        if (StringTemplates.Parse(text) is { } t && t.Count(StringSlotKind.Terminus) + t.Count(StringSlotKind.BusStop) > 0)
            return StringFileLayout.OmsiStrings;

        var lines = Lines(text).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count > 0 && lines.Count(l => NumberedLine.IsMatch(l)) * 2 >= lines.Count) return StringFileLayout.Numbered;
        return StringFileLayout.OnePerLine;
    }

    /// <summary>A name for the preset taken from the file's name ("my_ibis_strings.txt" gives "My ibis strings").</summary>
    public static string SuggestName(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath).Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Length == 0 ? Localizer.T("My strings") : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static StringFilePreview Preview(string text, StringFileLayout layout, int firstSlot = 0)
    {
        if (layout == StringFileLayout.OmsiStrings)
        {
            var t = StringTemplates.Parse(text);
            int slots = Math.Max(t.Count(StringSlotKind.Terminus), t.Count(StringSlotKind.BusStop));
            var sample = new List<string>();
            for (int i = 0; sample.Count < 5 && i < 400; i++)
                if (t.Get(StringSlotKind.Terminus, i) is { } info && !info.Name.StartsWith("String ", StringComparison.Ordinal))
                    sample.Add($"{i} = {info.Name}");
            return new StringFilePreview(slots, sample);
        }

        var named = ReadSlots(text, layout, firstSlot);
        return new StringFilePreview(named.Count, named.Take(5).Select(s => $"{s.Index} = {s.Name}").ToList());
    }

    /// <summary>The names of a plain file: lines starting with # are notes, an empty line leaves that slot without a name.</summary>
    public static List<(int Index, string Name, string Hint)> ReadSlots(string text, StringFileLayout layout, int firstSlot = 0)
    {
        var result = new List<(int, string, string)>();
        int next = Math.Max(0, firstSlot);

        foreach (string raw in Lines(text))
        {
            string line = raw.Trim();
            if (line.StartsWith('#')) continue;

            int index;
            string content;
            if (layout == StringFileLayout.Numbered)
            {
                var m = NumberedLine.Match(line);
                if (!m.Success) continue;
                index = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                content = m.Groups[2].Value;
            }
            else
            {
                index = next++;
                content = line;
            }

            if (content.Length == 0) continue;
            string[] parts = content.Split('|', 2);
            result.Add((index, parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : ""));
        }
        return result;
    }

    /// <summary>The strings file in the format Hofwerk reads (what a preset folder holds), made from the person's file.</summary>
    public static string ToStringsFile(string text, PresetImportOptions options)
    {
        if (options.Layout == StringFileLayout.OmsiStrings) return text;

        var sb = new StringBuilder();
        foreach (var (index, name, hint) in ReadSlots(text, options.Layout, options.FirstSlot).DistinctBy(s => s.Index))
        {
            string english = hint.Length > 0 ? $"{name}|{hint}" : name;
            foreach (var (flag, tag) in new[] { (SlotTargets.Destinations, "[TerminusString]"), (SlotTargets.BusStops, "[BusstopString]") })
            {
                if (!options.Targets.HasFlag(flag)) continue;
                sb.Append("string ").Append(index).Append(":\n").Append(tag).Append('\n').Append(name).Append('\n').Append(english).Append("\n\n\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>Writes the preset into <paramref name="userFolder"/> and returns it as the library loads it.</summary>
    /// <exception cref="PresetImportException">Something is missing or the file has no names in it; the message says what.</exception>
    public static StringPreset Import(string text, PresetImportOptions options, string userFolder)
    {
        if (string.IsNullOrWhiteSpace(options.Name)) throw new PresetImportException(Localizer.T("Give the preset a name."));
        if (options.Layout != StringFileLayout.OmsiStrings && options.Targets == SlotTargets.None)
            throw new PresetImportException(Localizer.T("Choose what the names are for: destinations, bus stops, or both."));

        string strings = ToStringsFile(text, options);
        var parsed = StringTemplates.Parse(strings);
        if (parsed.Count(StringSlotKind.Terminus) + parsed.Count(StringSlotKind.BusStop) == 0)
            throw new PresetImportException(options.Layout == StringFileLayout.OmsiStrings
                ? Localizer.T("No names were found. Is this file written the way OMSI strings files are? Try one of the other ways of reading it.")
                : Localizer.T("No names were found in the file with this way of reading it. Try another one."));

        string id = FreeId(userFolder, Slug(options.Name));
        string folder = Path.Combine(userFolder, id);
        try
        {
            Directory.CreateDirectory(folder);
            var manifest = new PresetManifest(options.Name.Trim(), options.Author.Trim(), options.Description.Trim(), options.Url.Trim(), "strings.txt");
            File.WriteAllText(Path.Combine(folder, "preset.json"), JsonSerializer.Serialize(manifest, HofwerkJson.Default.PresetManifest), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "strings.txt"), strings, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PresetImportException(Localizer.F("The preset could not be saved: {0}", ex.Message));
        }

        return PresetLibrary.LoadAll(userFolder).First(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    static IEnumerable<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r'));

    static string Slug(string name)
    {
        string slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "my-strings" : slug;
    }

    // A preset never replaces another one: a name that is taken gets -2, -3 ...; nor does it take a built-in's id.
    static string FreeId(string userFolder, string slug)
    {
        var taken = PresetLibrary.LoadAll(userFolder).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string id = slug;
        for (int n = 2; taken.Contains(id) || Directory.Exists(Path.Combine(userFolder, id)); n++) id = $"{slug}-{n}";
        return id;
    }
}
