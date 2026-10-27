using Hofwerk.Installer;

namespace Hofwerk.Core.Tests;

/// <summary>The logic behind the setup window's "Where is OMSI 2?" step. The window itself is covered by installer/check-setup-ui.ps1.</summary>
[Trait("Area", "Installer")]
public class InstallerWindowTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-setupvm-" + Guid.NewGuid().ToString("N"));

    public InstallerWindowTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    string MakeOmsi()
    {
        string dir = Path.Combine(_root, "OMSI 2");
        Directory.CreateDirectory(Path.Combine(dir, "Vehicles"));
        Directory.CreateDirectory(Path.Combine(dir, "Maps"));
        return dir;
    }

    [Fact]
    public void A_folder_picked_by_hand_that_looks_like_OMSI_is_offered_as_it_is()
    {
        var vm = new InstallerViewModel([]);

        vm.LocationChosen(MakeOmsi());

        Assert.Equal(LocationState.Selected, vm.Location);
        Assert.True(vm.LocationSelected);
        Assert.Equal("Use this folder", vm.LocationUseLabel);
        Assert.Equal("Choose again…", vm.LocationChooseLabel);
        Assert.True(vm.LocationHasPath);
    }

    [Fact]
    public void A_folder_that_does_not_look_like_OMSI_is_flagged_but_can_still_be_used()
    {
        string notOmsi = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(notOmsi);
        var vm = new InstallerViewModel([]);

        vm.LocationChosen(notOmsi);

        Assert.Equal(LocationState.Invalid, vm.Location);
        Assert.True(vm.LocationInvalid);
        Assert.Equal(notOmsi, vm.LocationPath);
        Assert.Equal("Use it anyway", vm.LocationUseLabel);
        Assert.True(vm.LocationHasPath);   // "use it anyway" stays available
    }

    [Fact]
    public void When_OMSI_cannot_be_found_the_page_offers_select_or_skip_but_no_use_button()
    {
        var vm = new InstallerViewModel([]) { Location = LocationState.NotFound };

        Assert.True(vm.LocationNotFound);
        Assert.False(vm.LocationHasPath);
        Assert.Equal("Select the folder…", vm.LocationChooseLabel);
    }

    [Fact]
    public void A_detected_folder_is_confirmed_with_a_yes_or_a_different_choice()
    {
        var vm = new InstallerViewModel([]) { Location = LocationState.Detected };

        Assert.Equal("Yes, that is correct", vm.LocationUseLabel);
        Assert.Equal("No, choose a folder…", vm.LocationChooseLabel);
    }

    [Fact]
    public void Picking_again_after_a_wrong_folder_moves_to_the_new_state()
    {
        var vm = new InstallerViewModel([]);
        vm.LocationChosen(Path.Combine(_root, "wrong"));
        Assert.Equal(LocationState.Invalid, vm.Location);

        vm.LocationChosen(MakeOmsi());

        Assert.Equal(LocationState.Selected, vm.Location);
    }

    [Fact]
    public void Next_refuses_a_folder_with_someone_elses_files_and_stays_on_the_first_page()
    {
        string foreign = Path.Combine(_root, "Documents");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "thesis.docx"), "precious");
        var vm = new InstallerViewModel([]) { Directory = foreign };

        vm.NextCommand.Execute(null);

        Assert.Equal(SetupStage.Welcome, vm.Stage);
        Assert.True(vm.HasWelcomeError);
        Assert.Contains("not empty", vm.WelcomeError);
    }

    [Fact]
    public void Next_with_a_good_folder_moves_to_the_OMSI_question_and_back_returns()
    {
        var vm = new InstallerViewModel([]) { Directory = Path.Combine(_root, "Hofwerk") };

        vm.NextCommand.Execute(null);

        Assert.Equal(SetupStage.Location, vm.Stage);
        Assert.True(vm.ShowLocation);
        Assert.False(vm.ShowWelcome);
        Assert.False(vm.HasWelcomeError);
        Assert.True(vm.LocationDetected || vm.LocationNotFound); // depends on whether this machine has OMSI

        vm.BackToWelcomeCommand.Execute(null);

        Assert.Equal(SetupStage.Welcome, vm.Stage);
    }
}
