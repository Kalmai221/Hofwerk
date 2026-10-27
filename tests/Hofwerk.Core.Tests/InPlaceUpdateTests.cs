using System.Diagnostics;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Core.Tests;

public class InPlaceUpdateTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hofwerk-inplace-" + Guid.NewGuid().ToString("N"));
    string Dir => Path.Combine(_root, "installed");

    public InPlaceUpdateTests() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        foreach (var p in Process.GetProcessesByName("Hofwerk"))
        {
            try { if (p.MainModule?.FileName.StartsWith(_root, StringComparison.OrdinalIgnoreCase) == true) { p.Kill(); p.WaitForExit(5000); } }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            p.Dispose();
        }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A platform that registers nothing, so the tests never touch the real Start menu or Installed apps.</summary>
    sealed class FakePlatform : PlatformInstaller
    {
        public string? RegisteredVersion { get; private set; }
        public override string DefaultDirectory => "unused";
        public override string ProgramName => "Hofwerk.exe";
        public override string UninstallerName => "Uninstall.exe";
        protected override void Register(InstallRequest request, string directory, long sizeInBytes) => RegisteredVersion = request.Version;
        protected override void Unregister(string directory) { }
        public override void Launch(string directory) { }
    }

    void WriteInstalled(Dictionary<string, byte[]> files)
    {
        foreach (var (name, bytes) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Dir, name))!);
            File.WriteAllBytes(Path.Combine(Dir, name), bytes);
        }
        File.WriteAllLines(Path.Combine(Dir, PlatformInstaller.ManifestName), files.Keys);
    }

    string MakePack(Dictionary<string, byte[]> files)
    {
        string pack = Path.Combine(_root, "new.hwpack");
        var sources = files.Select(f => new PackSource(f.Key, f.Value.Length, () => new MemoryStream(f.Value))).ToList();
        using (var output = File.Create(pack)) PayloadPack.Write(sources, output);
        return pack;
    }

    static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Files_are_replaced_added_and_removed_and_the_manifest_matches()
    {
        WriteInstalled(new()
        {
            ["Hofwerk.exe"] = Bytes("old program"), ["a.txt"] = Bytes("old a"), ["gone.txt"] = Bytes("only in the old version"),
            ["Uninstall.exe"] = Bytes("setup"), ["sub/x.dat"] = Bytes("old x"),
        });
        string pack = MakePack(new() { ["Hofwerk.exe"] = Bytes("new program"), ["a.txt"] = Bytes("new a"), ["new.txt"] = Bytes("brand new"), ["sub/x.dat"] = Bytes("new x") });
        var platform = new FakePlatform();
        var progress = new List<double>();

        InPlaceUpdate.Apply(platform, pack, Dir, "9.9.9", new Progress<double>(progress.Add));

        Assert.Equal("new program", File.ReadAllText(Path.Combine(Dir, "Hofwerk.exe")));
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Dir, "a.txt")));
        Assert.Equal("brand new", File.ReadAllText(Path.Combine(Dir, "new.txt")));
        Assert.Equal("new x", File.ReadAllText(Path.Combine(Dir, "sub", "x.dat")));
        Assert.False(File.Exists(Path.Combine(Dir, "gone.txt")));
        Assert.Equal("setup", File.ReadAllText(Path.Combine(Dir, "Uninstall.exe")));      // the uninstaller stays
        Assert.Empty(Directory.GetFiles(Dir, "*.hwnew", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Dir, "*.hwold", SearchOption.AllDirectories));    // nothing was in use, so nothing is left over
        Assert.Equal("9.9.9", platform.RegisteredVersion);

        var manifest = File.ReadAllLines(Path.Combine(Dir, PlatformInstaller.ManifestName));
        Assert.Equivalent(new[] { "Hofwerk.exe", "a.txt", "new.txt", "sub/x.dat", "Uninstall.exe" }, manifest);
        Assert.Equal(1.0, progress.Max(), 3);
    }

    [Fact]
    public void A_program_that_is_running_can_be_replaced_and_keeps_running()
    {
        if (!OperatingSystem.IsWindows()) return;   // Linux replaces running files outright; this is the Windows case

        // A real running program: a copy of ping that keeps going until it is stopped.
        string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        WriteInstalled(new() { ["a.txt"] = Bytes("old a"), ["Uninstall.exe"] = Bytes("setup") });
        File.Copy(ping, Path.Combine(Dir, "Hofwerk.exe"));
        File.AppendAllLines(Path.Combine(Dir, PlatformInstaller.ManifestName), ["Hofwerk.exe"]);

        using var running = Process.Start(new ProcessStartInfo(Path.Combine(Dir, "Hofwerk.exe"), "-t 127.0.0.1")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        Thread.Sleep(500);
        Assert.False(running.HasExited);

        // Overwriting or deleting it is impossible while it runs.
        Assert.ThrowsAny<Exception>(() => File.Delete(Path.Combine(Dir, "Hofwerk.exe")));

        string pack = MakePack(new() { ["Hofwerk.exe"] = Bytes("the new program"), ["a.txt"] = Bytes("new a") });
        InPlaceUpdate.Apply(new FakePlatform(), pack, Dir, "2.0.0");

        Assert.Equal("the new program", File.ReadAllText(Path.Combine(Dir, "Hofwerk.exe")));   // the new one is in place
        Assert.Equal("new a", File.ReadAllText(Path.Combine(Dir, "a.txt")));
        Assert.False(running.HasExited);                                                      // the old one carries on

        // The old program could not be deleted yet, so it is recorded for the uninstaller and cleaned up later.
        Assert.True(File.Exists(Path.Combine(Dir, "Hofwerk.exe.hwold")));
        Assert.Contains("Hofwerk.exe.hwold", File.ReadAllLines(Path.Combine(Dir, PlatformInstaller.ManifestName)));

        running.Kill();
        running.WaitForExit(5000);
        InPlaceUpdate.CleanLeftovers(Dir);
        Assert.False(File.Exists(Path.Combine(Dir, "Hofwerk.exe.hwold")));
    }

    [Fact]
    public void If_the_swap_fails_halfway_the_old_version_is_put_back()
    {
        if (!OperatingSystem.IsWindows()) return;   // needs a file that cannot be renamed, which is how Windows locks files

        WriteInstalled(new() { ["a.txt"] = Bytes("old a"), ["b.txt"] = Bytes("old b"), ["Uninstall.exe"] = Bytes("setup") });
        string pack = MakePack(new() { ["a.txt"] = Bytes("new a"), ["b.txt"] = Bytes("new b") });

        // b.txt is held open without sharing, so it can be neither renamed nor deleted; a.txt is swapped first.
        using (new FileStream(Path.Combine(Dir, "b.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Assert.Throws<InstallerException>(() => InPlaceUpdate.Apply(new FakePlatform(), pack, Dir, "2.0.0"));
            Assert.Contains("left as it was", ex.Message);
        }

        Assert.Equal("old a", File.ReadAllText(Path.Combine(Dir, "a.txt")));   // restored
        Assert.Equal("old b", File.ReadAllText(Path.Combine(Dir, "b.txt")));
        Assert.Empty(Directory.GetFiles(Dir, "*.hwnew"));
        Assert.Empty(Directory.GetFiles(Dir, "*.hwold"));
    }

    [Fact]
    public void An_update_that_points_outside_the_install_folder_is_refused_and_writes_nothing()
    {
        WriteInstalled(new() { ["a.txt"] = Bytes("old a"), ["Uninstall.exe"] = Bytes("setup") });
        string pack = MakePack(new() { ["../evil.txt"] = Bytes("outside") });

        Assert.Throws<InstallerException>(() => InPlaceUpdate.Apply(new FakePlatform(), pack, Dir, "2.0.0"));

        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.Equal("old a", File.ReadAllText(Path.Combine(Dir, "a.txt")));
    }

    [Fact]
    public void A_folder_that_is_not_an_install_cannot_be_updated()
    {
        string pack = MakePack(new() { ["a.txt"] = Bytes("x") });
        string other = Path.Combine(_root, "other");
        Directory.CreateDirectory(other);

        Assert.False(InPlaceUpdate.CanUpdate(other));
        Assert.Throws<InstallerException>(() => InPlaceUpdate.Apply(new FakePlatform(), pack, other, "2.0.0"));
    }

    [Fact]
    public void A_damaged_update_leaves_the_install_untouched()
    {
        WriteInstalled(new() { ["a.txt"] = Bytes("old a"), ["Uninstall.exe"] = Bytes("setup") });
        string pack = MakePack(new() { ["a.txt"] = Bytes(new string('z', 5000)) });
        byte[] bytes = File.ReadAllBytes(pack);
        File.WriteAllBytes(pack, bytes[..(bytes.Length / 2)]);   // cut in half

        Assert.ThrowsAny<Exception>(() => InPlaceUpdate.Apply(new FakePlatform(), pack, Dir, "2.0.0"));

        Assert.Equal("old a", File.ReadAllText(Path.Combine(Dir, "a.txt")));
        Assert.Empty(Directory.GetFiles(Dir, "*.hwnew"));
    }

    sealed class FailingRegistrationPlatform : PlatformInstaller
    {
        public override string DefaultDirectory => "unused";
        public override string ProgramName => "Hofwerk.exe";
        public override string UninstallerName => "Uninstall.exe";
        protected override void Register(InstallRequest request, string directory, long sizeInBytes) =>
            throw new NotSupportedException("Built-in COM has been disabled via a feature switch.");   // what the trimmed app threw
        protected override void Unregister(string directory) { }
        public override void Launch(string directory) { }
    }

    [Fact]
    public void An_error_while_refreshing_the_Start_menu_and_Installed_apps_does_not_undo_or_break_the_update()
    {
        WriteInstalled(new() { ["Hofwerk.exe"] = Bytes("old"), ["Uninstall.exe"] = Bytes("setup") });
        string pack = MakePack(new() { ["Hofwerk.exe"] = Bytes("new") });

        var warning = InPlaceUpdate.Apply(new FailingRegistrationPlatform(), pack, Dir, "2.0.0");   // used to throw out of the app

        Assert.IsType<NotSupportedException>(warning);                                 // handed back, so the app can log it
        Assert.Equal("new", File.ReadAllText(Path.Combine(Dir, "Hofwerk.exe")));      // the update is complete
        Assert.Equal(["Hofwerk.exe", "Uninstall.exe"], File.ReadAllLines(Path.Combine(Dir, PlatformInstaller.ManifestName)));
    }

    [Fact]
    public void A_registration_that_works_gives_back_no_warning()
    {
        WriteInstalled(new() { ["Hofwerk.exe"] = Bytes("old"), ["Uninstall.exe"] = Bytes("setup") });

        Assert.Null(InPlaceUpdate.Apply(new FakePlatform(), MakePack(new() { ["Hofwerk.exe"] = Bytes("new") }), Dir, "2.0.0"));
    }
}
