using Hofwerk.Core;
using Hofwerk.Core.Updates;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

public class PreReleaseChoiceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-prerel-" + Guid.NewGuid().ToString("N"));

    public PreReleaseChoiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    MainViewModel NewViewModel(string current)
    {
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets"));
        vm.ConfigureUpdates(new UpdateChecker(new HttpClient(), runtimeId: "win-x64"), AppVersion.TryParse(current)!, Path.Combine(_dir, "dl"), _ => { });
        return vm;
    }

    [Fact]
    public void The_switch_is_on_by_itself_while_running_a_pre_release_and_says_why()
    {
        var vm = NewViewModel("0.1-dev1");

        Assert.True(vm.IncludePreReleases);
        Assert.Contains("running a pre-release", vm.PreReleaseHint);
    }

    [Fact]
    public void A_final_version_does_not_get_pre_releases_unless_asked()
    {
        var vm = NewViewModel("0.1.0");

        Assert.False(vm.IncludePreReleases);
        Assert.Equal("", vm.PreReleaseHint);
    }

    [Fact]
    public void Turning_the_switch_off_is_respected_even_on_a_pre_release_and_is_remembered()
    {
        var vm = NewViewModel("0.1-dev1");

        vm.IncludePreReleases = false;

        Assert.False(vm.IncludePreReleases);
        Assert.Equal("", vm.PreReleaseHint);
        Assert.False(NewViewModel("0.1-dev1").IncludePreReleases);        // read back from the settings file
        Assert.False(AppSettings.Load(Path.Combine(_dir, "settings.json")).AllowPreReleases);

        vm.IncludePreReleases = true;
        Assert.True(NewViewModel("0.1.0").IncludePreReleases);            // an explicit yes holds for a final version too
    }

    [Fact]
    public void An_old_settings_file_with_the_earlier_setting_is_still_read()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"IncludePreReleases\": false, \"Theme\": \"Dark\" }");

        var vm = NewViewModel("0.1-dev1");

        Assert.True(vm.IncludePreReleases);        // the old false was only the unset default, so it does not count as a choice
        Assert.Equal("Dark", vm.ThemeId);
    }
}
