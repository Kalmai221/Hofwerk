using System.Diagnostics;

namespace Hofwerk.Core.Errors;

/// <summary>What the error dialog's buttons do.</summary>
public static class ErrorActions
{
    /// <summary>Starts Hofwerk again (without a file, since the file may be what went wrong). The caller then closes this copy.</summary>
    public static bool Restart()
    {
        string? program = Environment.ProcessPath;
        if (program is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(program) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(program)! })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>Opens a web page in the browser. Only plain https addresses are opened.</summary>
    public static bool OpenInBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
}
