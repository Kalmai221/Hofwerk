using Hofwerk.Core.Omsi;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Omsi")]
public class OmsiBrowserTests
{
    // Paths as the locator reports them: the folder under Vehicles goes in Subtitle (with the OS separator).
    static HofFileEntry File(string dir, string name) => new(@$"C:\OMSI 2\Vehicles\{dir}\{name}".Replace(@"\\", @"\"), name, dir);

    static readonly OmsiBrowser Library = new(
    [
        File(@"Citaro", "Berlin.hof"),
        File(@"Citaro", "Hamburg.hof"),
        File(@"Citaro\Facelift", "Wien.hof"),
        File(@"A3", "Berlin.hof"),
        File("", "Loose.hof"),
        File(@"Caetano Levante", "BoTW.hof"),
    ]);

    [Fact]
    public void The_top_level_lists_folders_first_with_counts_and_then_loose_files()
    {
        var items = Library.Browse("");

        Assert.Equal(["A3", "Caetano Levante", "Citaro", "Loose.hof"], items.Select(i => i.Name));
        Assert.Equal([true, true, true, false], items.Select(i => i.IsFolder));
        Assert.Equal("1 .hof file", items[0].Detail);
        Assert.Equal("3 .hof files", items[2].Detail); // counts files in sub-folders too
    }

    [Fact]
    public void Stepping_into_a_folder_shows_its_sub_folders_before_its_files()
    {
        var items = Library.Browse("Citaro");

        Assert.Equal(["Facelift", "Berlin.hof", "Hamburg.hof"], items.Select(i => i.Name));
        Assert.Equal("Citaro/Facelift", items[0].Path);
        Assert.Equal(@"C:\OMSI 2\Vehicles\Citaro\Berlin.hof", items[1].Path);
    }

    [Fact]
    public void A_folder_path_given_with_backslashes_works_the_same()
    {
        Assert.Equal(["Wien.hof"], Library.Browse(@"Citaro\Facelift").Select(i => i.Name));
    }

    [Fact]
    public void Folders_without_any_hof_file_never_appear()
    {
        var onlyHofs = new OmsiBrowser([File("A3", "Berlin.hof")]);
        Assert.Equal(["A3"], onlyHofs.Browse("").Select(i => i.Name));
        Assert.Empty(new OmsiBrowser([]).Browse(""));
    }

    [Fact]
    public void Search_looks_through_every_folder_by_file_name_or_folder_name()
    {
        Assert.Equal(["Berlin.hof", "Berlin.hof"], Library.Search("berlin").Select(i => i.Name));
        Assert.Equal(["Wien.hof"], Library.Search("facelift").Select(i => i.Name));

        var hit = Library.Search("wien").Single();
        Assert.Equal("Citaro › Facelift", hit.Detail);
        Assert.False(hit.IsFolder);
        Assert.Equal("Vehicles", Library.Search("loose").Single().Detail);
    }

    [Fact]
    public void Search_results_are_capped()
    {
        var many = new OmsiBrowser(Enumerable.Range(0, 500).Select(i => File("Pack", $"line{i}.hof")).ToList());
        Assert.Equal(300, many.Search("line").Count);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Citaro", "")]
    [InlineData("Citaro/Facelift", "Citaro")]
    [InlineData(@"Citaro\Facelift\Sub", "Citaro/Facelift")]
    public void Parent_goes_up_one_level(string folder, string parent) => Assert.Equal(parent, OmsiBrowser.Parent(folder));

    [Theory]
    [InlineData("", "Vehicles")]
    [InlineData("Citaro", "Vehicles › Citaro")]
    [InlineData("Citaro/Facelift", "Vehicles › Citaro › Facelift")]
    public void Breadcrumb_reads_like_a_path(string folder, string text) => Assert.Equal(text, OmsiBrowser.Breadcrumb(folder));
}
