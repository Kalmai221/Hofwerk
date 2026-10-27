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
        ? $"What's new in {_currentVersion.Text}"
        : ShownInstalledSection is { } newest ? $"What's new in {newest.Version}" : "What's new";

    public string InstalledNotesSubtitle => InstalledSection is not null ? "You have this version."
        : ShownInstalledSection is not null
            ? $"This build ({_currentVersion.Text}) has no notes of its own, so these are the newest ones in the changelog."
            : "Release notes are not available in this build.";

    /// <summary>The notes of the newer version found by the update check, when its release has any.</summary>
    public IReadOnlyList<ChangeGroup> UpdateNotes => _update is null ? [] : ChangeNotes.FromMarkdown(_update.Notes);
    public bool ShowUpdateNotes => UpdateNotes.Count > 0;
    public string UpdateNotesTitle => _update is null ? "What's new" : $"What's new in {_update.Version}";

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
