using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;
using Hofwerk.Core.Presets;

namespace Hofwerk.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // Parts of the file the editor doesn't expose (header text, global strings, encoding) travel through here.
    HofDocument _source = new();

    [ObservableProperty] public partial string? FilePath { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Open a .hof file to start.";

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string ServiceTrip { get; set; } = "";
    [ObservableProperty] public partial int TerminusStringCount { get; set; }
    [ObservableProperty] public partial int BusStopStringCount { get; set; }

    public ObservableCollection<TerminusViewModel> Termini { get; } = [];
    public ObservableCollection<BusStopViewModel> BusStops { get; } = [];
    public ObservableCollection<RouteViewModel> Routes { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty] public partial TerminusViewModel? SelectedTerminus { get; set; }
    [ObservableProperty] public partial BusStopViewModel? SelectedBusStop { get; set; }
    [ObservableProperty] public partial RouteViewModel? SelectedRoute { get; set; }

    public bool HasProblems => Problems.Count > 0;
    public bool HasFile => FilePath is not null;

    // Home screen: recently opened files and the .hof files of the detected OMSI install.
    readonly RecentFiles _recent = new();
    OmsiBrowser _browser = new([]);
    string _omsiPath = ""; // the folder shown in the library, relative to Vehicles
    public ObservableCollection<HofFileEntry> Recent { get; } = [];
    public ObservableCollection<OmsiItem> OmsiItems { get; } = [];
    public bool HasRecent => Recent.Count > 0;
    public bool HasOmsi => OmsiFolder is not null;
    [ObservableProperty] public partial string? OmsiFolder { get; set; }
    [ObservableProperty] public partial string OmsiFilter { get; set; } = "";

    // The three summary cards at the top of Home.
    public HofFileEntry? LatestRecent => Recent.FirstOrDefault();
    public string LatestTitle => LatestRecent?.Title ?? "Nothing opened yet";
    public string LatestDetail => LatestRecent?.Subtitle ?? "Open a .hof file and it will wait for you here.";
    public string OmsiHeadline => HasOmsi ? $"{_browser.FileCount:N0} .hof files found" : "OMSI 2 not found";
    public string OmsiDetail => OmsiFolder ?? "Use “Open a .hof file” to pick one yourself.";

    // String presets: the library of slot-name sets and the one currently in use (saved in the app settings).
    readonly AppSettings _settings;
    StringPreset _activePreset = null!;
    public ObservableCollection<PresetViewModel> Presets { get; } = [];
    StringTemplates Names => _activePreset.Templates;
    public string ActivePresetName => _activePreset.Name;
    readonly string _userPresetsFolder;
    public string UserPresetsFolder => _userPresetsFolder;

    public string WindowTitle => FilePath is null ? "Hofwerk" : $"{Path.GetFileName(FilePath)}{(IsDirty ? " •" : "")} – Hofwerk";

    public MainViewModel() : this(AppSettings.Load()) { }

    public MainViewModel(AppSettings settings, string? userPresetsFolder = null)
    {
        _settings = settings;
        _userPresetsFolder = userPresetsFolder ?? PresetLibrary.UserFolder;
        InitEditorLists();
        Termini.CollectionChanged += (_, _) => Touch();
        BusStops.CollectionChanged += (_, _) =>
        {
            Touch();
            RefreshKnownStops();
        };
        Routes.CollectionChanged += (_, _) => Touch();
        Problems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProblems));
        Recent.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRecent));
            OnPropertyChanged(nameof(LatestRecent));
            OnPropertyChanged(nameof(LatestTitle));
            OnPropertyChanged(nameof(LatestDetail));
        };
        WatchFolderRows();
        RefreshPresets();
        RefreshHome();
        HomeTab = HasRecent || !HasOmsi ? 0 : 1; // start on whichever list has something to show
    }

    /// <summary>Reads the built-in and user presets again, e.g. after the user dropped a new folder in.</summary>
    public void RefreshPresets()
    {
        var all = PresetLibrary.LoadAll(_userPresetsFolder);
        _activePreset = all.FirstOrDefault(p => p.Id == _settings.ActivePresetId)
                        ?? all.First(p => p.Id == PresetLibrary.DefaultId);

        Presets.Clear();
        foreach (var p in all) Presets.Add(new PresetViewModel(p, p.Id == _activePreset.Id));

        ApplyPresetNames();
        OnPropertyChanged(nameof(ActivePresetName));
    }

    public void UsePreset(PresetViewModel chosen)
    {
        _activePreset = chosen.Preset;
        foreach (var p in Presets) p.IsActive = ReferenceEquals(p, chosen);

        _settings.ActivePresetId = chosen.Preset.Id;
        _settings.Save();

        ApplyPresetNames();
        OnPropertyChanged(nameof(ActivePresetName));
        Status = $"Using the {chosen.Name}";
    }

    void ApplyPresetNames()
    {
        foreach (var t in Termini) StringSlots.Relabel(t.Strings, StringSlotKind.Terminus, Names);
        foreach (var b in BusStops) StringSlots.Relabel(b.Strings, StringSlotKind.BusStop, Names);
    }

    public void RefreshHome()
    {

        // The folder chosen in the installer or with Change... wins; otherwise look for Steam installs.
        string? chosen = _settings.OmsiFolder;
        OmsiFolder = !string.IsNullOrWhiteSpace(chosen) && Directory.Exists(chosen) ? chosen : OmsiLocator.FindInstallFolders().FirstOrDefault();
        _vehicleIndexBuilt = false; // the folder checks on the Settings page read the new install next time they are shown
        OnPropertyChanged(nameof(VehiclesFolder));
        SetRecent(_recent.Load());   // after the OMSI folder is known, so paths can start at Vehicles\
        OnPropertyChanged(nameof(FileDisplayPath));
        OnPropertyChanged(nameof(FileFolder));
        _browser = new OmsiBrowser(OmsiFolder is null ? [] : OmsiLocator.FindHofFiles(OmsiFolder));
        _omsiPath = "";
        OnPropertyChanged(nameof(HasOmsi));
        OnPropertyChanged(nameof(OmsiHeadline));
        OnPropertyChanged(nameof(OmsiDetail));
        ShowOmsiFolder();
    }

    /// <summary>Remembers an OMSI folder picked by hand. Returns false, and says why in the status bar, if it is not an OMSI folder.</summary>
    public bool TrySetOmsiFolder(string folder)
    {
        if (!OmsiLocator.LooksLikeOmsi(folder))
        {
            Status = "That doesn't look like an OMSI 2 folder: it needs a Vehicles folder and a Maps folder or Omsi.exe.";
            return false;
        }

        _settings.OmsiFolder = folder;
        _settings.Save();
        RefreshHome();
        Status = $"Using OMSI at {folder}";
        return true;
    }

    // The OMSI library works like a file explorer: folders with .hof files first, then files. Typing searches everything.
    public bool IsSearchingOmsi => OmsiFilter.Trim().Length > 0;
    public bool CanGoUp => _omsiPath.Length > 0 && !IsSearchingOmsi;
    public string OmsiLocation => IsSearchingOmsi ? $"Search results for “{OmsiFilter.Trim()}”" : OmsiBrowser.Breadcrumb(_omsiPath);

    partial void OnOmsiFilterChanged(string value) => ShowOmsiFolder();

    void ShowOmsiFolder()
    {
        OmsiItems.Clear();
        foreach (var item in IsSearchingOmsi ? _browser.Search(OmsiFilter) : _browser.Browse(_omsiPath))
            OmsiItems.Add(item);

        OnPropertyChanged(nameof(IsSearchingOmsi));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(OmsiLocation));
    }

    /// <summary>Clicking a row: a folder is entered (returns null), a file returns its path for the caller to open.</summary>
    public string? ActivateOmsiItem(OmsiItem item)
    {
        if (!item.IsFolder) return item.Path;

        _omsiPath = item.Path;
        ShowOmsiFolder();
        return null;
    }

    [RelayCommand]
    void OmsiUp()
    {
        _omsiPath = OmsiBrowser.Parent(_omsiPath);
        ShowOmsiFolder();
    }

    void SetRecent(IEnumerable<string> paths)
    {
        Recent.Clear();
        foreach (string p in paths)
            Recent.Add(new HofFileEntry(p, Path.GetFileName(p), DisplayPath(Path.GetDirectoryName(p) ?? "")));
    }

    /// <summary>Opens a file and remembers it. Returns false (and says why in the status bar) if it can't be read.</summary>
    public bool Open(string path)
    {
        try { Load(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not open the file: {ex.Message}";
            return false;
        }
        SetRecent(_recent.Add(path));
        return true;
    }

    bool _loading;

    void Touch()
    {
        if (!_loading) IsDirty = true;
    }

    // Any edit marks the document dirty; the inner view models raise PropertyChanged for every keystroke.
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(ServiceTrip) or nameof(TerminusStringCount) or nameof(BusStopStringCount)
            or nameof(Ibis9xx) or nameof(Ibis8xx) or nameof(Ibis5xx)) Touch();
        if (e.PropertyName is nameof(FilePath) or nameof(IsDirty))
        {
            OnPropertyChanged(nameof(FileDisplayName));
            OnPropertyChanged(nameof(FileDisplayPath));
            OnPropertyChanged(nameof(FileFolder));
            OnPropertyChanged(nameof(FileStateText));
        }
        if (e.PropertyName is nameof(FilePath) or nameof(IsDirty)) OnPropertyChanged(nameof(WindowTitle));
        if (e.PropertyName is nameof(FilePath)) OnPropertyChanged(nameof(HasFile));
    }

    partial void OnTerminusStringCountChanged(int value)
    {
        value = Math.Max(0, value);
        foreach (var t in Termini) StringSlots.Resize(t.Strings, value, StringSlotKind.Terminus, Names);
    }

    partial void OnBusStopStringCountChanged(int value)
    {
        value = Math.Max(0, value);
        foreach (var b in BusStops) StringSlots.Resize(b.Strings, value, StringSlotKind.BusStop, Names);
    }

    public void Load(string path)
    {
        var doc = HofFile.Load(path);
        _loading = true;
        try
        {
            _source = doc;
            Name = doc.Name;
            ServiceTrip = doc.ServiceTrip;
            TerminusStringCount = doc.TerminusStringCount;
            BusStopStringCount = doc.BusStopStringCount;
            Termini.Clear(); foreach (var t in doc.Termini) Termini.Add(Track(new TerminusViewModel(t, Names)));
            BusStops.Clear(); foreach (var b in doc.BusStops) BusStops.Add(Track(new BusStopViewModel(b, Names)));
            Routes.Clear(); foreach (var r in doc.Routes) Routes.Add(Track(new RouteViewModel(r)));
            LoadGlobals(doc.GlobalStrings);
            RebuildServiceTripChoices();
            RebuildDestinationChoices();
            RefreshKnownStops();
            RefreshFolderStatuses(rescan: false);
            Problems.Clear();
            foreach (var d in doc.Diagnostics) Problems.Add($"Line {d.Line}: {d.Message}");
            SelectedTerminus = Termini.FirstOrDefault();
            SelectedBusStop = BusStops.FirstOrDefault();
            SelectedRoute = Routes.FirstOrDefault();
            FilePath = path;
        }
        finally { _loading = false; }
        IsDirty = false;
        Status = $"Loaded {Termini.Count} destinations, {BusStops.Count} bus stops, {Routes.Count} routes"
                 + (Problems.Count > 0 ? $" – {Problems.Count} problem(s) found" : "");
    }

    public void Save(string path)
    {
        HofFile.Save(ToDocument(), path);
        FilePath = path;
        IsDirty = false;
        SetRecent(_recent.Add(path));
        Status = $"Saved {Path.GetFileName(path)}";
    }

    HofDocument ToDocument()
    {
        var doc = new HofDocument
        {
            Name = Name,
            ServiceTrip = ServiceTrip,
            TerminusStringCount = Math.Max(0, TerminusStringCount),
            BusStopStringCount = Math.Max(0, BusStopStringCount),
            Encoding = _source.Encoding,
        };
        doc.Preamble.AddRange(_source.Preamble);
        doc.SuiteDirectories.AddRange(_source.SuiteDirectories);
        doc.GlobalStrings.AddRange(GlobalLines());
        doc.Termini.AddRange(Termini.Select(t => t.ToModel()));
        doc.BusStops.AddRange(BusStops.Select(b => b.ToModel()));
        doc.Routes.AddRange(Routes.Select(r => r.ToModel()));
        return doc;
    }

    T Track<T>(T item) where T : ObservableObject
    {
        item.PropertyChanged += (_, _) => Touch();
        if (item is RouteViewModel route) route.Stops.CollectionChanged += (_, _) => RefreshKnownStops();
        if (item is BusStopViewModel stop) stop.Renamed += RenameStopInRoutes;
        return item;
    }

    [RelayCommand]
    void AddTerminus()
    {
        TerminusFilter = "";
        var t = Track(new TerminusViewModel(new Terminus { Ident = NextIdent(Termini.Select(x => x.Ident)) }, Names));
        StringSlots.Resize(t.Strings, TerminusStringCount, StringSlotKind.Terminus, Names);
        AddAfter(Termini, SelectedTerminus, t);
        SelectedTerminus = t;
    }

    [RelayCommand]
    void RemoveTerminus() => SelectedTerminus = RemoveSelected(Termini, SelectedTerminus);

    [RelayCommand]
    void AddBusStop()
    {
        BusStopFilter = "";
        var b = Track(new BusStopViewModel(new BusStop { Name = "New stop" }, Names));
        StringSlots.Resize(b.Strings, BusStopStringCount, StringSlotKind.BusStop, Names);
        AddAfter(BusStops, SelectedBusStop, b);
        SelectedBusStop = b;
    }

    [RelayCommand]
    void RemoveBusStop() => SelectedBusStop = RemoveSelected(BusStops, SelectedBusStop);

    [RelayCommand]
    void AddRoute()
    {
        RouteFilter = "";
        var r = Track(new RouteViewModel(new Route { Ident = NextIdent(Routes.Select(x => x.Ident)), Name = "New route" }));
        AddAfter(Routes, SelectedRoute, r);
        SelectedRoute = r;
    }

    [RelayCommand]
    void RemoveRoute() => SelectedRoute = RemoveSelected(Routes, SelectedRoute);

    static void AddAfter<T>(ObservableCollection<T> list, T? selected, T item) where T : class
    {
        int at = selected is null ? list.Count : list.IndexOf(selected) + 1;
        list.Insert(at, item);
    }

    static T? RemoveSelected<T>(ObservableCollection<T> list, T? selected) where T : class
    {
        if (selected is null) return null;
        int at = list.IndexOf(selected);
        list.RemoveAt(at);
        return list.Count == 0 ? null : list[Math.Min(at, list.Count - 1)];
    }

    /// <summary>Next free numeric ident, keeping the zero-padding style of the existing ones.</summary>
    static string NextIdent(IEnumerable<string> existing)
    {
        var list = existing.ToList();
        var nums = list.Select(s => int.TryParse(s, out int n) ? n : (int?)null).Where(n => n.HasValue).Select(n => n!.Value).ToList();
        int next = nums.Count == 0 ? 1 : nums.Max() + 1;
        int width = list.Where(s => s.Length > 1 && s.StartsWith('0')).Select(s => s.Length).DefaultIfEmpty(0).Max();
        return next.ToString().PadLeft(width, '0');
    }
}
