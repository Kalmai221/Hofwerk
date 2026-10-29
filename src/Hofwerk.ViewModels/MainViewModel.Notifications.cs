using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Localization;

namespace Hofwerk.ViewModels;

/// <summary>A message that pops up for a moment: what happened, and where in the app to go to do something about it.</summary>
public sealed partial class AppNotification : ObservableObject
{
    public AppNotification(string id, string title, string message, string target)
    {
        Id = id;
        Title = title;
        Message = message;
        Target = target;
    }

    /// <summary>Tells notifications apart: showing one with the same id replaces the one that is still up.</summary>
    public string Id { get; }
    public string Title { get; }
    public string Message { get; }

    /// <summary>Where clicking it goes, one of the <c>Notification…</c> names of <see cref="MainViewModel"/>; empty for none.</summary>
    public string Target { get; }
}

// The pop-ups in a corner of the window. The window shows <see cref="Notifications"/>, and goes where a clicked one points
// (<see cref="NavigateRequested"/>). Today the only one is "Update available".
public partial class MainViewModel
{
    /// <summary>Goes to the Updates part of App settings.</summary>
    public const string TargetUpdates = "updates";

    const string UpdateNotificationId = "update";

    public ObservableCollection<AppNotification> Notifications { get; } = [];

    /// <summary>How long a pop-up stays when nobody touches it. Zero keeps it until it is closed or clicked (for tests).</summary>
    public TimeSpan NotificationTime { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Raised when a notification was clicked: the part of the app to go to (a <c>Target…</c> name).</summary>
    public event Action<string>? NavigateRequested;

    string? _notifiedVersion;

    /// <summary>Shows a pop-up. One with the same id that is still there is replaced, so the same news is never up twice.</summary>
    public void Notify(AppNotification notification)
    {
        DismissNotification(notification.Id);
        Notifications.Add(notification);

        if (NotificationTime <= TimeSpan.Zero) return;
        _ = Task.Delay(NotificationTime).ContinueWith(_ =>
        {
            void Remove() { if (Notifications.Contains(notification)) Notifications.Remove(notification); }
            if (_uiContext is null) Remove(); else _uiContext.Post(_ => Remove(), null);
        }, TaskScheduler.Default);
    }

    void DismissNotification(string id)
    {
        foreach (var old in Notifications.Where(n => n.Id == id).ToList()) Notifications.Remove(old);
    }

    [RelayCommand]
    void CloseNotification(AppNotification? notification)
    {
        if (notification is not null) Notifications.Remove(notification);
    }

    /// <summary>A notification was clicked: it goes away and the window is asked to show what it is about.</summary>
    [RelayCommand]
    void OpenNotification(AppNotification? notification)
    {
        if (notification is null) return;
        Notifications.Remove(notification);
        if (notification.Target.Length > 0) NavigateRequested?.Invoke(notification.Target);
    }

    // The start-up check found a newer version: tell once per version (a manual check from App settings is already looking at it).
    void NotifyAboutUpdate(bool quiet)
    {
        if (_update is null) { DismissNotification(UpdateNotificationId); return; }
        string version = _update.Version.ToString();
        if (!quiet || _notifiedVersion == version) return;

        _notifiedVersion = version;
        Notify(new AppNotification(UpdateNotificationId, Localizer.T("Update available"),
            Localizer.F("Hofwerk {0} is ready to install. Click to see it in App settings.", version), TargetUpdates));
    }
}
