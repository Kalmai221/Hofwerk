using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Omsi")]
public class ExportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-export-" + Guid.NewGuid().ToString("N"));
    string Omsi => Path.Combine(_dir, "OMSI 2");
    string Vehicles => Path.Combine(Omsi, "Vehicles");

    public ExportTests()
    {
        Directory.CreateDirectory(Vehicles);
        Bus("London Citybus 400");
        Bus("MS_G3Pack");
        Bus("Volvo B7L");
        Directory.CreateDirectory(Path.Combine(Vehicles, "Announcements", "Cotterell"));   // not a vehicle
        Directory.CreateDirectory(Path.Combine(Vehicles, "Pack", "Inner Bus"));            // a pack that keeps its bus one folder down
        File.WriteAllText(Path.Combine(Vehicles, "Pack", "Inner Bus", "inner.bus"), "bus");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    void Bus(string name)
    {
        Directory.CreateDirectory(Path.Combine(Vehicles, name));
        File.WriteAllText(Path.Combine(Vehicles, name, "bus.bus"), "bus");
    }

    string SourceFile(string folder = "Source", string name = "Cotterell.hof", string text = "[name]\nCotterell\n")
    {
        string dir = Path.Combine(Vehicles, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    // ---- finding folders -----------------------------------------------------------------------------------------

    [Fact]
    public void Vehicle_folders_are_the_ones_with_a_bus_or_hof_in_them_also_one_level_down()
    {
        Assert.Equal(["Inner Bus", "London Citybus 400", "MS_G3Pack", "Volvo B7L"], HofExporter.FindVehicleFolders(Vehicles));
        Assert.Empty(HofExporter.FindVehicleFolders(Path.Combine(_dir, "nothing")));
    }

    [Fact]
    public void A_name_from_a_file_is_found_in_any_letter_case_and_one_level_down_but_never_outside_Vehicles()
    {
        Assert.Equal(Path.Combine(Vehicles, "MS_G3Pack"), HofExporter.ResolveFolder(Vehicles, "MS_G3Pack"));
        Assert.EndsWith("Inner Bus", HofExporter.ResolveFolder(Vehicles, "Inner Bus"));
        Assert.NotNull(HofExporter.ResolveFolder(Vehicles, " ms_g3pack "));
        Assert.Null(HofExporter.ResolveFolder(Vehicles, "Not There"));
        foreach (string bad in new[] { "", "..", "../Maps", @"..\Maps", "a/b", "C:\\Windows" })
            Assert.Null(HofExporter.ResolveFolder(Vehicles, bad));
    }

    // ---- copying -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_file_goes_into_every_listed_folder_under_the_same_name_and_the_progress_reaches_the_end()
    {
        string source = SourceFile();
        var seen = new List<double>();

        var results = HofExporter.Export(source, Vehicles, ["London Citybus 400", "MS_G3Pack"], progress: new SyncProgress(seen.Add));

        Assert.All(results, r => Assert.Equal(ExportOutcome.Written, r.Outcome));
        Assert.Equal("[name]\nCotterell\n", File.ReadAllText(Path.Combine(Vehicles, "London Citybus 400", "Cotterell.hof")));
        Assert.True(File.Exists(Path.Combine(Vehicles, "MS_G3Pack", "Cotterell.hof")));
        Assert.Equal(1.0, seen[^1]);
        Assert.Empty(Directory.GetFiles(Vehicles, "*.hwtmp", SearchOption.AllDirectories));   // no half-written files
    }

    sealed class SyncProgress(Action<double> report) : IProgress<double> { public void Report(double v) => report(v); }

    [Fact]
    public void A_folder_that_already_has_the_same_file_is_left_alone_and_a_different_one_is_backed_up_then_replaced()
    {
        string source = SourceFile(text: "new");
        string same = Path.Combine(Vehicles, "MS_G3Pack", "Cotterell.hof");
        File.WriteAllText(same, "new");
        string different = Path.Combine(Vehicles, "Volvo B7L", "Cotterell.hof");
        File.WriteAllText(different, "old");

        var results = HofExporter.Export(source, Vehicles, ["MS_G3Pack", "Volvo B7L"]);

        Assert.Equal(ExportOutcome.Unchanged, results[0].Outcome);
        Assert.False(File.Exists(same + ".bak"));
        Assert.Equal(ExportOutcome.Written, results[1].Outcome);
        Assert.Equal("new", File.ReadAllText(different));
        Assert.Equal("old", File.ReadAllText(different + ".bak"));
    }

    [Fact]
    public void The_backup_can_be_switched_off()
    {
        string source = SourceFile(text: "new");
        string target = Path.Combine(Vehicles, "MS_G3Pack", "Cotterell.hof");
        File.WriteAllText(target, "old");

        HofExporter.Export(source, Vehicles, ["MS_G3Pack"], new ExportOptions(BackupReplaced: false));

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".bak"));
    }

    [Fact]
    public void Missing_folders_and_the_files_own_folder_are_skipped_with_a_reason_and_the_rest_still_goes_ahead()
    {
        string source = SourceFile();

        var results = HofExporter.Export(source, Vehicles, ["Source", "Gone", "MS_G3Pack", "ms_g3pack", "..\\Maps"]);

        Assert.Equal(["Source", "Gone", "MS_G3Pack", "..\\Maps"], results.Select(r => r.Folder));   // a name twice counts once
        Assert.Equal(ExportOutcome.Skipped, results[0].Outcome);
        Assert.Contains("already saved", results[0].Message);
        Assert.Equal(ExportOutcome.Skipped, results[1].Outcome);
        Assert.Contains("no folder", results[1].Message);
        Assert.Equal(ExportOutcome.Written, results[2].Outcome);
        Assert.Equal(ExportOutcome.Skipped, results[3].Outcome);                                  // a name never leaves Vehicles
        Assert.False(File.Exists(Path.Combine(Omsi, "Cotterell.hof")));
    }

    [Fact]
    public void The_result_is_described_in_words()
    {
        var text = ExportViewModel.Describe([
            new ExportResult("A", "x", ExportOutcome.Written, ""),
            new ExportResult("B", "x", ExportOutcome.Unchanged, ""),
            new ExportResult("C", null, ExportOutcome.Skipped, "There is no folder with this name in Vehicles."),
        ]);

        Assert.StartsWith("1 folder updated, 1 already had the file, 1 not done.", text);
        Assert.Contains("C: There is no folder", text);
    }

    // ---- the list kept in the file (HOF Suite's [HOF_SUITE_DIRECTORIES]) -----------------------------------------

    MainViewModel ViewModel(string hofText)
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        settings.OmsiFolder = Omsi;
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        string file = SourceFile(text: hofText);
        Assert.True(vm.Open(file));
        return vm;
    }

    const string WithList = "[HOF_SUITE_DIRECTORIES]\r\nMS_G3Pack\r\nLondon Citybus 400\r\nOld Pack\r\n[HOF_SUITE_DIRECTORIES_END]\r\n\r\n[name]\r\nCotterell\r\n";

    [Fact]
    public void The_export_list_written_by_HOF_Suite_shows_on_the_settings_page_with_a_check_for_each_folder()
    {
        var vm = ViewModel(WithList);

        Assert.Equal(["MS_G3Pack", "London Citybus 400", "Old Pack"], vm.ExportLocations.Select(l => l.Name));
        Assert.Equal([true, true, false], vm.ExportLocations.Select(l => l.Exists));
        Assert.False(vm.IsDirty);                                                     // looking is not editing
        Assert.DoesNotContain("MS_G3Pack", vm.ExportChoices);                         // already listed
        Assert.Contains("Volvo B7L", vm.ExportChoices);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public void Adding_and_removing_a_location_marks_the_file_changed_and_is_saved_in_the_file_in_alphabetical_order()
    {
        var vm = ViewModel("[name]\r\nCotterell\r\n");
        Assert.True(vm.HasNoExportLocations);

        vm.SelectedExportChoice = "Volvo B7L";
        vm.AddExportLocationCommand.Execute(null);
        vm.SelectedExportChoice = "London Citybus 400";
        vm.AddExportLocationCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.Equal(["London Citybus 400", "Volvo B7L"], vm.ExportLocations.Select(l => l.Name));
        Assert.DoesNotContain("Volvo B7L", vm.ExportChoices);

        vm.RemoveExportLocation(vm.ExportLocations[0]);
        Assert.Contains("London Citybus 400", vm.ExportChoices);                      // can be chosen again

        string saved = Path.Combine(_dir, "saved.hof");
        vm.Save(saved);
        string text = File.ReadAllText(saved, HofEncodings.Windows1252);
        Assert.Contains("[HOF_SUITE_DIRECTORIES]\r\nVolvo B7L\r\n[HOF_SUITE_DIRECTORIES_END]", text);
        Assert.DoesNotContain("London Citybus 400", text);
    }

    // ---- the dialog and the whole export -------------------------------------------------------------------------

    [Fact]
    public void The_dialog_starts_with_the_files_folders_ticked_and_can_be_searched()
    {
        var vm = ViewModel(WithList);
        var export = vm.StartExport()!;

        Assert.Equal(3, export.SelectedCount);
        Assert.Equal(["London Citybus 400", "MS_G3Pack", "Old Pack"], export.SelectedNames);   // sorted, the missing one kept
        Assert.Contains(export.Shown, i => i.Name == "Old Pack" && !i.Exists);

        export.Filter = "volvo";
        Assert.Equal(["Volvo B7L"], export.Shown.Select(i => i.Name));
        export.SelectAllShown(true);
        Assert.Equal(4, export.SelectedCount);
        Assert.Contains("4 folders", export.SelectionText);
    }

    [Fact]
    public async Task Exporting_saves_the_ticked_folders_in_the_file_copies_it_and_says_how_it_went()
    {
        var vm = ViewModel(WithList);
        var export = vm.StartExport()!;
        foreach (var item in export.Shown) item.IsSelected = item.Name is "Volvo B7L" or "MS_G3Pack";   // also drops the folder that is gone

        Assert.True(await vm.ExportAsync(export));

        Assert.True(File.Exists(Path.Combine(Vehicles, "Volvo B7L", "Cotterell.hof")));
        Assert.True(File.Exists(Path.Combine(Vehicles, "MS_G3Pack", "Cotterell.hof")));
        Assert.False(File.Exists(Path.Combine(Vehicles, "London Citybus 400", "Cotterell.hof")));   // unticked
        Assert.Equal(["MS_G3Pack", "Volvo B7L"], vm.ExportLocations.Select(l => l.Name));
        Assert.False(vm.IsDirty);                                                                    // it was saved
        Assert.Contains("[HOF_SUITE_DIRECTORIES]\r\nMS_G3Pack\r\nVolvo B7L\r\n", File.ReadAllText(vm.FilePath!, HofEncodings.Windows1252));
        Assert.True(export.IsDone);
        Assert.StartsWith("2 folders updated", export.Report);
        Assert.Contains("Exported Cotterell.hof: 2 folders updated", vm.Status);
        Assert.Equal(1.0, export.Progress);
        Assert.False(export.CanExport);                                                              // done: the button turns into Close
    }

    [Fact]
    public void Export_needs_an_open_file_and_a_known_Vehicles_folder_and_says_which_is_missing()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        settings.OmsiFolder = Omsi;
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));

        Assert.False(vm.CanExport);
        Assert.Null(vm.StartExport());
        Assert.Contains("Open a .hof file", vm.Status);
    }
}
