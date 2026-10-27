using System.Reflection;
using System.Text.RegularExpressions;

namespace Hofwerk.Core.Updates;

/// <summary>One heading of release notes ("Added", "Fixed", ...) and its points. <see cref="Title"/> is empty for loose text.</summary>
public sealed record ChangeGroup(string Title, IReadOnlyList<string> Items);

/// <summary>
/// Turns the markdown of release notes (a section of CHANGELOG.md, or the text of a GitHub release) into plain groups and
/// bullet points the app can show: headings become group titles, "- " and "* " lines become points, and the markdown
/// marks (bold, code, links) are removed.
/// </summary>
public static partial class ChangeNotes
{
    /// <summary>The marker the release workflow puts between a version's notes and the download guide that follows them.</summary>
    public const string EndOfNotes = "<!-- end-of-notes -->";

    public static IReadOnlyList<ChangeGroup> FromMarkdown(string? markdown)
    {
        var groups = new List<ChangeGroup>();
        if (string.IsNullOrWhiteSpace(markdown)) return groups;

        // A release on GitHub ends its notes with this marker, then adds text that is only for people reading the release page.
        int end = markdown.IndexOf(EndOfNotes, StringComparison.Ordinal);
        if (end >= 0) markdown = markdown[..end];

        string title = "";
        var items = new List<string>();
        bool continuing = false;

        void Close()
        {
            if (items.Count > 0) groups.Add(new ChangeGroup(title, items.ToList()));
            items.Clear();
            continuing = false;
        }

        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Trim().Length == 0) { continuing = false; continue; }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                Close();
                title = Plain(heading.Groups[1].Value);
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                items.Add(Plain(bullet.Groups[1].Value));
                continuing = true;
                continue;
            }

            // A wrapped line of the point above (indented), or a loose sentence.
            if (continuing && char.IsWhiteSpace(line[0]) && items.Count > 0) items[^1] += " " + Plain(line);
            else
            {
                items.Add(Plain(line));
                continuing = false;
            }
        }
        Close();
        return groups;
    }

    /// <summary>Removes markdown marks from one line, keeping the words.</summary>
    internal static string Plain(string text)
    {
        text = LinkPattern().Replace(text, "$1");              // [words](address) -> words
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");     // **bold**
        text = Regex.Replace(text, @"`([^`]+)`", "$1");         // `code`
        text = Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "");    // stray HTML tags
        return text.Trim();
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*)$")] private static partial Regex HeadingPattern();
    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.*)$")] private static partial Regex BulletPattern();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")] private static partial Regex LinkPattern();
}

/// <summary>One "## [version]" part of CHANGELOG.md.</summary>
public sealed record ChangelogSection(string Version, string Heading, string Body)
{
    public IReadOnlyList<ChangeGroup> Groups => ChangeNotes.FromMarkdown(Body);
}

/// <summary>CHANGELOG.md (shipped inside the app) split into its versions, newest first as written in the file.</summary>
public sealed class Changelog
{
    public IReadOnlyList<ChangelogSection> Sections { get; }

    Changelog(IReadOnlyList<ChangelogSection> sections) => Sections = sections;

    public static Changelog Parse(string markdown)
    {
        var sections = new List<ChangelogSection>();
        string? version = null, heading = null;
        var body = new List<string>();

        void Close()
        {
            if (version is not null) sections.Add(new ChangelogSection(version, heading!, string.Join('\n', body).Trim()));
            body.Clear();
        }

        foreach (string line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            // "## [0.1-dev2] - pre-release" starts a version; the link list at the end of the file is not part of one.
            var start = Regex.Match(line, @"^##\s+\[([^\]]+)\](.*)$");
            if (start.Success)
            {
                Close();
                version = start.Groups[1].Value.Trim();
                heading = line.TrimStart('#', ' ');
            }
            else if (version is not null && !Regex.IsMatch(line, @"^\[[^\]]+\]:\s"))
            {
                body.Add(line);
            }
        }
        Close();
        return new Changelog(sections);
    }

    /// <summary>The changelog that comes with this app (the CHANGELOG.md of the project, embedded when it was built).</summary>
    public static Changelog Embedded
    {
        get
        {
            using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is null) return Parse("");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
    }

    /// <summary>The notes written for exactly this version, or null.</summary>
    public ChangelogSection? Find(AppVersion version) =>
        Sections.FirstOrDefault(s => AppVersion.TryParse(s.Version) is { } v && v.Equals(version));

    /// <summary>The newest version that has something written under it (not "Unreleased").</summary>
    public ChangelogSection? Newest =>
        Sections.FirstOrDefault(s => !s.Version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase) && s.Groups.Count > 0);
}
