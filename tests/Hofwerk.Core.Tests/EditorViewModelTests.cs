using System.Collections.ObjectModel;
using System.Text;
using Hofwerk.Core;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "HofFiles")]
public class FilteredCollectionTests
{
    sealed record Item(string Name);

    static FilteredCollection<Item> Make(ObservableCollection<Item> source) =>
        new(source, (i, s) => i.Name.Contains(s, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Shows_everything_until_there_is_search_text_then_only_matches_in_the_same_order()
    {
        var source = new ObservableCollection<Item> { new("Oakley"), new("Bus Station"), new("Bathwick Hill"), new("Bus Bay") };
        var f = Make(source);
        Assert.Equal(4, f.View.Count);

        f.Text = "bus";
        Assert.Equal(["Bus Station", "Bus Bay"], f.View.Select(i => i.Name));

        f.Text = "";
        Assert.Equal(["Oakley", "Bus Station", "Bathwick Hill", "Bus Bay"], f.View.Select(i => i.Name));
    }

    [Fact]
    public void Follows_additions_removals_and_moves_in_the_source()
    {
        var source = new ObservableCollection<Item> { new("A1"), new("B1") };
        var f = Make(source);
        f.Text = "1";

        var added = new Item("C1");
        source.Insert(1, added);
        Assert.Equal(["A1", "C1", "B1"], f.View.Select(i => i.Name));

        source.Remove(added);
        source.Move(0, 1);
        Assert.Equal(["B1", "A1"], f.View.Select(i => i.Name));
    }

    [Fact]
    public void Items_that_stay_visible_are_the_same_objects_so_a_list_keeps_its_selection()
    {
        var keep = new Item("Keep");
        var source = new ObservableCollection<Item> { new("Drop"), keep, new("Keep too") };
        var f = Make(source);

        f.Text = "keep";

        Assert.Same(keep, f.View[0]);
        Assert.Equal(2, f.View.Count);
    }
}

[Trait("Area", "HofFiles")]
public class EditorViewModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-editorvm-" + Guid.NewGuid().ToString("N"));

    public EditorViewModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string WriteHof()
    {
        string path = Path.Combine(_dir, "test.hof");
        File.WriteAllText(path,
            "[name]\nDepot\n\n[servicetrip]\nUniversity\n\nstringcount_terminus\n1\n\nstringcount_busstop\n0\n\n" +
            "[addterminus]\n1001\nUniversity\nUniversity\n\n[addterminus]\n1002\nBus Station\nBus Station\n\n" +
            "[addbusstop]\nBus Station\n\n[addbusstop]\nOakley\n\n[addbusstop]\nThe Pavillion\n\n" +
            "[infosystem_trip]\n1801\nU18 Ctr-Uni\n1001\n18\n\n[infosystem_busstop_list]\n3\nBus Station\nOakley\nNowhere Lane\n\n" +
            "[infosystem_trip]\n1802\nU18 Uni-Ctr\n1002\n18\n\n[infosystem_busstop_list]\n1\nOakley\n",
            Hofwerk.Core.Model.HofEncodings.Windows1252);
        return path;
    }

    MainViewModel Open(string? omsi = null)
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        if (omsi is not null) settings.OmsiFolder = omsi;
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        Assert.True(vm.Open(WriteHof()));
        return vm;
    }

    static string[] Names(RouteViewModel route) => route.Stops.Select(s => s.Name).ToArray();

    // ---- searching the lists ----

    [Fact]
    public void Each_list_can_be_searched()
    {
        var vm = Open();

        vm.TerminusFilter = "bus st";
        Assert.Equal(["1002"], vm.FilteredTermini.Select(t => t.Ident));

        vm.BusStopFilter = "pav";
        Assert.Equal(["The Pavillion"], vm.FilteredBusStops.Select(b => b.Name));

        vm.RouteFilter = "uni-ctr";
        Assert.Equal(["1802"], vm.FilteredRoutes.Select(r => r.Ident));

        vm.StopPickerFilter = "oak";
        Assert.Equal(["Oakley"], vm.StopPickerStops.Select(b => b.Name));
    }

    [Fact]
    public void Adding_an_entry_clears_the_search_so_the_new_entry_is_visible()
    {
        var vm = Open();
        vm.BusStopFilter = "pav";

        vm.AddBusStopCommand.Execute(null);

        Assert.Equal("", vm.BusStopFilter);
        Assert.Contains(vm.SelectedBusStop, vm.FilteredBusStops);
    }

    // ---- picking route stops ----

    [Fact]
    public void A_stop_picked_from_the_list_is_added_to_the_selected_route_and_the_file_is_marked_changed()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[1];                       // 1802: Oakley
        var pavillion = vm.BusStops.Single(b => b.Name == "The Pavillion");

        vm.AddStopToRoute(pavillion);

        Assert.Equal(["Oakley", "The Pavillion"], Names(vm.Routes[1]));
        Assert.Equal("The Pavillion", vm.Routes[1].SelectedStop!.Name);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void A_new_stop_goes_after_the_selected_stop()
    {
        var vm = Open();
        var route = vm.Routes[0];                              // Bus Station, Oakley, Nowhere Lane
        vm.SelectedRoute = route;
        route.SelectedStop = route.Stops[0];

        vm.AddStopToRoute(vm.BusStops.Single(b => b.Name == "The Pavillion"));

        Assert.Equal(["Bus Station", "The Pavillion", "Oakley", "Nowhere Lane"], Names(route));
    }

    [Fact]
    public void The_same_stop_can_be_on_a_route_twice()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[1];
        var oakley = vm.BusStops.Single(b => b.Name == "Oakley");

        vm.AddStopToRoute(oakley);

        Assert.Equal(["Oakley", "Oakley"], Names(vm.Routes[1]));
    }

    [Fact]
    public void Nothing_happens_when_no_route_is_selected()
    {
        var vm = Open();
        vm.SelectedRoute = null;

        vm.AddStopToRoute(vm.BusStops[0]);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Route_stops_can_be_moved_removed_and_cleared()
    {
        var vm = Open();
        var route = vm.Routes[0];
        route.SelectedStop = route.Stops[2];                   // Nowhere Lane

        route.MoveStopUpCommand.Execute(null);
        Assert.Equal(["Bus Station", "Nowhere Lane", "Oakley"], Names(route));
        Assert.Equal("Nowhere Lane", route.SelectedStop!.Name);

        route.MoveStopDownCommand.Execute(null);
        route.MoveStopDownCommand.Execute(null);               // already last: stays put
        Assert.Equal(["Bus Station", "Oakley", "Nowhere Lane"], Names(route));

        route.RemoveStopCommand.Execute(null);
        Assert.Equal(["Bus Station", "Oakley"], Names(route));
        Assert.Equal("Oakley", route.SelectedStop!.Name);      // the selection moves to a neighbour

        route.ClearStopsCommand.Execute(null);
        Assert.Empty(route.Stops);
        Assert.Equal("0 stops", route.StopSummary);
    }

    // ---- stops that are not defined ----

    [Fact]
    public void A_route_stop_without_a_bus_stop_of_that_name_is_flagged()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[0];

        Assert.Equal([true, true, false], vm.Routes[0].Stops.Select(s => s.IsKnown));
        Assert.Equal("1 stop on this route is not in your bus stop list.", vm.RouteWarning);
    }

    [Fact]
    public void Defining_the_missing_bus_stop_clears_the_flag()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[0];

        vm.AddBusStopCommand.Execute(null);
        vm.SelectedBusStop!.Name = "Nowhere Lane";

        Assert.All(vm.Routes[0].Stops, s => Assert.True(s.IsKnown));
        Assert.Equal("", vm.RouteWarning);
    }

    [Fact]
    public void Stop_names_match_in_any_letter_case()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[1];

        vm.Routes[1].Stops[0].Name = "OAKLEY";
        vm.RefreshKnownStops();

        Assert.True(vm.Routes[1].Stops[0].IsKnown);
    }

    [Fact]
    public void Renaming_a_bus_stop_renames_it_on_the_routes_that_use_it()
    {
        var vm = Open();
        var oakley = vm.BusStops.Single(b => b.Name == "Oakley");

        oakley.Name = "Oakley Road";

        Assert.Equal(["Bus Station", "Oakley Road", "Nowhere Lane"], Names(vm.Routes[0]));
        Assert.Equal(["Oakley Road"], Names(vm.Routes[1]));
        Assert.True(vm.Routes[1].Stops[0].IsKnown);
    }

    [Fact]
    public void Renaming_while_a_file_loads_does_not_touch_routes()
    {
        var vm = Open();
        Assert.False(vm.IsDirty);
        Assert.Equal(["Bus Station", "Oakley", "Nowhere Lane"], Names(vm.Routes[0]));
    }

    [Fact]
    public void A_bus_stop_says_how_many_routes_use_it()
    {
        var vm = Open();

        vm.SelectedBusStop = vm.BusStops.Single(b => b.Name == "Oakley");
        Assert.Equal("Used in 2 routes", vm.BusStopUsage);

        vm.SelectedBusStop = vm.BusStops.Single(b => b.Name == "Bus Station");
        Assert.Equal("Used in 1 route", vm.BusStopUsage);

        vm.SelectedBusStop = vm.BusStops.Single(b => b.Name == "The Pavillion");
        Assert.Equal("Not used in any route", vm.BusStopUsage);
    }

    [Fact]
    public void Saving_writes_the_stops_in_the_new_order()
    {
        var vm = Open();
        var route = vm.Routes[0];
        route.SelectedStop = route.Stops[2];
        route.MoveStopUpCommand.Execute(null);
        string copy = Path.Combine(_dir, "saved.hof");

        vm.Save(copy);

        string text = File.ReadAllText(copy, Hofwerk.Core.Model.HofEncodings.Windows1252);
        Assert.Contains("[infosystem_busstop_list]\r\n3\r\nBus Station\r\nNowhere Lane\r\nOakley\r\n", text);
    }

    // ---- duplicate ----

    [Fact]
    public void Duplicating_a_route_copies_its_stops_under_a_new_code()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[0];

        vm.DuplicateRouteCommand.Execute(null);

        Assert.Equal(3, vm.Routes.Count);
        var copy = vm.SelectedRoute!;
        Assert.NotEqual("1801", copy.Ident);
        Assert.Equal("U18 Ctr-Uni (copy)", copy.Name);
        Assert.Equal(Names(vm.Routes[0]), Names(copy));
        Assert.NotSame(vm.Routes[0].Stops[0], copy.Stops[0]);   // editing the copy does not change the original
    }

    [Fact]
    public void Duplicating_a_destination_and_a_bus_stop_works_too()
    {
        var vm = Open();
        vm.SelectedTerminus = vm.Termini[0];
        vm.DuplicateTerminusCommand.Execute(null);
        Assert.Equal(3, vm.Termini.Count);
        Assert.NotEqual("1001", vm.SelectedTerminus!.Ident);
        Assert.Equal("University", vm.SelectedTerminus.Station);

        vm.SelectedBusStop = vm.BusStops[1];
        vm.DuplicateBusStopCommand.Execute(null);
        Assert.Equal(4, vm.BusStops.Count);
        Assert.Equal("Oakley (copy)", vm.SelectedBusStop!.Name);
    }

    // ---- route destination ----

    [Fact]
    public void A_routes_destination_is_chosen_from_the_destinations()
    {
        var vm = Open();
        vm.SelectedRoute = vm.Routes[0];
        vm.RefreshRouteData();

        Assert.Equal(["1001: University", "1002: Bus Station"], vm.DestinationChoices.Select(c => c.Display));
        Assert.Equal("1001", vm.SelectedRouteDestination?.Ident);

        vm.SelectedRouteDestination = vm.DestinationChoices[1];

        Assert.Equal("1002", vm.Routes[0].TerminusIdent);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void A_destination_code_that_does_not_exist_is_kept_and_shown_as_such()
    {
        var vm = Open();
        vm.Routes[0].TerminusIdent = "9999";
        vm.SelectedRoute = vm.Routes[0];
        vm.RefreshRouteData();

        Assert.Equal("9999", vm.SelectedRouteDestination?.Ident);
        Assert.Contains("not in the destination list", vm.SelectedRouteDestination!.Display);
    }

    // ---- file path ----

    [Fact]
    public void The_file_path_starts_at_Vehicles_when_the_file_is_inside_the_OMSI_folder()
    {
        string omsi = Path.Combine(_dir, "OMSI 2");
        string folder = Path.Combine(omsi, "Vehicles", "[SP] Studio Polygon 400MMC");
        Directory.CreateDirectory(Path.Combine(omsi, "Maps"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "[HOF] Cotterell.hof");
        File.Copy(WriteHof(), file);

        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        settings.OmsiFolder = omsi;
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        vm.Open(file);

        Assert.Equal(Path.Combine("Vehicles", "[SP] Studio Polygon 400MMC", "[HOF] Cotterell.hof"), vm.FileDisplayPath);
        Assert.Equal(Path.Combine("Vehicles", "[SP] Studio Polygon 400MMC"), vm.FileFolder);
    }

    [Fact]
    public void A_file_outside_the_OMSI_folder_shows_its_full_path()
    {
        string omsi = Path.Combine(_dir, "OMSI 2");
        Directory.CreateDirectory(Path.Combine(omsi, "Maps"));
        Directory.CreateDirectory(Path.Combine(omsi, "Vehicles"));
        var vm = Open(omsi);

        Assert.Equal(Path.Combine(_dir, "test.hof"), vm.FileDisplayPath);
    }
}
