using System.Text;
using Hofwerk.Core.Model;

namespace Hofwerk.Core;

public static class HofWriter
{
    public static List<string> ToLines(HofDocument doc)
    {
        var o = new List<string>();
        if (doc.Preamble.Count > 0) { o.AddRange(doc.Preamble); o.Add(""); }

        if (doc.SuiteDirectories.Count > 0)
        {
            o.Add("[HOF_SUITE_DIRECTORIES]");
            o.AddRange(doc.SuiteDirectories);
            o.Add("[HOF_SUITE_DIRECTORIES_END]");
            o.Add("");
        }

        o.Add("[name]"); o.Add(doc.Name); o.Add("");
        o.Add("[servicetrip]"); o.Add(doc.ServiceTrip); o.Add("");
        o.Add("stringcount_terminus"); o.Add(doc.TerminusStringCount.ToString()); o.Add("");
        o.Add("stringcount_busstop"); o.Add(doc.BusStopStringCount.ToString()); o.Add("");

        if (doc.GlobalStrings.Count > 0)
        {
            o.Add("[global_strings]"); o.Add(doc.GlobalStrings.Count.ToString());
            o.AddRange(doc.GlobalStrings); o.Add("");
        }

        foreach (var t in doc.Termini)
        {
            o.Add(t.IsAllExit ? "[addterminus_allexit]" : "[addterminus]");
            o.Add(t.Ident);
            if (!t.IsAllExit) o.Add(t.Station);
            o.AddRange(Pad(t.Strings, doc.TerminusStringCount));
            o.Add("");
        }

        foreach (var b in doc.BusStops)
        {
            o.Add("[addbusstop]"); o.Add(b.Name);
            o.AddRange(Pad(b.Strings, doc.BusStopStringCount));
            o.Add("");
        }

        foreach (var r in doc.Routes)
        {
            o.Add("[infosystem_trip]"); o.Add(r.Ident); o.Add(r.Name); o.Add(r.TerminusIdent); o.Add(r.Line); o.Add("");
            if (r.Stops.Any(s => s.Extra.Count > 0))
            {
                foreach (var s in r.Stops) { o.Add("[infosystem_busstop]"); o.Add(s.Name); o.AddRange(Pad(s.Extra, 2)); o.Add(""); }
            }
            else
            {
                o.Add("[infosystem_busstop_list]"); o.Add(r.Stops.Count.ToString());
                o.AddRange(r.Stops.Select(s => s.Name)); o.Add("");
            }
        }
        return o;
    }

    public static string ToText(HofDocument doc) => string.Join("\r\n", ToLines(doc)) + "\r\n";

    // The reader expects exactly `count` string lines per entry, whatever the editor holds.
    static IEnumerable<string> Pad(List<string> strings, int count) =>
        Enumerable.Range(0, count).Select(i => i < strings.Count ? strings[i] : "");
}

public static class HofFile
{
    static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static HofDocument Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.AsSpan().StartsWith(Utf8Bom);
        Encoding enc = bom ? new UTF8Encoding(true) : HofEncodings.Windows1252;
        string text = enc.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        // Some files contain "\r\r\n"; drop any stray trailing CR so it can't end up inside a value.
        var doc = HofParser.Parse(text.Split('\n').Select(l => l.TrimEnd('\r')));
        doc.Encoding = enc;
        return doc;
    }

    public static void Save(HofDocument doc, string path)
    {
        // Write to a temp file first so a crash never leaves a half-written file in the OMSI folder.
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, [.. doc.Encoding.GetPreamble(), .. doc.Encoding.GetBytes(HofWriter.ToText(doc))]);
        File.Move(tmp, path, overwrite: true);
    }
}
