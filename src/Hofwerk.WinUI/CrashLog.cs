namespace Hofwerk.WinUI;

/// <summary>
/// Writes unexpected errors to %LOCALAPPDATA%\Hofwerk\crash.log, so a crash can be reported with details
/// instead of "it just closed".
/// </summary>
static class CrashLog
{
    public static string Path_ { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hofwerk", "crash.log");

    public static void Write(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
            File.AppendAllText(Path_, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n");
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done if even the log cannot be written.
        }
    }
}
