using Hofwerk.Core.Errors;
using Hofwerk.Core.Model;

namespace Hofwerk.Core.Tests;

public class ErrorReportTests
{
    static Exception Thrown(string message = "Something went wrong")
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void The_report_says_what_the_error_was_where_it_was_caught_and_on_what_system()
    {
        var report = ErrorReport.From(Thrown("The list is empty"), "Unhandled exception", "WinUI");

        Assert.Equal("System.InvalidOperationException", report.Kind);
        Assert.Equal("InvalidOperationException: The list is empty", report.Summary);
        Assert.Equal("Error: InvalidOperationException: The list is empty", report.Title);
        Assert.Contains("`System.InvalidOperationException`: The list is empty", report.Text);
        Assert.Contains("caught by: Unhandled exception", report.Text);
        Assert.Contains("(WinUI)", report.Text);
        Assert.Contains("Hofwerk ", report.Text);
        Assert.Contains(".NET", report.Text);
        Assert.Contains("What were you doing", report.Text);               // asks the person to say what they did
    }

    [Fact]
    public void The_user_name_and_profile_folder_are_taken_out_of_the_report()
    {
        string profile = @"C:\Users\Jane Doe";
        string text = @"Could not open C:\Users\Jane Doe\Documents\depot.hof and C:/Users/Jane Doe/x and D:\Backups\Jane Doe\old.hof";

        string redacted = Privacy.Redact(text, profile, "Jane Doe");

        Assert.DoesNotContain("Jane", redacted);
        Assert.Contains("depot.hof", redacted);                              // what is useful stays
        Assert.Contains(@"\<user>\", redacted);
    }

    [Fact]
    public void Redaction_ignores_letter_case_and_short_or_empty_names()
    {
        Assert.DoesNotContain("jane", Privacy.Redact(@"c:\users\JANE DOE\file", @"C:\Users\Jane Doe", "Jane Doe"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"C:\a\b\c", Privacy.Redact(@"C:\a\b\c", "", ""));
        Assert.Equal(@"C:\ab\x", Privacy.Redact(@"C:\ab\x", @"C:\nobody", "ab"));      // a two-letter name would match too much to be safe
    }

    [Fact]
    public void The_issue_link_opens_a_new_issue_with_the_title_and_the_report_filled_in()
    {
        var report = ErrorReport.From(Thrown("Boom"), "Unhandled exception", "Avalonia");

        string url = report.IssueUrl();
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        Assert.StartsWith("https://github.com/Kalmai221/Hofwerk/issues/new?", url);
        Assert.Equal("bug", query["labels"]);
        Assert.Equal(report.Title, query["title"]);
        Assert.Equal(report.Text, query["body"]);
        Assert.Contains("(Avalonia)", query["body"]);
    }

    [Fact]
    public void A_very_long_stack_trace_is_cut_so_the_address_stays_short_enough_for_GitHub()
    {
        string deep = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"   at Hofwerk.Some.Namespace.SomeClass.SomeMethod{i}(String argument) in File{i}.cs:line {i}"));
        var report = ErrorReport.From(new Exception(deep), "Unhandled exception", "WinUI");

        string url = report.IssueUrl();

        Assert.True(url.Length <= ErrorReport.MaxUrlLength, $"address is {url.Length} characters");
        var body = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["body"]!;
        Assert.Contains("crash.log", body);                                  // says the rest is in the log
        Assert.Contains("Your system", body);                                // the important parts are kept
        Assert.Contains("What were you doing", body);
    }

    [Fact]
    public void A_message_over_several_lines_gives_a_one_line_title()
    {
        var report = ErrorReport.From(new Exception("first line\r\nsecond line\nthird"), "x", "WinUI");

        Assert.DoesNotContain('\n', report.Title);
        Assert.Contains("first line", report.Title);
        Assert.DoesNotContain("second line", report.Title);
        Assert.True(report.Title.Length <= 110);
    }

    [Fact]
    public void Only_https_addresses_are_opened_in_the_browser()
    {
        Assert.False(ErrorActions.OpenInBrowser("http://example.com"));
        Assert.False(ErrorActions.OpenInBrowser("file:///C:/Windows/notepad.exe"));
        Assert.False(ErrorActions.OpenInBrowser("javascript:alert(1)"));
        Assert.False(ErrorActions.OpenInBrowser("not an address"));
    }

    [Fact]
    public void The_crash_log_gets_a_dated_entry_with_the_whole_error()
    {
        string log = CrashLog.Path_;
        string before = File.Exists(log) ? File.ReadAllText(log) : "";
        try
        {
            CrashLog.Write("Test entry", Thrown("logged for a test"));

            string after = File.ReadAllText(log);
            Assert.StartsWith(before, after);
            string added = after[before.Length..];
            Assert.Contains("Test entry", added);
            Assert.Contains("logged for a test", added);
            Assert.Contains("InvalidOperationException", added);
        }
        finally
        {
            // put the file back the way it was, so a real log is not filled with test entries
            if (before.Length == 0) { try { File.Delete(log); } catch (IOException) { } }
            else File.WriteAllText(log, before);
        }
    }

    [Fact]
    public void The_marker_in_new_files_is_still_the_credit_text()
    {
        Assert.Equal("https://github.com/Kalmai221/Hofwerk", HofHeader.Url);
    }
}
