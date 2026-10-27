using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace Hofwerk.ViewModels;

// The App settings page: preferences for Hofwerk itself (updates, where OMSI is, the string preset), as opposed to the
// settings of the .hof file that is open.
/// <summary>One of the colour themes the user can pick: "System", "Light" or "Dark".</summary>
public sealed record ThemeChoice(string Id, string Label);

public partial class MainViewModel
{
    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
        [new("System", "System default"), new("Light", "Light"), new("Dark", "Dark")];

    /// <summary>Raised when the user picks another theme; the window applies it. Gets "System", "Light" or "Dark".</summary>
    public event Action<string>? ThemeChanged;

    /// <summary>The theme to start with (an unknown value in the settings file means "System").</summary>
    public string ThemeId => SelectedTheme.Id;

    public ThemeChoice SelectedTheme
    {
        get => ThemeChoices.FirstOrDefault(t => t.Id == _settings.Theme) ?? ThemeChoices[0];
        set
        {
            if (value is null || value.Id == SelectedTheme.Id) return;
            _settings.Theme = value.Id;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeId));
            ThemeChanged?.Invoke(value.Id);
        }
    }

    /// <summary>The preset in use, for the drop-down in App settings. Choosing another one switches to it.</summary>
    public PresetViewModel? SelectedPreset
    {
        get => Presets.FirstOrDefault(p => p.IsActive);
        set
        {
            if (value is not null && !value.IsActive) UsePreset(value);
        }
    }

    public string OmsiFolderText => OmsiFolder ?? "Not found yet. Hofwerk looks for OMSI 2 in your Steam libraries.";

    /// <summary>Forgets a folder chosen by hand and looks for OMSI 2 again.</summary>
    [RelayCommand]
    void FindOmsiAutomatically()
    {
        _settings.OmsiFolder = null;
        _settings.Save();
        RefreshHome();
        Status = OmsiFolder is null ? "OMSI 2 was not found. Choose its folder by hand." : $"Found OMSI at {OmsiFolder}";
    }
}
