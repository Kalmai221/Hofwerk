using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core.Updates;

namespace Hofwerk.ViewModels;

// Finding, downloading and starting a newer version of Hofwerk.
public partial class MainViewModel
{
    UpdateChecker _updater = new();
    AppVersion _currentVersion = AppVersion.Current;
    UpdateInfo? _update;
    string _downloadFolder = Path.Combine(Path.GetTempPath(), "Hofwerk-update");
    Action<string> _launchInstaller = UpdateChecker.Launch;

    /// <summary>Raised after the installer was started: the window closes so the installer can replace the files.</summary>
    public event Action? ExitRequested;

    /// <summary>For tests: a checker that talks to a fake server, a pretend running version, and no real installer.</summary>
    public void ConfigureUpdates(UpdateChecker updater, AppVersion current, string downloadFolder, Action<string> launchInstaller)
    {
        _updater = updater;
        _currentVersion = current;
        _downloadFolder = downloadFolder;
        _launchInstaller = launchInstaller;
        OnPropertyChanged(nameof(CurrentVersion));
    }

    public string CurrentVersion => _currentVersion.Text;

    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";
    [ObservableProperty] public partial bool IsUpdateBusy { get; set; }
    [ObservableProperty] public partial double UpdateProgress { get; set; }

    public bool UpdateAvailable => _update is not null;
    public bool CanCheckForUpdates => !IsUpdateBusy;
    public string UpdateHeadline => _update is null ? "You are up to date" : $"Hofwerk {_update.Version} is available";
    public string UpdateDetail => _update is null
        ? UpdateStatus
        : $"{(_update.IsPreRelease ? "Pre-release · " : "")}{_update.AssetName} · {_update.Size / 1_048_576.0:0.#} MB";

    public bool CheckForUpdatesOnStart
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value) return;
            _settings.CheckForUpdates = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool IncludePreReleases
    {
        get => _settings.IncludePreReleases;
        set
        {
            if (_settings.IncludePreReleases == value) return;
            _settings.IncludePreReleases = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    partial void OnUpdateStatusChanged(string value) => OnPropertyChanged(nameof(UpdateDetail));

    partial void OnIsUpdateBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
        InstallUpdateCommand.NotifyCanExecuteChanged();
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called once when the app starts; quiet when there is nothing new or no network.</summary>
    public async Task CheckForUpdatesOnStartAsync()
    {
        if (_settings.CheckForUpdates) await CheckForUpdatesAsync(quiet: true);
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    async Task CheckForUpdates() => await CheckForUpdatesAsync(quiet: false);

    async Task CheckForUpdatesAsync(bool quiet)
    {
        IsUpdateBusy = true;
        if (!quiet) UpdateStatus = "Checking…";
        try
        {
            _update = await _updater.CheckAsync(_currentVersion, _settings.IncludePreReleases);
            UpdateStatus = _update is null ? $"You have the latest version ({_currentVersion})." : "";
            if (_update is not null) Status = $"Hofwerk {_update.Version} is available – see Settings.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            _update = null;
            UpdateStatus = quiet ? "" : "Could not check for updates. Check your internet connection and try again.";
        }
        finally
        {
            IsUpdateBusy = false;
            OnPropertyChanged(nameof(UpdateAvailable));
            OnPropertyChanged(nameof(UpdateHeadline));
            OnPropertyChanged(nameof(UpdateDetail));
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    bool CanInstallUpdate() => _update is not null && !IsUpdateBusy;

    /// <summary>Downloads the new installer, starts it and asks the window to close so the install can replace the files.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    async Task InstallUpdate()
    {
        if (_update is null) return;
        if (IsDirty)
        {
            UpdateStatus = "Save your changes first, then update.";
            return;
        }

        IsUpdateBusy = true;
        UpdateProgress = 0;
        UpdateStatus = "Downloading…";
        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateProgress = p;
                UpdateStatus = $"Downloading… {p:P0}";
            });
            string installer = await _updater.DownloadAsync(_update, _downloadFolder, progress);
            UpdateStatus = "Starting the installer…";
            _launchInstaller(installer);
            ExitRequested?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            UpdateStatus = $"The update failed: {ex.Message}";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    [RelayCommand]
    void OpenReleasePage()
    {
        if (_update is null || !Uri.TryCreate(_update.PageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }
}
