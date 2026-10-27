using System.Runtime.InteropServices;
using System.Text;
using Hofwerk.Core.Model;
using Hofwerk.Core.Updates;

namespace Hofwerk.Core.Errors;

/// <summary>
/// What goes into a bug report when Hofwerk hits an error: what kind of error, where it happened, and what system it ran on. The text is
/// shown to the user before anything is sent, personal parts of paths (the user name) are removed, and nothing leaves the computer
/// until the user presses "Submit" on the GitHub page that opens.
/// </summary>
public sealed class ErrorReport
{
    /// <summary>The longest address that is opened: GitHub accepts about 8 000 characters, so there is some room to spare.</summary>
    public const int MaxUrlLength = 7000;

    ErrorReport(string kind, string message, string stack, string where, string ui)
    {
        Kind = kind;
        Message = message;
        Stack = stack;
        Where = where;
        Ui = ui;
    }

    /// <summary>The exception type, for example <c>System.InvalidOperationException</c>.</summary>
    public string Kind { get; }
    public string Message { get; }
    public string Stack { get; }
    /// <summary>Which handler caught it ("Unhandled exception", "Background task", ...).</summary>
    public string Where { get; }
    /// <summary>"WinUI" or "Avalonia".</summary>
    public string Ui { get; }

    public static ErrorReport From(Exception exception, string where, string ui) =>
        new(exception.GetType().FullName ?? "Exception", Privacy.Redact(exception.Message), Privacy.Redact(exception.ToString()), where, ui);

    /// <summary>One line for the dialog: "InvalidOperationException: the message".</summary>
    public string Summary => $"{ShortKind}: {FirstLine(Message)}".TrimEnd(' ', ':');

    string ShortKind => Kind[(Kind.LastIndexOf('.') + 1)..];

    /// <summary>The title of the GitHub issue.</summary>
    public string Title => Cut($"Error: {Summary}", 110);

    /// <summary>The system Hofwerk ran on, in words a person can read.</summary>
    public static string SystemDescription =>
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}";

    /// <summary>The report as it is shown to the user and written into the issue. Longer stack traces are cut to fit an address.</summary>
    public string Text => Build(int.MaxValue);

    string Build(int stackLines)
    {
        string[] stack = Stack.Replace("\r\n", "\n").Split('\n');
        string shownStack = stack.Length <= stackLines
            ? string.Join('\n', stack)
            : string.Join('\n', stack.Take(stackLines)) + "\n… (cut here; the whole report is in the crash.log file on your computer)";

        return new StringBuilder()
            .AppendLine("**What were you doing when this happened?**")
            .AppendLine("<!-- Please write a few words: what you clicked or opened. It helps a lot. -->")
            .AppendLine()
            .AppendLine()
            .AppendLine("**The error**")
            .AppendLine($"`{Kind}`: {FirstLine(Message)}")
            .AppendLine($"(caught by: {Where})")
            .AppendLine()
            .AppendLine("<details><summary>Technical details</summary>")
            .AppendLine()
            .AppendLine("```")
            .AppendLine(shownStack)
            .AppendLine("```")
            .AppendLine("</details>")
            .AppendLine()
            .AppendLine("**Your system**")
            .AppendLine($"- Hofwerk {AppVersion.Current.Text} ({Ui})")
            .AppendLine($"- {SystemDescription}")
            .ToString();
    }

    /// <summary>
    /// The address of a new GitHub issue with the title and the report already filled in. The stack trace is cut as far as needed so the
    /// address stays under <see cref="MaxUrlLength"/>.
    /// </summary>
    public string IssueUrl()
    {
        string Url(string body) =>
            $"{HofHeader.Url}/issues/new?labels=bug&title={Uri.EscapeDataString(Title)}&body={Uri.EscapeDataString(body)}";

        int lines = Stack.Replace("\r\n", "\n").Split('\n').Length;
        string url = Url(Build(lines));
        while (url.Length > MaxUrlLength && lines > 0)
        {
            lines = lines > 12 ? lines * 2 / 3 : lines - 1;
            url = Url(Build(Math.Max(lines, 0)));
        }
        return url;
    }

    static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        return Cut(end < 0 ? text : text[..end], 300).Trim();
    }

    static string Cut(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}

/// <summary>Takes the parts out of text that say who the user is, so a report can be shared without them.</summary>
public static class Privacy
{
    /// <summary>Replaces the user's profile folder and user name with neutral words, in any letter case and with either slash.</summary>
    public static string Redact(string text, string? profile = null, string? userName = null)
    {
        profile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        userName ??= Environment.UserName;

        if (!string.IsNullOrWhiteSpace(profile))
        {
            foreach (string variant in new[] { profile, profile.Replace('\\', '/'), profile.Replace('/', '\\') }.Distinct())
                text = text.Replace(variant, OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~", StringComparison.OrdinalIgnoreCase);
        }

        // Any path that still has the user name in it (another drive, a network share) keeps its shape without the name.
        if (userName.Length >= 3)
        {
            foreach (string sep in new[] { "\\", "/" })
                text = text.Replace($"{sep}{userName}{sep}", $"{sep}<user>{sep}", StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }
}
