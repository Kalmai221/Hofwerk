using Hofwerk.Core;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

public class ThemeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-theme-" + Guid.NewGuid().ToString("N"));

    public ThemeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string SettingsFile => Path.Combine(_dir, "settings.json");

    MainViewModel NewViewModel() => new(AppSettings.Load(SettingsFile), Path.Combine(_dir, "presets"));

    [Fact]
    public void The_theme_defaults_to_the_system_one_and_offers_three_choices()
    {
        var vm = NewViewModel();

        Assert.Equal("System", vm.ThemeId);
        Assert.Equal(["System default", "Light", "Dark"], vm.ThemeChoices.Select(t => t.Label));
        Assert.Equal(["System", "Light", "Dark"], vm.ThemeChoices.Select(t => t.Id));
    }

    [Fact]
    public void A_chosen_theme_is_announced_saved_and_remembered_next_time()
    {
        var vm = NewViewModel();
        var announced = new List<string>();
        vm.ThemeChanged += announced.Add;

        vm.SelectedTheme = vm.ThemeChoices.Single(t => t.Id == "Dark");
        vm.SelectedTheme = vm.ThemeChoices.Single(t => t.Id == "Dark");   // choosing the same one again does nothing

        Assert.Equal(["Dark"], announced);
        Assert.Equal("Dark", NewViewModel().ThemeId);
        Assert.Equal("Dark", AppSettings.Load(SettingsFile).Theme);

        vm.SelectedTheme = vm.ThemeChoices.Single(t => t.Id == "Light");
        Assert.Equal("Light", NewViewModel().ThemeId);
    }

    [Fact]
    public void An_unknown_theme_in_the_settings_file_means_the_system_theme()
    {
        File.WriteAllText(SettingsFile, "{ \"Theme\": \"Purple\" }");

        Assert.Equal("System", NewViewModel().ThemeId);
    }
}
