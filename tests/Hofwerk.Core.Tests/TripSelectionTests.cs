using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;
using Hofwerk.Localization;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

/// <summary>Choosing which trips of a map to take (HOF Suite's second import step), when making a new file and when importing into one.</summary>
[Trait("Area", "Omsi")]
public class TripSelectionTests
{
    // Three trips: two write their stops as names, one as station links; the map's Busstops.cfg lists a stop no trip uses.
    static MapData Map() => new()
    {
        Name = "Test",
        BusStops = ["Oakley", "Central", "Hill", "Lonely"],
        Destinations = ["Central", "Hill"],
        Trips =
        [
            new MapTrip("A", "Central", "28", ["Oakley", "Central"]),
            new MapTrip("B", "Hill", "30", ["Central", "Hill"]),
            new MapTrip("C", "Central", "28", ["Hill", "Central"], UsesStationLinks: true),
        ],
    };

    static TripSelection Picker(MapData map)
    {
        var picker = new TripSelection();
        picker.Load(map);
        return picker;
    }

    [Fact]
    public void Every_trip_starts_ticked_and_then_the_whole_map_is_taken_including_stops_only_Busstops_cfg_lists()
    {
        var map = Map();
        var picker = Picker(map);

        Assert.Equal(3, picker.SelectedCount);
        Assert.Same(map, picker.Apply(map));
        Assert.Contains("Lonely", picker.Apply(map)!.BusStops);
    }

    [Fact]
    public void Unticking_a_trip_leaves_out_its_route_and_the_stops_and_destinations_only_it_has()
    {
        var map = Map();
        var picker = Picker(map);

        picker.Trips[1].IsSelected = false;                  // B goes to Hill; Hill is also on C

        var chosen = picker.Apply(map)!;
        Assert.Equal(["A", "C"], chosen.Trips.Select(t => t.Name));
        Assert.Equal(["Oakley", "Central", "Hill"], chosen.BusStops);       // from the ticked trips only: "Lonely" is gone
        Assert.Equal(["Central"], chosen.Destinations);                      // nobody goes to Hill now
        Assert.Equal("2 of 3 trips", picker.Summary);
    }

    [Fact]
    public void Select_all_none_and_by_kind_tick_the_right_trips()
    {
        var picker = Picker(Map());
        Assert.True(picker.HasBothKinds);

        picker.SelectNoneCommand.Execute(null);
        Assert.Equal(0, picker.SelectedCount);

        picker.SelectStationLinksCommand.Execute(null);
        Assert.Equal(["C"], picker.Trips.Where(t => t.IsSelected).Select(t => t.Name));

        picker.SelectPlainCommand.Execute(null);
        Assert.Equal(["A", "B"], picker.Trips.Where(t => t.IsSelected).Select(t => t.Name));

        picker.SelectAllCommand.Execute(null);
        Assert.Equal(3, picker.SelectedCount);
    }

    [Fact]
    public void A_choice_changes_the_picker_once_however_many_trips_it_ticks()
    {
        var picker = Picker(Map());
        int changes = 0;
        picker.Changed += () => changes++;

        picker.SelectNoneCommand.Execute(null);

        Assert.Equal(1, changes);                             // the dialog works out what would be imported once, not once per trip
    }

    [Fact]
    public void A_trip_is_described_by_its_line_destination_and_stops()
    {
        var choice = new TripChoice(new MapTrip("A", "Central", "28", ["Oakley", "Central"]));

        Assert.Equal("Line 28 to Central, 2 stops", choice.Detail);
        using var german = Localizer.Override("de");
        Assert.Equal("Linie 28 nach Central, 2 Haltestellen", choice.Detail);
    }

    // ---- in the two dialogs ----

    [Fact]
    public void The_new_file_dialog_makes_the_file_from_the_ticked_trips_only()
    {
        var map = Map();
        var vm = new NewFileViewModel([new OmsiMap("Test", "C:\\x")], _ => map) { FromMap = true };
        vm.AddMapFolder(Path.GetTempPath());                  // a map is chosen and read
        SpinWait.SpinUntil(() => vm.Data is not null, TimeSpan.FromSeconds(5));
        vm.TripPicker.Trips[0].IsSelected = false;
        vm.TripPicker.Trips[1].IsSelected = false;

        var plan = vm.ToPlan();

        Assert.Equal(["C"], plan.Map!.Trips.Select(t => t.Name));
        Assert.True(vm.CanContinue);

        vm.TripPicker.SelectNoneCommand.Execute(null);
        Assert.False(vm.CanContinue);                         // nothing ticked: nothing to make
        Assert.Equal("Tick at least one trip.", vm.Summary);
    }

    [Fact]
    public void The_import_dialog_adds_only_the_ticked_trips_to_the_open_file()
    {
        var map = Map();
        var doc = new HofDocument { TerminusStringCount = 1 };
        var vm = new ImportViewModel([new OmsiMap("Test", "C:\\x")], (data, options) => MapImporter.Preview(doc, data, options), _ => map);
        vm.AddMapFolder(Path.GetTempPath());
        SpinWait.SpinUntil(() => vm.Data is not null, TimeSpan.FromSeconds(5));

        Assert.Equal(3, vm.Result!.NewRoutes);
        vm.TripPicker.SelectNoneCommand.Execute(null);
        vm.TripPicker.Trips[0].IsSelected = true;             // A: Oakley to Central

        Assert.Equal(1, vm.Result!.NewRoutes);
        Assert.Equal(2, vm.Result.NewBusStops);               // Oakley and Central, not Hill or Lonely
        Assert.Equal(1, vm.Result.NewDestinations);
        Assert.True(vm.CanImport);

        vm.TripPicker.SelectNoneCommand.Execute(null);
        Assert.False(vm.CanImport);
        Assert.Equal("Tick at least one trip.", vm.Summary);
    }

    [Fact]
    public void A_single_timetable_file_is_not_a_choice()
    {
        var vm = new ImportViewModel([], (data, options) => new MergeResult(0, 0, 0, 0, 1, 0), readTrip: _ => Map());
        vm.SetTripFile("x.ttp");

        Assert.False(vm.TripPicker.HasTrips);                 // the dialog offers the choice for a whole map only
    }
}
