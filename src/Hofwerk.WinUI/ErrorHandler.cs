using System.Runtime.InteropServices;
using Hofwerk.Core;
using Hofwerk.Core.Errors;
using Hofwerk.Core.Updates;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Hofwerk.WinUI;

/// <summary>
/// Catches the errors Hofwerk did not expect. Each one is written to crash.log, then a dialog offers to restart Hofwerk, to try the other
/// version of Hofwerk on this computer (the compatibility version, drawn another way), or to report the bug on GitHub with the
/// details already filled in. Nothing is sent anywhere unless the user presses Submit on the GitHub page.
/// </summary>
static class ErrorHandler
{
    static MainWindow? _window;
    static bool _showing;
    static DateTime _lastShown = DateTime.MinValue;

    public static void Attach(MainWindow window) => _window = window;

    /// <summary>Logs an error and, when there is a window to show it in, opens the error dialog. Safe to call from any thread.</summary>
    public static void Handle(Exception exception, string where)
    {
        CrashLog.Write(where, exception);
        var report = ErrorReport.From(exception, where, "WinUI");

        var window = _window;
        if (window is null || !window.TryRunOnUiThread(() => _ = ShowAsync(window, report)))
            NativeBox(report);
    }

    /// <summary>An error that ends the program: there is no window left to show a dialog in, so the system's own message box is used.</summary>
    public static void HandleFatal(Exception exception, string where)
    {
        CrashLog.Write(where, exception);
        NativeBox(ErrorReport.From(exception, where, "WinUI"));
    }

    static async Task ShowAsync(MainWindow window, ErrorReport report)
    {
        // One dialog at a time, and not one for every error of a burst: they are all in the log.
        if (_showing || DateTime.UtcNow - _lastShown < TimeSpan.FromSeconds(2)) return;
        _showing = true;
        try
        {
            if (window.DialogRoot is not { } root) { NativeBox(report); return; }

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            var progress = new ProgressBar { Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };

            var restart = new Button { Content = "Restart Hofwerk" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(restart, "Restart Hofwerk");
            var other = new Button
            {
                Content = "Try a different version",
                Visibility = WindowsUi.Current.CanSwitch ? Visibility.Visible : Visibility.Collapsed,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(other, "Try a different version");
            ToolTipService.SetToolTip(other, "Hofwerk also comes in a version that is drawn another way. If the standard one does not work on this computer, this one may. It is downloaded the first time (about 15 MB).");
            var send = new Button { Content = "Report a bug" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(send, "Report a bug");

            var details = new TextBox
            {
                Text = report.Text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                MaxHeight = 230,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(details, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(details, ScrollBarVisibility.Auto);

            var content = new StackPanel { Spacing = 12, MinWidth = 520 };
            content.Children.Add(new TextBlock { Text = report.Summary, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = "Hofwerk ran into a problem it did not expect. You can restart it, "
                       + (WindowsUi.Current.CanSwitch ? "try a different version, or report" : "or report") + " the bug so it can be fixed.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(restart);
            buttons.Children.Add(other);
            buttons.Children.Add(send);
            content.Children.Add(buttons);
            content.Children.Add(progress);
            content.Children.Add(status);
            content.Children.Add(new Expander
            {
                Header = "What a bug report contains",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = details,
            });
            content.Children.Add(new TextBlock
            {
                Text = "Your name is taken out of file paths. Nothing is sent until you press Submit on the GitHub page.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.65,
                FontSize = 12,
            });

            var dialog = new ContentDialog
            {
                XamlRoot = root,
                RequestedTheme = window.ActualThemeForDialogs,
                Title = "Something went wrong",
                Content = new ScrollViewer { Content = content, MaxHeight = 560, Padding = new Thickness(0, 0, 12, 0) },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 680.0;

            restart.Click += (_, _) =>
            {
                if (ErrorActions.Restart()) Application.Current.Exit();
                else Say(status, "Hofwerk could not be started again. Please start it from the Start menu.");
            };
            send.Click += (_, _) =>
            {
                if (!ErrorActions.OpenInBrowser(report.IssueUrl()))
                    Say(status, "The browser could not be opened. The report is in the box below; you can copy it into an issue at " + HofHeaderUrl + "/issues.");
            };
            other.Click += async (_, _) => await TryOtherVersionAsync(window, other, progress, status);

            _lastShown = DateTime.UtcNow;
            await dialog.ShowAsync();
        }
        finally
        {
            _showing = false;
            _lastShown = DateTime.UtcNow;
        }
    }

    const string HofHeaderUrl = Hofwerk.Core.Model.HofHeader.Url;

    static void Say(TextBlock status, string text)
    {
        status.Text = text;
        status.Visibility = Visibility.Visible;
    }

    // Starts the compatibility version, downloading it first if this computer does not have it yet.
    static async Task TryOtherVersionAsync(MainWindow window, Button button, ProgressBar progress, TextBlock status)
    {
        var ui = WindowsUi.Current;
        button.IsEnabled = false;
        try
        {
            if (!ui.CompatInstalled)
            {
                progress.Value = 0;
                progress.Visibility = Visibility.Visible;
                Say(status, "Downloading the other version…");
                var shown = new Progress<double>(p => { progress.Value = p; status.Text = $"Downloading the other version… {p:P0}"; });
                await ui.InstallCompatAsync(new UpdateChecker(), AppVersion.Current, Path.Combine(Path.GetTempPath(), "Hofwerk-compat"), shown);
            }

            string? file = window.CurrentFileIfSaved;
            if (ui.SwitchToCompat(AppSettings.Load(), file)) Application.Current.Exit();
            else Say(status, "The other version could not be started.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException
                                       or Hofwerk.Installer.Engine.InstallerException)
        {
            Say(status, $"The other version could not be downloaded: {ex.Message}");
        }
        finally
        {
            progress.Visibility = Visibility.Collapsed;
            button.IsEnabled = true;
        }
    }

    // ---- when there is no window to show a dialog in (an error while starting, or one that ends the program) ----------------------

    static void NativeBox(ErrorReport report)
    {
        string text = $"Hofwerk ran into a problem it did not expect:\n\n{report.Summary}\n\nThe details were saved to:\n{CrashLog.Path_}\n\nDo you want to report this problem on GitHub?";
        const uint MB_YESNO = 0x4, MB_ICONERROR = 0x10, IDYES = 6;
        try
        {
            if (MessageBoxW(IntPtr.Zero, text, "Hofwerk", MB_YESNO | MB_ICONERROR) == IDYES) ErrorActions.OpenInBrowser(report.IssueUrl());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
