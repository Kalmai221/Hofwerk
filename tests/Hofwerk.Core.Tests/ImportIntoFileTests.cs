using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Omsi")]
public class ImportIntoFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-import-" + Guid.NewGuid().ToString("N"));
    string Omsi => Path.Combine(_dir, "OMSI 2");

    public ImportIntoFileTests()
    {
        Directory.CreateDirectory(Path.Combine(Omsi, "Vehicles"));
        Directory.CreateDirectory(Path.Combine(Omsi, "Maps"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static string Station(string name) => $"[station]\r\n1234\r\n10\r\n{name}\r\n5\r\n-1.0\r\n100.0\r\n20.0\r\n1.0\r\n\r\n";

    static string Trip(string internalName, string destination, string line, params string[] stations) =>
        "-----------------------\r\nTime Table Trip File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.3.004\r\n\r\n"
        + $"[trip]\r\n{internalName}\r\n{destination}\r\n{line}\r\n\r\n.........................\r\n        Stations\r\n.........................\r\n\r\n"
        + string.Concat(stations.Select(Station))
        + ".........................\r\n        Profiles\r\n.........................\r\n\r\n[profile]\r\nstandard\r\n4.000\r\n";

    string WriteTrip(string map, string file, string text)
    {
        string dir = Path.Combine(Omsi, "Maps", map, "TTData");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, file);
        File.WriteAllBytes(path, HofEncodings.Windows1252.GetBytes(text));
        return path;
    }

    /// <summary>A map with two trips: U28 (3 stops, to University) and Return (2 of the same stops, to City Centre).</summary>
    string TwoTripMap()
    {
        WriteTrip("Test", "U28.ttp", Trip("AI_U28", "University", "U28", "St Mary's Church", "Sydney Buildings", "Widcombe Lane"));
        WriteTrip("Test", "Return.ttp", Trip("Return", "City Centre", "U28", "Widcombe Lane", "Sydney Buildings"));
        return Path.Combine(Omsi, "Maps", "Test");
    }

    HofDocument Depot(params (string Station, string Ident)[] destinations)
    {
        var doc = HofFile.CreateNew("Depot", 2, 1);
        foreach (var (station, ident) in destinations) doc.Termini.Add(new Terminus { Ident = ident, Station = station, Strings = ["", ""] });
        return doc;
    }

    // ---- merging into what a file already has -------------------------------------------------------------------

    [Fact]
    public void A_map_is_added_to_a_file_that_already_has_things_and_nothing_is_added_twice()
    {
        var map = MapImporter.Read(TwoTripMap());
        var doc = Depot(("University", "1500"));
        doc.BusStops.Add(new BusStop { Name = "sydney buildings", Strings = [""] });      // letter case does not matter
        doc.Routes.Add(new Route { Ident = "2801", Name = "return", Line = "28", TerminusIdent = "1500" });

        var result = MapImporter.Fill(doc, map, new MapImportOptions());

        Assert.Equal(new MergeResult(NewBusStops: 2, KnownBusStops: 1, NewDestinations: 1, KnownDestinations: 1, NewRoutes: 1, KnownRoutes: 1), result);
        Assert.Equal(2, doc.Termini.Count(t => t.Station is "University" or "City Centre"));    // University was already there
        Assert.Single(doc.Termini, t => t.Station == "University");
        Assert.Equal("1500", doc.Routes.Single(r => r.Name == "U28").TerminusIdent);            // the new route uses the code the file already had
        Assert.Equal(3, doc.BusStops.Count - 0);                                                  // the 3 stops of the map; "Sydney Buildings" not doubled
        Assert.Equal(2, doc.Routes.Count);
    }

    [Fact]
    public void New_codes_never_collide_with_codes_the_file_already_uses()
    {
        var map = MapImporter.Read(TwoTripMap());
        var doc = Depot(("Somewhere Else", "1001"), ("Another", "1002"));
        doc.Routes.Add(new Route { Ident = "2801", Name = "old", Line = "28", TerminusIdent = "1001" });

        MapImporter.Fill(doc, map, new MapImportOptions());

        Assert.Equal(doc.Termini.Count, doc.Termini.Select(t => t.Ident).Distinct().Count());
        Assert.Equal(doc.Routes.Count, doc.Routes.Select(r => r.Ident).Distinct().Count());
        Assert.DoesNotContain(doc.Routes.Skip(1), r => r.Ident == "2801");
    }

    [Fact]
    public void Importing_the_same_map_again_adds_nothing()
    {
        var map = MapImporter.Read(TwoTripMap());
        var doc = Depot();
        MapImporter.Fill(doc, map, new MapImportOptions());

        var again = MapImporter.Fill(doc, map, new MapImportOptions());

        Assert.Equal(0, again.Added);
        Assert.Equal("Nothing new to add; 3 bus stops, 2 destinations and 2 routes are already in the file.", again.Describe());
    }

    [Fact]
    public void Only_the_ticked_parts_are_added()
    {
        var map = MapImporter.Read(TwoTripMap());
        var doc = Depot();

        var result = MapImporter.Fill(doc, map, new MapImportOptions(BusStops: true, Destinations: false, Routes: false));

        Assert.Equal(3, result.NewBusStops);
        Assert.Equal(0, result.NewRoutes);
        Assert.Empty(doc.Routes);
        Assert.Single(doc.Termini);       // only the not-in-service entry a new file starts with
    }

    [Fact]
    public void The_preview_says_what_would_happen_and_leaves_the_file_alone()
    {
        var map = MapImporter.Read(TwoTripMap());
        var doc = Depot();
        int termini = doc.Termini.Count;

        var result = MapImporter.Preview(doc, map, new MapImportOptions());

        Assert.Equal(3, result.NewBusStops);
        Assert.Equal(termini, doc.Termini.Count);
        Assert.Empty(doc.BusStops);
        Assert.Empty(doc.Routes);
    }

    [Fact]
    public void The_result_is_described_in_plain_words()
    {
        Assert.Equal("12 bus stops, 2 destinations and 8 routes will be added; 3 bus stops and 1 route are already in the file.",
            new MergeResult(12, 3, 2, 0, 8, 1).Describe());
        Assert.Equal("1 bus stop added.", new MergeResult(1, 0, 0, 0, 0, 0).Describe(past: true));
        Assert.Equal("2 routes will be added; 1 route is already in the file.", new MergeResult(0, 0, 0, 0, 2, 1).Describe());
    }

    // ---- one timetable file --------------------------------------------------------------------------------------

    [Fact]
    public void A_single_timetable_file_gives_one_trip_with_its_stops_and_destination()
    {
        string file = WriteTrip("Test", "U28.ttp", Trip("AI_U28", "University", "U28", "A", "B", "A"));

        var data = MapImporter.ReadTripFile(file)!;

        Assert.Equal("U28", data.Name);
        Assert.Equal(["A", "B"], data.BusStops);
        Assert.Equal(["University"], data.Destinations);
        Assert.Single(data.Trips);
    }

    [Fact]
    public void A_file_that_is_not_a_timetable_is_not_taken()
    {
        string file = Path.Combine(_dir, "notes.ttp");
        File.WriteAllText(file, "just some notes\nnothing here");

        Assert.Null(MapImporter.ReadTripFile(file));
    }

    // ---- in the app ----------------------------------------------------------------------------------------------

    MainViewModel Open()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        settings.OmsiFolder = Omsi;
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        vm.EditSettleTime = TimeSpan.Zero;
        string file = Path.Combine(Omsi, "Vehicles", "depot.hof");
        HofFile.Save(Depot(("University", "1500")), file);
        Assert.True(vm.Open(file));
        return vm;
    }

    static async Task<ImportViewModel> Choose(ImportViewModel import, OmsiMap map)
    {
        import.SelectedMap = map;
        for (int i = 0; i < 200 && (import.IsReading || import.Data is null); i++) await Task.Delay(10);
        return import;
    }

    [Fact]
    public async Task The_dialog_shows_what_would_be_added_and_what_is_already_in_the_file()
    {
        TwoTripMap();
        var vm = Open();
        var import = vm.StartImport()!;

        Assert.Equal("Choose a map.", import.Summary);
        await Choose(import, import.Maps.Single());

        Assert.True(import.CanImport);
        Assert.Equal("3 bus stops, 1 destination and 2 routes will be added; 1 destination is already in the file.", import.Summary);

        import.ImportRoutes = false;
        import.ImportDestinations = false;
        Assert.Equal("3 bus stops will be added.", import.Summary);

        import.ImportBusStops = false;
        Assert.Equal("Tick what to take.", import.Summary);
        Assert.False(import.CanImport);
    }

    [Fact]
    public async Task Importing_adds_to_the_open_file_as_one_step_that_Undo_takes_back()
    {
        TwoTripMap();
        var vm = Open();
        var import = vm.StartImport()!;
        await Choose(import, import.Maps.Single());
        int stops = vm.BusStops.Count, routes = vm.Routes.Count, termini = vm.Termini.Count;

        Assert.True(vm.ApplyImport(import));

        Assert.Equal(stops + 3, vm.BusStops.Count);
        Assert.Equal(routes + 2, vm.Routes.Count);
        Assert.Equal(termini + 1, vm.Termini.Count);
        Assert.True(vm.IsDirty);
        Assert.StartsWith("Imported Test: 3 bus stops, 1 destination and 2 routes added", vm.Status);

        vm.Undo();                                           // the whole import in one step
        Assert.Equal(stops, vm.BusStops.Count);
        Assert.Equal(routes, vm.Routes.Count);
        Assert.Equal(termini, vm.Termini.Count);
        Assert.False(vm.IsDirty);

        vm.Redo();
        Assert.Equal(routes + 2, vm.Routes.Count);
    }

    [Fact]
    public async Task Importing_what_is_already_there_changes_nothing()
    {
        TwoTripMap();
        var vm = Open();
        var first = vm.StartImport()!;
        await Choose(first, first.Maps.Single());
        Assert.True(vm.ApplyImport(first));
        vm.Save(vm.FilePath!);

        var again = vm.StartImport()!;
        await Choose(again, again.Maps.Single());

        Assert.False(again.CanImport);
        Assert.False(vm.ApplyImport(again));
        Assert.False(vm.IsDirty);
        Assert.Contains("already in the file", vm.Status);
    }

    [Fact]
    public void A_single_timetable_file_can_be_added_from_the_dialog()
    {
        string trip = WriteTrip("Test", "U28.ttp", Trip("AI_U28", "University", "U28", "A", "B"));
        var vm = Open();
        var import = vm.StartImport()!;

        import.SetTripFile(trip);

        Assert.True(import.CanImport);
        Assert.Equal("2 bus stops and 1 route will be added; 1 destination is already in the file.", import.Summary);
        Assert.True(vm.ApplyImport(import));
        Assert.Contains(vm.Routes, r => r.Name == "U28" && r.Stops.Count == 2);

        var notATimetable = vm.StartImport()!;
        string junk = Path.Combine(_dir, "junk.ttp");
        File.WriteAllText(junk, "nothing");
        notATimetable.SetTripFile(junk);
        Assert.False(notATimetable.CanImport);
        Assert.Contains("not an OMSI timetable", notATimetable.Summary);
    }

    [Fact]
    public void Import_needs_an_open_file()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));

        Assert.False(vm.CanImport);
        Assert.Null(vm.StartImport());
        Assert.Contains("Open a .hof file", vm.Status);
    }
}
