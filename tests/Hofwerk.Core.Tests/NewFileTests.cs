using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class NewFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-newfile-" + Guid.NewGuid().ToString("N"));

    public NewFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static HofDocument NewDoc() => HofFile.CreateNew("Cotterell", 26, 9);

    [Fact]
    public void A_new_file_says_it_was_made_by_Hofwerk_and_links_to_GitHub()
    {
        string text = HofWriter.ToText(NewDoc());

        Assert.Contains("Made with Hofwerk", text);
        Assert.Contains("https://github.com/Kalmai221/Hofwerk", text);
        Assert.DoesNotContain("Created by Hofwerk", text);   // only the credit box, no long explanation
        Assert.DoesNotContain("You do not have to edit it by hand", text);
        Assert.StartsWith("*****", text);
        Assert.All(text, c => Assert.True(c < 128, $"non-ASCII character {c} in the header or notes"));
    }

    [Fact]
    public void A_new_file_has_notes_that_explain_each_part_in_plain_words()
    {
        string text = HofWriter.ToText(NewDoc());

        Assert.Contains("* Destinations: what the bus display shows.", text);
        Assert.Contains("* How many text strings every destination and every bus stop has below", text);
        Assert.Contains("* Settings for the bus pack", text);
    }

    [Fact]
    public void The_credit_box_lines_are_all_the_same_width_and_hold_the_link()
    {
        var box = HofHeader.Lines();

        Assert.Equal(4, box.Count);
        Assert.Single(box.Select(l => l.Length).Distinct());
        Assert.Contains(box, l => l.Contains(HofHeader.Url));
    }

    [Fact]
    public void Nothing_Hofwerk_writes_can_be_mistaken_for_one_of_OMSIs_markers()
    {
        var doc = NewDoc();
        doc.BusStops.Add(new BusStop { Name = "Totnes", Strings = Enumerable.Repeat("", 9).ToList() });
        doc.Routes.Add(new Route { Ident = "101", Name = "A1", TerminusIdent = "1000", Line = "1", Stops = [new RouteStop { Name = "Totnes" }] });
        var lines = HofWriter.ToLines(doc);

        // Every line that starts in column 0 with "[" or is a string count is a real marker; the notes and the opening text never are.
        var notes = lines.Where(l => l.StartsWith('*')).Concat(HofHeader.Lines()).ToList();
        Assert.All(notes, l => Assert.False(l.TrimStart().StartsWith('[') || l.StartsWith("stringcount", StringComparison.OrdinalIgnoreCase), l));
        Assert.DoesNotContain(notes, l => l.Contains("addterminus") || l.Contains("addbusstop") || l.Contains("infosystem"));
    }

    [Fact]
    public void Notes_stay_between_entries_never_in_front_of_the_first_marker()
    {
        var lines = HofWriter.ToLines(NewDoc());
        int firstMarker = lines.FindIndex(l => l == "[name]");
        var openingText = lines.Take(firstMarker).ToList();

        Assert.Equal(HofHeader.Lines(), openingText.Take(HofHeader.Lines().Count));
        Assert.DoesNotContain(openingText.Skip(HofHeader.Lines().Count), l => l.StartsWith("* "));   // only the opening text itself is up there
    }

    [Fact]
    public void A_new_file_reads_back_cleanly_and_saving_it_again_changes_nothing()
    {
        var doc = NewDoc();
        string text = HofWriter.ToText(doc);

        var again = HofParser.Parse(text.Split("\r\n"));

        Assert.Empty(again.Diagnostics);
        Assert.True(again.HelpComments);
        Assert.Equal("Cotterell", again.Name);
        Assert.Equal("Not in service", again.ServiceTrip);
        Assert.Equal(6, again.GlobalStrings.Count);
        var terminus = Assert.Single(again.Termini);
        Assert.True(terminus.IsAllExit);
        Assert.Equal(26, terminus.Strings.Count);
        Assert.Equal(text, HofWriter.ToText(again));       // no notes pile up, nothing is lost
        Assert.Equal(text, HofWriter.ToText(HofParser.Parse(HofWriter.ToText(again).Split("\r\n"))));
    }

    [Fact]
    public void A_file_from_someone_else_gets_no_notes_and_no_credit()
    {
        string text = "Written by someone else.\r\n\r\n[name]\r\nDepot\r\n\r\n[servicetrip]\r\nEmpty\r\n\r\nstringcount_terminus\r\n0\r\n\r\nstringcount_busstop\r\n0\r\n\r\n";
        var doc = HofParser.Parse(text.Split("\r\n"));

        Assert.False(doc.HelpComments);
        string written = HofWriter.ToText(doc);
        Assert.DoesNotContain("Hofwerk", written);
        Assert.DoesNotContain("\r\n* ", written);
        Assert.StartsWith("Written by someone else.", written);
    }

    MainViewModel NewViewModel() => new(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets"));

    [Fact]
    public void The_view_model_creates_a_new_file_named_after_it_and_opens_it()
    {
        var vm = NewViewModel();
        string path = Path.Combine(_dir, "My Depot.hof");

        Assert.True(vm.CreateNew(path));

        Assert.True(File.Exists(path));
        Assert.Equal(path, vm.FilePath);
        Assert.Equal("My Depot", vm.Name);
        Assert.Single(vm.Termini);
        Assert.Equal("Created My Depot.hof", vm.Status);
        Assert.False(vm.IsDirty);
        Assert.True(vm.TerminusStringCount > 0);
        Assert.Equal(vm.TerminusStringCount, vm.Termini[0].Strings.Count);
        Assert.Contains("Made with Hofwerk", File.ReadAllText(path));
    }

    [Fact]
    public void Editing_and_saving_a_file_Hofwerk_made_keeps_its_credit_and_notes_without_repeating_them()
    {
        var vm = NewViewModel();
        string path = Path.Combine(_dir, "depot.hof");
        Assert.True(vm.CreateNew(path));

        vm.AddBusStopCommand.Execute(null);
        vm.Save(path);
        vm.AddRouteCommand.Execute(null);
        vm.Save(path);
        vm.Save(path);

        string text = File.ReadAllText(path);
        Assert.Contains("https://github.com/Kalmai221/Hofwerk", text);
        Assert.Equal(1, Count(text, "* Bus stops:"));
        Assert.Equal(1, Count(text, "* Routes:"));
        Assert.Equal(1, Count(text, "Made with Hofwerk"));
        Assert.Empty(HofFile.Load(path).Diagnostics);
    }

    static int Count(string text, string needle) => (text.Length - text.Replace(needle, "").Length) / needle.Length;
}
