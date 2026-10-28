using Hofwerk.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

public enum FolderState
{
    /// <summary>No name entered yet.</summary>
    Empty,
    /// <summary>OMSI was not found, so there is nothing to check against.</summary>
    Unknown,
    Found,
    Missing,
}

/// <summary>One of the three folder names in a .hof file (announcements, roll signs, side plates) and whether it exists.</summary>
public partial class FolderRow(string label, string hint) : ObservableObject
{
    public string Label => Localizer.T(label);
    public string Hint => Localizer.T(hint);

    /// <summary>Shows the texts again after the language changed.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(StatusText));
    }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial FolderState State { get; set; } = FolderState.Unknown;
    [ObservableProperty] public partial string Where { get; set; } = "";

    public bool IsFound => State == FolderState.Found;
    public bool IsMissing => State == FolderState.Missing;
    public bool CanCreate => State == FolderState.Missing;

    public string StatusText => State switch
    {
        FolderState.Found => Localizer.F("Found in {0}", Where),
        FolderState.Missing => Localizer.T("Not found in your OMSI Vehicles folder"),
        FolderState.Unknown => Localizer.T("Open OMSI to check this folder"),
        _ => "",
    };

    partial void OnStateChanged(FolderState value)
    {
        OnPropertyChanged(nameof(IsFound));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnWhereChanged(string value) => OnPropertyChanged(nameof(StatusText));

    /// <summary>Looks the name up again, e.g. after the user typed or after a folder was created.</summary>
    public void Update(VehicleFolderIndex index, bool omsiKnown)
    {
        if (string.IsNullOrWhiteSpace(Name)) { Where = ""; State = FolderState.Empty; return; }
        if (!omsiKnown) { Where = ""; State = FolderState.Unknown; return; }

        string? parent = index.Find(Name);
        Where = parent is null ? "" : parent == "." ? Localizer.T("Vehicles") : Localizer.T("Vehicles") + " › " + parent.Replace('\\', '/').Replace("/", " › ");
        State = parent is null ? FolderState.Missing : FolderState.Found;
    }
}

/// <summary>A destination the "service trip" (the display shown when the bus is not in service) can be set to.</summary>
public sealed record ServiceTripChoice(string Display, string Name);
