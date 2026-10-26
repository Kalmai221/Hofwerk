using Hofwerk.Core;
using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

public class OmsiLocationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-omsi-" + Guid.NewGuid().ToString("N"));

    public OmsiLocationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Makes a fake OMSI folder with one vehicle that has a .hof file.</summary>
    string MakeOmsi(string name = "OMSI 2", bool withMaps = true, bool withExe = false)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "Vehicles", "Citaro"));
        File.WriteAllText(Path.Combine(dir, "Vehicles", "Citaro", "Berlin.hof"), "[name]\nBerlin\n");
        if (withMaps) Directory.CreateDirectory(Path.Combine(dir, "Maps"));
        if (withExe) File.WriteAllText(Path.Combine(dir, "Omsi.exe"), "");
        return dir;
    }

    [Fact]
    public void An_OMSI_folder_has_Vehicles_and_Maps()
    {
        Assert.True(OmsiLocator.LooksLikeOmsi(MakeOmsi()));
    }

    [Fact]
    public void Vehicles_and_the_game_executable_also_count()
    {
        Assert.True(OmsiLocator.LooksLikeOmsi(MakeOmsi("by-exe", withMaps: false, withExe: true)));
    }

    [Fact]
    public void Folder_names_are_matched_without_regard_to_case()
    {
        string dir = Path.Combine(_root, "linux");
        Directory.CreateDirectory(Path.Combine(dir, "vehicles"));
        Directory.CreateDirectory(Path.Combine(dir, "MAPS"));

        Assert.True(OmsiLocator.LooksLikeOmsi(dir));
    }

    [Fact]
    public void Other_folders_are_not_mistaken_for_OMSI()
    {
        string documents = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(documents);
        string onlyVehicles = MakeOmsi("half", withMaps: false);

        Assert.False(OmsiLocator.LooksLikeOmsi(documents));
        Assert.False(OmsiLocator.LooksLikeOmsi(onlyVehicles));         // no Maps and no Omsi.exe
        Assert.False(OmsiLocator.LooksLikeOmsi(Path.Combine(_root, "does-not-exist")));
        Assert.False(OmsiLocator.LooksLikeOmsi(""));
    }

    [Fact]
    public void The_app_uses_the_folder_saved_by_the_installer_instead_of_guessing()
    {
        string omsi = MakeOmsi();
        var settings = AppSettings.Load(Path.Combine(_root, "settings.json"));
        settings.OmsiFolder = omsi;
        settings.Save();

        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_root, "settings.json")), Path.Combine(_root, "presets"));

        Assert.Equal(omsi, vm.OmsiFolder);
        Assert.True(vm.HasOmsi);
        Assert.Equal("1 .hof files found", vm.OmsiHeadline);
        Assert.Equal(["Citaro"], vm.OmsiItems.Select(i => i.Name));
    }

    [Fact]
    public void A_saved_folder_that_no_longer_exists_falls_back_to_detection()
    {
        var settings = AppSettings.Load(Path.Combine(_root, "settings.json"));
        settings.OmsiFolder = Path.Combine(_root, "deleted");
        settings.Save();

        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_root, "settings.json")), Path.Combine(_root, "presets"));

        Assert.NotEqual(Path.Combine(_root, "deleted"), vm.OmsiFolder);
    }

    [Fact]
    public void Choosing_a_folder_on_Home_checks_it_and_remembers_it()
    {
        string omsi = MakeOmsi();
        string file = Path.Combine(_root, "settings.json");
        var vm = new MainViewModel(AppSettings.Load(file), Path.Combine(_root, "presets"));

        Assert.False(vm.TrySetOmsiFolder(Path.Combine(_root, "not-omsi")));
        Assert.Contains("doesn't look like an OMSI", vm.Status);

        Assert.True(vm.TrySetOmsiFolder(omsi));
        Assert.Equal(omsi, vm.OmsiFolder);
        Assert.Equal(omsi, AppSettings.Load(file).OmsiFolder); // survives a restart
    }
}
