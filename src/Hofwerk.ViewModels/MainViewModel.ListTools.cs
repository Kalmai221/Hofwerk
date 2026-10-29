using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Model;
using Hofwerk.Localization;

namespace Hofwerk.ViewModels;

// Tools for the three lists (Destinations, Bus stops, Routes): sort by code or name, move an item up or down, and the two sets of
// destinations HOF Suite can add in one click. Every change is an edit of the file, so Undo takes it back.
public partial class MainViewModel
{
    /// <summary>Codes are put in number order ("9" before "10"); a code that is not a number sorts after the numbers, by its letters.</summary>
    public static int CompareCodes(string? a, string? b)
    {
        bool numA = int.TryParse(a?.Trim(), out int x), numB = int.TryParse(b?.Trim(), out int y);
        if (numA && numB) return x.CompareTo(y);
        if (numA != numB) return numA ? -1 : 1;
        return string.Compare(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static int CompareNames(string? a, string? b) => string.Compare(a?.Trim(), b?.Trim(), StringComparison.CurrentCultureIgnoreCase);

    // Puts the items in order by moving them (so the list keeps its selection and the other views follow), keeping equal ones as they were.
    static void SortInPlace<T>(ObservableCollection<T> list, Comparison<T> compare)
    {
        var sorted = list.Select((item, index) => (item, index)).OrderBy(p => p.item, Comparer<T>.Create(compare)).ThenBy(p => p.index).Select(p => p.item).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int at = list.IndexOf(sorted[i]);
            if (at != i) list.Move(at, i);
        }
    }

    static bool MoveSelected<T>(ObservableCollection<T> list, T? selected, int step) where T : class
    {
        if (selected is null) return false;
        int at = list.IndexOf(selected), to = at + step;
        if (at < 0 || to < 0 || to >= list.Count) return false;
        list.Move(at, to);
        return true;
    }

    static string FirstString(TerminusViewModel t) => t.Strings.Count > 0 ? t.Strings[0].Value : "";

    // ---- Sort ---------------------------------------------------------------------------------------------------------

    [RelayCommand] void SortTerminiByCode() { TerminusFilter = ""; Grouped(() => SortInPlace(Termini, (a, b) => CompareCodes(a.Ident, b.Ident))); Status = Localizer.T("Destinations sorted by code."); }
    [RelayCommand] void SortTerminiByName() { TerminusFilter = ""; Grouped(() => SortInPlace(Termini, (a, b) => CompareNames(FirstString(a), FirstString(b)))); Status = Localizer.T("Destinations sorted by name."); }
    [RelayCommand] void SortBusStopsByName() { BusStopFilter = ""; Grouped(() => SortInPlace(BusStops, (a, b) => CompareNames(a.Name, b.Name))); Status = Localizer.T("Bus stops sorted by name."); }
    [RelayCommand] void SortRoutesByCode() { RouteFilter = ""; Grouped(() => SortInPlace(Routes, (a, b) => CompareCodes(a.Ident, b.Ident))); Status = Localizer.T("Routes sorted by code."); }
    [RelayCommand] void SortRoutesByName() { RouteFilter = ""; Grouped(() => SortInPlace(Routes, (a, b) => CompareNames(a.Name, b.Name))); Status = Localizer.T("Routes sorted by name."); }


    // ---- Move the selected item ----------------------------------------------------------------------------------------

    [RelayCommand] void MoveTerminusUp() { TerminusFilter = ""; MoveSelected(Termini, SelectedTerminus, -1); }
    [RelayCommand] void MoveTerminusDown() { TerminusFilter = ""; MoveSelected(Termini, SelectedTerminus, 1); }
    [RelayCommand] void MoveBusStopUp() { BusStopFilter = ""; MoveSelected(BusStops, SelectedBusStop, -1); }
    [RelayCommand] void MoveBusStopDown() { BusStopFilter = ""; MoveSelected(BusStops, SelectedBusStop, 1); }
    [RelayCommand] void MoveRouteUp() { RouteFilter = ""; MoveSelected(Routes, SelectedRoute, -1); }
    [RelayCommand] void MoveRouteDown() { RouteFilter = ""; MoveSelected(Routes, SelectedRoute, 1); }

    // ---- Destinations HOF Suite adds in one click ------------------------------------------------------------------------

    /// <summary>The code of the add-plate destination.</summary>
    public const string AddPlateCode = "1000";

    /// <summary>The destination whose code the "Options" destination of the KPP set has, and the number added to the codes that are copied.</summary>
    public const int KppOptionsCode = 14990;
    public const int KppOffset = 20000;

    bool HasTerminusCode(string code) => Termini.Any(t => CompareCodes(t.Ident, code) == 0);

    TerminusViewModel NewTerminus(Terminus model)
    {
        var t = Track(new TerminusViewModel(model, Names));
        StringSlots.Resize(t.Strings, TerminusStringCount, StringSlotKind.Terminus, Names);
        return t;
    }

    /// <summary>Adds a destination with code 1000 (the add-plate one) at the end; when there already is one it is selected instead.</summary>
    [RelayCommand]
    void AddAddPlateDestination()
    {
        TerminusFilter = "";
        if (Termini.FirstOrDefault(t => CompareCodes(t.Ident, AddPlateCode) == 0) is { } existing)
        {
            SelectedTerminus = existing;
            Status = Localizer.T("There already is a destination with code 1000.");
            return;
        }
        var added = NewTerminus(new Terminus { Ident = AddPlateCode });
        Termini.Add(added);
        SelectedTerminus = added;
        Status = Localizer.T("Added a destination with code 1000.");
    }

    /// <summary>
    /// The KPP set: an "Options" destination (14990), and a copy of every destination with a code under 1000 at its code plus 20000.
    /// What is already there is left alone, so it is safe to press again.
    /// </summary>
    [RelayCommand]
    void AddKppDestinations()
    {
        TerminusFilter = "";
        int added = 0;
        Grouped(() => added = AddKppCore());
        Status = added == 0
            ? Localizer.T("The KPP destinations are already there.")
            : Localizer.Count(added, "Added {0} destination for KPP.", "Added {0} destinations for KPP.");
    }

    int AddKppCore()
    {
        int added = 0;

        if (!HasTerminusCode(KppOptionsCode.ToString()))
        {
            var options = NewTerminus(new Terminus { Ident = KppOptionsCode.ToString() });
            if (options.Strings.Count > 0) options.Strings[0].Value = "Options";
            Termini.Add(options);
            added++;
        }

        foreach (var source in Termini.ToList())
        {
            if (!int.TryParse(source.Ident.Trim(), out int code) || code < 0 || code >= 1000) continue;
            string copyCode = (code + KppOffset).ToString();
            if (HasTerminusCode(copyCode)) continue;

            var model = source.ToModel();
            model.Ident = copyCode;
            Termini.Add(Track(new TerminusViewModel(model, Names)));
            added++;
        }
        return added;
    }
}
