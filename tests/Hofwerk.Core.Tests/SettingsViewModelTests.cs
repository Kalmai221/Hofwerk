using System.Text;
using Hofwerk.Core;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class SettingsViewModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-settingsvm-" + Guid.NewGuid().ToString("N"));
    string _omsi => Path.Combine(_dir, "OMSI 2");

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(Path.Combine(_omsi, "Maps"));
        Directory.CreateDirectory(Path.Combine(_omsi, "Vehicles", "Announcements", "Cotterell"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A view model that has OMSI at a fake location, so the folder checks have something to look at.</summary>
    MainViewModel NewViewModel()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        settings.OmsiFolder = _omsi;
        return new MainViewModel(settings, Path.Combine(_dir, "presets"));
    }

    string WriteHof(string globals = "[global_strings]\n6\nCotterell\nCotterell v1.1\nCotterell v1.1\n00\n07\n42\n")
    {
        string path = Path.Combine(_dir, "test.hof");
        File.WriteAllText(path,
            "[name]\nCotterell\n\n[servicetrip]\nNot in Service\n\n" + globals + "\nstringcount_terminus\n1\n\nstringcount_busstop\n0\n\n" +
            "[addterminus_allexit]\n1001\nNot in Service\nNot in Service\n\n[addterminus]\n1002\nSpecial\nSpecial\n",
            Hofwerk.Core.Model.HofEncodings.Windows1252);
        return path;
    }

    string Saved(MainViewModel vm)
    {
        string copy = Path.Combine(_dir, "saved.hof");
        vm.Save(copy);
        return File.ReadAllText(copy, Hofwerk.Core.Model.HofEncodings.Windows1252);
    }

    [Fact]
    public void The_six_global_strings_fill_the_folders_and_IBIS_codes()
    {
        var vm = NewViewModel();
        Assert.True(vm.Open(WriteHof()));

        Assert.Equal(["Cotterell", "Cotterell v1.1", "Cotterell v1.1"], vm.Folders.Select(f => f.Name));
        Assert.Equal(("00", "07", "42"), (vm.Ibis9xx, vm.Ibis8xx, vm.Ibis5xx));
        Assert.Equal("Cotterell", vm.Name);
    }

    [Fact]
    public void Saving_without_changes_keeps_the_global_strings_as_they_were()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());

        string saved = Saved(vm);

        Assert.Contains("[global_strings]\r\n6\r\nCotterell\r\nCotterell v1.1\r\nCotterell v1.1\r\n00\r\n07\r\n42\r\n", saved);
    }

    [Fact]
    public void Edited_folders_and_IBIS_codes_are_saved_and_a_one_digit_code_becomes_two_digits()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());

        vm.Announcements.Name = "Brand New";
        vm.Ibis9xx = "5";
        vm.Ibis5xx = "x9y";   // letters are dropped while typing

        Assert.Equal("9", vm.Ibis5xx);
        string saved = Saved(vm);
        Assert.Contains("[global_strings]\r\n6\r\nBrand New\r\nCotterell v1.1\r\nCotterell v1.1\r\n05\r\n07\r\n09\r\n", saved);
    }

    [Fact]
    public void A_code_cannot_be_longer_than_two_digits()
    {
        var vm = NewViewModel();
        vm.Ibis8xx = "12345";
        Assert.Equal("12", vm.Ibis8xx);
    }

    [Fact]
    public void Editing_any_of_the_new_settings_marks_the_file_as_changed()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());
        Assert.False(vm.IsDirty);
        vm.Ibis9xx = "11";
        Assert.True(vm.IsDirty);

        vm = NewViewModel();
        vm.Open(WriteHof());
        vm.Rollsign.Name = "Another folder";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void A_file_without_a_global_block_does_not_get_one_unless_something_is_entered()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof(globals: ""));

        Assert.DoesNotContain("[global_strings]", Saved(vm));

        vm.SidePlate.Name = "My side plates";
        Assert.Contains("[global_strings]", Saved(vm));
    }

    [Fact]
    public void Each_folder_says_whether_it_exists_under_the_OMSI_Vehicles_folder()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());
        vm.RefreshSettingsData();

        Assert.Equal(FolderState.Found, vm.Announcements.State);          // Vehicles\Announcements\Cotterell
        Assert.Contains("Announcements", vm.Announcements.Where);
        Assert.Equal(FolderState.Missing, vm.Rollsign.State);
        Assert.True(vm.Rollsign.CanCreate);

        vm.Rollsign.Name = "";
        Assert.Equal(FolderState.Empty, vm.Rollsign.State);
    }

    [Fact]
    public void Creating_a_missing_folder_in_a_chosen_place_turns_its_status_to_found()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());
        vm.RefreshSettingsData();
        string parent = Path.Combine(_omsi, "Vehicles", "Announcements");

        Assert.True(vm.CreateFolder(vm.Rollsign, parent));

        Assert.True(Directory.Exists(Path.Combine(parent, "Cotterell v1.1")));
        Assert.Equal(FolderState.Found, vm.Rollsign.State);
        Assert.Equal(FolderState.Found, vm.SidePlate.State);   // same name, same folder
    }

    [Fact]
    public void Creating_a_folder_with_an_unusable_name_reports_it_and_changes_nothing()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());
        vm.Rollsign.Name = "bad|name";

        Assert.False(vm.CreateFolder(vm.Rollsign, Path.Combine(_omsi, "Vehicles")));
        Assert.Contains("Could not create", vm.Status);
    }

    [Fact]
    public void Folders_cannot_be_checked_when_OMSI_is_not_known()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        vm.Open(WriteHof());
        if (vm.HasOmsi) return; // this machine has OMSI installed, so detection finds it; nothing to check here

        vm.RefreshSettingsData();

        Assert.Equal(FolderState.Unknown, vm.Announcements.State);
    }

    [Fact]
    public void The_service_trip_can_be_picked_from_the_destinations()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());
        vm.RefreshSettingsData();

        Assert.Equal(["1001: Not in Service", "1002: Special"], vm.ServiceTripChoices.Select(c => c.Display));
        Assert.Equal("Not in Service", vm.SelectedServiceTrip?.Name);   // what the file says now

        vm.SelectedServiceTrip = vm.ServiceTripChoices[1];

        Assert.Equal("Special", vm.ServiceTrip);
        Assert.True(vm.IsDirty);
        Assert.Contains("[servicetrip]\r\nSpecial\r\n", Saved(vm));
    }

    [Fact]
    public void A_service_trip_that_is_not_a_destination_is_kept_and_shown_as_such()
    {
        var vm = NewViewModel();
        vm.Open(WriteHof());
        vm.ServiceTrip = "Something else";
        vm.RefreshSettingsData();

        Assert.Equal("Something else", vm.SelectedServiceTrip?.Name);
        Assert.Contains("not in the destination list", vm.SelectedServiceTrip!.Display);
    }

    [Fact]
    public void The_sidebar_file_card_follows_the_open_file_and_its_saved_state()
    {
        var vm = NewViewModel();
        Assert.Equal("No file open", vm.FileDisplayName);
        Assert.Equal("", vm.FileStateText);

        vm.Open(WriteHof());
        Assert.Equal("test.hof", vm.FileDisplayName);
        Assert.Equal("All changes saved", vm.FileStateText);

        vm.Name = "Changed";
        Assert.Equal("Unsaved changes", vm.FileStateText);
    }

    [Fact]
    public void Home_starts_on_the_library_when_there_are_no_recent_files_but_OMSI_is_known()
    {
        var vm = NewViewModel();
        if (vm.HasRecent) return; // this machine has recent files; the tab is then the recent list

        Assert.Equal(1, vm.HomeTab);
        Assert.True(vm.ShowLibraryTab);
        Assert.False(vm.ShowRecentTab);

        vm.HomeTab = 0;
        Assert.True(vm.ShowRecentTab);
    }
}
