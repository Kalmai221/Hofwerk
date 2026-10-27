namespace Hofwerk.Core.Model;

/// <summary>
/// The credit Hofwerk puts at the top of the .hof files it makes, and the short notes between their sections. OMSI skips any
/// line that is not one of its markers, so these are notes for people only. They stay ASCII so any editor shows them correctly.
/// </summary>
public static class HofHeader
{
    /// <summary>Found in a file's opening text, this marks it as made by Hofwerk (so Hofwerk keeps its notes up to date).</summary>
    public const string Marker = "Made with Hofwerk";

    public const string Url = "https://github.com/Kalmai221/Hofwerk";

    const int BoxWidth = 77;

    /// <summary>The credit box at the top of a file: who made it and where to find them.</summary>
    public static List<string> Lines() =>
    [
        new string('*', BoxWidth),
        BoxLine("Made with Hofwerk - a modern editor for OMSI 2 .hof files"),
        BoxLine(Url),
        new string('*', BoxWidth),
    ];

    static string BoxLine(string text) => ("* " + text).PadRight(BoxWidth - 1) + "*";

    /// <summary>The short notes Hofwerk writes above each group of entries, in plain words. No line starts with a marker.</summary>
    public static class Notes
    {
        public static readonly string[] ServiceTrip =
        [
            "* The destination shown when the bus is not in service. It is the text of one",
            "* of the destinations further down.",
        ];

        public static readonly string[] StringCounts =
        [
            "* How many text strings every destination and every bus stop has below (for",
            "* example the text on the display and the announcements). In Hofwerk, a string",
            "* preset gives each of these strings a name.",
        ];

        public static readonly string[] GlobalStrings =
        [
            "* Settings for the bus pack, one per line, six lines in all: the folder with",
            "* the announcements, the folder with the roll sign textures, the folder with",
            "* the side plate textures, then the last two digits of the IBIS code for 900,",
            "* 800 and 500 routes. The folders are inside the Vehicles folder of OMSI.",
        ];

        public static readonly string[] Destinations =
        [
            "* Destinations: what the bus display shows. Each one has a code, the stop",
            "* where everybody gets off (left out for an all-exit destination, where nobody",
            "* boards), and then its text strings.",
        ];

        public static readonly string[] BusStops =
        [
            "* Bus stops: each one has a name, and then its announcement and display strings.",
        ];

        public static readonly string[] Routes =
        [
            "* Routes: each one has a code, a name, the code of the destination it ends at",
            "* and a line number. Below it is the list of bus stops the route calls at: how",
            "* many there are, then one stop name per line.",
        ];
    }
}
