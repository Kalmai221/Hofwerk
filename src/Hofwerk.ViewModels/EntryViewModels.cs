using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Model;

namespace Hofwerk.ViewModels;

/// <summary>One editable line of text (a string slot of a destination or bus stop, or a stop name on a route).</summary>
public partial class StringItem(string label, string value) : ObservableObject
{
    [ObservableProperty]
    public partial string Label { get; set; } = label;

    /// <summary>Slot number as OMSI counts it (0-based); -1 when the item isn't a numbered slot.</summary>
    public int Index { get; set; } = -1;

    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    [ObservableProperty]
    public partial string Value { get; set; } = value;
}

public static class StringSlots
{
    static StringItem Slot(StringSlotKind kind, int index, string value, StringTemplates names)
    {
        var info = names.Get(kind, index);
        return new StringItem(info.Name, value) { Index = index, Hint = info.Hint };
    }

    /// <summary>Grows or shrinks the list to <paramref name="count"/> slots, keeping existing text.</summary>
    public static void Resize(ObservableCollection<StringItem> items, int count, StringSlotKind kind, StringTemplates names)
    {
        while (items.Count > count) items.RemoveAt(items.Count - 1);
        while (items.Count < count) items.Add(Slot(kind, items.Count, "", names));
    }

    public static ObservableCollection<StringItem> From(IEnumerable<string> values, StringSlotKind kind, StringTemplates names) =>
        new(values.Select((v, i) => Slot(kind, i, v, names)));

    /// <summary>Gives the slots the names of another preset without touching their text.</summary>
    public static void Relabel(IEnumerable<StringItem> items, StringSlotKind kind, StringTemplates names)
    {
        foreach (var item in items)
        {
            var info = names.Get(kind, item.Index);
            item.Label = info.Name;
            item.Hint = info.Hint;
        }
    }

    /// <summary>Calls <paramref name="changed"/> when the text of any slot is edited or slots are added or removed (not on relabelling).</summary>
    public static void Watch(ObservableCollection<StringItem> items, Action changed)
    {
        foreach (var item in items) Hook(item);
        items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (StringItem item in e.NewItems) Hook(item);
            changed();
        };

        void Hook(StringItem item) => item.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(StringItem.Value)) changed();
        };
    }
}

public partial class TerminusViewModel : ObservableObject
{
    public TerminusViewModel(Terminus t, StringTemplates names)
    {
        Ident = t.Ident;
        IsAllExit = t.IsAllExit;
        Station = t.Station;
        Strings = StringSlots.From(t.Strings, StringSlotKind.Terminus, names);
        StringSlots.Watch(Strings, () =>
        {
            OnPropertyChanged(nameof(Strings));
            OnPropertyChanged(nameof(Title));
        });
    }

    [ObservableProperty] public partial string Ident { get; set; } = "";
    [ObservableProperty] public partial bool IsAllExit { get; set; }
    [ObservableProperty] public partial string Station { get; set; } = "";
    public ObservableCollection<StringItem> Strings { get; }

    /// <summary>Text shown in the list; the first string is what the bus display shows.</summary>
    public string Title => $"{Ident}  {(Strings.Count > 0 ? Strings[0].Value.Trim() : "")}";

    partial void OnIdentChanged(string value) => OnPropertyChanged(nameof(Title));

    public Terminus ToModel() => new()
    {
        Ident = Ident,
        IsAllExit = IsAllExit,
        Station = IsAllExit ? "" : Station,
        Strings = Strings.Select(s => s.Value).ToList(),
    };
}

public partial class BusStopViewModel : ObservableObject
{
    public BusStopViewModel(BusStop b, StringTemplates names)
    {
        Name = b.Name;
        Strings = StringSlots.From(b.Strings, StringSlotKind.BusStop, names);
        StringSlots.Watch(Strings, () => OnPropertyChanged(nameof(Strings)));
    }

    [ObservableProperty] public partial string Name { get; set; } = "";
    public ObservableCollection<StringItem> Strings { get; }

    public BusStop ToModel() => new() { Name = Name, Strings = Strings.Select(s => s.Value).ToList() };
}

public partial class RouteViewModel : ObservableObject
{
    // Stops that came from the old one-by-one form keep their extra lines so nothing is lost on save.
    readonly Dictionary<StringItem, List<string>> _extra = [];

    public RouteViewModel(Route r)
    {
        Ident = r.Ident;
        Name = r.Name;
        TerminusIdent = r.TerminusIdent;
        Line = r.Line;
        foreach (var s in r.Stops)
        {
            var item = new StringItem("Stop", s.Name);
            if (s.Extra.Count > 0) _extra[item] = s.Extra;
            Stops.Add(item);
        }
        StringSlots.Watch(Stops, () => OnPropertyChanged(nameof(Stops)));
    }

    [ObservableProperty] public partial string Ident { get; set; } = "";
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string TerminusIdent { get; set; } = "";
    [ObservableProperty] public partial string Line { get; set; } = "";
    [ObservableProperty] public partial StringItem? SelectedStop { get; set; }
    public ObservableCollection<StringItem> Stops { get; } = [];

    public string Title => $"{Ident}  {Name}";
    partial void OnIdentChanged(string value) => OnPropertyChanged(nameof(Title));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Title));

    [RelayCommand]
    void AddStop()
    {
        var item = new StringItem("Stop", "");
        int at = SelectedStop is null ? Stops.Count : Stops.IndexOf(SelectedStop) + 1;
        Stops.Insert(at, item);
        SelectedStop = item;
    }

    [RelayCommand]
    void RemoveStop()
    {
        if (SelectedStop is null) return;
        int at = Stops.IndexOf(SelectedStop);
        Stops.RemoveAt(at);
        SelectedStop = Stops.Count == 0 ? null : Stops[Math.Min(at, Stops.Count - 1)];
    }

    public Route ToModel() => new()
    {
        Ident = Ident,
        Name = Name,
        TerminusIdent = TerminusIdent,
        Line = Line,
        Stops = Stops.Select(s => new RouteStop { Name = s.Value, Extra = _extra.GetValueOrDefault(s) ?? [] }).ToList(),
    };
}

/// <summary>A preset as shown in the library: who made it, what it is for, and whether it is the one in use.</summary>
public partial class PresetViewModel(Hofwerk.Core.Presets.StringPreset preset, bool isActive) : ObservableObject
{
    public Hofwerk.Core.Presets.StringPreset Preset { get; } = preset;

    [ObservableProperty]
    public partial bool IsActive { get; set; } = isActive;

    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string Url => Preset.Url;
    public bool HasUrl => Preset.Url.Length > 0;
    public string Credit => Preset.Author.Length > 0 ? $"by {Preset.Author}" : "";
    public string Source => Preset.IsBuiltIn ? "Built in" : "Your preset";
    public string SlotSummary =>
        $"{Preset.Templates.Count(StringSlotKind.Terminus)} destination slots · {Preset.Templates.Count(StringSlotKind.BusStop)} bus stop slots";
}
