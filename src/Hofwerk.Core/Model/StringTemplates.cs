using System.Text.RegularExpressions;

namespace Hofwerk.Core.Model;

/// <summary>What one string slot of a destination or bus stop means, e.g. "IBIS 1" or "ANNAX (side)".</summary>
public sealed record StringSlotInfo(string Name, string Hint);

public enum StringSlotKind { Terminus, BusStop }

/// <summary>
/// Names for the string slots, read from a strings file in the format OMSI add-ons ship with:
/// "string N:", "[TerminusString]" or "[BusstopString]", a German line, an English line ("Name|Hint"), then a mask line.
/// </summary>
public sealed partial class StringTemplates
{
    readonly Dictionary<StringSlotKind, Dictionary<int, StringSlotInfo>> _slots = new()
    {
        [StringSlotKind.Terminus] = [],
        [StringSlotKind.BusStop] = [],
    };

    public int Count(StringSlotKind kind) => _slots[kind].Count;

    public static StringTemplates Parse(string text) =>
        Parse(text.Split('\n').Select(l => l.TrimEnd('\r')).ToList());

    public static StringTemplates Parse(IReadOnlyList<string> lines)
    {
        var t = new StringTemplates();
        for (int i = 0; i + 3 < lines.Count; i++)
        {
            var header = SlotHeader().Match(lines[i]);
            if (!header.Success) continue;

            var kind = lines[i + 1].Trim() switch
            {
                "[TerminusString]" => (StringSlotKind?)StringSlotKind.Terminus,
                "[BusstopString]" => StringSlotKind.BusStop,
                _ => null,
            };
            if (kind is null || !int.TryParse(header.Groups[1].Value, out int index)) continue;

            // lines[i + 2] is the German text, lines[i + 3] the English one.
            string[] english = lines[i + 3].Split('|', 2);
            t._slots[kind.Value][index] =
                new StringSlotInfo(english[0].Trim(), english.Length > 1 ? english[1].Trim() : "");
        }
        return t;
    }

    /// <summary>Name for slot <paramref name="index"/>, or a generic "String N" when the slot has no known meaning.</summary>
    public StringSlotInfo Get(StringSlotKind kind, int index) =>
        _slots[kind].TryGetValue(index, out var info) ? info : new StringSlotInfo($"String {index}", "");

    [GeneratedRegex(@"^string (\d+):\s*$")]
    private static partial Regex SlotHeader();
}
