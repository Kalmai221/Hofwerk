using Hofwerk.Core;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class MainViewModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofsuite-" + Guid.NewGuid().ToString("N"));
    string UserPresets => Path.Combine(_dir, "presets");

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_dir);

        // A second preset, so switching has something to switch to. It lives in the test's own folder.
        string mine = Path.Combine(UserPresets, "my-bus-pack");
        Directory.CreateDirectory(mine);
        File.WriteAllText(Path.Combine(mine, "preset.json"), """{ "name": "My Bus Pack", "author": "Me" }""");
        File.WriteAllText(Path.Combine(mine, "strings.txt"), "string 3:\n[TerminusString]\nde\nFront display\n\n");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    MainViewModel NewViewModel() =>
        new(AppSettings.Load(Path.Combine(_dir, "settings.json")), UserPresets);

    MainViewModel Open()
    {
        string hof = Path.Combine(_dir, "test.hof");
        File.WriteAllText(hof, "[name]\nT\n\nstringcount_terminus\n9\n\nstringcount_busstop\n0\n\n[addterminus]\n001\nTotnes\n" +
                               string.Join("\n", Enumerable.Range(0, 9).Select(i => "s" + i)) + "\n");
        var vm = NewViewModel();
        Assert.True(vm.Open(hof));
        return vm;
    }

    [Fact]
    public void Editing_a_string_slot_marks_the_document_changed()
    {
        var vm = Open();
        Assert.False(vm.IsDirty);

        vm.SelectedTerminus!.Strings[3].Value = "edited";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Switching_preset_renames_slots_keeps_text_and_does_not_mark_changed()
    {
        var vm = Open();
        var terminus = vm.SelectedTerminus!;
        Assert.Equal("ANNAX (side)", terminus.Strings[3].Label);          // HOF Suite Default

        vm.UsePreset(vm.Presets.Single(p => p.Name == "My Bus Pack"));

        Assert.Equal("Front display", terminus.Strings[3].Label);
        Assert.Equal("s3", terminus.Strings[3].Value);
        Assert.False(vm.IsDirty);
        Assert.Equal("My Bus Pack", vm.ActivePresetName);
        Assert.Single(vm.Presets, p => p.IsActive);
    }

    [Fact]
    public void Chosen_preset_is_remembered_in_the_settings()
    {
        var vm = NewViewModel();
        vm.UsePreset(vm.Presets.Single(p => p.Preset.Id == "my-bus-pack"));

        Assert.Equal("My Bus Pack", NewViewModel().ActivePresetName);
    }

    [Fact]
    public void A_remembered_preset_that_no_longer_exists_falls_back_to_the_default()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        settings.ActivePresetId = "deleted-preset";
        settings.Save();

        Assert.Equal("HOF Suite Default", NewViewModel().ActivePresetName);
    }
}
