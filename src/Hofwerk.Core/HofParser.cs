using Hofwerk.Core.Model;

namespace Hofwerk.Core;

/// <summary>Lenient line-based parser. Never throws on bad input; problems end up in <see cref="HofDocument.Diagnostics"/>.</summary>
public static class HofParser
{
    public static HofDocument Parse(IEnumerable<string> lines)
    {
        var r = new Reader(lines.ToList());
        var doc = new HofDocument();
        bool seenKeyword = false;

        while (!r.Eof)
        {
            int lineNo = r.LineNumber;
            string raw = r.Next();
            string key = KeywordOf(raw);

            if (!IsKeyword(key))
            {
                if (!seenKeyword) doc.Preamble.Add(raw);
                continue;
            }
            seenKeyword = true;

            try
            {
                switch (key.ToLowerInvariant())
                {
                    case "[name]": doc.Name = r.Take().TrimEnd(); break;
                    case "[servicetrip]": doc.ServiceTrip = r.Take().TrimEnd(); break;
                    case "stringcount_terminus": doc.TerminusStringCount = ReadInt(r, doc, lineNo); break;
                    case "stringcount_busstop": doc.BusStopStringCount = ReadInt(r, doc, lineNo); break;
                    case "[global_strings]":
                        int n = ReadInt(r, doc, lineNo);
                        for (int i = 0; i < n; i++) doc.GlobalStrings.Add(r.Take());
                        break;
                    case "[hof_suite_directories]":
                        while (!r.Eof && !KeywordOf(r.Peek()).Equals("[HOF_SUITE_DIRECTORIES_END]", StringComparison.OrdinalIgnoreCase))
                            doc.SuiteDirectories.Add(r.Next());
                        if (!r.Eof) r.Next();
                        break;
                    case "[addterminus]": doc.Termini.Add(ReadTerminus(r, doc, allExit: false)); break;
                    case "[addterminus_allexit]": doc.Termini.Add(ReadTerminus(r, doc, allExit: true)); break;
                    case "[addterminus_list]":
                        foreach (var row in ReadRows(r))
                            doc.Termini.Add(TerminusFromRow(row, doc));
                        break;
                    case "[addbusstop]":
                        doc.BusStops.Add(new BusStop { Name = r.Take().TrimEnd(), Strings = r.TakeMany(doc.BusStopStringCount) });
                        break;
                    case "[addbusstop_list]":
                        foreach (var row in ReadRows(r))
                            doc.BusStops.Add(BusStopFromRow(row, doc));
                        break;
                    case "[infosystem_trip]":
                        doc.Routes.Add(new Route
                        {
                            Ident = r.Take().TrimEnd(),
                            Name = r.Take().TrimEnd(),
                            TerminusIdent = r.Take().TrimEnd(),
                            Line = r.Take().TrimEnd(),
                        });
                        break;
                    case "[infosystem_busstop_list]":
                        if (CurrentRoute(doc, lineNo) is { } route)
                        {
                            int count = ReadInt(r, doc, lineNo);
                            for (int i = 0; i < count; i++) route.Stops.Add(new RouteStop { Name = r.Take().TrimEnd() });
                        }
                        break;
                    case "[infosystem_busstop]":
                        if (CurrentRoute(doc, lineNo) is { } owner)
                            owner.Stops.Add(new RouteStop { Name = r.Take().TrimEnd(), Extra = r.TakeMany(2) });
                        break;
                    case "[end]": break;
                }
            }
            catch (EndOfFileException)
            {
                Report(doc, lineNo, HofSeverity.Error, $"File ended unexpectedly while reading {key}.");
            }
        }

        while (doc.Preamble.Count > 0 && string.IsNullOrWhiteSpace(doc.Preamble[^1]))
            doc.Preamble.RemoveAt(doc.Preamble.Count - 1);
        return doc;
    }

    static Route? CurrentRoute(HofDocument doc, int at)
    {
        if (doc.Routes.Count > 0) return doc.Routes[^1];
        Report(doc, at, HofSeverity.Error, "Bus stop list found before any [infosystem_trip].");
        return null;
    }

    static Terminus ReadTerminus(Reader r, HofDocument doc, bool allExit)
    {
        var t = new Terminus { IsAllExit = allExit, Ident = r.Take().TrimEnd() };
        if (!allExit) t.Station = r.Take();
        t.Strings = r.TakeMany(doc.TerminusStringCount);
        return t;
    }

    static Terminus TerminusFromRow(string[] cells, HofDocument doc)
    {
        // columns: {ALLEX} or empty | ident | name | strings... (unlike the one-entry form, all-exit rows keep the name column)
        bool allExit = cells[0].Trim().Equals("{ALLEX}", StringComparison.OrdinalIgnoreCase);
        return new Terminus
        {
            IsAllExit = allExit,
            Ident = Cell(cells, 1).Trim(),
            Station = Cell(cells, 2),
            Strings = Enumerable.Range(3, doc.TerminusStringCount).Select(i => Cell(cells, i)).ToList(),
        };
    }

    static BusStop BusStopFromRow(string[] cells, HofDocument doc) => new()
    {
        Name = cells[0].TrimEnd(),
        Strings = Enumerable.Range(1, doc.BusStopStringCount).Select(i => Cell(cells, i)).ToList(),
    };

    static string Cell(string[] cells, int i) => i < cells.Length ? cells[i] : "";

    /// <summary>Tab-separated rows up to [end] or the next keyword.</summary>
    static IEnumerable<string[]> ReadRows(Reader r)
    {
        while (!r.Eof)
        {
            string key = KeywordOf(r.Peek());
            if (key.Equals("[end]", StringComparison.OrdinalIgnoreCase)) { r.Next(); yield break; }
            if (IsKeyword(key)) yield break;
            string row = r.Next();
            if (row.Trim().Length == 0) continue;
            yield return row.Split('\t');
        }
    }

    static int ReadInt(Reader r, HofDocument doc, int at)
    {
        string s = r.Take().Trim();
        if (int.TryParse(s, out int v) && v >= 0) return v;
        Report(doc, at, HofSeverity.Error, $"Expected a number but found \"{s}\".");
        return 0;
    }

    static void Report(HofDocument doc, int line, HofSeverity sev, string msg) => doc.Diagnostics.Add(new(line, sev, msg));

    // Keywords start in column 0; indented ones are part of the template documentation.
    static string KeywordOf(string line) => line.Length > 0 && !char.IsWhiteSpace(line[0]) ? line.TrimEnd() : "";

    static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "[name]", "[servicetrip]", "stringcount_terminus", "stringcount_busstop", "[global_strings]",
        "[HOF_SUITE_DIRECTORIES]", "[HOF_SUITE_DIRECTORIES_END]", "[addterminus]", "[addterminus_allexit]",
        "[addterminus_list]", "[addbusstop]", "[addbusstop_list]", "[infosystem_trip]",
        "[infosystem_busstop_list]", "[infosystem_busstop]", "[end]",
    };

    static bool IsKeyword(string key) => key.Length > 0 && Keywords.Contains(key);

    sealed class EndOfFileException : Exception;

    sealed class Reader(List<string> lines)
    {
        int _pos;
        public bool Eof => _pos >= lines.Count;
        public int LineNumber => _pos + 1;
        public string Peek() => lines[_pos];
        public string Next() => lines[_pos++];
        public string Take() => Eof ? throw new EndOfFileException() : lines[_pos++];
        public List<string> TakeMany(int n)
        {
            var list = new List<string>(n);
            for (int i = 0; i < n; i++) list.Add(Take());
            return list;
        }
    }
}
