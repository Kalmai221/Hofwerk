using System.Text;
using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Presets;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "HofFiles")]
public class PresetImporterTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-presetimport-" + Guid.NewGuid().ToString("N"));

    public PresetImporterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    string Presets => Path.Combine(_dir, "presets");

    static string Name(StringPreset p, StringSlotKind kind, int slot) => p.Templates.Get(kind, slot).Name;

    // ---- reading and guessing ------------------------------------------------------------------------------------

    [Fact]
    public void The_layout_is_guessed_from_the_file()
    {
        Assert.Equal(StringFileLayout.OmsiStrings, PresetImporter.Detect("string 0:\n[TerminusString]\nDeutsch\nEnglish\n\n"));
        Assert.Equal(StringFileLayout.Numbered, PresetImporter.Detect("0 = IBIS 1\n1 = IBIS 2\n# note\n7: ANNAX\n"));
        Assert.Equal(StringFileLayout.OnePerLine, PresetImporter.Detect("IBIS 1\nIBIS 2\nANNAX\n"));
        Assert.Equal(StringFileLayout.OnePerLine, PresetImporter.Detect(""));
    }

    [Fact]
    public void Text_is_read_as_utf8_utf16_or_windows_1252()
    {
        Assert.Equal("Köln", PresetImporter.ReadText(Encoding.UTF8.GetBytes("Köln")));
        Assert.Equal("Köln", PresetImporter.ReadText([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Köln")]));
        Assert.Equal("Köln", PresetImporter.ReadText([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Köln")]));
        Assert.Equal("Köln", PresetImporter.ReadText(HofEncodings.Windows1252.GetBytes("Köln")));
    }

    [Fact]
    public void The_preset_is_named_after_the_file_by_default()
    {
        Assert.Equal("My ibis strings", PresetImporter.SuggestName(@"C:\Downloads\my_ibis-strings.txt"));
        Assert.Equal("My strings", PresetImporter.SuggestName(".txt"));
    }

    // ---- plain files ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_numbered_file_gives_each_name_to_its_slot_for_destinations_and_bus_stops()
    {
        var preset = PresetImporter.Import("0 = IBIS 1\n3: ANNAX (side)|The side display\n# a note\n", new PresetImportOptions("Numbered", Layout: StringFileLayout.Numbered), Presets);

        Assert.Equal("IBIS 1", Name(preset, StringSlotKind.Terminus, 0));
        Assert.Equal("ANNAX (side)", Name(preset, StringSlotKind.BusStop, 3));
        Assert.Equal("The side display", preset.Templates.Get(StringSlotKind.Terminus, 3).Hint);
        Assert.Equal("String 1", Name(preset, StringSlotKind.Terminus, 1));   // not in the file
        Assert.False(preset.IsBuiltIn);
    }

    [Fact]
    public void A_one_per_line_file_counts_slots_from_the_chosen_first_slot_and_an_empty_line_leaves_a_slot_unnamed()
    {
        string text = "# my names\nFirst\n\nThird\n";

        var preset = PresetImporter.Import(text, new PresetImportOptions("Lines", Layout: StringFileLayout.OnePerLine, FirstSlot: 5), Presets);

        Assert.Equal("First", Name(preset, StringSlotKind.Terminus, 5));
        Assert.Equal("String 6", Name(preset, StringSlotKind.Terminus, 6));
        Assert.Equal("Third", Name(preset, StringSlotKind.Terminus, 7));
    }

    [Fact]
    public void The_names_can_be_for_destinations_only_or_bus_stops_only()
    {
        var destinationsOnly = PresetImporter.Import("A\nB\n", new PresetImportOptions("Dest", Layout: StringFileLayout.OnePerLine, Targets: SlotTargets.Destinations), Presets);
        var busStopsOnly = PresetImporter.Import("A\nB\n", new PresetImportOptions("Stops", Layout: StringFileLayout.OnePerLine, Targets: SlotTargets.BusStops), Presets);

        Assert.Equal(2, destinationsOnly.Templates.Count(StringSlotKind.Terminus));
        Assert.Equal(0, destinationsOnly.Templates.Count(StringSlotKind.BusStop));
        Assert.Equal(0, busStopsOnly.Templates.Count(StringSlotKind.Terminus));
        Assert.Equal(2, busStopsOnly.Templates.Count(StringSlotKind.BusStop));
    }

    [Fact]
    public void An_OMSI_strings_file_is_kept_as_it_is_and_the_credit_is_saved()
    {
        string text = "string 2:\n[TerminusString]\nDeutsch\nIBIS 3|Hint\n\n";

        var preset = PresetImporter.Import(text, new PresetImportOptions("Real", "Someone", "For my bus", "https://example.com"), Presets);

        Assert.Equal("IBIS 3", Name(preset, StringSlotKind.Terminus, 2));
        Assert.Equal("Someone", preset.Author);
        Assert.Equal("For my bus", preset.Description);
        Assert.Equal("https://example.com", preset.Url);
        Assert.Equal(text, File.ReadAllText(Path.Combine(Presets, "real", "strings.txt")));
    }

    // ---- where it is saved ---------------------------------------------------------------------------------------

    [Fact]
    public void An_imported_preset_is_found_by_the_library_next_to_the_built_in_ones()
    {
        PresetImporter.Import("A\n", new PresetImportOptions("Mine", Layout: StringFileLayout.OnePerLine), Presets);

        var all = PresetLibrary.LoadAll(Presets);

        Assert.Contains(all, p => p.Id == "mine" && !p.IsBuiltIn);
        Assert.Contains(all, p => p.Id == PresetLibrary.DefaultId);
    }

    [Fact]
    public void A_name_that_is_taken_never_replaces_the_other_preset_or_a_built_in_one()
    {
        var options = new PresetImportOptions("Same name", Layout: StringFileLayout.OnePerLine);
        var first = PresetImporter.Import("One\n", options, Presets);
        var second = PresetImporter.Import("Two\n", options, Presets);
        var builtIn = PresetImporter.Import("Three\n", options with { Name = "hof suite default" }, Presets);

        Assert.Equal("same-name", first.Id);
        Assert.Equal("same-name-2", second.Id);
        Assert.Equal("One", Name(first, StringSlotKind.Terminus, 0));
        Assert.Equal("hof-suite-default-2", builtIn.Id);
        Assert.Equal("Two", Name(PresetLibrary.LoadAll(Presets).Single(p => p.Id == "same-name-2"), StringSlotKind.Terminus, 0));
    }

    // ---- when it cannot work: a message the person can act on ----------------------------------------------------

    [Fact]
    public void Missing_answers_and_unreadable_files_are_explained_and_nothing_is_saved()
    {
        var noName = Assert.Throws<PresetImportException>(() => PresetImporter.Import("A\n", new PresetImportOptions("  ", Layout: StringFileLayout.OnePerLine), Presets));
        var noTarget = Assert.Throws<PresetImportException>(() => PresetImporter.Import("A\n", new PresetImportOptions("X", Layout: StringFileLayout.OnePerLine, Targets: SlotTargets.None), Presets));
        var noNames = Assert.Throws<PresetImportException>(() => PresetImporter.Import("just some text\n", new PresetImportOptions("X"), Presets));
        var notNumbered = Assert.Throws<PresetImportException>(() => PresetImporter.Import("A\nB\n", new PresetImportOptions("X", Layout: StringFileLayout.Numbered), Presets));

        Assert.Contains("name", noName.Message);
        Assert.Contains("destinations, bus stops", noTarget.Message);
        Assert.Contains("OMSI strings", noNames.Message);
        Assert.Contains("another one", notNumbered.Message);
        Assert.False(Directory.Exists(Presets) && Directory.GetDirectories(Presets).Length > 0);
    }

    // ---- the questions the app asks ------------------------------------------------------------------------------

    [Fact]
    public void The_questions_start_with_a_guess_and_show_what_was_understood()
    {
        var vm = new PresetImportViewModel("0 = IBIS 1\n1 = IBIS 2\n", @"C:\x\ibis_names.txt");

        Assert.Equal(StringFileLayout.Numbered, vm.Layout);
        Assert.Equal("Ibis names", vm.Name);
        Assert.True(vm.AsksWhatFor);
        Assert.False(vm.AsksFirstSlot);
        Assert.Contains("2 names", vm.Summary);
        Assert.Contains("0 = IBIS 1", vm.Summary);
        Assert.True(vm.CanImport);

        vm.LayoutIndex = (int)StringFileLayout.OnePerLine;      // a wrong guess is corrected
        Assert.True(vm.AsksFirstSlot);
        vm.ForDestinations = false;
        vm.ForBusStops = false;
        Assert.False(vm.CanImport);                              // the names must be for something
        vm.ForBusStops = true;
        vm.Name = " ";
        Assert.False(vm.CanImport);                              // and the preset needs a name
    }

    [Fact]
    public void Importing_in_the_app_adds_the_preset_and_switches_to_it()
    {
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Presets);
        var import = new PresetImportViewModel("0 = My first\n", "mine.txt");

        Assert.True(vm.FinishPresetImport(import));

        Assert.Equal("Mine", vm.ActivePresetName);
        Assert.Contains(vm.Presets, p => p.Name == "Mine" && p.IsActive);
        Assert.Contains("Imported", vm.Status);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_reported_in_the_status_bar()
    {
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Presets);

        Assert.Null(vm.StartPresetImport(Path.Combine(_dir, "missing.txt")));

        Assert.Contains("Could not read", vm.Status);
    }
}
