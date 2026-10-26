using System.Text;
using System.Text.Json;
using Hofwerk.Core.Model;

namespace Hofwerk.Core.Presets;

/// <summary>A named set of string slot names, with who made it.</summary>
public sealed record StringPreset(
    string Id,
    string Name,
    string Author,
    string Description,
    string Url,
    StringTemplates Templates,
    bool IsBuiltIn);

/// <summary>
/// Collects presets from two places: the <c>Presets/</c> folder of the repository (built into the app) and the
/// user's own presets folder. Each preset is a folder holding preset.json and a strings file.
/// </summary>
public static class PresetLibrary
{
    public const string DefaultId = "hof-suite-default";
    const string EmbeddedPrefix = "Presets/";



    public static string UserFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hofwerk", "Presets");

    /// <summary>Built-in presets first, then the user's. A user preset with a built-in's id is ignored.</summary>
    public static List<StringPreset> LoadAll(string? userFolder = null)
    {
        var all = LoadEmbedded().ToList();
        foreach (var p in LoadFolder(userFolder ?? UserFolder))
            if (all.All(x => !string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
                all.Add(p);
        return all;
    }

    static IEnumerable<StringPreset> LoadEmbedded()
    {
        var asm = typeof(PresetLibrary).Assembly;
        // Resource names look like "Presets/<id>\preset.json" (MSBuild keeps the OS path separator).
        var files = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
            .Select(n => (Name: n, Parts: n[EmbeddedPrefix.Length..].Replace('\\', '/').Split('/')))
            .Where(f => f.Parts.Length == 2)
            .GroupBy(f => f.Parts[0]);

        foreach (var folder in files)
        {
            string? Read(string file)
            {
                var entry = folder.FirstOrDefault(f => string.Equals(f.Parts[1], file, StringComparison.OrdinalIgnoreCase));
                if (entry.Name is null) return null;
                using var s = asm.GetManifestResourceStream(entry.Name)!;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return Decode(ms.ToArray());
            }

            if (Build(folder.Key, Read, builtIn: true) is { } preset) yield return preset;
        }
    }

    static IEnumerable<StringPreset> LoadFolder(string root)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (string dir in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            string? Read(string file)
            {
                string path = Path.Combine(dir, file);
                try { return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
            }

            if (Build(Path.GetFileName(dir), Read, builtIn: false) is { } preset) yield return preset;
        }
    }

    /// <summary>Returns null for a folder that isn't a usable preset (no or broken preset.json, no strings file).</summary>
    static StringPreset? Build(string id, Func<string, string?> read, bool builtIn)
    {
        if (read("preset.json") is not { } json) return null;

        PresetManifest? m;
        try { m = JsonSerializer.Deserialize(json, HofwerkJson.Default.PresetManifest); }
        catch (JsonException) { return null; }
        if (m is null || string.IsNullOrWhiteSpace(m.Name)) return null;

        if (read(m.Strings is { Length: > 0 } ? m.Strings : "strings.txt") is not { } strings) return null;

        return new StringPreset(id, m.Name.Trim(), m.Author?.Trim() ?? "", m.Description?.Trim() ?? "", m.Url?.Trim() ?? "",
            StringTemplates.Parse(strings), builtIn);
    }

    /// <summary>Contributors save UTF-8, older OMSI files are Windows-1252; accept both.</summary>
    static string Decode(byte[] bytes)
    {
        int skip = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) { return HofEncodings.Windows1252.GetString(bytes, skip, bytes.Length - skip); }
    }
}

/// <summary>The contents of a preset's preset.json.</summary>
internal sealed record PresetManifest(string? Name, string? Author, string? Description, string? Url, string? Strings);
