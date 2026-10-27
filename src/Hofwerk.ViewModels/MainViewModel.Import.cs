using Hofwerk.Core.Omsi;

namespace Hofwerk.ViewModels;

// Adding a map, or one timetable file, to the open file. It is an edit like any other: the file becomes unsaved and Undo takes it back.
public partial class MainViewModel
{
    public bool CanImport => FilePath is not null;

    /// <summary>The Import dialog for the open file, or null (with the reason in the status bar) when no file is open.</summary>
    public ImportViewModel? StartImport()
    {
        if (FilePath is null) { Status = "Open a .hof file first."; return null; }
        return new ImportViewModel(FindMaps(), (data, options) => MapImporter.Preview(ToDocument(), data, options));
    }

    /// <summary>Adds what the dialog chose to the file as one undo step. False, with the reason in the status bar, when there was nothing to add.</summary>
    public bool ApplyImport(ImportViewModel import)
    {
        if (import.Data is not { } data) return false;

        CommitEdit();
        var doc = ToDocument();
        var result = MapImporter.Fill(doc, data, import.Options);
        if (result.Added == 0)
        {
            Status = result.Describe();
            return false;
        }

        var keep = (Termini.IndexOf(SelectedTerminus!), BusStops.IndexOf(SelectedBusStop!), Routes.IndexOf(SelectedRoute!));
        ApplyDocument(doc, refreshProblems: false, keep);
        Touch();            // unsaved now, and the step is kept for Undo
        CommitEdit();
        Status = $"Imported {data.Name}: {result.Describe(past: true)}";
        return true;
    }
}
