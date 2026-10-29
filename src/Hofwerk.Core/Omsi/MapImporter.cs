using Hofwerk.Localization;
using System.Text.RegularExpressions;
using Hofwerk.Core.Model;

namespace Hofwerk.Core.Omsi;

/// <summary>
/// An OMSI map: a folder inside the game's Maps folder that has timetable data (TTData). <see cref="Name"/> is the folder's name;
/// <see cref="DisplayName"/> is the name the map gives itself in its global.cfg ("Cotterell v1.1"), and <see cref="PicturePath"/> is its
/// picture (picture.jpg), which OMSI shows in its own map list.
/// </summary>
public sealed record OmsiMap(string Name, string Folder, string? DisplayName = null, string? PicturePath = null)
{
    /// <summary>What to show the user: the map's own name, or the folder's when it has none.</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
}

/// <summary>
/// One trip of a map's timetable (a .ttp file): where it goes, its line, and the bus stops it serves in order.
/// <see cref="UsesStationLinks"/> is true when the file writes its stops as numbers that Busstops.cfg gives names to (HOF Suite's "type 2").
/// </summary>
public sealed record MapTrip(string Name, string Destination, string Line, IReadOnlyList<string> Stops, bool UsesStationLinks = false);

/// <summary>What Hofwerk found in a map's timetable files.</summary>
public sealed class MapData
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> BusStops { get; init; } = [];
    public IReadOnlyList<string> Destinations { get; init; } = [];
    public IReadOnlyList<MapTrip> Trips { get; init; } = [];

    public bool IsEmpty => BusStops.Count == 0 && Trips.Count == 0;

    /// <summary>
    /// The map as if it only had these trips: their destinations and the bus stops they serve (the stops that only Busstops.cfg lists are
    /// left out, since no chosen trip stops there).
    /// </summary>
    public MapData WithTrips(IEnumerable<MapTrip> trips)
    {
        var chosen = trips.ToList();
        var stops = new List<string>();
        var seenStops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new List<string>();
        var seenDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trip in chosen)
        {
            if (trip.Destination.Trim() is { Length: > 0 } destination && seenDestinations.Add(destination)) destinations.Add(destination);
            foreach (string stop in trip.Stops)
                if (stop.Trim() is { Length: > 0 } name && seenStops.Add(name)) stops.Add(name);
        }
        return new MapData { Name = Name, BusStops = stops, Destinations = destinations, Trips = chosen };
    }

    /// <summary>"22 bus stops, 2 destinations, 17 routes".</summary>
    public string Summary => string.Join(", ", Localizer.Count(BusStops.Count, "{0} bus stop", "{0} bus stops"),
        Localizer.Count(Destinations.Count, "{0} destination", "{0} destinations"), Localizer.Count(Trips.Count, "{0} route", "{0} routes"));
}

/// <summary>What to take from the map when making a new .hof file.</summary>
public sealed record MapImportOptions(bool BusStops = true, bool Destinations = true, bool Routes = true);

/// <summary>What importing did to a file (or would do): what was added, and what was already there and so was left alone.</summary>
public sealed record MergeResult(int NewBusStops, int KnownBusStops, int NewDestinations, int KnownDestinations, int NewRoutes, int KnownRoutes)
{
    public int Added => NewBusStops + NewDestinations + NewRoutes;
    public int AlreadyThere => KnownBusStops + KnownDestinations + KnownRoutes;


    /// <summary>"12 bus stops, 2 destinations and 8 routes will be added; 3 bus stops and 1 route are already in the file."</summary>
    public string Describe(bool past = false)
    {
        var added = new List<string>();
        if (NewBusStops > 0) added.Add(Localizer.Count(NewBusStops, "{0} bus stop", "{0} bus stops"));
        if (NewDestinations > 0) added.Add(Localizer.Count(NewDestinations, "{0} destination", "{0} destinations"));
        if (NewRoutes > 0) added.Add(Localizer.Count(NewRoutes, "{0} route", "{0} routes"));
        var known = new List<string>();
        if (KnownBusStops > 0) known.Add(Localizer.Count(KnownBusStops, "{0} bus stop", "{0} bus stops"));
        if (KnownDestinations > 0) known.Add(Localizer.Count(KnownDestinations, "{0} destination", "{0} destinations"));
        if (KnownRoutes > 0) known.Add(Localizer.Count(KnownRoutes, "{0} route", "{0} routes"));

        string text = added.Count == 0 ? Localizer.T("Nothing new to add") : Join(added) + (past ? Localizer.T(" added") : Localizer.T(" will be added"));
        if (known.Count > 0) text += "; " + Localizer.F(AlreadyThere == 1 ? "{0} is already in the file" : "{0} are already in the file", Join(known));
        return text + ".";
    }

    static string Join(List<string> parts) => parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + Localizer.T(" and ") + parts[^1];
}

/// <summary>
/// Reads the timetable files of an OMSI map and turns them into the parts of a .hof file. A map (Maps\Name\TTData) holds
/// <c>.ttp</c> trip files (each has a destination text, a line and a list of stations) and <c>Busstops.cfg</c>. The station names
/// of all trips, plus those in Busstops.cfg, are the bus stops. Each trip becomes a route, named like its file.
/// </summary>
public static class MapImporter
{
    const int MaxFiles = 20_000;
    static readonly EnumerationOptions IgnoreCase = new() { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };

    /// <summary>The maps in OMSI's Maps folder that have timetable data, by name.</summary>
    public static IReadOnlyList<OmsiMap> FindMaps(string omsiFolder)
    {
        if (string.IsNullOrWhiteSpace(omsiFolder) || Sub(omsiFolder, "Maps") is not { } maps) return [];

        return Directory.EnumerateDirectories(maps)
            .Where(folder => Sub(folder, "TTData") is not null)
            .Select(Describe)
            .OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>A map folder with its own name and picture, read from the folder itself.</summary>
    public static OmsiMap Describe(string mapFolder)
    {
        mapFolder = mapFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return new OmsiMap(Path.GetFileName(mapFolder), mapFolder, ReadDisplayName(mapFolder), FindPicture(mapFolder));
    }

    /// <summary>The map's picture: picture.jpg (or .png, .bmp), in any letter case, or null.</summary>
    static string? FindPicture(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "picture.*", IgnoreCase)
                .FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The name under [name] in the map's global.cfg. OMSI writes that file as UTF-16, but older maps use plain text, so the encoding is
    /// recognised from the start of the file.
    /// </summary>
    static string? ReadDisplayName(string folder)
    {
        try
        {
            string? file = Directory.EnumerateFiles(folder, "global.cfg", IgnoreCase).FirstOrDefault();
            if (file is null) return null;

            using var stream = File.OpenRead(file);
            byte[] bytes = new byte[(int)Math.Min(stream.Length, 64 * 1024)];
            int read = stream.Read(bytes, 0, bytes.Length);

            string[] lines = Decode(bytes.AsSpan(0, read)).Split('\n');
            for (int i = 0; i + 1 < lines.Length; i++)
                if (lines[i].Trim() == "[name]") return lines[i + 1].Trim().Trim('\uFEFF') is { Length: > 0 } name ? name : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    static string Decode(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return System.Text.Encoding.Unicode.GetString(b[2..]);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return System.Text.Encoding.BigEndianUnicode.GetString(b[2..]);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return System.Text.Encoding.UTF8.GetString(b[3..]);
        if (b.Length >= 4 && b[1] == 0 && b[3] == 0) return System.Text.Encoding.Unicode.GetString(b);    // UTF-16 without a marker
        return HofEncodings.Windows1252.GetString(b);
    }

    /// <summary>Reads the timetable files of one map. A map without any gives an empty result; unreadable files are skipped.</summary>
    public static MapData Read(string mapFolder)
    {
        string name = Path.GetFileName(mapFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (Sub(mapFolder, "TTData") is not { } data) return new MapData { Name = name };

        var trips = new List<MapTrip>();
        var stops = new Names();
        var destinations = new Names();
        var ids = new Lazy<Dictionary<int, string>>(() => ReadStopIds(data));

        foreach (string file in Directory.EnumerateFiles(data, "*.ttp", IgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(MaxFiles))
        {
            if (ReadTrip(file, ids) is not { } trip) continue;
            trips.Add(trip);
            destinations.Add(trip.Destination);
            foreach (string stop in trip.Stops) stops.Add(stop);
        }

        // Bigger maps also list stops that no trip uses (yet) in Busstops.cfg.
        foreach (string file in Directory.EnumerateFiles(data, "Busstops.cfg", IgnoreCase))
        {
            var lines = ReadLines(file);
            for (int i = 0; i + 1 < lines.Length; i++)
                if (lines[i] == "[busstop]") stops.Add(lines[i + 1]);
        }

        return new MapData { Name = name, BusStops = stops.List, Destinations = destinations.List, Trips = trips };
    }

    /// <summary>
    /// Adds what was found in the map to a new document. Destinations get codes from 1001 up ("1000" is the not-in-service one); a
    /// route's code is its line number times 100 plus a running number (line 28: 2801, 2802), like the .hof files people make by
    /// hand. All of these are only suggestions, and every one of them can be changed in the editor.
    /// </summary>
    public static MergeResult Fill(HofDocument doc, MapData map, MapImportOptions options)
    {
        var destinationIdents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedDestinationCodes = new HashSet<string>(doc.Termini.Select(t => t.Ident));
        int newStops = 0, knownStops = 0, newDestinations = 0, knownDestinations = 0, newRoutes = 0, knownRoutes = 0;

        // A file that already has a destination with this text keeps it: its code is used and nothing is added twice.
        foreach (var existing in doc.Termini.Where(t => !t.IsAllExit && t.Station.Trim().Length > 0))
            destinationIdents.TryAdd(existing.Station.Trim(), existing.Ident);

        if (options.Destinations || options.Routes)
        {
            int next = 1001;
            foreach (string text in map.Destinations)
            {
                if (destinationIdents.ContainsKey(text.Trim()))
                {
                    if (options.Destinations) knownDestinations++;
                    continue;
                }
                while (usedDestinationCodes.Contains(next.ToString())) next++;
                string ident = next.ToString();
                usedDestinationCodes.Add(ident);
                destinationIdents[text] = ident;

                if (!options.Destinations) continue;   // routes still point at the code, but the destination itself is not added
                var strings = Enumerable.Repeat("", doc.TerminusStringCount).ToList();
                if (strings.Count > 0) strings[0] = text;
                doc.Termini.Add(new Terminus { Ident = ident, Station = text, Strings = strings });
                newDestinations++;
            }
        }

        if (options.BusStops)
        {
            var known = new HashSet<string>(doc.BusStops.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string stop in map.BusStops)
            {
                if (known.Add(stop))
                {
                    doc.BusStops.Add(new BusStop { Name = stop, Strings = Enumerable.Repeat("", doc.BusStopStringCount).ToList() });
                    newStops++;
                }
                else knownStops++;
            }
        }

        if (options.Routes)
        {
            var usedCodes = new HashSet<string>(doc.Routes.Select(r => r.Ident));
            var knownNames = new HashSet<string>(doc.Routes.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            var perLine = new Dictionary<int, int>();

            foreach (var trip in map.Trips)
            {
                // A trip is told apart by its name (the name of its timetable file): importing it again does not add it again.
                if (!knownNames.Add(trip.Name)) { knownRoutes++; continue; }
                string line = LineNumber(trip.Line);
                int baseCode = line.Length > 0 ? int.Parse(line.Length > 2 ? line[..2] : line) * 100 : 5000;
                int sequence = perLine.GetValueOrDefault(baseCode);
                string ident;
                do ident = (baseCode + ++sequence).ToString(); while (!usedCodes.Add(ident));
                perLine[baseCode] = sequence;

                doc.Routes.Add(new Route
                {
                    Ident = ident,
                    Name = trip.Name,
                    Line = line,
                    TerminusIdent = destinationIdents.GetValueOrDefault(trip.Destination, "1000"),
                    Stops = trip.Stops.Select(s => new RouteStop { Name = s }).ToList(),
                });
                newRoutes++;
            }
        }

        return new MergeResult(newStops, knownStops, newDestinations, knownDestinations, newRoutes, knownRoutes);
    }

    /// <summary>What <see cref="Fill"/> would do to <paramref name="doc"/>, worked out on a copy, so the file is not touched.</summary>
    public static MergeResult Preview(HofDocument doc, MapData map, MapImportOptions options) =>
        Fill(HofParser.Parse(HofWriter.ToLines(doc)), map, options);

    /// <summary>
    /// One timetable file (<c>.ttp</c>) as a map with a single trip: its stops, its destination and the trip itself, so it can be
    /// added to a file like a whole map can. Null when the file is not a timetable.
    /// </summary>
    public static MapData? ReadTripFile(string file)
    {
        string? folder = Path.GetDirectoryName(file);
        var ids = new Lazy<Dictionary<int, string>>(() => folder is null ? [] : ReadStopIds(folder));
        if (ReadTrip(file, ids) is not { } trip) return null;
        var stops = new Names();
        foreach (string stop in trip.Stops) stops.Add(stop);
        return new MapData
        {
            Name = trip.Name,
            BusStops = stops.List,
            Destinations = trip.Destination.Trim().Length > 0 ? [trip.Destination.Trim()] : [],
            Trips = [trip],
        };
    }

    // ---- reading ---------------------------------------------------------------------------------------------

    static MapTrip? ReadTrip(string file, Lazy<Dictionary<int, string>> ids)
    {
        string[] lines = ReadLines(file);
        int trip = Array.IndexOf(lines, "[trip]");
        if (trip < 0) return null;

        // [trip] / an internal name / the destination text / the line name
        string destination = trip + 2 < lines.Length ? lines[trip + 2].Trim() : "";
        string line = trip + 3 < lines.Length ? lines[trip + 3].Trim() : "";

        var stops = new List<string>();
        bool stationLinks = false;
        for (int i = 0; i < lines.Length; i++)
        {
            // A stop is written one of two ways, and a file can use either (HOF Suite 1.3 added the second, "station links"):
            //   [station] / id / distance / NAME / ...      the name is in the trip file
            //   [station_typ2] / NUMBER                      only a number; the name is the [busstop] with that number in Busstops.cfg
            if (lines[i] == "[station]" && i + 3 < lines.Length && lines[i + 3].Trim() is { Length: > 0 } stop) stops.Add(stop);
            else if (lines[i] == "[station_typ2]" && i + 1 < lines.Length)
            {
                stationLinks = true;
                if (int.TryParse(lines[i + 1].Trim(), out int number) && ids.Value.TryGetValue(number, out string? linked))
                    stops.Add(linked);      // a number the map does not know is left out rather than dropping the whole trip
            }
        }
        return new MapTrip(Path.GetFileNameWithoutExtension(file), destination, line, stops, stationLinks);
    }

    /// <summary>
    /// The stops of a map by number, from the Busstops.cfg beside its timetable files: "[busstop]", the name, one line that is not
    /// needed, then the number. Station-link trips only carry these numbers.
    /// </summary>
    static Dictionary<int, string> ReadStopIds(string ttDataFolder)
    {
        var ids = new Dictionary<int, string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(ttDataFolder, "Busstops.cfg", IgnoreCase))
            {
                var lines = ReadLines(file);
                for (int i = 0; i + 3 < lines.Length; i++)
                {
                    if (lines[i] != "[busstop]") continue;
                    if (int.TryParse(lines[i + 3].Trim(), out int number) && lines[i + 1].Trim() is { Length: > 0 } name) ids.TryAdd(number, name);
                    i += 3;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return ids;
    }

    static string[] ReadLines(string file)
    {
        try { return Decode(File.ReadAllBytes(file)).Split('\n').Select(l => l.TrimEnd('\r', '\uFEFF')).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static string? Sub(string folder, string name)
    {
        try { return Directory.EnumerateDirectories(folder, name, IgnoreCase).FirstOrDefault(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The first number in a line name: "U28" gives "28", "X39" "39", "GWR" nothing.</summary>
    static string LineNumber(string line) => Regex.Match(line, @"\d+") is { Success: true } m ? m.Value : "";

    /// <summary>A list of names without repeats (letter case ignored), in the order they were first seen.</summary>
    sealed class Names
    {
        readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        public List<string> List { get; } = [];

        public void Add(string name)
        {
            name = name.Trim();
            if (name.Length > 0 && _seen.Add(name)) List.Add(name);
        }
    }
}
