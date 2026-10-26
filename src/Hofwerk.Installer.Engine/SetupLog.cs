namespace Hofwerk.Installer.Engine;

/// <summary>Unexpected errors go to %TEMP%\Hofwerk-Setup.log, so "it failed" can be reported with details.</summary>
public static class SetupLog
{
    public static string Path_ { get; } = Path.Combine(Path.GetTempPath(), "Hofwerk-Setup.log");

    public static void Write(string what, Exception ex)
    {
        try { File.AppendAllText(Path_, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}\n{ex}\n\n"); }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException) { }
    }
}
