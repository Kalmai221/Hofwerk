using Hofwerk.Localization;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

/// <summary>What the user chose in the New file dialog: an empty depot (<see cref="Map"/> is null) or one filled from a map.</summary>
public sealed record NewFilePlan(MapData? Map, MapImportOptions Options);

/// <summary>
/// The New file dialog: start empty, or take the bus stops, destinations and routes from an OMSI map. Picking a map reads its
/// timetable files in the background and shows what was found, so the user knows what they get before choosing where to save.
/// </summary>
public sealed partial class NewFileViewModel : ObservableObject
{
    readonly Func<string, MapData> _read;
    int _readNumber;

    public NewFileViewModel(IEnumerable<OmsiMap> maps, Func<string, MapData>? read = null)
    {
        _read = read ?? MapImporter.Read;
        foreach (var map in maps) Maps.Add(map);
        TripPicker.Changed += Changed;
    }

    /// <summary>Which trips of the chosen map to take (all of them to begin with).</summary>
    public TripSelection TripPicker { get; } = new();

    public ObservableCollection<OmsiMap> Maps { get; } = [];
    public bool HasMaps => Maps.Count > 0;

    [ObservableProperty] public partial bool FromMap { get; set; }
    [ObservableProperty] public partial OmsiMap? SelectedMap { get; set; }
    [ObservableProperty] public partial bool ImportBusStops { get; set; } = true;
    [ObservableProperty] public partial bool ImportDestinations { get; set; } = true;
    [ObservableProperty] public partial bool ImportRoutes { get; set; } = true;
    [ObservableProperty] public partial bool IsReading { get; set; }

    /// <summary>What was found in the chosen map; null until one is chosen and read.</summary>
    [ObservableProperty] public partial MapData? Data { get; set; }

    /// <summary>One line under the map: "Reading…", "22 bus stops, 2 destinations, 17 routes", or why nothing can be imported.</summary>
    public string Summary
    {
        get
        {
            if (!FromMap) return "";
            if (SelectedMap is null) return Maps.Count == 0 ? Localizer.T("No maps were found in OMSI's Maps folder. Choose a map folder yourself.") : Localizer.T("Choose a map.");
            if (IsReading) return Localizer.T("Reading the map…");
            if (Data is null) return "";
            if (Data.IsEmpty) return Localizer.T("This map has no timetable data (TTData) to import from.");
            if (TripPicker.HasTrips && TripPicker.SelectedCount == 0) return Localizer.T("Tick at least one trip.");
            return Localizer.F("Found {0}.", Data.Summary);
        }
    }

    /// <summary>The button that goes on to choosing where to save is available when there is something to make.</summary>
    public bool CanContinue => !FromMap || (TripPicker.Apply(Data) is { IsEmpty: false } && !IsReading && (ImportBusStops || ImportDestinations || ImportRoutes));

    public NewFilePlan ToPlan() =>
        FromMap && Data is not null
            ? new NewFilePlan(TripPicker.Apply(Data), new MapImportOptions(ImportBusStops, ImportDestinations, ImportRoutes))
            : new NewFilePlan(null, new MapImportOptions());

    /// <summary>A name to offer for the file: the map's, or a plain one.</summary>
    public string SuggestedFileName => FromMap && SelectedMap is not null ? SelectedMap.Name : Localizer.T("New depot");

    /// <summary>Adds a map folder the user picked by hand (one that is not in OMSI's Maps folder) and selects it.</summary>
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
        FromMap = true;
        SelectedMap = existing;
    }

    partial void OnFromMapChanged(bool value) => Changed();
    partial void OnImportBusStopsChanged(bool value) => Changed();
    partial void OnImportDestinationsChanged(bool value) => Changed();
    partial void OnImportRoutesChanged(bool value) => Changed();
    partial void OnDataChanged(MapData? value) { TripPicker.Load(value); Changed(); }
    partial void OnIsReadingChanged(bool value) => Changed();

    // Reads the map on another thread; if another map is chosen meanwhile, the older answer is dropped.
    async partial void OnSelectedMapChanged(OmsiMap? value)
    {
        int number = ++_readNumber;
        Data = null;
        if (value is null) { IsReading = false; return; }

        IsReading = true;
        MapData data;
        try { data = await Task.Run(() => _read(value.Folder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { data = new MapData { Name = value.Name }; }

        if (number != _readNumber) return;
        Data = data;
        IsReading = false;
    }

    void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(SuggestedFileName));
    }
}
