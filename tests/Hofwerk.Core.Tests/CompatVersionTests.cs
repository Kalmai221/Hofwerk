using System.Net;
using System.Security.Cryptography;
using Hofwerk.Core;
using Hofwerk.Core.Errors;
using Hofwerk.Core.Updates;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Core.Tests;

public class CompatVersionTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-compat-" + Guid.NewGuid().ToString("N"));
    string Install => Path.Combine(_root, "install");

    public CompatVersionTests() => Directory.CreateDirectory(Install);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    void MakeInstall()
    {
        File.WriteAllText(Path.Combine(Install, "Hofwerk.exe"), "standard");
        File.WriteAllText(Path.Combine(Install, "Uninstall.exe"), "setup");
        File.WriteAllLines(Path.Combine(Install, PlatformInstaller.ManifestName), ["Hofwerk.exe", "Uninstall.exe"]);
    }

    string MakePack(Dictionary<string, string> files)
    {
        string pack = Path.Combine(_root, "compat.hwpack");
        var sources = files.Select(f => new PackSource(f.Key, f.Value.Length, () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(f.Value)))).ToList();
        using (var output = File.Create(pack)) PayloadPack.Write(sources, output);
        return pack;
    }

    // ---- where things are --------------------------------------------------------------------------------

    [Fact]
    public void The_standard_version_knows_where_the_compatibility_version_would_be()
    {
        MakeInstall();

        var ui = new WindowsUi(Install + Path.DirectorySeparatorChar);

        Assert.False(ui.IsCompat);
        Assert.Equal(Install, ui.InstallFolder);
        Assert.Equal(Path.Combine(Install, "compat", "hofwerk.exe"), ui.CompatExe);
        Assert.False(ui.CompatInstalled);
    }

    [Fact]
    public void The_compatibility_version_finds_the_standard_one_next_to_its_folder()
    {
        MakeInstall();
        Directory.CreateDirectory(Path.Combine(Install, "compat"));

        var ui = new WindowsUi(Path.Combine(Install, "compat") + Path.DirectorySeparatorChar);

        Assert.True(ui.IsCompat);
        Assert.Equal(Install, ui.InstallFolder);
        Assert.Equal(Path.Combine(Install, "Hofwerk.exe"), ui.StandardExe);
    }

    [Fact]
    public void A_folder_called_compat_without_a_Hofwerk_exe_beside_it_is_not_taken_for_one()
    {
        var elsewhere = Path.Combine(_root, "something", "compat");
        Directory.CreateDirectory(elsewhere);

        Assert.False(new WindowsUi(elsewhere).IsCompat);
    }

    [Fact]
    public void Switching_is_only_offered_for_an_installed_copy_on_Windows()
    {
        Assert.False(new WindowsUi(Install).CanSwitch);           // no install manifest yet: a copy run from a build folder
        MakeInstall();
        Assert.Equal(OperatingSystem.IsWindows(), new WindowsUi(Install).CanSwitch);
    }

    // ---- unpacking into the install ------------------------------------------------------------------------

    [Fact]
    public void The_compatibility_version_is_unpacked_into_compat_and_the_uninstaller_will_remove_it()
    {
        MakeInstall();
        string pack = MakePack(new() { ["hofwerk.exe"] = "avalonia", ["lib/a.dll"] = "library" });

        InPlaceUpdate.ExtractFolder(pack, Install, "compat");

        Assert.Equal("avalonia", File.ReadAllText(Path.Combine(Install, "compat", "hofwerk.exe")));
        Assert.Equal("library", File.ReadAllText(Path.Combine(Install, "compat", "lib", "a.dll")));
        Assert.Equal(["Hofwerk.exe", "Uninstall.exe", "compat/hofwerk.exe", "compat/lib/a.dll"], File.ReadAllLines(Path.Combine(Install, PlatformInstaller.ManifestName)));
        Assert.Equal("standard", File.ReadAllText(Path.Combine(Install, "Hofwerk.exe")));      // the standard version is untouched
    }

    [Fact]
    public void Unpacking_again_replaces_the_old_files_and_does_not_repeat_the_manifest_entries()
    {
        MakeInstall();
        InPlaceUpdate.ExtractFolder(MakePack(new() { ["hofwerk.exe"] = "old", ["old-only.dll"] = "x" }), Install, "compat");

        InPlaceUpdate.ExtractFolder(MakePack(new() { ["hofwerk.exe"] = "new" }), Install, "compat");

        Assert.Equal("new", File.ReadAllText(Path.Combine(Install, "compat", "hofwerk.exe")));
        Assert.False(File.Exists(Path.Combine(Install, "compat", "old-only.dll")));
        Assert.Equal(["Hofwerk.exe", "Uninstall.exe", "compat/hofwerk.exe"], File.ReadAllLines(Path.Combine(Install, PlatformInstaller.ManifestName)));
    }

    [Fact]
    public void A_pack_that_points_outside_the_folder_is_refused_and_leaves_nothing_behind()
    {
        MakeInstall();
        string pack = MakePack(new() { ["../escape.txt"] = "no" });

        Assert.Throws<InstallerException>(() => InPlaceUpdate.ExtractFolder(pack, Install, "compat"));

        Assert.False(File.Exists(Path.Combine(Install, "escape.txt")));
        Assert.False(Directory.Exists(Path.Combine(Install, "compat")));
    }

    [Fact]
    public void Nothing_can_be_added_to_a_folder_that_is_not_an_install()
    {
        string pack = MakePack(new() { ["a"] = "b" });

        Assert.Throws<InstallerException>(() => InPlaceUpdate.ExtractFolder(pack, Install, "compat"));
    }

    [Fact]
    public void Updating_the_main_app_removes_the_old_compatibility_version_because_it_is_not_in_the_new_pack()
    {
        MakeInstall();
        InPlaceUpdate.ExtractFolder(MakePack(new() { ["hofwerk.exe"] = "avalonia" }), Install, "compat");

        string newApp = Path.Combine(_root, "new.hwpack");
        using (var output = File.Create(newApp))
            PayloadPack.Write([new PackSource("Hofwerk.exe", 3, () => new MemoryStream("new"u8.ToArray()))], output);
        InPlaceUpdate.Apply(new FakePlatform(), newApp, Install, "2.0.0");

        Assert.Equal("new", File.ReadAllText(Path.Combine(Install, "Hofwerk.exe")));
        Assert.False(File.Exists(Path.Combine(Install, "compat", "hofwerk.exe")));
    }

    sealed class FakePlatform : PlatformInstaller
    {
        public override string DefaultDirectory => "unused";
        public override string ProgramName => "Hofwerk.exe";
        public override string UninstallerName => "Uninstall.exe";
        protected override void Register(InstallRequest request, string directory, long sizeInBytes) { }
        protected override void Unregister(string directory) { }
        public override void Launch(string directory) { }
    }

    // ---- finding and downloading it -----------------------------------------------------------------------

    sealed class Fake(string json, byte[] pack, string sum) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            HttpContent body = url.StartsWith("https://api.github.com") ? new StringContent(json)
                : url.EndsWith(".sha256") ? new StringContent(sum)
                : new ByteArrayContent(pack);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    static string ReleaseJson(string tag, int size) =>
        "[" + $$"""{"tag_name":"{{tag}}","html_url":"u","draft":false,"prerelease":false,"assets":[""" +
        $$"""{"name":"Hofwerk-Compat-{{tag}}-win-x64.hwpack","size":{{size}},"browser_download_url":"https://github.com/x/y/Hofwerk-Compat-{{tag}}-win-x64.hwpack"},""" +
        $$"""{"name":"Hofwerk-Compat-{{tag}}-win-x64.hwpack.sha256","size":80,"browser_download_url":"https://github.com/x/y/Hofwerk-Compat-{{tag}}-win-x64.hwpack.sha256"}]}""" + "]";

    [Fact]
    public async Task The_compatibility_version_of_exactly_the_running_version_is_found_and_other_versions_are_not()
    {
        var checker = new UpdateChecker(new HttpClient(new Fake(ReleaseJson("V1-dev1", 10), [], "")), runtimeId: "win-x64");

        Assert.NotNull(await checker.FindCompatAsync(AppVersion.TryParse("1-dev1")!));
        Assert.Null(await checker.FindCompatAsync(AppVersion.TryParse("0.1-dev2")!));
        Assert.Null(await checker.FindCompatAsync(AppVersion.TryParse("1.0.0")!));        // a development build has no release
    }

    [Fact]
    public async Task It_is_downloaded_checked_and_unpacked_into_the_install_in_one_go()
    {
        MakeInstall();
        string pack = MakePack(new() { ["hofwerk.exe"] = "avalonia" });
        byte[] bytes = File.ReadAllBytes(pack);
        string sum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var checker = new UpdateChecker(new HttpClient(new Fake(ReleaseJson("V1-dev1", bytes.Length), bytes, sum + "  x\n")), runtimeId: "win-x64");

        await new WindowsUi(Install).InstallCompatAsync(checker, AppVersion.TryParse("1-dev1")!, Path.Combine(_root, "dl"));

        Assert.Equal("avalonia", File.ReadAllText(Path.Combine(Install, "compat", "hofwerk.exe")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "dl")));                       // the downloaded file is removed
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_installs_nothing()
    {
        MakeInstall();
        string pack = MakePack(new() { ["hofwerk.exe"] = "avalonia" });
        byte[] bytes = File.ReadAllBytes(pack);
        var checker = new UpdateChecker(new HttpClient(new Fake(ReleaseJson("V1-dev1", bytes.Length), bytes, new string('0', 64))), runtimeId: "win-x64");

        await Assert.ThrowsAsync<IOException>(() => new WindowsUi(Install).InstallCompatAsync(checker, AppVersion.TryParse("1-dev1")!, Path.Combine(_root, "dl")));

        Assert.False(Directory.Exists(Path.Combine(Install, "compat")));
    }

    [Fact]
    public async Task A_version_without_a_compatibility_download_says_so()
    {
        MakeInstall();
        var checker = new UpdateChecker(new HttpClient(new Fake("[]", [], "")), runtimeId: "win-x64");

        var ex = await Assert.ThrowsAsync<IOException>(() => new WindowsUi(Install).InstallCompatAsync(checker, AppVersion.TryParse("1-dev1")!, Path.Combine(_root, "dl")));

        Assert.Contains("no compatibility version", ex.Message);
    }

    // ---- the choice is remembered ------------------------------------------------------------------------

    [Fact]
    public void Switching_is_remembered_in_the_settings_and_undone_by_switching_back()
    {
        MakeInstall();
        string settingsFile = Path.Combine(_root, "settings.json");
        var settings = AppSettings.Load(settingsFile);

        // nothing to switch to until the compatibility version has been installed
        Assert.False(new WindowsUi(Install).SwitchToCompat(settings));
        Assert.False(AppSettings.Load(settingsFile).PreferCompatibilityVersion);

        settings.PreferCompatibilityVersion = true;
        settings.Save();
        Assert.True(AppSettings.Load(settingsFile).PreferCompatibilityVersion);
        settings.PreferCompatibilityVersion = false;
        settings.Save();
        Assert.False(AppSettings.Load(settingsFile).PreferCompatibilityVersion);
    }
}
