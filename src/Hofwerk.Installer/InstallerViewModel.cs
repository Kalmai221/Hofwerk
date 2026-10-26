using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Omsi;
using Hofwerk.Installer.Engine;

namespace Hofwerk.Installer;

public enum SetupMode { Install, Uninstall, Unavailable }
public enum SetupStage { Welcome, Location, Working, Done, Failed }

/// <summary>The four situations on the "Where is OMSI 2?" page.</summary>
public enum LocationState { Detected, NotFound, Selected, Invalid }

public partial class InstallerViewModel : ObservableObject
{
    readonly PlatformInstaller _platform = PlatformInstaller.Create();
    readonly string _program = Environment.ProcessPath ?? "";
    string? _installedDirectory;

    public InstallerViewModel(string[] args)
    {
        bool wantsUninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase);
        bool hasPayload = _program.Length > 0 && PayloadFile.HasPayload(_program);

        if (hasPayload && !wantsUninstall)
        {
            Mode = SetupMode.Install;
            Directory = _platform.FindInstalledDirectory() ?? _platform.DefaultDirectory;
        }
        else
        {
            // No payload: this is the uninstaller sitting in the install folder.
            string? here = Path.GetDirectoryName(_program);
            if (here is not null && PlatformInstaller.IsInstalled(here))
            {
                Mode = SetupMode.Uninstall;
                Directory = here;
            }
            else if (hasPayload && _platform.FindInstalledDirectory() is { } found)
            {
                Mode = SetupMode.Uninstall;
                Directory = found;
            }
            else
            {
                Mode = SetupMode.Unavailable;
                Directory = _platform.DefaultDirectory;
            }
        }
    }

    public SetupMode Mode { get; }
    public string Version => AppInfo.Version;
    public bool SupportsFileAssociation => _platform.SupportsFileAssociation;

    [ObservableProperty] public partial SetupStage Stage { get; set; } = SetupStage.Welcome;
    [ObservableProperty] public partial string Directory { get; set; } = "";
    [ObservableProperty] public partial bool DesktopShortcut { get; set; }
    [ObservableProperty] public partial bool FileAssociation { get; set; } = true;
    [ObservableProperty] public partial bool RemoveUserData { get; set; }
    [ObservableProperty] public partial double Fraction { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial string ErrorText { get; set; } = "";
    [ObservableProperty] public partial string WelcomeError { get; set; } = "";

    // The "Where is OMSI 2?" page.
    [ObservableProperty] public partial LocationState Location { get; set; }
    [ObservableProperty] public partial string LocationPath { get; set; } = "";

    // What each screen shows. Computed from the mode and stage so the XAML stays free of logic.
    public bool IsInstall => Mode == SetupMode.Install;
    public bool IsUninstall => Mode == SetupMode.Uninstall;
    public bool ShowWelcome => Stage == SetupStage.Welcome && Mode != SetupMode.Unavailable;
    public bool ShowInstallOptions => ShowWelcome && IsInstall;
    public bool ShowUninstallOptions => ShowWelcome && IsUninstall;
    public bool ShowLocation => Stage == SetupStage.Location;
    public bool LocationDetected => Location == LocationState.Detected;
    public bool LocationNotFound => Location == LocationState.NotFound;
    public bool LocationSelected => Location == LocationState.Selected;
    public bool LocationInvalid => Location == LocationState.Invalid;
    public bool LocationHasPath => Location != LocationState.NotFound;
    public string LocationChooseLabel => Location switch
    {
        LocationState.Detected => "No, choose a folder…",
        LocationState.NotFound => "Select the folder…",
        _ => "Choose again…",
    };
    public string LocationUseLabel => Location switch
    {
        LocationState.Detected => "Yes, that is correct",
        LocationState.Invalid => "Use it anyway",
        _ => "Use this folder",
    };
    public bool HasWelcomeError => WelcomeError.Length > 0;
    public bool ShowWorking => Stage == SetupStage.Working;
    public bool ShowDone => Stage == SetupStage.Done;
    public bool ShowLaunch => ShowDone && IsInstall;
    public bool ShowFailed => Stage == SetupStage.Failed;
    public bool ShowUnavailable => Mode == SetupMode.Unavailable;

    public bool IsUpdate => IsInstall && PlatformInstaller.IsInstalled(SafeFullPath(Directory));
    public string Title => IsUninstall ? "Remove Hofwerk" : IsUpdate ? "Update Hofwerk" : "Install Hofwerk";
    public string PrimaryLabel => IsUninstall ? "Uninstall" : "Next";
    public string WorkingTitle => IsUninstall ? "Removing Hofwerk…" : IsUpdate ? "Updating Hofwerk…" : "Installing Hofwerk…";
    public string DoneTitle => IsUninstall ? "Hofwerk was removed" : IsUpdate ? "Hofwerk is up to date" : "Hofwerk is ready";
    public string DoneDetail => IsUninstall
        ? "The app and its shortcuts are gone. Your .hof files were not touched."
        : $"Installed to {_installedDirectory}";
    public string Percent => $"{(int)Math.Round(Fraction * 100)}%";

    partial void OnStageChanged(SetupStage value)
    {
        foreach (string name in new[]
                 {
                     nameof(ShowWelcome), nameof(ShowInstallOptions), nameof(ShowUninstallOptions), nameof(ShowWorking),
                     nameof(ShowDone), nameof(ShowLaunch), nameof(ShowFailed), nameof(DoneDetail), nameof(ShowLocation),
                 })
            OnPropertyChanged(name);
    }

    partial void OnDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsUpdate));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PrimaryLabel));
        OnPropertyChanged(nameof(WorkingTitle));
        OnPropertyChanged(nameof(DoneTitle));
    }

    partial void OnFractionChanged(double value) => OnPropertyChanged(nameof(Percent));

    partial void OnWelcomeErrorChanged(string value) => OnPropertyChanged(nameof(HasWelcomeError));

    partial void OnLocationChanged(LocationState value)
    {
        foreach (string name in new[]
                 {
                     nameof(LocationDetected), nameof(LocationNotFound), nameof(LocationSelected), nameof(LocationInvalid),
                     nameof(LocationHasPath), nameof(LocationChooseLabel), nameof(LocationUseLabel),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Welcome page, install mode: check the folder, then ask where OMSI 2 is.</summary>
    [RelayCommand]
    void Next()
    {
        if (IsUninstall) return;

        try { _platform.CheckTarget(Directory); }
        catch (InstallerException ex)
        {
            WelcomeError = ex.Message;
            return;
        }
        WelcomeError = "";

        // Look for OMSI now, so the next page can show what was found and ask whether it is right.
        string? found = OmsiLocator.FindInstallFolders().FirstOrDefault();
        LocationPath = found ?? "";
        Location = found is null ? LocationState.NotFound : LocationState.Detected;
        Stage = SetupStage.Location;
    }

    [RelayCommand]
    void BackToWelcome() => Stage = SetupStage.Welcome;

    /// <summary>The user accepted the folder shown (detected, picked, or "use it anyway"): install and remember it.</summary>
    [RelayCommand]
    Task UseLocationAsync() => StartAsync(LocationPath);

    /// <summary>The user does not want to say where OMSI is; Hofwerk will look for it itself.</summary>
    [RelayCommand]
    Task SkipLocationAsync() => StartAsync(null);

    /// <summary>Called with the folder the user picked by hand.</summary>
    public void LocationChosen(string folder)
    {
        LocationPath = folder;
        Location = OmsiLocator.LooksLikeOmsi(folder) ? LocationState.Selected : LocationState.Invalid;
    }

    [RelayCommand]
    Task RunAsync() => StartAsync(null); // uninstall

    async Task StartAsync(string? omsiFolder)
    {
        Fraction = 0;
        Message = "Getting ready";
        Stage = SetupStage.Working;

        var progress = new Progress<InstallProgress>(p =>
        {
            Fraction = p.Fraction;
            Message = p.Message;
        });

        try
        {
            if (IsInstall)
            {
                using var payload = PayloadFile.OpenPayload(_program) ?? throw new InstallerException("This installer has no app attached.");
                await _platform.InstallAsync(new InstallRequest(Directory, DesktopShortcut, FileAssociation && SupportsFileAssociation, omsiFolder),
                    payload, _program, progress);
                _installedDirectory = SafeFullPath(Directory);
            }
            else
            {
                await _platform.UninstallAsync(Directory, RemoveUserData, progress);
            }

            Fraction = 1;
            await Task.Delay(450); // let the bar reach the end before the screen changes
            Stage = SetupStage.Done;
        }
        catch (Exception ex)
        {
            // Expected problems carry a message meant for the user; anything else is logged and shown with its log path.
            if (ex is not (InstallerException or IOException or UnauthorizedAccessException)) SetupLog.Write("Unexpected error", ex);
            ErrorText = ex is InstallerException or IOException or UnauthorizedAccessException
                ? ex.Message
                : $"Something unexpected went wrong: {ex.Message}\nDetails were saved to {SetupLog.Path_}";
            Stage = SetupStage.Failed;
        }
    }

    [RelayCommand]
    void TryAgain() => Stage = IsInstall ? SetupStage.Location : SetupStage.Welcome;

    public void LaunchApp()
    {
        if (_installedDirectory is not null) _platform.Launch(_installedDirectory);
    }

    /// <summary>Picks a sensible install folder after browsing: inside a "Hofwerk" subfolder unless the folder is already empty or ours.</summary>
    public void UseBrowsedFolder(string chosen)
    {
        bool isOurs = PlatformInstaller.IsInstalled(chosen);
        bool isEmpty = !System.IO.Directory.Exists(chosen) || !System.IO.Directory.EnumerateFileSystemEntries(chosen).Any();
        Directory = isOurs || isEmpty ? chosen : Path.Combine(chosen, "Hofwerk");
    }

    static string SafeFullPath(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
    }
}
