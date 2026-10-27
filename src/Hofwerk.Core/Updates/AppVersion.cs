using System.Reflection;

namespace Hofwerk.Core.Updates;

/// <summary>
/// A release version such as <c>0.1.0</c>, <c>1.2.3.4</c> or <c>0.1-dev1</c>. Numbers compare as numbers, and a version
/// with a suffix (<c>-dev1</c>, <c>-rc2</c>) is older than the same version without one.
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    readonly int[] _numbers;
    readonly string _suffix;

    AppVersion(int[] numbers, string suffix, string text)
    {
        _numbers = numbers;
        _suffix = suffix;
        Text = text;
    }

    /// <summary>The version as written, without a leading "v" or build metadata.</summary>
    public string Text { get; }

    public bool IsPreRelease => _suffix.Length > 0;

    public static AppVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text[0] is 'v' or 'V') text = text[1..];

        int plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        int dash = text.IndexOf('-');
        string numberPart = dash >= 0 ? text[..dash] : text;
        string suffix = dash >= 0 ? text[(dash + 1)..] : "";

        var numbers = new List<int>();
        foreach (var part in numberPart.Split('.'))
        {
            if (!int.TryParse(part, out int n) || n < 0) return null;
            numbers.Add(n);
        }
        return numbers.Count == 0 ? null : new AppVersion([.. numbers], suffix, text);
    }

    /// <summary>The version of the running app, from the version it was built with (the release tag).</summary>
    public static AppVersion Current { get; } =
        TryParse((Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? TryParse("0.0.0")!;

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;

        int length = Math.Max(_numbers.Length, other._numbers.Length);
        for (int i = 0; i < length; i++)
        {
            int a = i < _numbers.Length ? _numbers[i] : 0, b = i < other._numbers.Length ? other._numbers[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        if (_suffix.Length == 0 && other._suffix.Length == 0) return 0;
        if (_suffix.Length == 0) return 1;       // 1.0 is newer than 1.0-dev3
        if (other._suffix.Length == 0) return -1;
        return CompareSuffix(_suffix, other._suffix);
    }

    // "dev2" before "dev10": letters compare as text, digits as numbers.
    static int CompareSuffix(string a, string b)
    {
        var x = Chunks(a).ToList();
        var y = Chunks(b).ToList();
        for (int i = 0; i < Math.Min(x.Count, y.Count); i++)
        {
            bool xn = long.TryParse(x[i], out long xv), yn = long.TryParse(y[i], out long yv);
            int c = xn && yn ? xv.CompareTo(yv) : string.Compare(x[i], y[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return x.Count.CompareTo(y.Count);
    }

    static IEnumerable<string> Chunks(string s)
    {
        int start = 0;
        for (int i = 1; i <= s.Length; i++)
        {
            if (i == s.Length || char.IsDigit(s[i]) != char.IsDigit(s[i - 1]) || s[i] is '.' or '-')
            {
                string chunk = s[start..i].Trim('.', '-');
                if (chunk.Length > 0) yield return chunk;
                start = i;
            }
        }
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => Equals(obj as AppVersion);
    public override int GetHashCode() => Text.ToLowerInvariant().GetHashCode();
    public override string ToString() => Text;

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
}
