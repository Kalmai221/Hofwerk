using Hofwerk.Core;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class DiagnosticsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-diagnostics-" + Guid.NewGuid().ToString("N"));

    public DiagnosticsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // ---- repeated clicks ------------------------------------------------------------------------------------------

    [Fact]
    public void The_tenth_quick_click_completes_the_count_and_not_before()
    {
        var taps = new RepeatedClicks();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 9; i++)
            Assert.False(taps.Tap(now.AddMilliseconds(i * 200)), $"click {i}");

        Assert.True(taps.Tap(now.AddMilliseconds(2000)));
    }

    [Fact]
    public void After_opening_the_count_starts_again()
    {
        var taps = new RepeatedClicks(3);
        var now = DateTime.UtcNow;

        Assert.False(taps.Tap(now));
        Assert.False(taps.Tap(now));
        Assert.True(taps.Tap(now));
        Assert.False(taps.Tap(now));
        Assert.False(taps.Tap(now));
        Assert.True(taps.Tap(now));
    }

    [Fact]
    public void A_pause_between_clicks_loses_the_count()
    {
        var taps = new RepeatedClicks(3, TimeSpan.FromSeconds(2));
        var now = DateTime.UtcNow;

        taps.Tap(now);
        taps.Tap(now.AddSeconds(1));
        Assert.False(taps.Tap(now.AddSeconds(10)));         // too long since the last one: this is click 1 again
        Assert.False(taps.Tap(now.AddSeconds(11)));
        Assert.True(taps.Tap(now.AddSeconds(12)));
    }

    [Fact]
    public void A_click_somewhere_else_loses_the_count()
    {
        var taps = new RepeatedClicks(3);
        var now = DateTime.UtcNow;

        taps.Tap(now);
        taps.Tap(now);
        taps.Reset();

        Assert.False(taps.Tap(now));
        Assert.False(taps.Tap(now));
        Assert.True(taps.Tap(now));
    }

    // ---- what the window shows -------------------------------------------------------------------------------------

    MainViewModel ViewModel()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        return new MainViewModel(settings, Path.Combine(_dir, "presets"));
    }

    [Fact]
    public void The_report_names_the_version_the_system_the_files_and_the_state_of_the_open_file()
    {
        var vm = ViewModel();
        string file = Path.Combine(_dir, "test.hof");
        File.WriteAllText(file, "[name]\nCotterell\n\n[servicetrip]\nNot in Service\n", Hofwerk.Core.Model.HofEncodings.Windows1252);
        vm.EditSettleTime = TimeSpan.Zero;
        Assert.True(vm.Open(file));
        vm.Name = "Changed";

        string report = vm.DiagnosticsReport("Avalonia");

        Assert.Contains("HOFWERK", report);
        Assert.Contains("Avalonia", report);
        Assert.Contains(Hofwerk.Core.Updates.AppVersion.Current.Text, report);
        Assert.Contains("settings.json", report);
        Assert.Contains("recent.json", report);
        Assert.Contains("OPEN FILE", report);
        Assert.Contains("test.hof", report);
        Assert.Matches(@"Unsaved\s+yes", report);
        Assert.Matches(@"Undo / redo\s+1 / 0 steps", report);
    }

    [Fact]
    public void Without_a_file_the_report_says_so_and_leaves_out_the_details()
    {
        string report = ViewModel().DiagnosticsReport("WinUI");

        Assert.Matches(@"File\s+none", report);
        Assert.DoesNotContain("Undo / redo", report);
    }

    [Fact]
    public void The_users_name_is_taken_out_of_the_paths_in_the_report()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var vm = ViewModel();

        string report = vm.DiagnosticsReport("WinUI");

        // the install folder or the crash log are under the profile on most computers; where they are not, there is nothing to hide
        if (report.Contains("%USERPROFILE%") || report.Contains("~/")) Assert.DoesNotContain(profile, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(profile + Path.DirectorySeparatorChar, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clearing_the_recent_files_empties_the_list_and_leaves_the_files_alone()
    {
        var vm = ViewModel();
        string file = Path.Combine(_dir, "keep.hof");
        File.WriteAllText(file, "[name]\nKeep\n", Hofwerk.Core.Model.HofEncodings.Windows1252);
        Assert.True(vm.Open(file));
        Assert.Single(vm.Recent);

        vm.ClearRecentFiles();

        Assert.Empty(vm.Recent);
        Assert.True(File.Exists(file));
        Assert.Empty(new RecentFiles(Path.Combine(_dir, "recent.json")).Load());      // and it stays empty next time
    }
}
