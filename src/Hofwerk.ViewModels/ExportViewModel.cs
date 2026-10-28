using Hofwerk.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

/// <summary>One vehicle folder in the export list: ticked means the file goes into it (and the folder is kept in the file's export list).</summary>
public sealed partial class ExportFolderItem(string name, bool exists) : ObservableObject
{
    public string Name { get; } = name;
    /// <summary>False for a folder named in the file that is not in this OMSI install (it is kept in the list, but nothing is copied there).</summary>
    public bool Exists { get; } = exists;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public string Note => Exists ? "" : Localizer.T("not in your Vehicles folder");
}

/// <summary>
/// The export dialog: the vehicle folders of the OMSI install with those in the file's own list (HOF Suite's
/// [HOF_SUITE_DIRECTORIES]) ticked. Exporting saves the list in the file and puts the file into every ticked folder.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    readonly List<ExportFolderItem> _all = [];
    readonly string _vehiclesFolder;
    readonly string _file;

    public ExportViewModel(string file, string vehiclesFolder, IEnumerable<string> listedInFile)
    {
        _file = file;
        _vehiclesFolder = vehiclesFolder;
        FileName = Path.GetFileName(file);

        var listed = listedInFile.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var installed = HofExporter.FindVehicleFolders(vehiclesFolder);
        foreach (string name in installed.Concat(listed.Where(l => !installed.Contains(l, StringComparer.OrdinalIgnoreCase))))
        {
            bool exists = installed.Contains(name, StringComparer.OrdinalIgnoreCase) || HofExporter.ResolveFolder(vehiclesFolder, name) is not null;
            var item = new ExportFolderItem(name, exists) { IsSelected = listed.Contains(name, StringComparer.OrdinalIgnoreCase) };
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedCount));
            _all.Add(item);
        }
        ApplyFilter();
    }

    public string FileName { get; }

    /// <summary>The folders matching the search box, the ticked ones listed first.</summary>
    public ObservableCollection<ExportFolderItem> Shown { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial bool BackupReplaced { get; set; } = true;
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial bool IsDone { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string Report { get; set; } = "";

    public int SelectedCount => _all.Count(i => i.IsSelected);
    public string SelectionText => SelectedCount switch { 0 => Localizer.T("No folder ticked"), var n => Localizer.Count(n, "{0} folder ticked", "{0} folders ticked") };
    public bool CanExport => !IsRunning && !IsDone && SelectedCount > 0;
    public bool HasFolders => _all.Count > 0;

    /// <summary>The names to keep in the file, in the alphabetical order HOF Suite writes them.</summary>
    public List<string> SelectedNames => _all.Where(i => i.IsSelected).Select(i => i.Name).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public void SelectAllShown(bool selected) { foreach (var item in Shown.Where(i => i.Exists)) item.IsSelected = selected; }

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanExport));
    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(CanExport));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(SelectedCount)) { OnPropertyChanged(nameof(SelectionText)); OnPropertyChanged(nameof(CanExport)); }
    }

    void ApplyFilter()
    {
        string text = Filter.Trim();
        Shown.Clear();
        foreach (var item in _all.Where(i => text.Length == 0 || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)))
            Shown.Add(item);
    }

    /// <summary>Copies the saved file into the ticked folders, reporting progress; the text of the result is in <see cref="Report"/>.</summary>
    public async Task<List<ExportResult>> RunAsync()
    {
        IsRunning = true;
        Progress = 0;
        Report = "";
        try
        {
            var names = SelectedNames;
            var progress = new Progress<double>(p => Progress = p);
            var options = new ExportOptions(BackupReplaced);
            var results = await Task.Run(() => HofExporter.Export(_file, _vehiclesFolder, names, options, progress));
            Progress = 1;
            Report = Describe(results);
            IsDone = true;
            return results;
        }
        finally { IsRunning = false; }
    }

    public static string Describe(IReadOnlyList<ExportResult> results)
    {
        int written = results.Count(r => r.Outcome == ExportOutcome.Written);
        int same = results.Count(r => r.Outcome == ExportOutcome.Unchanged);
        var problems = results.Where(r => r.Outcome is ExportOutcome.Failed or ExportOutcome.Skipped).ToList();

        var lines = new List<string>
        {
            Localizer.Count(written, "{0} folder updated", "{0} folders updated") + (same > 0 ? Localizer.F(", {0} already had the file", same) : "")
            + (problems.Count > 0 ? Localizer.F(", {0} not done", problems.Count) : "") + ".",
        };
        lines.AddRange(problems.Select(p => $"{p.Folder}: {p.Message}"));
        return string.Join(Environment.NewLine, lines);
    }
}
