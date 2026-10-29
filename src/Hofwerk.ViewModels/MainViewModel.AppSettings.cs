using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Localization;

namespace Hofwerk.ViewModels;

// The App settings page: preferences for Hofwerk itself (updates, where OMSI is, the string preset), as opposed to the
// settings of the .hof file that is open.
/// <summary>One of the colour themes the user can pick: "System", "Light" or "Dark".</summary>
public sealed record ThemeChoice(string Id, string Label);

public partial class MainViewModel
{
    // The choices are rebuilt when the language changes, so their names are in the language in use.
    public ObservableCollection<ThemeChoice> ThemeChoices { get; } = [];
    public ObservableCollection<LanguageChoice> LanguageChoices { get; } = [];

    /// <summary>The autosave timings, named in the language in use (<see cref="AutoSaveChoice"/> is the one picked).</summary>
    public ObservableCollection<string> AutoSaveOptions { get; } = [];

    void BuildChoices()
    {
        ThemeChoices.Clear();
        foreach (var (id, label) in new[] { ("System", "System default"), ("Light", "Light"), ("Dark", "Dark") })
            ThemeChoices.Add(new ThemeChoice(id, Localizer.T(label)));

        LanguageChoices.Clear();
        foreach (var choice in Localizer.Choices()) LanguageChoices.Add(choice);

        AutoSaveOptions.Clear();
        foreach (string option in AutoSaveChoices) AutoSaveOptions.Add(Localizer.T(option));
    }

    // A drop-down that is emptied and filled again loses its selection, and a change announced while the drop-down is still busy with
    // that is ignored by the binding, so the picked entry would show blank. Announcing it a moment later gets it shown again.
    void RaiseLater(params string[] names)
    {
        void Raise() { foreach (string name in names) OnPropertyChanged(name); }
        if (_uiContext is null) Raise(); else _uiContext.Post(_ => Raise(), null);
    }

    void OnLanguageChanged()
    {
        BuildChoices();
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(SelectedLanguage));
        RaiseLater(nameof(SelectedTheme), nameof(SelectedLanguage), nameof(AutoSaveChoice));
        OnPropertyChanged(nameof(ActivePresetName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(FileStateText));
        OnPropertyChanged(nameof(UpdateHeadline));
        OnPropertyChanged(nameof(UpdateDetail));
        OnPropertyChanged(nameof(HasExportLocations));
        foreach (var row in Folders) row.RefreshTexts();
        foreach (string name in new[] { nameof(LatestTitle), nameof(LatestDetail), nameof(OmsiHeadline), nameof(OmsiDetail), nameof(OmsiFolderText), nameof(OmsiLocation),
                     nameof(FileDisplayName), nameof(FileFolder), nameof(PreReleaseHint), nameof(InstalledNotesTitle), nameof(InstalledNotesSubtitle), nameof(UpdateNotesTitle) })
            OnPropertyChanged(name);
        if (FilePath is null) Status = Localizer.T("Open a .hof file to start.");
    }

    Action<string> _useLanguage = id => Localizer.Use(id);

    /// <summary>For tests: what happens when another language is chosen (the real one switches the whole app).</summary>
    public void ConfigureLanguage(Action<string> use) => _useLanguage = use;

    /// <summary>The language setting: "auto" (the language Windows or the desktop is set to) or a language code.</summary>
    string LanguageSetting => string.IsNullOrWhiteSpace(_settings.Language) ? Localizer.Automatic : _settings.Language.Trim().ToLowerInvariant();

    /// <summary>The language chosen in App settings. Picking one saves it and switches the whole app to it at once.</summary>
    public LanguageChoice SelectedLanguage
    {
        get => LanguageChoices.FirstOrDefault(c => c.Id == LanguageSetting) ?? LanguageChoices[0];
        set
        {
            if (value is null || value.Id == SelectedLanguage.Id) return;
            _settings.Language = value.Id;
            _settings.Save();
            OnPropertyChanged();
            _useLanguage(value.Id);
        }
    }

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
            if (new System.IO.FileInfo(file).Length > 2_000_000) { Status = Localizer.T("That file is too big to be a strings file."); return null; }
            return new PresetImportViewModel(Hofwerk.Core.Presets.PresetImporter.ReadText(System.IO.File.ReadAllBytes(file)), file);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = Localizer.F("Could not read the file: {0}", ex.Message);
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
            Status = Localizer.F("Imported the preset {0} and switched to it", preset.Name);
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
        if (FilePath is null) { Status = Localizer.T("Open a .hof file first."); return null; }
        if (VehiclesFolder is not { } vehicles || !System.IO.Directory.Exists(vehicles))
        {
            Status = Localizer.T("Hofwerk does not know where your OMSI Vehicles folder is. Set the OMSI folder in App settings.");
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
            Status = Localizer.F("Could not save the file, so nothing was exported: {0}", ex.Message);
            export.Report = Status;
            return false;
        }

        var results = await export.RunAsync();
        int written = results.Count(r => r.Outcome == Hofwerk.Core.Omsi.ExportOutcome.Written);
        int notDone = results.Count(r => r.Outcome is Hofwerk.Core.Omsi.ExportOutcome.Failed or Hofwerk.Core.Omsi.ExportOutcome.Skipped);
        Status = Localizer.F("Exported {0}: ", System.IO.Path.GetFileName(FilePath)) + Localizer.Count(written, "{0} folder updated", "{0} folders updated") + (notDone > 0 ? Localizer.F(", {0} not done", notDone) : "");
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
            Status = Localizer.F("Could not create the file: {0}", ex.Message);
            return false;
        }

        if (!Open(path)) return false;
        Status = map is null
            ? Localizer.F("Created {0}", System.IO.Path.GetFileName(path))
            : Localizer.F("Created {0} from the map {1}: {2} destinations, {3} bus stops, {4} routes", System.IO.Path.GetFileName(path), map.Name, Termini.Count - 1, BusStops.Count, Routes.Count);
        return true;
    }

    public string OmsiFolderText => OmsiFolder ?? Localizer.T("Not found yet. Hofwerk looks for OMSI 2 in your Steam libraries.");

    /// <summary>Forgets a folder chosen by hand and looks for OMSI 2 again.</summary>
    [RelayCommand]
    void FindOmsiAutomatically()
    {
        _settings.OmsiFolder = null;
        _settings.Save();
        RefreshHome();
        Status = OmsiFolder is null ? Localizer.T("OMSI 2 was not found. Choose its folder by hand.") : Localizer.F("Found OMSI at {0}", OmsiFolder);
    }
}
