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

/// <summary>One trip of a map's timetable (a .ttp file): where it goes, its line, and the bus stops it serves in order.</summary>
public sealed record MapTrip(string Name, string Destination, string Line, IReadOnlyList<string> Stops);

/// <summary>What Hofwerk found in a map's timetable files.</summary>
public sealed class MapData
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> BusStops { get; init; } = [];
    public IReadOnlyList<string> Destinations { get; init; } = [];
    public IReadOnlyList<MapTrip> Trips { get; init; } = [];

    public bool IsEmpty => BusStops.Count == 0 && Trips.Count == 0;

    /// <summary>"22 bus stops, 2 destinations, 17 routes".</summary>
    public string Summary => $"{Count(BusStops.Count, "bus stop")}, {Count(Destinations.Count, "destination")}, {Count(Trips.Count, "route")}";

    static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}

/// <summary>What to take from the map when making a new .hof file.</summary>
public sealed record MapImportOptions(bool BusStops = true, bool Destinations = true, bool Routes = true);

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

        foreach (string file in Directory.EnumerateFiles(data, "*.ttp", IgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(MaxFiles))
        {
            if (ReadTrip(file) is not { } trip) continue;
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
    public static void Fill(HofDocument doc, MapData map, MapImportOptions options)
    {
        var destinationIdents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedDestinationCodes = new HashSet<string>(doc.Termini.Select(t => t.Ident));

        if (options.Destinations || options.Routes)
        {
            int next = 1001;
            foreach (string text in map.Destinations)
            {
                while (usedDestinationCodes.Contains(next.ToString())) next++;
                string ident = next.ToString();
                usedDestinationCodes.Add(ident);
                destinationIdents[text] = ident;

                if (!options.Destinations) continue;   // routes still point at the code, but the destination itself is not added
                var strings = Enumerable.Repeat("", doc.TerminusStringCount).ToList();
                if (strings.Count > 0) strings[0] = text;
                doc.Termini.Add(new Terminus { Ident = ident, Station = text, Strings = strings });
            }
        }

        if (options.BusStops)
        {
            var known = new HashSet<string>(doc.BusStops.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string stop in map.BusStops)
                if (known.Add(stop))
                    doc.BusStops.Add(new BusStop { Name = stop, Strings = Enumerable.Repeat("", doc.BusStopStringCount).ToList() });
        }

        if (options.Routes)
        {
            var usedCodes = new HashSet<string>(doc.Routes.Select(r => r.Ident));
            var perLine = new Dictionary<int, int>();

            foreach (var trip in map.Trips)
            {
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
            }
        }
    }

    // ---- reading ---------------------------------------------------------------------------------------------

    static MapTrip? ReadTrip(string file)
    {
        string[] lines = ReadLines(file);
        int trip = Array.IndexOf(lines, "[trip]");
        if (trip < 0) return null;

        // [trip] / an internal name / the destination text / the line name
        string destination = trip + 2 < lines.Length ? lines[trip + 2].Trim() : "";
        string line = trip + 3 < lines.Length ? lines[trip + 3].Trim() : "";

        var stops = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            // [station] / id / distance / NAME / ...
            if (lines[i] == "[station]" && i + 3 < lines.Length && lines[i + 3].Trim() is { Length: > 0 } stop) stops.Add(stop);
        }
        return new MapTrip(Path.GetFileNameWithoutExtension(file), destination, line, stops);
    }

    static string[] ReadLines(string file)
    {
        try { return HofEncodings.Windows1252.GetString(File.ReadAllBytes(file)).Split('\n').Select(l => l.TrimEnd('\r')).ToArray(); }
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
