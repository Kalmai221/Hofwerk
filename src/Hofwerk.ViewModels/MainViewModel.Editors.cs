using Hofwerk.Localization;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hofwerk.ViewModels;

/// <summary>A destination a route can end at, shown as "1010: University".</summary>
public sealed record DestinationChoice(string Ident, string Display);

// The Destinations, Bus stops and Routes pages: search, duplicate, and picking route stops from the defined bus stops.
public partial class MainViewModel
{
    // ---- Search in the three lists ----------------------------------------------------------------------------

    FilteredCollection<TerminusViewModel> _terminiView = null!;
    FilteredCollection<BusStopViewModel> _busStopsView = null!;
    FilteredCollection<RouteViewModel> _routesView = null!;
    FilteredCollection<BusStopViewModel> _pickerView = null!;

    public ObservableCollection<TerminusViewModel> FilteredTermini => _terminiView.View;
    public ObservableCollection<BusStopViewModel> FilteredBusStops => _busStopsView.View;
    public ObservableCollection<RouteViewModel> FilteredRoutes => _routesView.View;
    public ObservableCollection<BusStopViewModel> StopPickerStops => _pickerView.View;

    [ObservableProperty] public partial string TerminusFilter { get; set; } = "";
    [ObservableProperty] public partial string BusStopFilter { get; set; } = "";
    [ObservableProperty] public partial string RouteFilter { get; set; } = "";
    [ObservableProperty] public partial string StopPickerFilter { get; set; } = "";

    partial void OnTerminusFilterChanged(string value) => _terminiView.Text = value;
    partial void OnBusStopFilterChanged(string value) => _busStopsView.Text = value;
    partial void OnRouteFilterChanged(string value) => _routesView.Text = value;
    partial void OnStopPickerFilterChanged(string value) => _pickerView.Text = value;

    static bool Has(string? text, string needle) => text?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;

    void InitEditorLists()
    {
        _terminiView = new(Termini, (t, s) => Has(t.Title, s) || Has(t.Station, s));
        _busStopsView = new(BusStops, (b, s) => Has(b.Name, s));
        _routesView = new(Routes, (r, s) => Has(r.Title, s) || Has(r.TerminusIdent, s) || Has(r.Line, s));
        _pickerView = new(BusStops, (b, s) => Has(b.Name, s));
    }

    // ---- Duplicate -------------------------------------------------------------------------------------------------

    [RelayCommand]
    void DuplicateTerminus()
    {
        if (SelectedTerminus is null) return;
        TerminusFilter = "";

        var copy = SelectedTerminus.ToModel();
        copy.Ident = NextIdent(Termini.Select(x => x.Ident));
        var t = Track(new TerminusViewModel(copy, Names));
        AddAfter(Termini, SelectedTerminus, t);
        SelectedTerminus = t;
    }

    [RelayCommand]
    void DuplicateBusStop()
    {
        if (SelectedBusStop is null) return;
        BusStopFilter = "";

        var copy = SelectedBusStop.ToModel();
        copy.Name += " (copy)";
        var b = Track(new Hofwerk.ViewModels.BusStopViewModel(copy, Names));
        AddAfter(BusStops, SelectedBusStop, b);
        SelectedBusStop = b;
    }

    [RelayCommand]
    void DuplicateRoute()
    {
        if (SelectedRoute is null) return;
        RouteFilter = "";

        var copy = SelectedRoute.ToModel();
        copy.Ident = NextIdent(Routes.Select(x => x.Ident));
        copy.Name += " (copy)";
        var r = Track(new RouteViewModel(copy));
        AddAfter(Routes, SelectedRoute, r);
        SelectedRoute = r;
        RefreshKnownStops();
    }

    // ---- Route stops: pick from the defined bus stops -------------------------------------------------------

    [ObservableProperty] public partial BusStopViewModel? SelectedPickerStop { get; set; }

    /// <summary>Adds a defined bus stop to the selected route, after the selected stop or at the end.</summary>
    public void AddStopToRoute(BusStopViewModel stop)
    {
        if (SelectedRoute is null) return;
        SelectedRoute.AddStop(stop.Name);
        RefreshKnownStops();
        Status = Localizer.F("Added {0} to {1}", stop.Name, SelectedRoute.Title.Trim());
    }

    [RelayCommand]
    void AddPickedStop()
    {
        if (SelectedPickerStop is not null) AddStopToRoute(SelectedPickerStop);
    }

    /// <summary>Marks every route stop that has no bus stop of that name; the same name is a match in any letter case.</summary>
    public void RefreshKnownStops()
    {
        var names = new HashSet<string>(BusStops.Select(b => b.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var route in Routes)
            foreach (var stop in route.Stops)
                stop.IsKnown = names.Contains(stop.Name.Trim());

        OnPropertyChanged(nameof(RouteWarning));
        OnPropertyChanged(nameof(BusStopUsage));
    }

    /// <summary>Said above the route's stop list when some stops are not defined bus stops.</summary>
    public string RouteWarning
    {
        get
        {
            int n = SelectedRoute?.Stops.Count(s => s.IsUnknown) ?? 0;
            return n == 0 ? "" : Localizer.Count(n, "{0} stop on this route is not in your bus stop list.", "{0} stops on this route are not in your bus stop list.");
        }
    }

    // A route follows a stop when the stop is renamed, so renaming does not silently break routes.
    void RenameStopInRoutes(string oldName, string newName)
    {
        if (_loading) return;
        foreach (var route in Routes)
            foreach (var stop in route.Stops)
                if (stop.Name == oldName) stop.Name = newName;
        RefreshKnownStops();
    }

    /// <summary>"Used in 3 routes", shown for the selected bus stop.</summary>
    public string BusStopUsage
    {
        get
        {
            if (SelectedBusStop is null) return "";
            int n = Routes.Count(r => r.Stops.Any(s => string.Equals(s.Name.Trim(), SelectedBusStop.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            return n == 0 ? Localizer.T("Not used in any route") : Localizer.Count(n, "Used in {0} route", "Used in {0} routes");
        }
    }

    partial void OnSelectedBusStopChanged(BusStopViewModel? value) => OnPropertyChanged(nameof(BusStopUsage));

    // ---- Route destination -----------------------------------------------------------------------------------

    public ObservableCollection<DestinationChoice> DestinationChoices { get; } = [];
    [ObservableProperty] public partial DestinationChoice? SelectedRouteDestination { get; set; }
    bool _syncingDestination;

    partial void OnSelectedRouteChanged(RouteViewModel? value)
    {
        SyncRouteDestination();
        OnPropertyChanged(nameof(RouteWarning));
    }

    partial void OnSelectedRouteDestinationChanged(DestinationChoice? value)
    {
        if (!_syncingDestination && value is not null && SelectedRoute is not null)
            SelectedRoute.TerminusIdent = value.Ident;
    }

    void RebuildDestinationChoices()
    {
        _syncingDestination = true;
        try
        {
            DestinationChoices.Clear();
            foreach (var t in Termini)
                DestinationChoices.Add(new DestinationChoice(t.Ident, $"{t.Ident}: {DestinationLabel(t)}"));

            // An ident that is not a destination stays selectable rather than being lost.
            string? ident = SelectedRoute?.TerminusIdent;
            if (!string.IsNullOrWhiteSpace(ident) && !DestinationChoices.Any(c => c.Ident == ident))
                DestinationChoices.Add(new DestinationChoice(ident, Localizer.F("{0} (not in the destination list)", ident)));
        }
        finally { _syncingDestination = false; }

        SyncRouteDestination();
    }

    void SyncRouteDestination()
    {
        _syncingDestination = true;
        try { SelectedRouteDestination = SelectedRoute is null ? null : DestinationChoices.FirstOrDefault(c => c.Ident == SelectedRoute.TerminusIdent); }
        finally { _syncingDestination = false; }
    }

    static string DestinationLabel(TerminusViewModel t) =>
        !string.IsNullOrWhiteSpace(t.Station) ? t.Station.Trim()
            : t.Strings.Select(s => s.Value.Trim()).FirstOrDefault(s => s.Length > 0) ?? t.Ident;

    /// <summary>Called when the Routes page is shown, so the destination list and stop checks are up to date.</summary>
    public void RefreshRouteData()
    {
        RebuildDestinationChoices();
        RefreshKnownStops();
    }

    // ---- File path as OMSI shows it -----------------------------------------------------------------------------

    /// <summary>
    /// The open file's path, starting at "Vehicles\…" when it is inside the OMSI folder (the part before that is the same
    /// for every file and only takes room); otherwise the full path.
    /// </summary>
    public string FileDisplayPath => FilePath is null ? "" : DisplayPath(FilePath);

    /// <summary>A path as shown to the user: from "Vehicles\\" on when it is inside the OMSI folder, otherwise in full.</summary>
    string DisplayPath(string path)
    {
        if (OmsiFolder is { Length: > 0 } omsi)
        {
            string relative = Path.GetRelativePath(omsi, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) return relative;
        }
        return path;
    }
}
