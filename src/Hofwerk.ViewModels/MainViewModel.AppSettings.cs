using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    // ---- Status messages disappear by themselves ----------------------------------------------------------------

    readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;   // the thread that made the view model: the UI
    CancellationTokenSource? _statusTimer;

    /// <summary>How long a message stays in the status bar before it clears itself.</summary>
    public TimeSpan StatusLifetime { get; set; } = TimeSpan.FromSeconds(3);

    partial void OnStatusChanged(string value)
    {
        _statusTimer?.Cancel();
        _statusTimer = null;
        if (string.IsNullOrEmpty(value)) return;

        var timer = _statusTimer = new CancellationTokenSource();
        _ = Task.Delay(StatusLifetime, timer.Token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;
            // Only the message that started this timer is cleared; a newer one has its own.
            void Clear() { if (Status == value) Status = ""; }
            if (_uiContext is null) Clear(); else _uiContext.Post(_ => Clear(), null);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Makes a new .hof file at <paramref name="path"/> (named after the file, with the string slots of the active preset and
    /// Hofwerk's credit and notes in it) and opens it. Returns false, and says why in the status bar, if it can't be written.
    /// </summary>
    public bool CreateNew(string path)
    {
        try
        {
            var doc = Hofwerk.Core.HofFile.CreateNew(
                System.IO.Path.GetFileNameWithoutExtension(path),
                Names.Count(Hofwerk.Core.Model.StringSlotKind.Terminus), Names.Count(Hofwerk.Core.Model.StringSlotKind.BusStop));
            Hofwerk.Core.HofFile.Save(doc, path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Could not create the file: {ex.Message}";
            return false;
        }

        if (!Open(path)) return false;
        Status = $"Created {System.IO.Path.GetFileName(path)}";
        return true;
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
