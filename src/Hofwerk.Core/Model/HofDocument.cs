using System.Text;

namespace Hofwerk.Core.Model;

/// <summary>An OMSI .hof file: destinations (termini), bus stops and routes (trips).</summary>
public sealed class HofDocument
{
    /// <summary>Free text found before the first keyword (kept verbatim).</summary>
    public List<string> Preamble { get; } = [];
    public List<string> SuiteDirectories { get; } = [];
    public string Name { get; set; } = "";
    public string ServiceTrip { get; set; } = "";
    public int TerminusStringCount { get; set; }
    public int BusStopStringCount { get; set; }
    /// <summary>Raw lines of the [global_strings] block (count line excluded).</summary>
    public List<string> GlobalStrings { get; } = [];
    public List<Terminus> Termini { get; } = [];
    public List<BusStop> BusStops { get; } = [];
    public List<Route> Routes { get; } = [];
    public List<HofDiagnostic> Diagnostics { get; } = [];
    /// <summary>Encoding the file was read with; used again when saving.</summary>
    public Encoding Encoding { get; set; } = HofEncodings.Windows1252;
}

public sealed class Terminus
{
    public string Ident { get; set; } = "";
    /// <summary>"allexit": nobody boards, everybody leaves (e.g. not in service).</summary>
    public bool IsAllExit { get; set; }
    /// <summary>Last stop where everybody alights; unused for all-exit entries.</summary>
    public string Station { get; set; } = "";
    public List<string> Strings { get; set; } = [];
}

public sealed class BusStop
{
    public string Name { get; set; } = "";
    public List<string> Strings { get; set; } = [];
}

public sealed class Route
{
    public string Ident { get; set; } = "";
    public string Name { get; set; } = "";
    public string TerminusIdent { get; set; } = "";
    public string Line { get; set; } = "";
    public List<RouteStop> Stops { get; set; } = [];
}

/// <summary>A stop on a route. <see cref="Extra"/> is only set for the older one-by-one [infosystem_busstop] form.</summary>
public sealed class RouteStop
{
    public string Name { get; set; } = "";
    public List<string> Extra { get; set; } = [];
}

public enum HofSeverity { Warning, Error }

public sealed record HofDiagnostic(int Line, HofSeverity Severity, string Message);

public static class HofEncodings
{
    // Property initializers run before the static constructor body, so the provider must be registered here.
    static HofEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    /// <summary>OMSI reads .hof files as Windows-1252.</summary>
    public static Encoding Windows1252 { get; }
}
