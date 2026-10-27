using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Hofwerk.Core.Errors;
using Hofwerk.Linux.Views;

namespace Hofwerk.Linux;

/// <summary>
/// Catches the errors Hofwerk did not expect. Each one is written to crash.log, then a window offers to restart Hofwerk, to go back to
/// the standard Windows version (when this is the compatibility version), or to report the bug on GitHub with the details already
/// filled in. Nothing is sent anywhere unless the user presses Submit on the GitHub page.
/// </summary>
static class ErrorHandler
{
    static bool _showing;
    static DateTime _lastShown = DateTime.MinValue;

    /// <summary>Logs an error and shows the error window. Safe to call from any thread.</summary>
    public static void Handle(Exception exception, string where)
    {
        CrashLog.Write(where, exception);
        var report = ErrorReport.From(exception, where, "Avalonia");

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            Dispatcher.UIThread.Post(() => _ = ShowAsync(owner, report));
        else
            Console.Error.WriteLine(report.Text);
    }

    /// <summary>An error that ends the program: there is no window to show, so the log, the terminal and (on Windows) a message box are used.</summary>
    public static void HandleFatal(Exception exception, string where)
    {
        CrashLog.Write(where, exception);
        var report = ErrorReport.From(exception, where, "Avalonia");
        Console.Error.WriteLine(report.Text);

        if (!OperatingSystem.IsWindows()) return;
        string text = $"Hofwerk ran into a problem it did not expect:\n\n{report.Summary}\n\nThe details were saved to:\n{CrashLog.Path_}\n\nDo you want to report this problem on GitHub?";
        try
        {
            if (MessageBoxW(IntPtr.Zero, text, "Hofwerk", 0x4 | 0x10) == 6) ErrorActions.OpenInBrowser(report.IssueUrl());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    static async System.Threading.Tasks.Task ShowAsync(Window owner, ErrorReport report)
    {
        // One window at a time, and not one for every error of a burst: they are all in the log.
        if (_showing || DateTime.UtcNow - _lastShown < TimeSpan.FromSeconds(2)) return;
        _showing = true;
        try
        {
            string? file = owner.DataContext is Hofwerk.ViewModels.MainViewModel { FilePath: { } path, IsDirty: false } && System.IO.File.Exists(path) ? path : null;
            await new ErrorWindow(report, file).ShowDialog(owner);
        }
        finally
        {
            _showing = false;
            _lastShown = DateTime.UtcNow;
        }
    }

    /// <summary>Closes the program after another copy of Hofwerk was started.</summary>
    public static void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Environment.Exit(0);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
