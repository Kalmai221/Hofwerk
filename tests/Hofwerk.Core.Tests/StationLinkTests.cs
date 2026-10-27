using System.Text;
using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;

namespace Hofwerk.Core.Tests;

/// <summary>
/// "Station links" (HOF Suite 1.3): a timetable file can write a stop as just a number, <c>[station_typ2]</c> and the number, and the
/// name is the <c>[busstop]</c> with that number in the map's Busstops.cfg. Maps like Lincolnshire, Yorkshire, London and Pimlico
/// write most of their trips this way.
/// </summary>
[Trait("Area", "Omsi")]
public class StationLinkTests : IDisposable
{
    readonly string _omsi = Path.Combine(Path.GetTempPath(), "hofwerk-links-" + Guid.NewGuid().ToString("N"), "OMSI 2");

    public StationLinkTests() => Directory.CreateDirectory(Path.Combine(_omsi, "Maps"));
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_omsi)!, recursive: true);

    string TtData(string map) => Path.Combine(_omsi, "Maps", map, "TTData");

    static string StopList(params (string Name, int Id)[] stops) =>
        "---------------------------\r\nTime Table Busstop File\r\n---------------------------\r\n\r\nCreated with Omsi Version 2.3.004\r\n\r\n"
        + string.Concat(stops.Select(s => $"[busstop]\r\n{s.Name}\r\n23\r\n{s.Id}\r\n0.0000000000\r\n0\r\n0\r\n\r\n"));

    static string Header(string internalName, string destination, string line) =>
        "-----------------------\r\nTime Table Trip File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.3.004\r\n\r\n"
        + $"[trip]\r\n{internalName}\r\n{destination}\r\n{line}\r\n\r\n.........................\r\n        Stations\r\n.........................\r\n\r\n";

    static string Linked(params int[] ids) => string.Concat(ids.Select(id => $"[station_typ2]\r\n{id}\r\n\r\n"));
    static string Plain(string name) => $"[station]\r\n1234\r\n10\r\n{name}\r\n5\r\n-1.0\r\n100.0\r\n20.0\r\n1.0\r\n\r\n";
    static string Footer => ".........................\r\n        Profiles\r\n.........................\r\n\r\n[profile]\r\nstandard\r\n4.000\r\n";

    string Write(string map, string file, string text, Encoding? encoding = null)
    {
        Directory.CreateDirectory(TtData(map));
        string path = Path.Combine(TtData(map), file);
        File.WriteAllBytes(path, [.. (encoding ?? HofEncodings.Windows1252).GetPreamble(), .. (encoding ?? HofEncodings.Windows1252).GetBytes(text)]);
        return path;
    }

    // ---- reading ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_station_link_trip_gets_the_names_of_its_stops_from_Busstops_cfg_in_the_order_of_the_trip()
    {
        Write("Linked", "Busstops.cfg", StopList(("Market Square", 1001), ("Church Lane", 2002), ("Station Road", 3003)));
        Write("Linked", "A.ttp", Header("A", "Station", "28") + Linked(3003, 1001, 2002) + Footer);

        var map = MapImporter.Read(Path.Combine(_omsi, "Maps", "Linked"));

        Assert.Equal(["Station Road", "Market Square", "Church Lane"], map.Trips.Single().Stops);
        Assert.Equal("Station", map.Trips.Single().Destination);
        Assert.Equal(["Station Road", "Market Square", "Church Lane"], map.BusStops.Take(3));      // the map's stop list has them too
    }

    [Fact]
    public void A_trip_can_mix_stops_with_their_name_in_the_file_and_stops_that_are_only_numbers()
    {
        Write("Mixed", "Busstops.cfg", StopList(("Linked Stop", 77)));
        Write("Mixed", "M.ttp", Header("M", "End", "5") + Plain("First, by name") + Linked(77) + Plain("Last, by name") + Footer);

        var trip = MapImporter.Read(Path.Combine(_omsi, "Maps", "Mixed")).Trips.Single();

        Assert.Equal(["First, by name", "Linked Stop", "Last, by name"], trip.Stops);
    }

    [Fact]
    public void A_number_the_map_does_not_know_is_left_out_and_the_rest_of_the_trip_is_kept()
    {
        Write("Gaps", "Busstops.cfg", StopList(("Known A", 1), ("Known B", 2)));
        Write("Gaps", "G.ttp", Header("G", "End", "1") + Linked(1, 999999, 2) + Footer);

        var trip = MapImporter.Read(Path.Combine(_omsi, "Maps", "Gaps")).Trips.Single();

        Assert.Equal(["Known A", "Known B"], trip.Stops);
    }

    [Fact]
    public void A_stop_number_used_twice_keeps_the_first_name_and_nothing_breaks()
    {
        Write("Twice", "Busstops.cfg", StopList(("First name", 5), ("Second name", 5)));
        Write("Twice", "T.ttp", Header("T", "End", "1") + Linked(5) + Footer);

        Assert.Equal(["First name"], MapImporter.Read(Path.Combine(_omsi, "Maps", "Twice")).Trips.Single().Stops);
    }

    [Fact]
    public void A_map_without_Busstops_cfg_still_reads_its_plain_trips_and_skips_the_numbers()
    {
        Write("NoList", "P.ttp", Header("P", "End", "1") + Plain("By name") + Linked(1, 2) + Footer);

        Assert.Equal(["By name"], MapImporter.Read(Path.Combine(_omsi, "Maps", "NoList")).Trips.Single().Stops);
    }

    [Fact]
    public void Files_written_as_UTF_16_are_read_too_and_umlauts_survive()
    {
        Write("Wide", "Busstops.cfg", StopList(("Köln Süd", 1), ("Grünstraße", 2)), Encoding.Unicode);
        Write("Wide", "W.ttp", Header("W", "Köln Süd", "7") + Linked(1, 2) + Footer, Encoding.Unicode);

        var trip = MapImporter.Read(Path.Combine(_omsi, "Maps", "Wide")).Trips.Single();

        Assert.Equal(["Köln Süd", "Grünstraße"], trip.Stops);
        Assert.Equal("Köln Süd", trip.Destination);
    }

    [Fact]
    public void One_timetable_file_finds_the_Busstops_cfg_in_its_own_folder()
    {
        Write("Single", "Busstops.cfg", StopList(("Alpha", 10), ("Beta", 20)));
        string file = Write("Single", "S.ttp", Header("S", "Beta", "3") + Linked(10, 20) + Footer);

        var data = MapImporter.ReadTripFile(file)!;

        Assert.Equal(["Alpha", "Beta"], data.BusStops);
        Assert.Equal(["Alpha", "Beta"], data.Trips.Single().Stops);
    }

    [Fact]
    public void Importing_a_linked_trip_gives_the_route_all_its_stops()
    {
        Write("Import", "Busstops.cfg", StopList(("Alpha", 10), ("Beta", 20), ("Gamma", 30)));
        Write("Import", "R.ttp", Header("R", "Gamma", "12") + Linked(10, 20, 30) + Footer);
        var doc = HofFile.CreateNew("Depot", 2, 1);

        MapImporter.Fill(doc, MapImporter.Read(Path.Combine(_omsi, "Maps", "Import")), new MapImportOptions());

        Assert.Equal(["Alpha", "Beta", "Gamma"], doc.Routes.Single().Stops.Select(s => s.Name));
        Assert.Equal(["Alpha", "Beta", "Gamma"], doc.BusStops.Select(b => b.Name));
    }

    // ---- real maps (only where OMSI 2 is installed) ----------------------------------------------------------------

    [Fact]
    public void On_the_maps_that_come_with_OMSI_and_the_popular_add_ons_every_bus_trip_gets_its_stops()
    {
        string? omsi = OmsiLocator.FindInstallFolders().FirstOrDefault(f => Directory.Exists(Path.Combine(f, "Maps")));
        if (omsi is null) return;

        // These maps write nearly all of their trips as station links; none of those used to have any stops.
        foreach (string name in new[] { "Lincolnshire V5", "Yorkshire", "Pimlico", "London" })
        {
            string folder = Path.Combine(omsi, "Maps", name);
            if (!Directory.Exists(folder)) continue;

            var map = MapImporter.Read(folder);

            Assert.NotEmpty(map.Trips);
            Assert.All(map.Trips, trip => Assert.True(trip.Stops.Count >= 1, $"{name}: {trip.Name} has {trip.Stops.Count} stops"));
        }
    }
}
