using System.Text;
using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;

namespace Hofwerk.Core.Tests;

public class MapImporterTests : IDisposable
{
    readonly string _omsi = Path.Combine(Path.GetTempPath(), "hofwerk-maps-" + Guid.NewGuid().ToString("N"), "OMSI 2");

    public MapImporterTests() => Directory.CreateDirectory(Path.Combine(_omsi, "Maps"));
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_omsi)!, recursive: true);

    string MapFolder(string name) => Path.Combine(_omsi, "Maps", name);

    static string Station(string name) => $"[station]\r\n1234\r\n10\r\n{name}\r\n5\r\n-1.0\r\n100.0\r\n20.0\r\n1.0\r\n\r\n";

    static string Trip(string internalName, string destination, string line, params string[] stations) =>
        "-----------------------\r\nTime Table Trip File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.3.004\r\n\r\n"
        + $"[trip]\r\n{internalName}\r\n{destination}\r\n{line}\r\n\r\n.........................\r\n        Stations\r\n.........................\r\n\r\n"
        + string.Concat(stations.Select(Station))
        + ".........................\r\n        Profiles\r\n.........................\r\n\r\n[profile]\r\nstandard\r\n4.000\r\n";

    void WriteTt(string map, string file, string text)
    {
        string dir = Path.Combine(MapFolder(map), "TTData");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, file), HofEncodings.Windows1252.GetBytes(text));
    }

    [Fact]
    public void Only_maps_with_timetable_data_are_listed_by_name()
    {
        WriteTt("Zeta", "a.ttp", Trip("a", "X", "1"));
        WriteTt("alpha", "b.ttp", Trip("b", "X", "1"));
        Directory.CreateDirectory(Path.Combine(MapFolder("No Timetables"), "texture"));

        var maps = MapImporter.FindMaps(_omsi);

        Assert.Equal(["alpha", "Zeta"], maps.Select(m => m.Name));
        Assert.Empty(MapImporter.FindMaps(Path.Combine(_omsi, "nowhere")));
    }

    [Fact]
    public void Trips_stops_and_destinations_are_read_from_the_trip_files()
    {
        WriteTt("Test", "U28.ttp", Trip("AI_U28", "University", "U28", "St Mary's Church", "Sydney Buildings", "Widcombe Lane"));
        WriteTt("Test", "Return.ttp", Trip("Return", "City Centre", "U28", "Widcombe Lane", "Sydney Buildings"));

        var map = MapImporter.Read(MapFolder("Test"));

        Assert.Equal("Test", map.Name);
        Assert.Equal(["Return", "U28"], map.Trips.Select(t => t.Name));                         // named like their files, in order
        Assert.Equal(["St Mary's Church", "Sydney Buildings", "Widcombe Lane"], map.Trips[1].Stops);
        Assert.Equal("University", map.Trips[1].Destination);
        Assert.Equal("U28", map.Trips[1].Line);
        Assert.Equal(["Widcombe Lane", "Sydney Buildings", "St Mary's Church"], map.BusStops);   // first time each is seen, no repeats
        Assert.Equal(["City Centre", "University"], map.Destinations);
        Assert.Equal("3 bus stops, 2 destinations, 2 routes", map.Summary);
    }

    [Fact]
    public void Stop_names_that_differ_only_in_letter_case_are_one_stop_and_umlauts_survive()
    {
        WriteTt("Test", "a.ttp", Trip("a", "Süd", "1", "Hauptstraße", "MARKT"));
        WriteTt("Test", "b.ttp", Trip("b", "süd", "1", "markt", "Hauptstraße"));

        var map = MapImporter.Read(MapFolder("Test"));

        Assert.Equal(["Hauptstraße", "MARKT"], map.BusStops);
        Assert.Equal(["Süd"], map.Destinations);
    }

    [Fact]
    public void Stops_listed_only_in_Busstops_cfg_are_added()
    {
        WriteTt("Test", "a.ttp", Trip("a", "X", "1", "Known"));
        WriteTt("Test", "Busstops.cfg", "---\r\nTime Table BusStopList File\r\n---\r\n\r\n[busstop]\r\nKnown\r\n3\r\n129\r\n0.0\r\n\r\n[busstop]\r\nUnused stop\r\n3\r\n130\r\n0.0\r\n");

        var map = MapImporter.Read(MapFolder("Test"));

        Assert.Equal(["Known", "Unused stop"], map.BusStops);
    }

    [Fact]
    public void A_map_without_timetables_and_broken_files_give_something_empty_not_an_error()
    {
        Directory.CreateDirectory(MapFolder("Bare"));
        Assert.True(MapImporter.Read(MapFolder("Bare")).IsEmpty);

        WriteTt("Test", "empty.ttp", "");
        WriteTt("Test", "junk.ttp", "[trip]\r\n");
        WriteTt("Test", "half.ttp", "[trip]\r\nname\r\nDest\r\nL1\r\n[station]\r\n1\r\n");

        var map = MapImporter.Read(MapFolder("Test"));

        Assert.Equal(["half", "junk"], map.Trips.Select(t => t.Name));
        Assert.Empty(map.BusStops);
    }

    [Fact]
    public void The_new_file_has_the_map_s_stops_destinations_and_routes_ready_to_use()
    {
        WriteTt("Test", "U28.ttp", Trip("x", "University", "U28", "St Mary's Church", "Sydney Buildings"));
        WriteTt("Test", "U28_in.ttp", Trip("x", "City", "U28", "Sydney Buildings", "St Mary's Church"));
        WriteTt("Test", "X39.ttp", Trip("x", "Bristol", "X39", "Sydney Buildings"));
        WriteTt("Test", "Train.ttp", Trip("x", "London", "GWR", "Bath Spa"));
        var map = MapImporter.Read(MapFolder("Test"));

        var doc = HofFile.CreateFromMap(map, 26, 9, new MapImportOptions());

        Assert.Equal("Test", doc.Name);
        Assert.True(doc.HelpComments);
        // trips are read in file order (Train, U28, U28_in, X39); each stop is added the first time it is seen
        Assert.Equal(["Bath Spa", "St Mary's Church", "Sydney Buildings"], doc.BusStops.Select(b => b.Name));
        Assert.All(doc.BusStops, b => Assert.Equal(9, b.Strings.Count));

        // 1000 is the not-in-service destination; the map's own start at 1001
        Assert.Equal(["1000", "1001", "1002", "1003", "1004"], doc.Termini.Select(t => t.Ident));
        var university = doc.Termini.Single(t => t.Station == "University");
        Assert.Equal("University", university.Strings[0]);
        Assert.Equal(26, university.Strings.Count);

        // line number x 100 + a running number, like the files people make by hand
        var routes = doc.Routes.ToDictionary(r => r.Name);
        Assert.Equal("2801", routes["U28"].Ident);
        Assert.Equal("2802", routes["U28_in"].Ident);
        Assert.Equal("3901", routes["X39"].Ident);
        Assert.Equal("5001", routes["Train"].Ident);               // a line name without a number
        Assert.Equal("28", routes["U28"].Line);
        Assert.Equal("", routes["Train"].Line);
        Assert.Equal(university.Ident, routes["U28"].TerminusIdent);
        Assert.Equal(["St Mary's Church", "Sydney Buildings"], routes["U28"].Stops.Select(s => s.Name));
    }

    [Fact]
    public void The_file_reads_back_with_no_problems_and_every_route_stop_is_a_defined_bus_stop()
    {
        WriteTt("Test", "A.ttp", Trip("x", "Town", "7", "One", "Two", "One"));
        var doc = HofFile.CreateFromMap(MapImporter.Read(MapFolder("Test")), 9, 3, new MapImportOptions());

        var again = HofParser.Parse(HofWriter.ToText(doc).Split("\r\n"));

        Assert.Empty(again.Diagnostics);
        var names = again.BusStops.Select(b => b.Name).ToHashSet();
        Assert.All(again.Routes.SelectMany(r => r.Stops), s => Assert.Contains(s.Name, names));
        Assert.Equal(["One", "Two", "One"], again.Routes[0].Stops.Select(s => s.Name));      // a stop can be visited twice
        Assert.Equal(HofWriter.ToText(doc), HofWriter.ToText(again));
    }

    [Fact]
    public void Parts_that_are_not_wanted_are_left_out()
    {
        WriteTt("Test", "A.ttp", Trip("x", "Town", "7", "One", "Two"));
        var map = MapImporter.Read(MapFolder("Test"));

        var stopsOnly = HofFile.CreateFromMap(map, 3, 3, new MapImportOptions(BusStops: true, Destinations: false, Routes: false));
        Assert.Equal(2, stopsOnly.BusStops.Count);
        Assert.Empty(stopsOnly.Routes);
        Assert.Single(stopsOnly.Termini);                          // just "not in service"

        var routesOnly = HofFile.CreateFromMap(map, 3, 3, new MapImportOptions(BusStops: false, Destinations: false, Routes: true));
        Assert.Empty(routesOnly.BusStops);
        Assert.Single(routesOnly.Routes);
        Assert.Single(routesOnly.Termini);
        Assert.Equal("1001", routesOnly.Routes[0].TerminusIdent);  // still points at the code the destination would have had
    }

    [Fact]
    public void Route_codes_never_repeat()
    {
        for (int i = 0; i < 12; i++) WriteTt("Test", $"T{i:D2}.ttp", Trip("x", "Town", "U28", "A"));

        var doc = HofFile.CreateFromMap(MapImporter.Read(MapFolder("Test")), 1, 1, new MapImportOptions());

        Assert.Equal(12, doc.Routes.Select(r => r.Ident).Distinct().Count());
        Assert.Equal("2801", doc.Routes[0].Ident);
        Assert.Equal("2812", doc.Routes[11].Ident);
    }

    [Fact]
    public void The_Cotterell_map_that_comes_with_OMSI_matches_a_hof_made_by_hand_for_it()
    {
        // Runs only on a machine that has OMSI 2 with its Cotterell map.
        string? cotterell = OmsiLocator.FindInstallFolders().Select(f => Path.Combine(f, "Maps", "Cotterell")).FirstOrDefault(Directory.Exists);
        if (cotterell is null) return;

        var map = MapImporter.Read(cotterell);

        Assert.Equal(22, map.BusStops.Count);      // the hand-made Cotterell.hof has 22 bus stops
        Assert.Equal(17, map.Trips.Count);         // and 17 routes
        Assert.Contains("A4", map.Trips.Select(t => t.Name));
        Assert.Contains("AI_U28", map.Trips.Select(t => t.Name));
        Assert.Equal(11, map.Trips.Single(t => t.Name == "AI_U28").Stops.Count);
    }
}
