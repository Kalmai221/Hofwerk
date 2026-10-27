using Hofwerk.Core.Omsi;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Omsi")]
public class PathCasingTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "Hofwerk-Casing-" + Guid.NewGuid().ToString("N"));

    public PathCasingTests() => Directory.CreateDirectory(Path.Combine(_root, "Program Files (x86)", "Steam", "OMSI 2"));
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Folders_get_the_capital_letters_they_really_have()
    {
        if (!OperatingSystem.IsWindows()) return;   // on Linux letter case is part of the name, so there is nothing to correct

        string exact = Path.Combine(_root, "Program Files (x86)", "Steam", "OMSI 2");
        string lower = exact.ToLowerInvariant();

        Assert.NotEqual(exact, lower);
        Assert.Equal(exact, PathCasing.Normalize(lower));
        Assert.Equal(exact, PathCasing.Normalize(exact.ToUpperInvariant().Replace(_root.ToUpperInvariant(), _root.ToLowerInvariant())));
    }

    [Fact]
    public void The_drive_letter_is_a_capital()
    {
        if (!OperatingSystem.IsWindows()) return;

        string result = PathCasing.Normalize(@"c:\windows");

        Assert.StartsWith("C:\\", result);
        Assert.Equal(@"C:\Windows", result, ignoreCase: false);
    }

    [Fact]
    public void A_name_that_does_not_exist_is_kept_as_written_and_empty_text_is_left_alone()
    {
        if (OperatingSystem.IsWindows())
            Assert.EndsWith(@"\not a real folder", PathCasing.Normalize(Path.Combine(_root, "not a real folder")));
        Assert.Equal("", PathCasing.Normalize(""));
    }

    [Fact]
    public void The_locator_gives_paths_the_way_they_are_spelled_on_disk()
    {
        foreach (string folder in OmsiLocator.FindInstallFolders())
            Assert.Equal(folder, PathCasing.Normalize(folder));
    }
}
