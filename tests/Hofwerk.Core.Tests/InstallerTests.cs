using System.Text;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Installer")]
public class InstallerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-inst-" + Guid.NewGuid().ToString("N"));
    string Path_(string name) => Path.Combine(_root, name);

    public InstallerTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Does everything the real installers do except talk to the OS (shortcuts, registry).</summary>
    sealed class FakeInstaller : PlatformInstaller
    {
        public bool Registered, Unregistered;
        public string SettingsFile = Path.Combine(Path.GetTempPath(), "hofwerk-unused-" + Guid.NewGuid().ToString("N") + ".json");
        protected override string SettingsPath => SettingsFile;
        public override string DefaultDirectory => "";
        public override string ProgramName => "app.exe";
        public override string UninstallerName => "uninstall.exe";
        protected override void Register(InstallRequest request, string directory, long sizeInBytes) => Registered = true;
        protected override void Unregister(string directory) => Unregistered = true;
        public override void Launch(string directory) { }
    }

    static PackSource Source(string path, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new PackSource(path, bytes.Length, () => new MemoryStream(bytes));
    }

    /// <summary>A pack file holding the given files; paths are used as written, so tests can try hostile ones.</summary>
    string MakePack(params (string Name, string Content)[] files)
    {
        string pack = Path_(Guid.NewGuid().ToString("N") + ".pack");
        using var output = File.Create(pack);
        PayloadPack.Write(files.Select(f => Source(f.Name, f.Content)).ToList(), output);
        return pack;
    }

    /// <summary>An "installer exe": some program bytes with the pack attached.</summary>
    string MakeInstaller(string pack)
    {
        string program = Path_("program.bin");
        File.WriteAllBytes(program, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        string installer = Path_(Guid.NewGuid().ToString("N") + ".exe");
        PayloadFile.Attach(program, pack, installer);
        return installer;
    }

    string MakeInstaller(params (string Name, string Content)[] files) => MakeInstaller(MakePack(files));

    async Task Install(FakeInstaller platform, string installer, string dir)
    {
        using var payload = PayloadFile.OpenPayload(installer)!; // closed even when the install is refused
        await platform.InstallAsync(new InstallRequest(dir, DesktopShortcut: true, FileAssociation: true), payload, installer);
    }

    [Fact]
    public void Attached_payload_can_be_read_back_and_removed()
    {
        string installer = MakeInstaller(("a.txt", "hello"));

        Assert.True(PayloadFile.HasPayload(installer));
        Assert.Equal(10, PayloadFile.ProgramLength(installer));

        var seen = new Dictionary<string, string>();
        using (var payload = PayloadFile.OpenPayload(installer)!)
            PayloadPack.Read(payload, _ => { }, (entry, content) => seen[entry.Path] = new StreamReader(content).ReadToEnd());
        Assert.Equal("hello", seen["a.txt"]);

        string copy = Path_("uninstall.exe");
        PayloadFile.CopyProgramOnly(installer, copy);
        Assert.False(PayloadFile.HasPayload(copy));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, File.ReadAllBytes(copy));
    }

    [Fact]
    public void Attaching_twice_replaces_the_old_payload()
    {
        string installer = MakeInstaller(("old.txt", "x"));
        string again = Path_("again.exe");
        PayloadFile.Attach(installer, MakePack(("new.txt", "y")), again);

        Assert.Equal(10, PayloadFile.ProgramLength(again));
        var names = new List<string>();
        using var payload = PayloadFile.OpenPayload(again)!;
        PayloadPack.Read(payload, entries => names.AddRange(entries.Select(e => e.Path)), (_, _) => { });
        Assert.Equal(["new.txt"], names);
    }

    [Fact]
    public void Pack_round_trips_binary_files_of_every_size_in_order()
    {
        var random = new Random(7);
        byte[] Bytes(int n) { var b = new byte[n]; random.NextBytes(b); return b; }
        var files = new (string Name, byte[] Data)[]
        {
            ("empty.bin", []),
            ("one.bin", Bytes(1)),
            ("sub/dir/medium.bin", Bytes(70_000)),
            ("big.bin", Bytes(1_500_000)),
            ("after-big.txt", Encoding.UTF8.GetBytes("still in step")),
        };

        using var stream = new MemoryStream();
        PayloadPack.Write(files.Select(f => new PackSource(f.Name, f.Data.Length, () => new MemoryStream(f.Data))).ToList(), stream);
        stream.Position = 0;

        var got = new List<(string, byte[])>();
        PayloadPack.Read(stream, _ => { }, (entry, content) =>
        {
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            got.Add((entry.Path, copy.ToArray()));
        });

        Assert.Equal(files.Length, got.Count);
        for (int i = 0; i < files.Length; i++)
        {
            Assert.Equal(files[i].Name, got[i].Item1);
            Assert.Equal(files[i].Data, got[i].Item2);
        }
    }

    [Fact]
    public void Pack_skips_files_the_reader_does_not_consume()
    {
        using var stream = new MemoryStream();
        PayloadPack.Write([Source("first.txt", new string('a', 5000)), Source("second.txt", "wanted")], stream);
        stream.Position = 0;

        string? second = null;
        PayloadPack.Read(stream, _ => { }, (entry, content) =>
        {
            if (entry.Path == "second.txt") second = new StreamReader(content).ReadToEnd();
        });

        Assert.Equal("wanted", second);
    }

    [Fact]
    public void A_pack_that_is_not_ours_is_rejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is not a pack"));
        Assert.ThrowsAny<Exception>(() => PayloadPack.Read(stream, _ => { }, (_, _) => { }));
    }

    [Fact]
    public void A_cut_off_pack_is_reported_instead_of_installing_half_a_file()
    {
        using var whole = new MemoryStream();
        PayloadPack.Write([Source("a.bin", new string('x', 200_000)), Source("b.bin", new string('y', 200_000))], whole);
        using var cut = new MemoryStream(whole.ToArray()[..(int)(whole.Length / 2)]);

        // Either error is fine; the installer turns both into "Installing failed: …" instead of leaving a half-written app.
        var error = Record.Exception(() => PayloadPack.Read(cut, _ => { }, (_, content) => content.CopyTo(Stream.Null)));
        Assert.True(error is IOException or InvalidDataException, $"unexpected: {error?.GetType().Name ?? "no error"}");
    }

    [Fact]
    public async Task Install_extracts_files_adds_uninstaller_and_manifest()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("app.exe", "program"), ("Assets/icon.png", "png"));
        string dir = Path_("Hofwerk");

        await Install(platform, installer, dir);

        Assert.Equal("program", File.ReadAllText(Path.Combine(dir, "app.exe")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(dir, "Assets", "icon.png")));
        Assert.True(File.Exists(Path.Combine(dir, "uninstall.exe")));
        Assert.False(PayloadFile.HasPayload(Path.Combine(dir, "uninstall.exe")));
        Assert.True(PlatformInstaller.IsInstalled(dir));
        Assert.True(platform.Registered);
    }

    [Fact]
    public async Task Progress_is_reported_from_start_to_finish()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("app.exe", new string('a', 10_000)), ("lib.dll", new string('b', 10_000)));
        var steps = new List<double>();

        using (var payload = PayloadFile.OpenPayload(installer)!)
            await platform.InstallAsync(new InstallRequest(Path_("Hofwerk"), false, false), payload, installer,
                new SyncProgress(p => steps.Add(p.Fraction)));

        Assert.Equal(1.0, steps[^1]);
        Assert.Equal(steps.Order().ToList(), steps); // never goes backwards
        Assert.Contains(steps, s => s is > 0.05 and < 0.9);
    }

    [Fact]
    public async Task An_install_goes_through_extract_move_cleanup_and_close_in_that_order_and_leaves_no_work_folder()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("app.exe", "program"), ("Assets/icon.png", "png"));
        string dir = Path_("Hofwerk");
        var seen = new List<InstallStep>();

        using (var payload = PayloadFile.OpenPayload(installer)!)
            await platform.InstallAsync(new InstallRequest(dir, false, false), payload, installer,
                new SyncProgress(p => { if (p.Step != InstallStep.None && (seen.Count == 0 || seen[^1] != p.Step)) seen.Add(p.Step); }));

        Assert.Equal([InstallStep.Extract, InstallStep.Move, InstallStep.Cleanup, InstallStep.Close], seen);
        Assert.Equal("program", File.ReadAllText(Path.Combine(dir, "app.exe")));
        Assert.False(Directory.Exists(Path.Combine(dir, ".hwsetup")));
    }

    [Fact]
    public async Task A_damaged_package_leaves_an_existing_install_untouched()
    {
        var platform = new FakeInstaller();
        string dir = Path_("Hofwerk");
        await Install(platform, MakeInstaller(("app.exe", "old program")), dir);

        string damaged = MakeInstaller(("app.exe", "new program"), ("../escape.txt", "x"));
        using (var payload = PayloadFile.OpenPayload(damaged)!)
            await Assert.ThrowsAsync<InstallerException>(() => platform.InstallAsync(new InstallRequest(dir, false, false), payload, damaged));

        Assert.Equal("old program", File.ReadAllText(Path.Combine(dir, "app.exe")));
        Assert.False(Directory.Exists(Path.Combine(dir, ".hwsetup")));
    }

    /// <summary>Progress&lt;T&gt; posts to another thread; tests want the values as they happen.</summary>
    sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    [Fact]
    public async Task Uninstall_removes_what_was_installed_but_not_other_files()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("app.exe", "program"), ("Assets/icon.png", "png"));
        string dir = Path_("Hofwerk");
        await Install(platform, installer, dir);
        File.WriteAllText(Path.Combine(dir, "my-notes.txt"), "keep me");

        await platform.UninstallAsync(dir, removeUserData: false);

        Assert.False(File.Exists(Path.Combine(dir, "app.exe")));
        Assert.False(Directory.Exists(Path.Combine(dir, "Assets")));
        Assert.False(File.Exists(Path.Combine(dir, PlatformInstaller.ManifestName)));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(dir, "my-notes.txt")));
        Assert.True(platform.Unregistered);
    }

    [Fact]
    public async Task Uninstall_removes_the_folder_when_nothing_else_is_in_it()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("app.exe", "program"));
        string dir = Path_("Hofwerk");
        await Install(platform, installer, dir);

        await platform.UninstallAsync(dir, removeUserData: false);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task Updating_replaces_old_files_including_ones_the_new_version_dropped()
    {
        var platform = new FakeInstaller();
        string dir = Path_("Hofwerk");
        await Install(platform, MakeInstaller(("app.exe", "v1"), ("old.dll", "gone soon")), dir);

        await Install(platform, MakeInstaller(("app.exe", "v2")), dir);

        Assert.Equal("v2", File.ReadAllText(Path.Combine(dir, "app.exe")));
        Assert.False(File.Exists(Path.Combine(dir, "old.dll")));
    }

    [Fact]
    public async Task A_package_that_writes_outside_the_install_folder_is_rejected()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("../evil.txt", "bad"));
        string dir = Path_("Hofwerk");

        var ex = await Assert.ThrowsAsync<InstallerException>(() => Install(platform, installer, dir));

        Assert.Contains("damaged", ex.Message);
        Assert.False(File.Exists(Path_("evil.txt")));
    }

    [Fact]
    public async Task A_folder_with_someone_elses_files_is_refused()
    {
        var platform = new FakeInstaller();
        string installer = MakeInstaller(("app.exe", "program"));
        string dir = Path_("Documents");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "thesis.docx"), "precious");

        var ex = await Assert.ThrowsAsync<InstallerException>(() => Install(platform, installer, dir));

        Assert.Contains("not empty", ex.Message);
        Assert.Equal("precious", File.ReadAllText(Path.Combine(dir, "thesis.docx")));
        Assert.False(platform.Registered);
    }

    [Fact]
    public async Task Uninstalling_where_nothing_is_installed_does_not_delete_anything()
    {
        var platform = new FakeInstaller();
        string dir = Path_("Documents");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "thesis.docx"), "precious");

        await Assert.ThrowsAsync<InstallerException>(() => platform.UninstallAsync(dir, removeUserData: false));

        Assert.True(File.Exists(Path.Combine(dir, "thesis.docx")));
    }

    [Fact]
    public async Task The_language_chosen_in_the_installer_is_saved_for_the_app_and_nothing_is_written_when_none_was_chosen()
    {
        var platform = new FakeInstaller { SettingsFile = Path_("settings.json") };
        File.WriteAllText(platform.SettingsFile, """{ "ActivePresetId": "my-bus-pack" }""");
        string installer = MakeInstaller(("app.exe", "program"));

        using (var payload = PayloadFile.OpenPayload(installer)!)
            await platform.InstallAsync(new InstallRequest(Path_("Hofwerk"), false, false, Language: "de"), payload, installer);

        var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(platform.SettingsFile))!;
        Assert.Equal("de", (string?)saved["Language"]);
        Assert.Equal("my-bus-pack", (string?)saved["ActivePresetId"]);

        // an update from inside the app does not ask again: the language stays as it is
        File.WriteAllText(platform.SettingsFile, """{ "Language": "en" }""");
        string again = MakeInstaller(("app.exe", "program 2"));
        using (var payload = PayloadFile.OpenPayload(again)!)
            await platform.InstallAsync(new InstallRequest(Path_("Hofwerk"), false, false), payload, again);
        Assert.Equal("en", (string?)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(platform.SettingsFile))!["Language"]);
    }

    [Fact]
    public async Task The_chosen_OMSI_folder_is_saved_into_the_apps_settings_without_losing_other_settings()
    {
        var platform = new FakeInstaller { SettingsFile = Path_("settings.json") };
        File.WriteAllText(platform.SettingsFile, """{ "ActivePresetId": "my-bus-pack" }""");
        string installer = MakeInstaller(("app.exe", "program"));
        string omsi = Path_("OMSI 2");

        using (var payload = PayloadFile.OpenPayload(installer)!)
            await platform.InstallAsync(new InstallRequest(Path_("Hofwerk"), false, false, omsi), payload, installer);

        var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(platform.SettingsFile))!;
        Assert.Equal(omsi, (string?)saved["OmsiFolder"]);
        Assert.Equal("my-bus-pack", (string?)saved["ActivePresetId"]);
    }

    [Fact]
    public async Task Skipping_the_OMSI_question_leaves_the_settings_alone()
    {
        var platform = new FakeInstaller { SettingsFile = Path_("settings.json") };
        string installer = MakeInstaller(("app.exe", "program"));

        using (var payload = PayloadFile.OpenPayload(installer)!)
            await platform.InstallAsync(new InstallRequest(Path_("Hofwerk"), false, false, OmsiFolder: null), payload, installer);

        Assert.False(File.Exists(platform.SettingsFile));
    }

    [Fact]
    public void A_broken_settings_file_is_replaced_instead_of_blocking_the_install()
    {
        string file = Path_("settings.json");
        File.WriteAllText(file, "{ this is not json");

        UserSettings.SaveOmsiFolder(@"D:\Games\OMSI 2", file);

        Assert.Contains("OMSI 2", File.ReadAllText(file));
    }

    [Fact]
    public void Linux_menu_entry_points_at_the_install_folder_and_offers_uninstall()
    {
        string dir = Path.Combine("home", "me", "hofwerk"); // the real installer only runs this on Linux, where this is a / path
#pragma warning disable CA1416 // LinuxInstaller is for Linux, but this method only builds text, so it is checked on every system
        string entry = LinuxInstaller.BuildDesktopEntry(dir);
#pragma warning restore CA1416

        Assert.Contains($"Exec=\"{Path.Combine(dir, "hofwerk")}\" %f", entry);
        Assert.Contains("Icon=hofwerk", entry);
        Assert.Contains("[Desktop Action Uninstall]", entry);
        Assert.Contains($"Exec=\"{Path.Combine(dir, "uninstall")}\" --uninstall", entry);
        Assert.StartsWith("[Desktop Entry]", entry);
    }
}
