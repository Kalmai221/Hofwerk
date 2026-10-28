using Hofwerk.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

/// <summary>One folder in the file's export list, with whether it exists in the OMSI install.</summary>
public sealed partial class ExportLocation(string name, bool exists) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool Exists { get; set; } = exists;
    public bool IsMissing => !Exists;
    public string StatusText => Exists ? Localizer.T("Found under Vehicles") : Localizer.T("Not found under Vehicles, so nothing is copied there");

    partial void OnExistsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(StatusText));
    }
}

// The file's export locations: the vehicle folders Export puts the file into. They are kept in the .hof itself between
// [HOF_SUITE_DIRECTORIES] and [HOF_SUITE_DIRECTORIES_END], the way HOF Suite does, and shown on the Settings page.
public partial class MainViewModel
{
    public ObservableCollection<ExportLocation> ExportLocations { get; } = [];

    /// <summary>The vehicle folders that are not in the list yet, for the "Add" choice.</summary>
    public ObservableCollection<string> ExportChoices { get; } = [];

    [ObservableProperty] public partial string? SelectedExportChoice { get; set; }

    public bool HasExportLocations => ExportLocations.Count > 0;
    public bool HasNoExportLocations => ExportLocations.Count == 0;

    void InitExportLocations() =>
        ExportLocations.CollectionChanged += (_, _) =>
        {
            Touch();
            OnPropertyChanged(nameof(HasExportLocations));
            OnPropertyChanged(nameof(HasNoExportLocations));
        };

    void LoadExportLocations(IEnumerable<string> names)
    {
        ExportLocations.Clear();
        foreach (string name in names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            ExportLocations.Add(new ExportLocation(name, FolderExists(name)));
        RefreshExportChoices();
    }

    bool FolderExists(string name) => VehiclesFolder is { } v && HofExporter.ResolveFolder(v, name) is not null;

    /// <summary>Called when the Settings page is shown (and a file is loaded): checks each folder again and rebuilds the list to choose from.</summary>
    void RefreshExportChoices()
    {
        foreach (var location in ExportLocations) location.Exists = FolderExists(location.Name);

        ExportChoices.Clear();
        if (VehiclesFolder is { } v)
            foreach (string name in HofExporter.FindVehicleFolders(v).Where(n => !ExportLocations.Any(l => string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase))))
                ExportChoices.Add(name);
        SelectedExportChoice = null;
    }

    bool CanAddExportLocation() => !string.IsNullOrEmpty(SelectedExportChoice);

    partial void OnSelectedExportChoiceChanged(string? value) => AddExportLocationCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanAddExportLocation))]
    void AddExportLocation()
    {
        if (SelectedExportChoice is not { } name) return;
        InsertExportLocation(name);
        ExportChoices.Remove(name);
        SelectedExportChoice = null;
    }

    public void RemoveExportLocation(ExportLocation location)
    {
        ExportLocations.Remove(location);
        if (location.Exists && VehiclesFolder is not null)
        {
            // Back into the choices, in alphabetical place.
            int at = 0;
            while (at < ExportChoices.Count && string.Compare(ExportChoices[at], location.Name, StringComparison.OrdinalIgnoreCase) < 0) at++;
            ExportChoices.Insert(at, location.Name);
        }
    }

    // The list is kept in alphabetical order, as HOF Suite writes it.
    void InsertExportLocation(string name)
    {
        int at = 0;
        while (at < ExportLocations.Count && string.Compare(ExportLocations[at].Name, name, StringComparison.OrdinalIgnoreCase) < 0) at++;
        ExportLocations.Insert(at, new ExportLocation(name, FolderExists(name)));
    }

    /// <summary>The names to write into the file.</summary>
    List<string> ExportLocationNames() => ExportLocations.Select(l => l.Name).ToList();
}
