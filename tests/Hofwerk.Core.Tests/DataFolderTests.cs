using Hofwerk.Core;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class DataFolderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-datadir-" + Guid.NewGuid().ToString("N"));
    readonly string? _before = Environment.GetEnvironmentVariable("HOFWERK_DATA_DIR");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOFWERK_DATA_DIR", _before);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void With_HOFWERK_DATA_DIR_set_the_settings_and_the_recent_files_live_there_instead_of_in_the_real_folder()
    {
        Environment.SetEnvironmentVariable("HOFWERK_DATA_DIR", _dir);

        var settings = AppSettings.Load();
        settings.Theme = "Dark";
        settings.Save();

        Assert.Equal(Path.Combine(_dir, "settings.json"), settings.FilePath);
        Assert.Equal(Path.Combine(_dir, "recent.json"), settings.RecentFilesPath);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.Equal("Dark", AppSettings.Load().Theme);
    }

    [Fact]
    public void Without_it_the_real_folder_is_used_as_before()
    {
        Environment.SetEnvironmentVariable("HOFWERK_DATA_DIR", null);

        var settings = AppSettings.Load();

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hofwerk", "settings.json"), settings.FilePath);
    }
}
