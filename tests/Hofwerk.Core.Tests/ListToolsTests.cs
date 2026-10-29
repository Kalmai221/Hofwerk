using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Localization;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "HofFiles")]
public class ListToolsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-listtools-" + Guid.NewGuid().ToString("N"));

    public ListToolsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // Four destinations in no order, three bus stops and three routes, and one route with four stops.
    MainViewModel Open()
    {
        string path = Path.Combine(_dir, "test.hof");
        File.WriteAllText(path,
            "[name]\nDepot\n\n[servicetrip]\nUniversity\n\nstringcount_terminus\n2\n\nstringcount_busstop\n0\n\n" +
            "[addterminus]\n10\nTen\nTen\n\n[addterminus]\n9\nNine\nNine\n\n[addterminus]\n1000\nAddplate\nAddplate\n\n[addterminus]\n700\nAardvark\nAardvark\n\n" +
            "[addbusstop]\nOakley\n\n[addbusstop]\nBus Station\n\n[addbusstop]\nCentral\n\n" +
            "[infosystem_trip]\n1803\nC route\n10\n18\n\n[infosystem_busstop_list]\n4\nA\nB\nC\nD\n\n" +
            "[infosystem_trip]\n1801\nA route\n9\n18\n\n[infosystem_busstop_list]\n1\nOakley\n\n" +
            "[infosystem_trip]\n1802\nB route\n700\n18\n\n[infosystem_busstop_list]\n1\nOakley\n",
            HofEncodings.Windows1252);
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets")) { EditSettleTime = TimeSpan.Zero };
        Assert.True(vm.Open(path));
        return vm;
    }

    [Fact]
    public void Codes_are_sorted_as_numbers_and_the_file_counts_as_changed()
    {
        var vm = Open();
        Assert.False(vm.IsDirty);

        vm.SortTerminiByCodeCommand.Execute(null);

        Assert.Equal(["9", "10", "700", "1000"], vm.Termini.Select(t => t.Ident));      // not "10", "1000", "700", "9"
        Assert.True(vm.IsDirty);
        vm.SortRoutesByCodeCommand.Execute(null);
        Assert.Equal(["1801", "1802", "1803"], vm.Routes.Select(r => r.Ident));
    }

    [Fact]
    public void Names_are_sorted_in_any_letter_case_and_each_list_follows()
    {
        var vm = Open();
        vm.BusStopFilter = "o";                                // a search is cleared so every entry is in the order

        vm.SortBusStopsByNameCommand.Execute(null);

        Assert.Equal(["Bus Station", "Central", "Oakley"], vm.BusStops.Select(b => b.Name));
        Assert.Equal("", vm.BusStopFilter);
        Assert.Equal(["Bus Station", "Central", "Oakley"], vm.FilteredBusStops.Select(b => b.Name));
        vm.SortTerminiByNameCommand.Execute(null);
        Assert.Equal(["Aardvark", "Addplate", "Nine", "Ten"], vm.Termini.Select(t => t.Strings[0].Value));
        vm.SortRoutesByNameCommand.Execute(null);
        Assert.Equal(["A route", "B route", "C route"], vm.Routes.Select(r => r.Name));
    }

    [Fact]
    public void Sorting_can_be_undone()
    {
        var vm = Open();
        var before = vm.Termini.Select(t => t.Ident).ToList();

        vm.SortTerminiByCodeCommand.Execute(null);
        vm.CommitEdit();
        vm.Undo();

        Assert.Equal(before, vm.Termini.Select(t => t.Ident));
    }

    [Fact]
    public void The_selected_entry_moves_one_place_and_stays_selected_and_the_ends_stay_put()
    {
        var vm = Open();
        vm.SelectedTerminus = vm.Termini[1];                  // 9

        vm.MoveTerminusUpCommand.Execute(null);
        Assert.Equal(["9", "10", "1000", "700"], vm.Termini.Select(t => t.Ident));
        Assert.Equal("9", vm.SelectedTerminus!.Ident);

        vm.MoveTerminusUpCommand.Execute(null);               // already first
        Assert.Equal("9", vm.Termini[0].Ident);

        vm.SelectedRoute = vm.Routes[2];
        vm.MoveRouteDownCommand.Execute(null);                // already last
        Assert.Equal(["1803", "1801", "1802"], vm.Routes.Select(r => r.Ident));
        vm.MoveRouteUpCommand.Execute(null);
        Assert.Equal(["1803", "1802", "1801"], vm.Routes.Select(r => r.Ident));
    }

    [Fact]
    public void Reversing_a_route_turns_its_stops_around_and_keeps_the_selected_one()
    {
        var vm = Open();
        var route = vm.Routes[0];
        route.SelectedStop = route.Stops[1];                  // B

        route.ReverseStopsCommand.Execute(null);

        Assert.Equal(["D", "C", "B", "A"], route.Stops.Select(s => s.Name));
        Assert.Equal("B", route.SelectedStop!.Name);
        Assert.True(vm.IsDirty);
        route.ReverseStopsCommand.Execute(null);
        Assert.Equal(["A", "B", "C", "D"], route.Stops.Select(s => s.Name));
    }

    [Fact]
    public void The_add_plate_destination_is_added_once_and_then_found_again()
    {
        var vm = Open();
        vm.Termini.Remove(vm.Termini.Single(t => t.Ident == "1000"));

        vm.AddAddPlateDestinationCommand.Execute(null);
        Assert.Equal("1000", vm.Termini[^1].Ident);
        Assert.Equal("1000", vm.SelectedTerminus!.Ident);
        Assert.Equal(2, vm.Termini[^1].Strings.Count);        // the file's number of strings

        int count = vm.Termini.Count;
        vm.AddAddPlateDestinationCommand.Execute(null);        // there is one now: no second one
        Assert.Equal(count, vm.Termini.Count);
    }

    [Fact]
    public void The_KPP_set_has_an_Options_destination_and_a_copy_of_each_destination_under_1000_and_can_be_pressed_again()
    {
        var vm = Open();                                      // 10, 9, 1000, 700

        vm.AddKppDestinationsCommand.Execute(null);

        var idents = vm.Termini.Select(t => t.Ident).ToList();
        Assert.Contains("14990", idents);
        Assert.Contains("20009", idents);
        Assert.Contains("20010", idents);
        Assert.Contains("20700", idents);
        Assert.DoesNotContain("21000", idents);               // 1000 and over are not copied
        Assert.Equal(4 + 4, idents.Count);
        Assert.Equal("Options", vm.Termini.Single(t => t.Ident == "14990").Strings[0].Value);
        Assert.Equal("Nine", vm.Termini.Single(t => t.Ident == "20009").Strings[0].Value);      // a copy keeps the text

        vm.AddKppDestinationsCommand.Execute(null);
        Assert.Equal(8, vm.Termini.Count);                    // nothing twice
        Assert.Equal("The KPP destinations are already there.", vm.Status);
    }

    [Fact]
    public void The_messages_follow_the_language()
    {
        var vm = Open();
        using var german = Localizer.Override("de");

        vm.SortTerminiByCodeCommand.Execute(null);

        Assert.Equal("Ziele nach Code sortiert.", vm.Status);
    }
}
