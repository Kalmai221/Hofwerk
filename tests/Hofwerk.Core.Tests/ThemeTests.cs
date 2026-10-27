using Hofwerk.Core;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
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

[Trait("Area", "App")]
public class StatusMessageTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-status-" + Guid.NewGuid().ToString("N"));

    public StatusMessageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    MainViewModel NewViewModel(TimeSpan lifetime) =>
        new(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets")) { StatusLifetime = lifetime };

    [Fact]
    public void A_status_message_is_three_seconds_by_default()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), NewViewModel(TimeSpan.FromSeconds(3)).StatusLifetime);
        Assert.Equal(TimeSpan.FromSeconds(3), new MainViewModel(AppSettings.Load(Path.Combine(_dir, "s2.json")), Path.Combine(_dir, "presets")).StatusLifetime);
    }

    // Waits until the status is empty (up to 10 seconds) instead of guessing how long a busy machine needs.
    static async Task<bool> ClearsWithin(MainViewModel vm, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (vm.Status == "") return true;
            await Task.Delay(25);
        }
        return vm.Status == "";
    }

    [Fact]
    public async Task A_message_clears_itself_after_its_time()
    {
        var vm = NewViewModel(TimeSpan.FromMilliseconds(150));

        vm.Status = "Added Railway Station to 401 A4";
        Assert.Equal("Added Railway Station to 401 A4", vm.Status);

        Assert.True(await ClearsWithin(vm, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_newer_message_is_not_cut_short_by_the_older_ones_timer()
    {
        var vm = NewViewModel(TimeSpan.FromMilliseconds(1500));

        vm.Status = "first";
        await Task.Delay(900);
        vm.Status = "second";
        await Task.Delay(900);          // the first message's 1.5 s are over (1.8 s in), the second one's are not (0.9 s in)

        Assert.Equal("second", vm.Status);
        Assert.True(await ClearsWithin(vm, TimeSpan.FromSeconds(10)));
    }
}
