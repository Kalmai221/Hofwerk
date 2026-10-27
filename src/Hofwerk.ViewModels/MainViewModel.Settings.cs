using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Hofwerk.Core.Model;
using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

// The Settings page (general settings, folders, IBIS codes), the Home tabs and the sidebar's file card.
public partial class MainViewModel
{
    // ---- Settings: general ------------------------------------------------------------------------------------

    public ObservableCollection<ServiceTripChoice> ServiceTripChoices { get; } = [];
    [ObservableProperty] public partial ServiceTripChoice? SelectedServiceTrip { get; set; }
    bool _syncingChoices;

    partial void OnSelectedServiceTripChanged(ServiceTripChoice? value)
    {
        if (!_syncingChoices && value is not null) ServiceTrip = value.Name;
    }

    partial void OnServiceTripChanged(string value) => SyncSelectedServiceTrip();

    /// <summary>The destinations the service trip can be set to, shown as "1001: Not in Service".</summary>
    void RebuildServiceTripChoices()
    {
        _syncingChoices = true;
        try
        {
            ServiceTripChoices.Clear();
            foreach (var t in Termini)
            {
                string label = !string.IsNullOrWhiteSpace(t.Station) ? t.Station.Trim()
                    : t.Strings.Select(s => s.Value.Trim()).FirstOrDefault(s => s.Length > 0) ?? t.Ident;
                ServiceTripChoices.Add(new ServiceTripChoice($"{t.Ident}: {label}", label));
            }

            // A value that is not one of the destinations stays selectable rather than being lost.
            if (!string.IsNullOrWhiteSpace(ServiceTrip) && !ServiceTripChoices.Any(c => SameName(c.Name, ServiceTrip)))
                ServiceTripChoices.Add(new ServiceTripChoice($"{ServiceTrip.Trim()} (not in the destination list)", ServiceTrip));
        }
        finally { _syncingChoices = false; }

        SyncSelectedServiceTrip();
    }

    void SyncSelectedServiceTrip()
    {
        _syncingChoices = true;
        try { SelectedServiceTrip = ServiceTripChoices.FirstOrDefault(c => SameName(c.Name, ServiceTrip)); }
        finally { _syncingChoices = false; }
    }

    static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // ---- Settings: folders ------------------------------------------------------------------------------------

    public FolderRow Announcements { get; } = new("Announcements", "The folder with the announcement sounds");
    public FolderRow Rollsign { get; } = new("Roll sign", "The folder with the roll sign (destination blind) textures");
    public FolderRow SidePlate { get; } = new("Side plate", "The folder with the side plate textures");
    public IReadOnlyList<FolderRow> Folders => [Announcements, Rollsign, SidePlate];

    VehicleFolderIndex _vehicleIndex = VehicleFolderIndex.Empty;
    bool _vehicleIndexBuilt;

    /// <summary>The Vehicles folder of the OMSI install, or null when OMSI is not known.</summary>
    public string? VehiclesFolder => OmsiFolder is null ? null : Path.Combine(OmsiFolder, "Vehicles");

    void WatchFolderRows()
    {
        foreach (var row in Folders)
        {
            var r = row;
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(FolderRow.Name)) return;
                Touch();
                r.Update(_vehicleIndex, HasOmsi);
            };
        }
    }

    /// <summary>Checks the three folder names against the OMSI install. The folder list is read once and kept.</summary>
    void RefreshFolderStatuses(bool rescan)
    {
        if (rescan || !_vehicleIndexBuilt)
        {
            _vehicleIndex = OmsiFolder is null ? VehicleFolderIndex.Empty : VehicleFolderIndex.Build(OmsiFolder);
            _vehicleIndexBuilt = true;
        }
        foreach (var row in Folders) row.Update(_vehicleIndex, HasOmsi);
    }

    /// <summary>Called when the Settings page is shown, so its lists and checks are up to date.</summary>
    public void RefreshSettingsData()
    {
        RebuildServiceTripChoices();
        RefreshFolderStatuses(rescan: false);
        RefreshExportChoices();
    }

    /// <summary>Creates the row's folder inside <paramref name="parent"/> (chosen by the user) and checks it again.</summary>
    public bool CreateFolder(FolderRow row, string parent)
    {
        try
        {
            string made = VehicleFolderIndex.CreateFolder(parent, row.Name);
            RefreshFolderStatuses(rescan: true);
            Status = $"Created {made}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not create the folder: {ex.Message}";
            return false;
        }
    }

    // ---- Settings: IBIS codes ---------------------------------------------------------------------------------

    // Two digits that change the last two numbers of the IBIS code for 900, 800 and 500 routes.
    [ObservableProperty] public partial string Ibis9xx { get; set; } = "00";
    [ObservableProperty] public partial string Ibis8xx { get; set; } = "00";
    [ObservableProperty] public partial string Ibis5xx { get; set; } = "00";

    partial void OnIbis9xxChanged(string value) => Ibis9xx = DigitsOnly(value);
    partial void OnIbis8xxChanged(string value) => Ibis8xx = DigitsOnly(value);
    partial void OnIbis5xxChanged(string value) => Ibis5xx = DigitsOnly(value);

    // While typing: digits only, at most two. (The file always gets two digits; see GlobalSettings.NormalizeCode.)
    static string DigitsOnly(string value)
    {
        string digits = new((value ?? "").Where(char.IsDigit).Take(2).ToArray());
        return digits;
    }

    // ---- Settings <-> file --------------------------------------------------------------------------------------

    GlobalSettings _globals = new();

    void LoadGlobals(IReadOnlyList<string> lines)
    {
        _globals = GlobalSettings.From(lines);
        Announcements.Name = _globals.AnnouncementsFolder;
        Rollsign.Name = _globals.RollsignFolder;
        SidePlate.Name = _globals.SidePlateFolder;
        Ibis9xx = _globals.Ibis9xx;
        Ibis8xx = _globals.Ibis8xx;
        Ibis5xx = _globals.Ibis5xx;
    }

    /// <summary>The [global_strings] lines to save, or none if the file had no such block and nothing was entered.</summary>
    List<string> GlobalLines()
    {
        _globals.AnnouncementsFolder = Announcements.Name.Trim();
        _globals.RollsignFolder = Rollsign.Name.Trim();
        _globals.SidePlateFolder = SidePlate.Name.Trim();
        _globals.Ibis9xx = Ibis9xx;
        _globals.Ibis8xx = Ibis8xx;
        _globals.Ibis5xx = Ibis5xx;

        var lines = _globals.ToLines();
        bool hadBlock = _source.GlobalStrings.Count > 0;
        bool enteredSomething = Folders.Any(f => f.Name.Trim().Length > 0)
                                || new[] { Ibis9xx, Ibis8xx, Ibis5xx }.Any(c => GlobalSettings.NormalizeCode(c) != "00");
        return hadBlock || enteredSomething ? lines : [];
    }

    // ---- Home tabs ----------------------------------------------------------------------------------------------

    /// <summary>0 = recent files, 1 = OMSI library.</summary>
    [ObservableProperty] public partial int HomeTab { get; set; }

    public bool ShowRecentTab => HomeTab == 0;
    public bool ShowLibraryTab => HomeTab == 1;

    partial void OnHomeTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowRecentTab));
        OnPropertyChanged(nameof(ShowLibraryTab));
    }

    // ---- Sidebar file card --------------------------------------------------------------------------------------

    public string FileDisplayName => FilePath is null ? "No file open" : Path.GetFileName(FilePath);
    public string FileFolder => FilePath is null ? "Open a .hof file to start editing" : Path.GetDirectoryName(FileDisplayPath) ?? "";
    public string FileStateText => FilePath is null ? "" : IsDirty ? "Unsaved changes" : "All changes saved";
}
