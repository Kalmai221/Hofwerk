using System.Text;
using Hofwerk.Core.Errors;

namespace Hofwerk.ViewModels;

// Diagnostics: what the app knows about itself, in one place, and a few helpers for looking into a problem.
public partial class MainViewModel
{
    public int UndoSteps => _undo.Count;
    public int RedoSteps => _redo.Count;

    public string SettingsFolder => Path.GetDirectoryName(_settings.FilePath) ?? "";
    public string CrashLogFile => CrashLog.Path_;

    /// <summary>Forgets the list of recently opened files (the files themselves are not touched).</summary>
    public void ClearRecentFiles()
    {
        _recent.Clear();
        SetRecent([]);
        Status = "Recent files cleared";
    }

    /// <summary>
    /// A plain-text summary of the app and the open file for the diagnostics window. The user's name is taken out of paths,
    /// so it can be pasted into a bug report as it is.
    /// </summary>
    /// <param name="ui">"WinUI" or "Avalonia".</param>
    public string DiagnosticsReport(string ui)
    {
        var sb = new StringBuilder();
        void Heading(string text) { if (sb.Length > 0) sb.AppendLine(); sb.AppendLine(text.ToUpperInvariant()); }
        void Line(string name, string? value) => sb.AppendLine($"  {name,-14}{value}");

        Heading("Hofwerk");
        Line("Version", Hofwerk.Core.Updates.AppVersion.Current.Text);
        Line("Drawn with", ui);
        Line("System", ErrorReport.SystemDescription);
        Line("Installed in", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

        Heading("Files");
        Line("Settings", _settings.FilePath);
        Line("Recent files", _settings.RecentFilesPath);
        Line("Presets", UserPresetsFolder);
        string crash = CrashLog.Path_;
        Line("Crash log", File.Exists(crash) ? $"{crash} ({new FileInfo(crash).Length / 1024.0:0.#} KB)" : $"{crash} (none)");

        Heading("OMSI");
        Line("Folder", OmsiFolder ?? "not found");
        Line("Vehicles", VehiclesFolder is { } v && Directory.Exists(v) ? v : "not found");

        Heading("Open file");
        Line("File", FilePath ?? "none");
        if (FilePath is not null)
        {
            Line("Unsaved", IsDirty ? "yes" : "no");
            Line("Contents", $"{Termini.Count} destinations, {BusStops.Count} bus stops, {Routes.Count} routes, {Problems.Count} problems");
            Line("Undo / redo", $"{UndoSteps} / {RedoSteps} steps");
            Line("Export folders", ExportLocations.Count == 0 ? "none" : string.Join(", ", ExportLocations.Select(l => l.Name)));
        }

        Heading("Settings");
        Line("Theme", ThemeId);
        Line("String preset", ActivePresetName);
        Line("Autosave", AutoSave ? $"on ({AutoSaveChoices[AutoSaveChoice]})" : "off");
        Line("Updates", $"{(CheckForUpdatesOnStart ? "checked at start" : "not checked at start")}, pre-releases {(IncludePreReleases ? "on" : "off")}");

        return Privacy.Redact(sb.ToString().TrimEnd());
    }
}
