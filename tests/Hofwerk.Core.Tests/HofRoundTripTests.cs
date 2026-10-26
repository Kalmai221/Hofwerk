using Hofwerk.Core;
using Hofwerk.Core.Model;

namespace Hofwerk.Core.Tests;

public class HofRoundTripTests
{
    const string Sample = """
        #####COMMENT#####
        my notes
        [name]
        Test Hof

        [servicetrip]
        Not in Service

        stringcount_terminus
        2

        stringcount_busstop
        1

        [addterminus_allexit]
        001
        NIS
        Blank
        ..........
        [addterminus]
        351
        Totnes
          TOTNES
        Totnes

        [addbusstop]
        Paignton
        PAIGNTON

        [infosystem_trip]
        3501
        PAIGNTON  >>  TOTNES
        351
        35

        [infosystem_busstop_list]
        2
        Paignton
        Totnes
        """;

    static HofDocument Parse(string text) => HofParser.Parse(text.Split('\n').Select(l => l.TrimEnd('\r')));

    [Fact]
    public void Parses_all_sections()
    {
        var d = Parse(Sample);
        Assert.Empty(d.Diagnostics);
        Assert.Equal("Test Hof", d.Name);
        Assert.Equal(2, d.Termini.Count);
        Assert.True(d.Termini[0].IsAllExit);
        Assert.Equal(["NIS", "Blank"], d.Termini[0].Strings);
        Assert.Equal("Totnes", d.Termini[1].Station);
        Assert.Equal("  TOTNES", d.Termini[1].Strings[0]); // leading spaces are significant
        Assert.Equal("Paignton", d.BusStops[0].Name);
        Assert.Equal(["Paignton", "Totnes"], d.Routes[0].Stops.Select(s => s.Name));
        Assert.Equal("#####COMMENT#####", d.Preamble[0]);
    }

    [Fact]
    public void Write_then_parse_is_stable()
    {
        string once = HofWriter.ToText(Parse(Sample));
        string twice = HofWriter.ToText(Parse(once));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Truncated_file_reports_error_instead_of_throwing()
    {
        var d = Parse("[name]\nX\nstringcount_terminus\n3\n[addterminus]\n1\n");
        Assert.Contains(d.Diagnostics, x => x.Severity == HofSeverity.Error);
    }

    [Fact]
    public void Parses_tab_separated_lists()
    {
        var d = Parse("stringcount_terminus\n2\nstringcount_busstop\n1\n[addterminus_list]\n{ALLEX}\t0\tEmpty\tEmpty\tx\n\t1\tTotnes\tTOTNES\tT\n[end]\n[addbusstop_list]\nA\tB\n[end]");
        Assert.Empty(d.Diagnostics);
        Assert.True(d.Termini[0].IsAllExit);
        Assert.Equal("Empty", d.Termini[0].Station);
        Assert.Equal(["Empty", "x"], d.Termini[0].Strings);
        Assert.Equal("Totnes", d.Termini[1].Station);
        Assert.Equal(["TOTNES", "T"], d.Termini[1].Strings);
        Assert.Equal(["B"], d.BusStops[0].Strings);
    }

    [Fact]
    public void List_rows_put_each_string_in_its_own_slot()
    {
        // A row from a real file written by the original HOF Suite: 16 columns, 14 strings after ident and name.
        string row = "{ALLEX}\t1001\tNot in Service\tNot in Service\t\t\t\t\t0\t\tNot_in_Service.png\t\t\t\tGeneric\\NIS.dds\tGeneric\\NIS.dds";
        var d = Parse($"stringcount_terminus\n13\n[addterminus_list]\n{row}\n[end]");

        var s = d.Termini[0].Strings;
        Assert.Equal("Not in Service", s[0]);
        Assert.Equal("", s[1]);
        Assert.Equal("0", s[5]);                      // IBIS 2
        Assert.Equal("Not_in_Service.png", s[7]);     // Krueger texture
        Assert.Equal(@"Generic\NIS.dds", s[11]);      // Full display (1)
        Assert.Equal(@"Generic\NIS.dds", s[12]);      // Full display (2)
    }

    [Fact]
    public void Real_omsi_files_parse_without_errors_and_round_trip()
    {
        string root = @"C:\Program Files (x86)\Steam\steamapps\common\OMSI 2";
        if (!Directory.Exists(root)) return; // corpus only exists on machines with OMSI installed

        var problems = new List<string>();
        foreach (string path in Directory.EnumerateFiles(root, "*.hof", SearchOption.AllDirectories))
        {
            var doc = HofFile.Load(path);
            foreach (var e in doc.Diagnostics.Where(x => x.Severity == HofSeverity.Error))
                problems.Add($"{path}:{e.Line} {e.Message}");
            string once = HofWriter.ToText(doc);
            if (once != HofWriter.ToText(Parse(once))) problems.Add($"{path}: not stable");
        }
        Assert.True(problems.Count == 0, string.Join('\n', problems.Take(25)));
    }
}
