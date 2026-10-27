using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Omsi")]
public class GlobalSettingsTests
{
    [Fact]
    public void Reads_the_three_folders_and_three_IBIS_codes_of_a_real_file()
    {
        // The [global_strings] block of the Cotterell file written by the original HOF Suite.
        var g = GlobalSettings.From(["Cotterell", "Cotterell v1.1", "Cotterell v1.1 side", "00", "07", "42"]);

        Assert.Equal("Cotterell", g.AnnouncementsFolder);
        Assert.Equal("Cotterell v1.1", g.RollsignFolder);
        Assert.Equal("Cotterell v1.1 side", g.SidePlateFolder);
        Assert.Equal(("00", "07", "42"), (g.Ibis9xx, g.Ibis8xx, g.Ibis5xx));
        Assert.True(g.HasIbisCodes);
    }

    [Fact]
    public void Writes_back_exactly_what_it_read()
    {
        string[] lines = ["Cotterell", "Cotterell v1.1", "Cotterell v1.1", "00", "00", "00"];
        Assert.Equal(lines, GlobalSettings.From(lines).ToLines());
    }

    [Fact]
    public void Lines_after_the_six_are_kept()
    {
        string[] lines = ["a", "b", "c", "01", "02", "03", "something newer versions add"];
        Assert.Equal(lines, GlobalSettings.From(lines).ToLines());
    }

    [Fact]
    public void An_older_short_block_is_written_back_unchanged_when_nothing_was_edited()
    {
        string[] old = ["Bowdenham_Announcements", "Bowdenham_Rollband", "Bowdenham_Window_Sign", "4"];

        var g = GlobalSettings.From(old);

        Assert.False(g.HasIbisCodes);
        Assert.Equal(old, g.ToLines());
    }

    [Fact]
    public void Editing_an_IBIS_code_on_an_older_file_upgrades_the_block_to_six_lines()
    {
        var g = GlobalSettings.From(["a", "b", "c", "4"]);

        g.Ibis8xx = "12";

        Assert.Equal(["a", "b", "c", "00", "12", "00"], g.ToLines());
    }

    [Theory]
    [InlineData("", "00")]
    [InlineData("7", "07")]
    [InlineData("42", "42")]
    [InlineData("123", "12")]
    [InlineData("a9b", "09")]
    [InlineData("xx", "00")]
    [InlineData(null, "00")]
    public void IBIS_codes_are_always_written_as_two_digits(string? typed, string written) =>
        Assert.Equal(written, GlobalSettings.NormalizeCode(typed));

    [Fact]
    public void A_code_typed_as_one_digit_is_padded_when_the_file_is_written()
    {
        var g = GlobalSettings.From(["a", "b", "c", "00", "00", "00"]);
        g.Ibis9xx = "5";
        Assert.Equal("05", g.ToLines()[3]);
    }
}

[Trait("Area", "Omsi")]
public class VehicleFolderIndexTests : IDisposable
{
    readonly string _omsi = Path.Combine(Path.GetTempPath(), "hofwerk-vfi-" + Guid.NewGuid().ToString("N"));

    public VehicleFolderIndexTests()
    {
        Directory.CreateDirectory(Path.Combine(_omsi, "Vehicles", "Announcements", "Cotterell"));
        Directory.CreateDirectory(Path.Combine(_omsi, "Vehicles", "SP_CustomDisplays", "Cotterell v1.1"));
        Directory.CreateDirectory(Path.Combine(_omsi, "Vehicles", "Bus", "Texture"));
    }

    public void Dispose() => Directory.Delete(_omsi, recursive: true);

    [Fact]
    public void A_folder_is_found_wherever_it_is_under_Vehicles()
    {
        var index = VehicleFolderIndex.Build(_omsi);

        Assert.Equal("Announcements", index.Find("Cotterell"));
        Assert.Equal("SP_CustomDisplays", index.Find("Cotterell v1.1"));
        Assert.Equal("Bus", index.Find("texture")); // case does not matter
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_not_found()
    {
        Assert.Null(VehicleFolderIndex.Build(_omsi).Find("Nowhere"));
        Assert.Null(VehicleFolderIndex.Build(_omsi).Find("  "));
    }

    [Fact]
    public void Without_an_OMSI_folder_nothing_is_found()
    {
        Assert.Null(VehicleFolderIndex.Build(Path.Combine(_omsi, "missing")).Find("Cotterell"));
        Assert.Null(VehicleFolderIndex.Empty.Find("Cotterell"));
    }

    [Fact]
    public void Creating_a_folder_makes_it_findable_after_the_index_is_rebuilt()
    {
        string parent = Path.Combine(_omsi, "Vehicles", "SP_CustomDisplays");

        string made = VehicleFolderIndex.CreateFolder(parent, "Brand New");

        Assert.True(Directory.Exists(made));
        Assert.Equal("SP_CustomDisplays", VehicleFolderIndex.Build(_omsi).Find("Brand New"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("bad|name")]
    [InlineData("trailing.")]
    public void Names_that_are_not_a_single_valid_folder_name_are_rejected(string name)
    {
        Assert.False(VehicleFolderIndex.IsValidFolderName(name));
        Assert.Throws<IOException>(() => VehicleFolderIndex.CreateFolder(_omsi, name));
    }
}
