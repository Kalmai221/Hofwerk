using Hofwerk.Core;
using Hofwerk.Core.Omsi;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Omsi")]
public class HomeTests
{
    [Fact]
    public void Parses_steam_library_paths()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0" { "path"  "C:\\Program Files (x86)\\Steam" "label" "" }
                "1" { "path"  "/mnt/games/SteamLibrary" }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", "/mnt/games/SteamLibrary"], OmsiLocator.ParseLibraryPaths(vdf));
    }

    [Fact]
    public void Finds_hof_files_in_vehicles_ignoring_folder_case()
    {
        string root = Path.Combine(Path.GetTempPath(), "hofsuite-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "vehicles", "Citaro"));
            File.WriteAllText(Path.Combine(root, "vehicles", "Citaro", "Berlin.hof"), "");
            File.WriteAllText(Path.Combine(root, "vehicles", "Citaro", "readme.txt"), "");

            var files = OmsiLocator.FindHofFiles(root);
            var entry = Assert.Single(files);
            Assert.Equal("Berlin.hof", entry.Title);
            Assert.Equal("Citaro", entry.Subtitle);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Recent_files_keep_newest_first_without_duplicates_and_drop_missing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hofsuite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string a = Path.Combine(dir, "a.hof"), b = Path.Combine(dir, "b.hof");
            File.WriteAllText(a, ""); File.WriteAllText(b, "");
            var store = new RecentFiles(Path.Combine(dir, "recent.json"));

            store.Add(a); store.Add(b);
            Assert.Equal([b, a], store.Add(b)); // re-adding keeps one entry
            Assert.Equal([a, b], store.Add(a));

            File.Delete(b);
            Assert.Equal([a], store.Load());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
