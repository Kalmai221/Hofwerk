using Hofwerk.Localization;
using System.Collections.Generic;
using System.Linq;
using Hofwerk.Core.Updates;

namespace Hofwerk.ViewModels;

// "What's new": the notes of the version that is installed (from the changelog inside the app) and of a newer version that
// the update check found (from its GitHub release).
public partial class MainViewModel
{
    static readonly Lazy<Changelog> ShippedChangelog = new(() => Changelog.Embedded);

    ChangelogSection? InstalledSection => ShippedChangelog.Value.Find(_currentVersion);

    // A development build has no version of its own in the changelog; it shows the newest notes instead, and says so.
    ChangelogSection? ShownInstalledSection => InstalledSection ?? ShippedChangelog.Value.Newest;

    public IReadOnlyList<ChangeGroup> InstalledNotes => ShownInstalledSection?.Groups ?? [];
    public bool HasInstalledNotes => InstalledNotes.Count > 0;
    public bool InstalledNotesMissing => !HasInstalledNotes;

    public string InstalledNotesTitle => InstalledSection is not null
        ? Localizer.F("What's new in {0}", _currentVersion.Text)
        : ShownInstalledSection is { } newest ? Localizer.F("What's new in {0}", newest.Version) : Localizer.T("What's new");

    public string InstalledNotesSubtitle => InstalledSection is not null ? Localizer.T("You have this version.")
        : ShownInstalledSection is not null
            ? Localizer.F("This build ({0}) has no notes of its own, so these are the newest ones in the changelog.", _currentVersion.Text)
            : Localizer.T("Release notes are not available in this build.");

    /// <summary>The notes of the newer version found by the update check, when its release has any.</summary>
    public IReadOnlyList<ChangeGroup> UpdateNotes => _update is null ? [] : ChangeNotes.FromMarkdown(_update.Notes);
    public bool ShowUpdateNotes => UpdateNotes.Count > 0;
    public string UpdateNotesTitle => _update is null ? Localizer.T("What's new") : Localizer.F("What's new in {0}", _update.Version);

    /// <summary>The page of the newer version's release on GitHub (shown as a button in the notes pop-up).</summary>
    public string? UpdatePageUrl => _update?.PageUrl;

    public const string ChangelogUrl = Hofwerk.Core.Model.HofHeader.Url + "changelog.html";

    void RaiseWhatsNewChanged()
    {
        OnPropertyChanged(nameof(UpdateNotes));
        OnPropertyChanged(nameof(ShowUpdateNotes));
        OnPropertyChanged(nameof(UpdateNotesTitle));
    }
}
