using Hofwerk.Core.Model;
using Hofwerk.Core.Presets;

namespace Hofwerk.Core.Tests;

/// <summary>
/// Checks the presets that live in the repository's Presets/ folder, so a pull request that adds or edits one
/// fails here with a clear message instead of shipping a preset that silently does not load.
/// </summary>
[Trait("Area", "HofFiles")]
public class RepositoryPresetTests
{
    static string? FindPresetsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Hofwerk.slnx")) && Directory.Exists(Path.Combine(dir.FullName, "Presets")))
                return Path.Combine(dir.FullName, "Presets");
        return null;
    }

    [Fact]
    public void Every_preset_folder_in_the_repository_loads_and_credits_its_author()
    {
        string? presets = FindPresetsFolder();
        if (presets is null) return; // not running from a checkout of the repository

        // Presets are built into the app, so loading with no user folder shows exactly what users get.
        var loaded = PresetLibrary.LoadAll(Path.Combine(Path.GetTempPath(), "hofwerk-no-user-presets-" + Guid.NewGuid().ToString("N")));
        var problems = new List<string>();

        foreach (string folder in Directory.EnumerateDirectories(presets))
        {
            string id = Path.GetFileName(folder);
            var preset = loaded.FirstOrDefault(p => p.Id == id);

            if (preset is null)
            {
                problems.Add($"{id}: does not load. It needs a preset.json with a \"name\" and a strings file (strings.txt by default). Rebuild if you just added it.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(preset.Author))
                problems.Add($"{id}: preset.json needs an \"author\" so the app can credit who made it.");
            if (preset.Templates.Count(StringSlotKind.Terminus) + preset.Templates.Count(StringSlotKind.BusStop) == 0)
                problems.Add($"{id}: no string slots were found. Check the format in Presets/README.md (\"string 0:\", \"[TerminusString]\", German line, English line).");
            if (preset.Url.Length > 0 && !(preset.Url.StartsWith("https://") || preset.Url.StartsWith("http://")))
                problems.Add($"{id}: \"url\" must be a web link (http:// or https://), or empty.");
            if (id != id.ToLowerInvariant() || id.Contains(' '))
                problems.Add($"{id}: name the folder in lowercase with dashes, like my-bus-pack.");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_default_preset_exists_because_the_app_falls_back_to_it()
    {
        string? presets = FindPresetsFolder();
        if (presets is null) return;

        Assert.True(Directory.Exists(Path.Combine(presets, PresetLibrary.DefaultId)),
            $"Presets/{PresetLibrary.DefaultId} must exist: the app uses it when no other preset is chosen.");
    }
}
