using Hofwerk.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

/// <summary>
/// The Import dialog of an open file: add the bus stops, destinations and routes of a whole OMSI map, or of one timetable file
/// (a single trip), to what the file already has. Shows what would be added, and what is already there, before anything changes.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    readonly Func<string, MapData> _read;
    readonly Func<string, MapData?> _readTrip;
    readonly Func<MapData, MapImportOptions, MergeResult> _preview;
    int _readNumber;
    MapData? _mapData;
    MapData? _tripData;

    public ImportViewModel(IEnumerable<OmsiMap> maps, Func<MapData, MapImportOptions, MergeResult> preview,
        Func<string, MapData>? read = null, Func<string, MapData?>? readTrip = null)
    {
        _preview = preview;
        _read = read ?? MapImporter.Read;
        _readTrip = readTrip ?? MapImporter.ReadTripFile;
        foreach (var map in maps) Maps.Add(map);
    }

    public ObservableCollection<OmsiMap> Maps { get; } = [];
    public bool HasMaps => Maps.Count > 0;

    /// <summary>False: a whole map. True: one timetable file.</summary>
    [ObservableProperty] public partial bool FromTrip { get; set; }
    [ObservableProperty] public partial OmsiMap? SelectedMap { get; set; }
    [ObservableProperty] public partial string? TripFile { get; set; }
    [ObservableProperty] public partial bool ImportBusStops { get; set; } = true;
    [ObservableProperty] public partial bool ImportDestinations { get; set; } = true;
    [ObservableProperty] public partial bool ImportRoutes { get; set; } = true;
    [ObservableProperty] public partial bool IsReading { get; set; }

    public MapImportOptions Options => new(ImportBusStops, ImportDestinations, ImportRoutes);
    bool AnyPart => ImportBusStops || ImportDestinations || ImportRoutes;

    /// <summary>What was found in the chosen map or timetable file; null until there is one.</summary>
    public MapData? Data => FromTrip ? _tripData : _mapData;

    /// <summary>What importing would do to the open file right now.</summary>
    public MergeResult? Result => Data is { IsEmpty: false } data && AnyPart && !IsReading ? _preview(data, Options) : null;

    public string Summary
    {
        get
        {
            if (FromTrip)
            {
                if (string.IsNullOrEmpty(TripFile)) return Localizer.T("Choose a timetable file (.ttp), for example from a map's TTData folder.");
                if (_tripData is null) return Localizer.T("That file is not an OMSI timetable (.ttp), so there is nothing to import.");
            }
            else
            {
                if (SelectedMap is null) return Maps.Count == 0 ? Localizer.T("No maps were found in OMSI's Maps folder. Choose a map folder yourself.") : Localizer.T("Choose a map.");
                if (IsReading) return Localizer.T("Reading the map…");
                if (_mapData is null) return "";
                if (_mapData.IsEmpty) return Localizer.T("This map has no timetable data (TTData) to import from.");
            }

            if (!AnyPart) return Localizer.T("Tick what to take.");
            return Result?.Describe() ?? "";
        }
    }

    public bool CanImport => Result is { Added: > 0 };

    /// <summary>Adds a map folder picked by hand (one that is not in OMSI's Maps folder) and selects it.</summary>
    public void AddMapFolder(string folder)
    {
        folder = System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var existing = Maps.FirstOrDefault(m => string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = MapImporter.Describe(folder);
            Maps.Add(existing);
            OnPropertyChanged(nameof(HasMaps));
        }
        FromTrip = false;
        SelectedMap = existing;
    }

    /// <summary>Takes a timetable file the user picked.</summary>
    public void SetTripFile(string file)
    {
        FromTrip = true;
        TripFile = file;
        try { _tripData = _readTrip(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _tripData = null; }
        Changed();
    }

    partial void OnFromTripChanged(bool value) => Changed();
    partial void OnImportBusStopsChanged(bool value) => Changed();
    partial void OnImportDestinationsChanged(bool value) => Changed();
    partial void OnImportRoutesChanged(bool value) => Changed();
    partial void OnIsReadingChanged(bool value) => Changed();

    // Reads the map on another thread; if another map is chosen meanwhile, the older answer is dropped.
    async partial void OnSelectedMapChanged(OmsiMap? value)
    {
        int number = ++_readNumber;
        _mapData = null;
        if (value is null) { IsReading = false; Changed(); return; }

        IsReading = true;
        MapData data;
        try { data = await Task.Run(() => _read(value.Folder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { data = new MapData { Name = value.Name }; }

        if (number != _readNumber) return;
        _mapData = data;
        IsReading = false;
        Changed();
    }

    void Changed()
    {
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanImport));
    }
}
