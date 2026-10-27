using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Hofwerk.ViewModels;

// The App settings page: preferences for Hofwerk itself (updates, where OMSI is, the string preset), as opposed to the
// settings of the .hof file that is open.
/// <summary>One of the colour themes the user can pick: "System", "Light" or "Dark".</summary>
public sealed record ThemeChoice(string Id, string Label);

public partial class MainViewModel
{
    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
        [new("System", "System default"), new("Light", "Light"), new("Dark", "Dark")];

    /// <summary>Raised when the user picks another theme; the window applies it. Gets "System", "Light" or "Dark".</summary>
    public event Action<string>? ThemeChanged;

    /// <summary>The theme to start with (an unknown value in the settings file means "System").</summary>
    public string ThemeId => SelectedTheme.Id;

    public ThemeChoice SelectedTheme
    {
        get => ThemeChoices.FirstOrDefault(t => t.Id == _settings.Theme) ?? ThemeChoices[0];
        set
        {
            if (value is null || value.Id == SelectedTheme.Id) return;
            _settings.Theme = value.Id;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeId));
            ThemeChanged?.Invoke(value.Id);
        }
    }

    /// <summary>The preset in use, for the drop-down in App settings. Choosing another one switches to it.</summary>
    public PresetViewModel? SelectedPreset
    {
        get => Presets.FirstOrDefault(p => p.IsActive);
        set
        {
            if (value is not null && !value.IsActive) UsePreset(value);
        }
    }

    // ---- Status messages disappear by themselves ----------------------------------------------------------------

    readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;   // the thread that made the view model: the UI
    CancellationTokenSource? _statusTimer;

    /// <summary>How long a message stays in the status bar before it clears itself.</summary>
    public TimeSpan StatusLifetime { get; set; } = TimeSpan.FromSeconds(3);

    partial void OnStatusChanged(string value)
    {
        _statusTimer?.Cancel();
        _statusTimer = null;
        if (string.IsNullOrEmpty(value)) return;

        var timer = _statusTimer = new CancellationTokenSource();
        _ = Task.Delay(StatusLifetime, timer.Token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;
            // Only the message that started this timer is cleared; a newer one has its own.
            void Clear() { if (Status == value) Status = ""; }
            if (_uiContext is null) Clear(); else _uiContext.Post(_ => Clear(), null);
        }, TaskScheduler.Default);
    }

    /// <summary>The OMSI maps that have timetable data, for the New file dialog.</summary>
    public IReadOnlyList<Hofwerk.Core.Omsi.OmsiMap> FindMaps() =>
        OmsiFolder is null ? [] : Hofwerk.Core.Omsi.MapImporter.FindMaps(OmsiFolder);

    /// <summary>Reads a strings file the user picked and returns the questions to ask about it; null (with the reason in the status bar) when it cannot be read.</summary>
    public PresetImportViewModel? StartPresetImport(string file)
    {
        try
        {
            if (new System.IO.FileInfo(file).Length > 2_000_000) { Status = "That file is too big to be a strings file."; return null; }
            return new PresetImportViewModel(Hofwerk.Core.Presets.PresetImporter.ReadText(System.IO.File.ReadAllBytes(file)), file);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the file: {ex.Message}";
            return null;
        }
    }

    /// <summary>Saves the imported preset in the user's presets folder and switches to it. False, with the reason in the status bar, when it fails.</summary>
    public bool FinishPresetImport(PresetImportViewModel import)
    {
        try
        {
            var preset = import.Import(_userPresetsFolder);
            RefreshPresets();
            if (Presets.FirstOrDefault(p => p.Preset.Id == preset.Id) is { } added) UsePreset(added);
            Status = $"Imported the preset {preset.Name} and switched to it";
            return true;
        }
        catch (Hofwerk.Core.Presets.PresetImportException ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    /// <summary>True when there is an open file and an OMSI install with a Vehicles folder to export into.</summary>
    public bool CanExport => FilePath is not null && VehiclesFolder is { } v && System.IO.Directory.Exists(v);

    /// <summary>The export dialog, with the folders the file already lists (its HOF Suite export list) ticked. Null, with the reason in the status bar, when there is nothing to export into.</summary>
    public ExportViewModel? StartExport()
    {
        if (FilePath is null) { Status = "Open a .hof file first."; return null; }
        if (VehiclesFolder is not { } vehicles || !System.IO.Directory.Exists(vehicles))
        {
            Status = "Hofwerk does not know where your OMSI Vehicles folder is. Set the OMSI folder in App settings.";
            return null;
        }
        return new ExportViewModel(FilePath, vehicles, ExportLocationNames());
    }

    /// <summary>
    /// Keeps the ticked folders in the file (as HOF Suite does), saves the file, then puts it into each of them. The result is in the
    /// dialog (<see cref="ExportViewModel.Report"/>) and in the status bar. False when the file could not be saved.
    /// </summary>
    public async Task<bool> ExportAsync(ExportViewModel export)
    {
        if (FilePath is null) return false;
        try
        {
            // What is ticked becomes the file's export list (shown on the Settings page), then the file is saved with it.
            LoadExportLocations(export.SelectedNames);
            Save(FilePath);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save the file, so nothing was exported: {ex.Message}";
            export.Report = Status;
            return false;
        }

        var results = await export.RunAsync();
        int written = results.Count(r => r.Outcome == Hofwerk.Core.Omsi.ExportOutcome.Written);
        int notDone = results.Count(r => r.Outcome is Hofwerk.Core.Omsi.ExportOutcome.Failed or Hofwerk.Core.Omsi.ExportOutcome.Skipped);
        Status = $"Exported {System.IO.Path.GetFileName(FilePath)}: {written} folder{(written == 1 ? "" : "s")} updated" + (notDone > 0 ? $", {notDone} not done" : "");
        return true;
    }

    /// <summary>The dialog for New: start empty or from a map.</summary>
    public NewFileViewModel StartNewFile() => new(FindMaps());

    /// <summary>
    /// Makes a new .hof file at <paramref name="path"/> and opens it: empty, or filled from the timetable files of an OMSI map
    /// (<paramref name="map"/>). Either way it has the string slots of the active preset and Hofwerk's credit and notes.
    /// Returns false, and says why in the status bar, if it can't be written.
    /// </summary>
    public bool CreateNew(string path, Hofwerk.Core.Omsi.MapData? map = null, Hofwerk.Core.Omsi.MapImportOptions? options = null)
    {
        try
        {
            int terminusStrings = Names.Count(Hofwerk.Core.Model.StringSlotKind.Terminus);
            int busStopStrings = Names.Count(Hofwerk.Core.Model.StringSlotKind.BusStop);
            var doc = map is null
                ? Hofwerk.Core.HofFile.CreateNew(System.IO.Path.GetFileNameWithoutExtension(path), terminusStrings, busStopStrings)
                : Hofwerk.Core.HofFile.CreateFromMap(map, terminusStrings, busStopStrings, options ?? new Hofwerk.Core.Omsi.MapImportOptions());
            Hofwerk.Core.HofFile.Save(doc, path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Could not create the file: {ex.Message}";
            return false;
        }

        if (!Open(path)) return false;
        Status = map is null
            ? $"Created {System.IO.Path.GetFileName(path)}"
            : $"Created {System.IO.Path.GetFileName(path)} from the map {map.Name}: {Termini.Count - 1} destinations, {BusStops.Count} bus stops, {Routes.Count} routes";
        return true;
    }

    public string OmsiFolderText => OmsiFolder ?? "Not found yet. Hofwerk looks for OMSI 2 in your Steam libraries.";

    /// <summary>Forgets a folder chosen by hand and looks for OMSI 2 again.</summary>
    [RelayCommand]
    void FindOmsiAutomatically()
    {
        _settings.OmsiFolder = null;
        _settings.Save();
        RefreshHome();
        Status = OmsiFolder is null ? "OMSI 2 was not found. Choose its folder by hand." : $"Found OMSI at {OmsiFolder}";
    }
}
