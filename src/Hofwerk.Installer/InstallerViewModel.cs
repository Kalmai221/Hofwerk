using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Omsi;
using Hofwerk.Core.Updates;
using Hofwerk.Installer.Engine;
using Hofwerk.Localization;
using System.Collections.ObjectModel;

namespace Hofwerk.Installer;

public enum SetupMode { Install, Uninstall, Unavailable }
public enum SetupStage { Welcome, Location, Working, Done, Failed }

/// <summary>The four situations on the "Where is OMSI 2?" page.</summary>
public enum LocationState { Detected, NotFound, Selected, Invalid }

public enum StepState { Pending, Active, Done }

/// <summary>One line of the list shown while installing: Download, Extract, Move files, Cleanup, Close.</summary>
public partial class SetupStep(InstallStep step, string name) : ObservableObject
{
    public InstallStep Step { get; } = step;
    public string Name { get; } = name;
    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] public partial StepState State { get; set; } = StepState.Pending;

    public string Marker => State switch { StepState.Done => "✓", StepState.Active => "●", _ => "○" };
    public double Dim => State == StepState.Pending ? 0.5 : 1;
    public bool IsActive => State == StepState.Active;

    partial void OnStateChanged(StepState value)
    {
        OnPropertyChanged(nameof(Marker));
        OnPropertyChanged(nameof(Dim));
        OnPropertyChanged(nameof(IsActive));
    }
}

public partial class InstallerViewModel : ObservableObject
{
    /// <summary>The stages of an install, in the order they happen.</summary>
    public IReadOnlyList<SetupStep> Steps { get; } =
    [
        new(InstallStep.Download, "Download"),
        new(InstallStep.Extract, "Extract"),
        new(InstallStep.Move, "Move files"),
        new(InstallStep.Cleanup, "Cleanup"),
        new(InstallStep.Close, "Close"),
    ];

    public bool ShowSteps => IsInstall;

    /// <summary>Everything before <paramref name="current"/> is done, <paramref name="current"/> is running, the rest waits.</summary>
    void SetStep(InstallStep current)
    {
        foreach (var s in Steps)
            s.State = s.Step < current ? StepState.Done : s.Step == current ? StepState.Active : StepState.Pending;
    }

    readonly PlatformInstaller _platform = PlatformInstaller.Create();
    readonly string _program = Environment.ProcessPath ?? "";
    string? _installedDirectory;
    readonly WebManifest? _web;

    // Looking for a newer installer is skipped by the offline installer, by --no-update-check (and HOFWERK_NO_UPDATE_CHECK=1), which the test scripts use.
    bool _checkForNewer;
    UpdateChecker _updater = new();
    AppVersion _ownVersion = AppVersion.TryParse(AppInfo.Version) ?? AppVersion.Current;
    UpdateInfo? _newer;
    string _downloadFolder = Path.Combine(Path.GetTempPath(), "Hofwerk-update");
    Action<string> _launchInstaller = UpdateChecker.Launch;

    /// <summary>Raised after a newer installer was started: this window closes so only one installer is open.</summary>
    public event Action? ExitRequested;

    // ---- Language: the installer is shown in it, and Hofwerk starts in it ------------------------------------------------

    public ObservableCollection<LanguageChoice> LanguageChoices { get; } = [];
    string _languageId = Localizer.Automatic;

    /// <summary>The language of the installer and of the installed Hofwerk. Picking one switches this window at once.</summary>
    public LanguageChoice SelectedLanguage
    {
        get => LanguageChoices.FirstOrDefault(c => c.Id == _languageId) ?? LanguageChoices[0];
        set
        {
            if (value is null || value.Id == _languageId) return;
            _languageId = value.Id;
            Localizer.Use(value.Id);
            OnPropertyChanged();
        }
    }

    /// <summary>"auto" or a language code: what the installed Hofwerk is told to start in.</summary>
    public string LanguageId => _languageId;

    void RebuildLanguageChoices()
    {
        LanguageChoices.Clear();
        foreach (var choice in Localizer.Choices()) LanguageChoices.Add(choice);
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    public InstallerViewModel(string[] args)
    {
        // The language chosen last time (installing again or updating keeps it), else the language of the system; --language overrides both.
        int option = Array.FindIndex(args, a => a.Equals("--language", StringComparison.OrdinalIgnoreCase));
        _languageId = option >= 0 && option + 1 < args.Length ? args[option + 1] : UserSettings.ReadLanguage() ?? Localizer.Automatic;
        Localizer.Use(_languageId);
        RebuildLanguageChoices();
        Localizer.Changed += () => { RebuildLanguageChoices(); OnPropertyChanged(string.Empty); };
        _checkForNewer = !args.Contains("--no-update-check", StringComparer.OrdinalIgnoreCase)
                         && Environment.GetEnvironmentVariable("HOFWERK_NO_UPDATE_CHECK") != "1";
        bool wantsUninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase);
        bool hasPayload = _program.Length > 0 && PayloadFile.HasPayload(_program);
        // The offline installer carries the app and never goes online by itself; only the online installer looks for a newer one.
        if (hasPayload) _checkForNewer = false;
        // The online installer has no app inside it, only where to download it from.
        _web = hasPayload || _program.Length == 0 ? null : WebPayload.TryRead(_program);
        bool canInstall = hasPayload || _web is not null;

        if (canInstall && !wantsUninstall)
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
            else if (canInstall && _platform.FindInstalledDirectory() is { } found)
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

    // ---- A newer version than this installer ------------------------------------------------------------------

    public bool ShowNewerVersion => _newer is not null && Stage == SetupStage.Welcome;
    public string NewerHeadline => _newer is null ? "" : Localizer.F("Hofwerk {0} is available", _newer.Version);
    public string NewerDetail => _newer is null ? "" : Localizer.F("This installer has {0}. Get the newest installer instead, or continue with this one.", Version);
    [ObservableProperty] public partial bool IsDownloadingNewer { get; set; }
    [ObservableProperty] public partial double NewerProgress { get; set; }
    [ObservableProperty] public partial string NewerStatus { get; set; } = "";
    public bool CanDownloadNewer => !IsDownloadingNewer;

    partial void OnIsDownloadingNewerChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownloadNewer));
        DownloadNewerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>For tests: a checker that talks to a fake server, a pretend version of this installer, and no real launch.</summary>
    public void ConfigureUpdates(UpdateChecker updater, AppVersion own, string downloadFolder, Action<string> launchInstaller)
    {
        _updater = updater;
        _ownVersion = own;
        _downloadFolder = downloadFolder;
        _launchInstaller = launchInstaller;
        _checkForNewer = true;
    }

    /// <summary>Asks GitHub whether a newer release exists. Quiet when it does not or when there is no network.</summary>
    public async Task CheckForNewerVersionAsync()
    {
        if (!_checkForNewer || Mode == SetupMode.Uninstall) return;
        try { _newer = await _updater.CheckAsync(_ownVersion, includePreReleases: false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException) { _newer = null; }

        OnPropertyChanged(nameof(ShowNewerVersion));
        OnPropertyChanged(nameof(NewerHeadline));
        OnPropertyChanged(nameof(NewerDetail));
    }

    /// <summary>Downloads the newest installer, starts it, and closes this one.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadNewer))]
    async Task DownloadNewer()
    {
        if (_newer is null) return;
        IsDownloadingNewer = true;
        NewerProgress = 0;
        NewerStatus = "Downloading…";
        try
        {
            var progress = new Progress<double>(p =>
            {
                NewerProgress = p;
                NewerStatus = Localizer.F("Downloading… {0:P0}", p);
            });
            string installer = await _updater.DownloadAsync(_newer, _downloadFolder, progress);
            NewerStatus = Localizer.T("Starting the new installer…");
            _launchInstaller(installer);
            ExitRequested?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            NewerStatus = Localizer.F("The download failed: {0} You can still install this version.", ex.Message);
        }
        finally { IsDownloadingNewer = false; }
    }

    public string Version => AppInfo.Version;
    public bool SupportsFileAssociation => _platform.SupportsFileAssociation;

    /// <summary>The online installer: the app is downloaded while installing instead of being inside this file.</summary>
    public bool IsWeb => _web is not null;
    public string WebNote => _web is null ? "" : Localizer.F("This small installer downloads Hofwerk ({0:0.#} MB) from GitHub while installing. It needs an internet connection.", _web.Size / 1_048_576.0);

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
        LocationState.Detected => Localizer.T("No, choose a folder…"),
        LocationState.NotFound => Localizer.T("Select the folder…"),
        _ => Localizer.T("Choose again…"),
    };
    public string LocationUseLabel => Location switch
    {
        LocationState.Detected => Localizer.T("Yes, that is correct"),
        LocationState.Invalid => Localizer.T("Use it anyway"),
        _ => Localizer.T("Use this folder"),
    };
    public bool HasWelcomeError => WelcomeError.Length > 0;
    public bool ShowWorking => Stage == SetupStage.Working;
    public bool ShowDone => Stage == SetupStage.Done;
    public bool ShowLaunch => ShowDone && IsInstall;
    public bool ShowFailed => Stage == SetupStage.Failed;
    public bool ShowUnavailable => Mode == SetupMode.Unavailable;

    public bool IsUpdate => IsInstall && PlatformInstaller.IsInstalled(SafeFullPath(Directory));
    public string Title => IsUninstall ? Localizer.T("Remove Hofwerk") : IsUpdate ? Localizer.T("Update Hofwerk") : Localizer.T("Install Hofwerk");
    public string PrimaryLabel => IsUninstall ? Localizer.T("Uninstall") : Localizer.T("Next");
    public string WorkingTitle => IsUninstall ? Localizer.T("Removing Hofwerk…") : IsUpdate ? Localizer.T("Updating Hofwerk…") : Localizer.T("Installing Hofwerk…");
    public string DoneTitle => IsUninstall ? Localizer.T("Hofwerk was removed") : IsUpdate ? Localizer.T("Hofwerk is up to date") : Localizer.T("Hofwerk is ready");
    public string DoneDetail => IsUninstall
        ? Localizer.T("The app and its shortcuts are gone. Your .hof files were not touched.")
        : Localizer.F("Installed to {0}", _installedDirectory);
    public string Percent => $"{(int)Math.Round(Fraction * 100)}%";

    partial void OnStageChanged(SetupStage value)
    {
        foreach (string name in new[]
                 {
                     nameof(ShowWelcome), nameof(ShowInstallOptions), nameof(ShowUninstallOptions), nameof(ShowWorking),
                     nameof(ShowDone), nameof(ShowLaunch), nameof(ShowFailed), nameof(DoneDetail), nameof(ShowLocation), nameof(ShowNewerVersion),
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
        Message = Localizer.T("Getting ready");
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
                // Online installer: the first part of the bar is the download, the rest is installing.
                double share = IsWeb ? 0.45 : 0;
                foreach (var s in Steps) s.Note = "";
                Steps[0].Note = IsWeb ? "" : "included in this installer";
                SetStep(IsWeb ? InstallStep.Download : InstallStep.Extract);
                var downloading = new Progress<double>(d =>
                {
                    Fraction = d * share;
                    Message = Localizer.F("Downloading Hofwerk… {0:P0}", d);
                });
                if (IsWeb) Message = Localizer.T("Downloading Hofwerk…");
                using var payload = await WebPayload.OpenInstallPayloadAsync(_program, downloading)
                                    ?? throw new InstallerException(Localizer.T("This installer has no app attached."));
                var installing = new Progress<InstallProgress>(p =>
                {
                    Fraction = share + p.Fraction * (1 - share);
                    Message = p.Message;
                    if (p.Step != InstallStep.None) SetStep(p.Step);
                });
                await _platform.InstallAsync(new InstallRequest(Directory, DesktopShortcut, FileAssociation && SupportsFileAssociation, omsiFolder, Language: _languageId),
                    payload, _program, installing);
                _installedDirectory = SafeFullPath(Directory);
            }
            else
            {
                await _platform.UninstallAsync(Directory, RemoveUserData, progress);
            }

            Fraction = 1;
            if (IsInstall) SetStep(InstallStep.Close);
            await Task.Delay(450); // let the bar reach the end before the screen changes
            if (IsInstall) foreach (var s in Steps) s.State = StepState.Done;
            Stage = SetupStage.Done;
        }
        catch (Exception ex)
        {
            // Expected problems carry a message meant for the user; anything else is logged and shown with its log path.
            if (ex is not (InstallerException or IOException or UnauthorizedAccessException)) SetupLog.Write("Unexpected error", ex);
            ErrorText = ex is InstallerException or IOException or UnauthorizedAccessException
                ? ex.Message
                : Localizer.F("Something unexpected went wrong: {0}\nDetails were saved to {1}", ex.Message, SetupLog.Path_);
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
