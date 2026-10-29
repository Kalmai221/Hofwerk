using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Omsi;
using Hofwerk.Localization;

namespace Hofwerk.ViewModels;

/// <summary>One trip of a map in the list of trips to choose from: a tick, its name, and a line saying where it goes.</summary>
public sealed partial class TripChoice : ObservableObject
{
    public TripChoice(MapTrip trip)
    {
        Trip = trip;
        IsSelected = true;
    }

    public MapTrip Trip { get; }
    public string Name => Trip.Name;
    public bool UsesStationLinks => Trip.UsesStationLinks;

    /// <summary>"Line 28 to Central, 12 stops".</summary>
    public string Detail
    {
        get
        {
            string stops = Localizer.Count(Trip.Stops.Count, "{0} stop", "{0} stops");
            string line = Trip.Line.Length > 0 ? Localizer.F("Line {0}", Trip.Line) : "";
            string where = Trip.Destination.Length > 0 ? Localizer.F("to {0}", Trip.Destination) : "";
            return string.Join(", ", new[] { string.Join(" ", new[] { line, where }.Where(p => p.Length > 0)), stops }.Where(p => p.Length > 0));
        }
    }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>
/// Which trips of a map to take. Every trip starts ticked. The two kinds that maps write their stops in (names in the trip file, or
/// numbers that Busstops.cfg gives names to) can be ticked on their own, as in HOF Suite's import.
/// </summary>
public sealed partial class TripSelection : ObservableObject
{
    MapData? _map;
    bool _setting;

    public ObservableCollection<TripChoice> Trips { get; } = [];

    /// <summary>Raised when the ticks change or another map is loaded.</summary>
    public event Action? Changed;

    public bool HasTrips => Trips.Count > 0;

    /// <summary>True when the map has trips of both kinds, so choosing by kind is worth offering.</summary>
    public bool HasBothKinds => Trips.Any(t => t.UsesStationLinks) && Trips.Any(t => !t.UsesStationLinks);

    public int SelectedCount => Trips.Count(t => t.IsSelected);

    /// <summary>"12 of 20 trips".</summary>
    public string Summary => Localizer.F("{0} of {1} trips", SelectedCount, Trips.Count);

    /// <summary>Starts over with the trips of a map (all ticked); no map or no trips empties the list.</summary>
    public void Load(MapData? map)
    {
        foreach (var old in Trips) old.PropertyChanged -= OnChoiceChanged;
        Trips.Clear();
        _map = map;
        if (map is not null)
            foreach (var trip in map.Trips)
            {
                var choice = new TripChoice(trip);
                choice.PropertyChanged += OnChoiceChanged;
                Trips.Add(choice);
            }
        Raise();
    }

    /// <summary>
    /// What to take from the map: the map itself when every trip is ticked (so stops that only Busstops.cfg lists come along), otherwise
    /// the map as if it only had the ticked trips.
    /// </summary>
    public MapData? Apply(MapData? map)
    {
        if (map is null || !ReferenceEquals(map, _map) || Trips.Count == 0 || Trips.All(t => t.IsSelected)) return map;
        return map.WithTrips(Trips.Where(t => t.IsSelected).Select(t => t.Trip));
    }

    [RelayCommand] void SelectAll() => Set(_ => true);
    [RelayCommand] void SelectNone() => Set(_ => false);

    /// <summary>Ticks only the trips that write their stops as numbers (station links).</summary>
    [RelayCommand] void SelectStationLinks() => Set(t => t.UsesStationLinks);

    /// <summary>Ticks only the trips that have the names of their stops in the trip file.</summary>
    [RelayCommand] void SelectPlain() => Set(t => !t.UsesStationLinks);

    // All the ticks change as one, so the dialog works out what would be imported once, not once per trip.
    void Set(Func<TripChoice, bool> select)
    {
        _setting = true;
        try { foreach (var trip in Trips) trip.IsSelected = select(trip); }
        finally { _setting = false; }
        Raise();
    }

    void OnChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_setting && e.PropertyName == nameof(TripChoice.IsSelected)) Raise();
    }

    void Raise()
    {
        OnPropertyChanged(nameof(HasTrips));
        OnPropertyChanged(nameof(HasBothKinds));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke();
    }
}
