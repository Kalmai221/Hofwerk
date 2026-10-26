namespace Hofwerk.Core.Model;

/// <summary>
/// The [global_strings] block of a .hof file: three folder names (announcements, roll signs, side plates) followed by
/// three two-digit IBIS code offsets (for the 9xx, 8xx and 5xx routes). Anything after those six lines is kept as is.
/// Older files have a shorter block; those are written back unchanged unless an IBIS code is edited.
/// </summary>
public sealed class GlobalSettings
{
    string _ibis9xx = "00", _ibis8xx = "00", _ibis5xx = "00";
    bool _codesEdited;
    readonly List<string> _tail = [];

    public string AnnouncementsFolder { get; set; } = "";
    public string RollsignFolder { get; set; } = "";
    public string SidePlateFolder { get; set; } = "";

    public string Ibis9xx { get => _ibis9xx; set => Set(ref _ibis9xx, value); }
    public string Ibis8xx { get => _ibis8xx; set => Set(ref _ibis8xx, value); }
    public string Ibis5xx { get => _ibis5xx; set => Set(ref _ibis5xx, value); }

    /// <summary>Whether the block had the IBIS code lines at all (older files stop after the three folder names).</summary>
    public bool HasIbisCodes { get; private set; } = true;

    public static GlobalSettings From(IReadOnlyList<string> lines)
    {
        var g = new GlobalSettings();
        string At(int i) => i < lines.Count ? lines[i] : "";

        g.AnnouncementsFolder = At(0).TrimEnd();
        g.RollsignFolder = At(1).TrimEnd();
        g.SidePlateFolder = At(2).TrimEnd();

        if (lines.Count >= 6)
        {
            g._ibis9xx = At(3).Trim();
            g._ibis8xx = At(4).Trim();
            g._ibis5xx = At(5).Trim();
            g._tail.AddRange(lines.Skip(6));
        }
        else
        {
            g.HasIbisCodes = false;
            g._tail.AddRange(lines.Skip(3)); // kept verbatim, whatever an older version put there
        }
        return g;
    }

    public List<string> ToLines()
    {
        var lines = new List<string> { AnnouncementsFolder, RollsignFolder, SidePlateFolder };
        if (!HasIbisCodes && !_codesEdited)
        {
            lines.AddRange(_tail);
            return lines;
        }

        lines.Add(NormalizeCode(_ibis9xx));
        lines.Add(NormalizeCode(_ibis8xx));
        lines.Add(NormalizeCode(_ibis5xx));
        if (HasIbisCodes) lines.AddRange(_tail);
        return lines;
    }

    /// <summary>Two digits, "00" when empty: "7" becomes "07", "123" becomes "12", letters are dropped.</summary>
    public static string NormalizeCode(string? value)
    {
        string digits = new((value ?? "").Where(char.IsDigit).ToArray());
        return digits.Length switch { 0 => "00", 1 => "0" + digits, 2 => digits, _ => digits[..2] };
    }

    void Set(ref string field, string value)
    {
        if (field == value) return;
        field = value;
        _codesEdited = true;
    }
}
