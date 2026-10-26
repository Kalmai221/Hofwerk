using Hofwerk.Core.Model;
using Hofwerk.Core.Presets;

namespace Hofwerk.Core.Tests;

public class StringTemplateTests
{
    [Fact]
    public void Parses_names_and_hints_from_the_english_line()
    {
        var t = StringTemplates.Parse(
        [
            "string 6:", "[TerminusString]", "Steckschild|de hint", "Add Plate texture|Add Plate file", "", "",
            "string 1:", "[BusstopString]", "Innenanzeige 1", "Inside display 1", "", "",
        ]);

        var plate = t.Get(StringSlotKind.Terminus, 6);
        Assert.Equal("Add Plate texture", plate.Name);
        Assert.Equal("Add Plate file", plate.Hint);
        Assert.Equal("Inside display 1", t.Get(StringSlotKind.BusStop, 1).Name);
    }

    [Fact]
    public void Unknown_slots_fall_back_to_generic_names()
    {
        Assert.Equal("String 99", StringTemplates.Parse("").Get(StringSlotKind.Terminus, 99).Name);
    }
}

public class PresetLibraryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofsuite-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    void WritePreset(string id, string json, string? strings = "string 0:\n[TerminusString]\nde\nMy first slot\n\n")
    {
        string dir = Path.Combine(_dir, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "preset.json"), json);
        if (strings is not null) File.WriteAllText(Path.Combine(dir, "strings.txt"), strings);
    }

    [Fact]
    public void The_default_preset_ships_with_the_app_and_credits_its_author()
    {
        var all = PresetLibrary.LoadAll(_dir);

        var hof = Assert.Single(all, p => p.Id == PresetLibrary.DefaultId);
        Assert.True(hof.IsBuiltIn);
        Assert.Equal("IBIS 1", hof.Templates.Get(StringSlotKind.Terminus, 0).Name);
        Assert.Equal("ANNAX (side)", hof.Templates.Get(StringSlotKind.Terminus, 3).Name);
        Assert.Equal("Lukas G.", hof.Author);
    }

    [Fact]
    public void Loads_user_presets_and_skips_broken_ones()
    {
        WritePreset("mine", """{ "name": "My Preset", "author": "Me" }""");
        WritePreset("broken-json", "{ nope");
        WritePreset("no-name", """{ "author": "x" }""");
        WritePreset("no-strings", """{ "name": "Empty" }""", strings: null);

        var all = PresetLibrary.LoadAll(_dir);

        var mine = Assert.Single(all, p => !p.IsBuiltIn);
        Assert.Equal("My Preset", mine.Name);
        Assert.Equal("My first slot", mine.Templates.Get(StringSlotKind.Terminus, 0).Name);
    }

    [Fact]
    public void User_preset_cannot_replace_a_built_in_one()
    {
        WritePreset(PresetLibrary.DefaultId, """{ "name": "Fake default" }""");

        var all = PresetLibrary.LoadAll(_dir);

        Assert.Equal("HOF Suite Default", Assert.Single(all, p => p.Id == PresetLibrary.DefaultId).Name);
    }

    [Fact]
    public void Settings_round_trip_and_survive_a_missing_file()
    {
        string path = Path.Combine(_dir, "settings.json");
        Assert.Equal(PresetLibrary.DefaultId, AppSettings.Load(path).ActivePresetId);

        var s = AppSettings.Load(path);
        s.ActivePresetId = "my-bus-pack";
        s.Save();

        Assert.Equal("my-bus-pack", AppSettings.Load(path).ActivePresetId);
    }
}
