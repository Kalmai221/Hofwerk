using Hofwerk.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hofwerk.Core.Presets;

namespace Hofwerk.ViewModels;

/// <summary>
/// The questions asked when someone brings a strings file of their own: what to call the preset, how the names are written in the
/// file, and what they are for. Shows what Hofwerk understood of the file so a wrong answer is easy to spot.
/// </summary>
public sealed partial class PresetImportViewModel : ObservableObject
{
    public static IReadOnlyList<string> LayoutChoices =>
    [
        Localizer.T("An OMSI strings file (string 0:, [TerminusString] ...)"),
        Localizer.T("One name per line, with the slot number in front (0 = IBIS 1)"),
        Localizer.T("One name per line, in slot order (the first line is the first slot)"),
    ];

    readonly string _text;

    public PresetImportViewModel(string text, string filePath)
    {
        _text = text;
        FileName = Path.GetFileName(filePath);
        Name = PresetImporter.SuggestName(filePath);
        LayoutIndex = (int)PresetImporter.Detect(text);
    }

    public string FileName { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Author { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial string Url { get; set; } = "";
    [ObservableProperty] public partial int LayoutIndex { get; set; }
    [ObservableProperty] public partial bool ForDestinations { get; set; } = true;
    [ObservableProperty] public partial bool ForBusStops { get; set; } = true;
    /// <summary>The slot the first line of a one-per-line file is for (OMSI counts from 0).</summary>
    [ObservableProperty] public partial int FirstSlot { get; set; }

    public StringFileLayout Layout => (StringFileLayout)Math.Clamp(LayoutIndex, 0, 2);

    /// <summary>The questions about plain files do not apply to an OMSI strings file, which says it all itself.</summary>
    public bool AsksWhatFor => Layout != StringFileLayout.OmsiStrings;
    public bool AsksFirstSlot => Layout == StringFileLayout.OnePerLine;

    public PresetImportOptions Options => new(Name, Author, Description, Url, Layout,
        (ForDestinations ? SlotTargets.Destinations : 0) | (ForBusStops ? SlotTargets.BusStops : 0), FirstSlot);

    public string Summary
    {
        get
        {
            var preview = PresetImporter.Preview(_text, Layout, FirstSlot);
            if (preview.Slots == 0) return Localizer.T("Nothing was found with this way of reading the file. Try another one.");
            return Localizer.F("Found {0} names, for example {1}.", preview.Slots, string.Join(", ", preview.Sample));
        }
    }

    public bool CanImport => Name.Trim().Length > 0 && (!AsksWhatFor || ForDestinations || ForBusStops)
                             && PresetImporter.Preview(_text, Layout, FirstSlot).Slots > 0;

    /// <summary>Writes the preset and returns it. Throws <see cref="PresetImportException"/> with a message for the person.</summary>
    public StringPreset Import(string userFolder) => PresetImporter.Import(_text, Options, userFolder);

    partial void OnNameChanged(string value) => Changed();
    partial void OnLayoutIndexChanged(int value)
    {
        OnPropertyChanged(nameof(AsksWhatFor));
        OnPropertyChanged(nameof(AsksFirstSlot));
        Changed();
    }
    partial void OnForDestinationsChanged(bool value) => Changed();
    partial void OnForBusStopsChanged(bool value) => Changed();
    partial void OnFirstSlotChanged(int value) => Changed();

    void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanImport));
    }
}
