using System.Net;
using System.Text;
using Hofwerk.Core;
using Hofwerk.Core.Updates;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Updates")]
public class AppVersionTests
{
    static AppVersion V(string s) => AppVersion.TryParse(s)!;

    [Theory]
    [InlineData("0.2.0", "0.1.0")]
    [InlineData("0.10", "0.9")]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("0.1.0", "0.1-dev1")]      // a release is newer than its own pre-release
    [InlineData("0.1-dev2", "0.1-dev1")]
    [InlineData("0.1-dev10", "0.1-dev2")]  // numbers compare as numbers
    [InlineData("0.2-dev1", "0.1.0")]
    public void Newer_versions_compare_higher(string newer, string older)
    {
        Assert.True(V(newer) > V(older), $"{newer} should be newer than {older}");
        Assert.True(V(older) < V(newer));
    }

    [Fact]
    public void A_leading_v_build_metadata_and_missing_zeros_do_not_matter()
    {
        Assert.Equal(V("0.1"), V("V0.1.0"));
        Assert.Equal(V("1.0.0"), V("v1.0.0+abc123"));
        Assert.False(V("1.0").IsPreRelease);
        Assert.True(V("1.0-rc1").IsPreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.x")]
    [InlineData("-dev1")]
    public void Text_that_is_not_a_version_is_rejected(string text) => Assert.Null(AppVersion.TryParse(text));
}

[Trait("Area", "Updates")]
public class UpdateTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-update-" + Guid.NewGuid().ToString("N"));

    public UpdateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    sealed class FakeGitHub(string releasesJson, byte[] download, bool offline = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (offline) throw new HttpRequestException("no network");
            return Task.FromResult(request.RequestUri.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releasesJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(download) });
        }
    }

    static string Release(string tag, bool prerelease, string assetUrl = "https://github.com/Kalmai221/Hofwerk/releases/download/{0}/Hofwerk-Setup-{1}-win-x64.exe",
        int size = 5, bool draft = false)
    {
        string version = tag.TrimStart('v', 'V');
        string url = string.Format(assetUrl, tag, version);
        return $$"""
            {"tag_name":"{{tag}}","html_url":"https://github.com/Kalmai221/Hofwerk/releases/tag/{{tag}}","body":"notes","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},
             "assets":[
               {"name":"Hofwerk-Setup-{{version}}-linux-x64.tar.gz","size":9,"browser_download_url":"https://github.com/x/y/z/Hofwerk-Setup-{{version}}-linux-x64.tar.gz"},
               {"name":"Hofwerk-Setup-{{version}}-win-x64.exe","size":{{size}},"browser_download_url":"{{url}}"}]}
            """;
    }

    static string Releases(params string[] items) => "[" + string.Join(",", items) + "]";

    static UpdateChecker Checker(FakeGitHub github, string rid = "win-x64") => new(new HttpClient(github), runtimeId: rid);

    [Fact]
    public async Task A_newer_release_is_found_for_this_platform()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false), Release("v0.1.0", false)), []);
        var found = await Checker(github).CheckAsync(AppVersion.TryParse("0.1.0")!, includePreReleases: false);

        Assert.NotNull(found);
        Assert.Equal("0.2.0", found.Version.Text);
        Assert.Equal("Hofwerk-Setup-0.2.0-win-x64.exe", found.AssetName);
        Assert.False(found.IsPreRelease);
    }

    [Fact]
    public async Task The_installer_for_the_other_platform_is_not_picked()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false)), []);
        var found = await Checker(github, "linux-x64").CheckAsync(AppVersion.TryParse("0.1.0")!, false);

        Assert.Equal("Hofwerk-Setup-0.2.0-linux-x64.tar.gz", found!.AssetName);
    }

    [Fact]
    public async Task Nothing_is_offered_when_up_to_date_or_ahead()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false)), []);
        Assert.Null(await Checker(github).CheckAsync(AppVersion.TryParse("0.2.0")!, false));
        Assert.Null(await Checker(github).CheckAsync(AppVersion.TryParse("0.3.0")!, false));
    }

    [Fact]
    public async Task Pre_releases_and_drafts_are_only_offered_when_wanted()
    {
        var json = Releases(Release("V0.3-dev1", true), Release("v0.2.0", false), Release("v0.9.0", false, draft: true));
        var current = AppVersion.TryParse("0.1.0")!;

        Assert.Equal("0.2.0", (await Checker(new FakeGitHub(json, [])).CheckAsync(current, includePreReleases: false))!.Version.Text);
        Assert.Equal("0.3-dev1", (await Checker(new FakeGitHub(json, [])).CheckAsync(current, includePreReleases: true))!.Version.Text);
    }

    [Fact]
    public async Task A_pre_release_is_only_offered_when_pre_releases_are_wanted()
    {
        var json = Releases(Release("V0.1-dev2", true), Release("V0.1-dev1", true));
        var current = AppVersion.TryParse("0.1-dev1")!;

        Assert.Equal("0.1-dev2", (await Checker(new FakeGitHub(json, [])).CheckAsync(current, includePreReleases: true))!.Version.Text);
        Assert.Null(await Checker(new FakeGitHub(json, [])).CheckAsync(current, includePreReleases: false));

        // the final release is offered either way
        var final = Releases(Release("v0.1.0", false), Release("V0.1-dev2", true));
        Assert.Equal("0.1.0", (await Checker(new FakeGitHub(final, [])).CheckAsync(AppVersion.TryParse("0.1-dev2")!, false))!.Version.Text);
    }

    [Fact]
    public async Task A_download_that_is_not_hosted_on_github_is_ignored()
    {
        var json = Releases(Release("v0.2.0", false, assetUrl: "https://evil.example.com/{0}/Hofwerk-Setup-{1}-win-x64.exe"));
        Assert.Null(await Checker(new FakeGitHub(json, [])).CheckAsync(AppVersion.TryParse("0.1.0")!, false));
    }

    [Fact]
    public async Task Download_saves_the_file_and_reports_progress()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var github = new FakeGitHub(Releases(Release("v0.2.0", false, size: 5)), bytes);
        var checker = Checker(github);
        var update = (await checker.CheckAsync(AppVersion.TryParse("0.1.0")!, false))!;

        double last = 0;
        string path = await checker.DownloadAsync(update, _dir, new Progress<double>(p => last = p));

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("Hofwerk-Setup-0.2.0-win-x64.exe", Path.GetFileName(path));
        await Task.Delay(50);
        Assert.Equal(1.0, last, 3);
    }

    [Fact]
    public async Task An_incomplete_download_is_an_error()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false, size: 99)), [1, 2, 3]);
        var checker = Checker(github);
        var update = (await checker.CheckAsync(AppVersion.TryParse("0.1.0")!, false))!;

        await Assert.ThrowsAsync<IOException>(() => checker.DownloadAsync(update, _dir));
    }

    // ---- The view model ---------------------------------------------------------------------------------

    MainViewModel NewViewModel(FakeGitHub github, string current, List<string> launched, out AppSettings settings)
    {
        settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        vm.ConfigureUpdates(Checker(github), AppVersion.TryParse(current)!, Path.Combine(_dir, "dl"), launched.Add);
        return vm;
    }

    [Fact]
    public async Task Checking_reports_a_new_version_and_installing_downloads_and_starts_it()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false, size: 3)), [7, 8, 9]);
        var launched = new List<string>();
        var vm = NewViewModel(github, "0.1.0", launched, out _);
        bool exit = false;
        vm.ExitRequested += () => exit = true;

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.True(vm.UpdateAvailable);
        Assert.Equal("Hofwerk 0.2.0 is available", vm.UpdateHeadline);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));

        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Single(launched);
        Assert.Equal([7, 8, 9], File.ReadAllBytes(launched[0]));
        Assert.True(exit);
    }

    [Fact]
    public async Task Up_to_date_says_so_and_offers_no_install()
    {
        var vm = NewViewModel(new FakeGitHub(Releases(Release("v0.1.0", false)), []), "0.1.0", [], out _);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.False(vm.UpdateAvailable);
        Assert.Contains("latest version", vm.UpdateStatus);
        Assert.False(vm.InstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Being_offline_is_reported_on_request_and_silent_at_start()
    {
        var vm = NewViewModel(new FakeGitHub("[]", [], offline: true), "0.1.0", [], out _);

        await vm.CheckForUpdatesOnStartAsync();
        Assert.Equal("", vm.UpdateStatus);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("Could not check", vm.UpdateStatus);
        Assert.False(vm.UpdateAvailable);
    }

    [Fact]
    public async Task The_start_check_can_be_turned_off()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false)), []);
        var vm = NewViewModel(github, "0.1.0", [], out var settings);

        vm.CheckForUpdatesOnStart = false;
        await vm.CheckForUpdatesOnStartAsync();

        Assert.Empty(github.Requests);
        Assert.False(AppSettings.Load(Path.Combine(_dir, "settings.json")).CheckForUpdates);
    }

    [Fact]
    public async Task Unsaved_changes_stop_the_update()
    {
        var github = new FakeGitHub(Releases(Release("v0.2.0", false, size: 3)), [1, 2, 3]);
        var launched = new List<string>();
        var vm = NewViewModel(github, "0.1.0", launched, out _);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.IsDirty = true;
        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Empty(launched);
        Assert.Contains("Save your changes", vm.UpdateStatus);
    }
}

[Trait("Area", "Updates")]
public class InstallerUpdateTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-setupupdate-" + Guid.NewGuid().ToString("N"));

    public InstallerUpdateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    sealed class Fake(string json, byte[] file) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(request.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) });
    }

    static string Json(string version) =>
        $$"""[{"tag_name":"v{{version}}","html_url":"https://github.com/x/y","draft":false,"prerelease":false,"assets":[{"name":"Hofwerk-Setup-{{version}}-win-x64.exe","size":3,"browser_download_url":"https://github.com/x/y/Hofwerk-Setup-{{version}}-win-x64.exe"}]}]""";

    Hofwerk.Installer.InstallerViewModel NewViewModel(string releaseVersion, string ownVersion, List<string> launched)
    {
        var vm = new Hofwerk.Installer.InstallerViewModel([]);
        var checker = new UpdateChecker(new HttpClient(new Fake(Json(releaseVersion), [1, 2, 3])), runtimeId: "win-x64");
        vm.ConfigureUpdates(checker, AppVersion.TryParse(ownVersion)!, _dir, launched.Add);
        return vm;
    }

    [Fact]
    public async Task A_newer_release_is_offered_and_downloading_it_starts_that_installer()
    {
        var launched = new List<string>();
        var vm = NewViewModel("0.3.0", "0.2.0", launched);
        bool closed = false;
        vm.ExitRequested += () => closed = true;

        await vm.CheckForNewerVersionAsync();
        Assert.Equal("Hofwerk 0.3.0 is available", vm.NewerHeadline);
        Assert.True(vm.ShowNewerVersion);

        await vm.DownloadNewerCommand.ExecuteAsync(null);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Assert.Single(launched)));
        Assert.True(closed);
    }

    [Fact]
    public async Task Nothing_is_offered_when_this_installer_is_the_newest()
    {
        var vm = NewViewModel("0.2.0", "0.2.0", []);

        await vm.CheckForNewerVersionAsync();

        Assert.False(vm.ShowNewerVersion);
    }

    [Fact]
    public async Task No_network_is_not_an_error()
    {
        var vm = new Hofwerk.Installer.InstallerViewModel([]);
        vm.ConfigureUpdates(new UpdateChecker(new HttpClient(new ThrowingHandler()), runtimeId: "win-x64"), AppVersion.TryParse("0.2.0")!, _dir, _ => { });

        await vm.CheckForNewerVersionAsync();

        Assert.False(vm.ShowNewerVersion);
    }

    sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("offline");
    }
}

[Trait("Area", "Updates")]
public class PackUpdateTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-packupdate-" + Guid.NewGuid().ToString("N"));

    public PackUpdateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    sealed class Fake(string json, byte[] pack, string checksumText, List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            requests.Add(url);
            HttpContent body = url.StartsWith("https://api.github.com") ? new StringContent(json)
                : url.EndsWith(".sha256") ? new StringContent(checksumText)
                : url.EndsWith(".hwpack") ? new ByteArrayContent(pack)
                : new ByteArrayContent([9, 9, 9]);   // the full installer
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    static string Release(string version, int packSize, bool withPack = true)
    {
        string baseUrl = $"https://github.com/x/y/releases/download/v{version}";
        string installer = $$"""{"name":"Hofwerk-Setup-{{version}}-win-x64.exe","size":3,"browser_download_url":"{{baseUrl}}/Hofwerk-Setup-{{version}}-win-x64.exe"}""";
        string pack = $$"""{"name":"Hofwerk-App-{{version}}-win-x64.hwpack","size":{{packSize}},"browser_download_url":"{{baseUrl}}/Hofwerk-App-{{version}}-win-x64.hwpack"}""";
        string sum = $$"""{"name":"Hofwerk-App-{{version}}-win-x64.hwpack.sha256","size":80,"browser_download_url":"{{baseUrl}}/Hofwerk-App-{{version}}-win-x64.hwpack.sha256"}""";
        string assets = withPack ? $"{installer},{pack},{sum}" : installer;
        return $$"""[{"tag_name":"v{{version}}","html_url":"https://github.com/x/y","draft":false,"prerelease":false,"assets":[""" + assets + "]}]";
    }

    static string Sha(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public async Task A_release_with_the_packed_app_is_downloaded_as_just_that_and_checked()
    {
        byte[] pack = Enumerable.Range(0, 3000).Select(i => (byte)(i % 200)).ToArray();
        var requests = new List<string>();
        var checker = new UpdateChecker(new HttpClient(new Fake(Release("0.2.0", pack.Length), pack, Sha(pack) + "  Hofwerk-App-0.2.0-win-x64.hwpack\n", requests)), runtimeId: "win-x64");

        var update = (await checker.CheckAsync(AppVersion.TryParse("0.1.0")!, false))!;
        Assert.NotNull(update.Pack);
        Assert.Equal(pack.Length, update.DownloadSize);   // the smaller download is what is reported

        var download = await checker.DownloadUpdateAsync(update, _dir, canApplyInPlace: true);

        Assert.True(download.IsPack);
        Assert.Equal(pack, File.ReadAllBytes(download.Path));
        Assert.DoesNotContain(requests, r => r.Contains("Hofwerk-Setup-"));   // the full installer is not downloaded
    }

    [Fact]
    public async Task A_pack_that_does_not_match_its_checksum_is_refused_and_removed()
    {
        byte[] pack = [1, 2, 3, 4, 5];
        var checker = new UpdateChecker(new HttpClient(new Fake(Release("0.2.0", 5), pack, Sha([9, 9]) + "  x\n", [])), runtimeId: "win-x64");
        var update = (await checker.CheckAsync(AppVersion.TryParse("0.1.0")!, false))!;

        var ex = await Assert.ThrowsAsync<IOException>(() => checker.DownloadUpdateAsync(update, _dir, canApplyInPlace: true));

        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(_dir, "*.hwpack"));
    }

    [Fact]
    public async Task Without_a_pack_or_without_an_installed_setup_the_full_installer_is_used()
    {
        var noPack = new UpdateChecker(new HttpClient(new Fake(Release("0.2.0", 5, withPack: false), [], "", [])), runtimeId: "win-x64");
        var update = (await noPack.CheckAsync(AppVersion.TryParse("0.1.0")!, false))!;
        Assert.Null(update.Pack);
        Assert.False((await noPack.DownloadUpdateAsync(update, _dir, canApplyInPlace: true)).IsPack);

        byte[] pack = [1, 2, 3, 4, 5];
        var withPack = new UpdateChecker(new HttpClient(new Fake(Release("0.2.0", 5), pack, Sha(pack), [])), runtimeId: "win-x64");
        update = (await withPack.CheckAsync(AppVersion.TryParse("0.1.0")!, false))!;
        Assert.False((await withPack.DownloadUpdateAsync(update, _dir, canApplyInPlace: false)).IsPack);   // e.g. a portable copy with no setup beside it
    }

    [Fact]
    public async Task The_view_model_applies_the_pack_itself_then_restarts_with_the_open_file_and_closes()
    {
        byte[] pack = Enumerable.Range(0, 500).Select(i => (byte)i).ToArray();
        var checker = new UpdateChecker(new HttpClient(new Fake(Release("0.2.0", pack.Length), pack, Sha(pack), [])), runtimeId: "win-x64");
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        string? appliedVersion = null;
        byte[]? appliedBytes = null;
        string? restartedWith = "not called";
        var launchedInstaller = new List<string>();
        vm.ConfigureUpdates(checker, AppVersion.TryParse("0.1.0")!, Path.Combine(_dir, "dl"), launchedInstaller.Add,
            applyPack: (file, version) => { appliedBytes = File.ReadAllBytes(file); appliedVersion = version; },
            restart: file => restartedWith = file);
        bool closed = false;
        vm.ExitRequested += () => closed = true;

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("download", vm.UpdateDetail);
        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal("0.2.0", appliedVersion);
        Assert.Equal(pack, appliedBytes);
        Assert.Null(restartedWith);                 // no file was open
        Assert.Empty(launchedInstaller);            // no separate installer is involved
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "dl")));   // the downloaded file is cleaned up
        Assert.True(closed);
    }

    MainViewModel UpdatingModel(Action<string, string> applyPack, Action<string?> restart)
    {
        byte[] pack = [1, 2, 3, 4, 5];
        var checker = new UpdateChecker(new HttpClient(new Fake(Release("0.2.0", pack.Length), pack, Sha(pack), [])), runtimeId: "win-x64");
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets"));
        vm.ConfigureUpdates(checker, AppVersion.TryParse("0.1.0")!, Path.Combine(_dir, "dl"), _ => { }, applyPack, restart);
        return vm;
    }

    [Fact]
    public async Task If_the_new_version_cannot_be_started_the_app_stays_open_and_says_so()
    {
        var vm = UpdatingModel((_, _) => { }, _ => throw new IOException("no way"));
        bool closed = false;
        vm.ExitRequested += () => closed = true;

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Contains("0.2.0 is installed", vm.UpdateStatus);
        Assert.Contains("open Hofwerk", vm.UpdateStatus);
    }

    [Fact]
    public async Task Any_error_while_applying_the_update_is_reported_and_does_not_close_or_crash_the_app()
    {
        bool restarted = false;
        var vm = UpdatingModel((_, _) => throw new NotSupportedException("COM is off"), _ => restarted = true);
        bool closed = false;
        vm.ExitRequested += () => closed = true;

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.False(restarted);
        Assert.StartsWith("The update failed", vm.UpdateStatus);
    }

    [Fact]
    public void Leftovers_of_an_update_are_removed_by_the_new_copy_once_it_runs()
    {
        string folder = Path.Combine(_dir, "install");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "Hofwerk.dll"), "new");
        File.WriteAllText(Path.Combine(folder, "Hofwerk.dll.hwold"), "old");
        File.WriteAllText(Path.Combine(folder, "sub", "x.dll.hwold"), "old");
        Assert.True(Hofwerk.Installer.Engine.InPlaceUpdate.HasLeftovers(folder));

        UpdateChecker.CleanUpAfterUpdate(folder);

        Assert.False(Hofwerk.Installer.Engine.InPlaceUpdate.HasLeftovers(folder));
        Assert.True(File.Exists(Path.Combine(folder, "Hofwerk.dll")));   // the new files stay
    }
}

[Trait("Area", "Updates")]
public class VersionNamingTests
{
    static AppVersion V(string s) => AppVersion.TryParse(s)!;

    [Fact]
    public void The_V1_dev_pre_releases_come_after_V0_1_and_before_V1()
    {
        // 0.1-dev1 < 0.1-dev2 < 1-dev1 < 1-dev2 < 1-dev10 < 1 (final)
        string[] order = ["0.1-dev1", "0.1-dev2", "1-dev1", "1-dev2", "1-dev10", "1", "1.1"];
        for (int i = 0; i + 1 < order.Length; i++)
            Assert.True(V(order[i]) < V(order[i + 1]), $"{order[i]} should be older than {order[i + 1]}");

        Assert.True(V("V1-dev1").IsPreRelease);
        Assert.False(V("V1").IsPreRelease);
        Assert.Equal(V("1"), V("V1.0.0"));
    }

    [Fact]
    public async Task Someone_on_V0_1_dev2_is_offered_V1_dev1_with_pre_releases_on_and_V1_when_it_exists()
    {
        string json = """
            [{"tag_name":"V1","html_url":"u","draft":false,"prerelease":false,"assets":[{"name":"Hofwerk-Setup-1-win-x64.exe","size":3,"browser_download_url":"https://github.com/x/y/Hofwerk-Setup-1-win-x64.exe"}]},
             {"tag_name":"V1-dev1","html_url":"u","draft":false,"prerelease":true,"assets":[{"name":"Hofwerk-Setup-1-dev1-win-x64.exe","size":3,"browser_download_url":"https://github.com/x/y/Hofwerk-Setup-1-dev1-win-x64.exe"}]}]
            """;
        var checker = new UpdateChecker(new HttpClient(new Fake(json)), runtimeId: "win-x64");

        var withPre = await checker.CheckAsync(V("0.1-dev2"), includePreReleases: true);
        var without = await checker.CheckAsync(V("0.1-dev2"), includePreReleases: false);

        Assert.Equal("1", withPre!.Version.Text);       // the final release is the newest of all
        Assert.Equal("1", without!.Version.Text);

        var onlyDev = new UpdateChecker(new HttpClient(new Fake(json.Replace("\"tag_name\":\"V1\"", "\"tag_name\":\"V0.5\""))), runtimeId: "win-x64");
        Assert.Equal("1-dev1", (await onlyDev.CheckAsync(V("0.1-dev2"), includePreReleases: true))!.Version.Text);
    }

    sealed class Fake(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
