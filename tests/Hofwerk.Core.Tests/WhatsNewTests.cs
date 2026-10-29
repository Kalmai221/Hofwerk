using System.Net;
using Hofwerk.Core;
using Hofwerk.Core.Updates;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "Updates")]
public class ChangeNotesTests
{
    [Fact]
    public void Headings_become_groups_and_bullets_become_points_without_markdown_marks()
    {
        var groups = ChangeNotes.FromMarkdown("""
            ### Added
            - **New file.** Make a new file from the `New` button, see [the docs](https://example.com/docs).
            - A second point
              that wraps onto another line.

            ### Fixed
            * Something was broken.
            """);

        Assert.Equal(["Added", "Fixed"], groups.Select(g => g.Title));
        Assert.Equal(
            ["New file. Make a new file from the New button, see the docs.", "A second point that wraps onto another line."],
            groups[0].Items);
        Assert.Equal(["Something was broken."], groups[1].Items);
    }

    [Fact]
    public void Loose_text_becomes_a_group_without_a_title_and_empty_notes_give_nothing()
    {
        var groups = ChangeNotes.FromMarkdown("First pre-release.");

        var only = Assert.Single(groups);
        Assert.Equal("", only.Title);
        Assert.Equal(["First pre-release."], only.Items);
        Assert.Empty(ChangeNotes.FromMarkdown(""));
        Assert.Empty(ChangeNotes.FromMarkdown(null));
        Assert.Empty(ChangeNotes.FromMarkdown("   \n  \n"));
    }

    [Fact]
    public void The_download_guide_after_the_end_marker_is_not_part_of_the_notes()
    {
        var groups = ChangeNotes.FromMarkdown("""
            ### Added
            - A feature.

            <!-- end-of-notes -->
            ---

            ### Which file do I download?
            - Windows: the exe
            """);

        var only = Assert.Single(groups);
        Assert.Equal("Added", only.Title);
        Assert.Equal(["A feature."], only.Items);
    }

    [Fact]
    public void GitHubs_own_release_notes_read_fine_too()
    {
        var groups = ChangeNotes.FromMarkdown("## What's Changed\n* Fix the thing by @someone in https://github.com/x/y/pull/12\n\n**Full Changelog**: https://github.com/x/y/compare/a...b");

        Assert.Equal("What's Changed", groups[0].Title);
        Assert.Contains("Fix the thing by @someone", groups[0].Items[0]);
    }
}

[Trait("Area", "Updates")]
public class ChangelogTests
{
    const string Sample = """
        # Changelog

        ## [Unreleased]

        Nothing yet.

        ## [0.2.0] - 2026-11-01

        ### Added
        - New things.

        ## [0.1-dev1] - pre-release

        First pre-release.

        [Unreleased]: https://example.com/a
        [0.2.0]: https://example.com/b
        """;

    [Fact]
    public void Versions_are_found_by_their_number_whatever_the_tag_looks_like()
    {
        var log = Changelog.Parse(Sample);

        Assert.Equal(["Unreleased", "0.2.0", "0.1-dev1"], log.Sections.Select(s => s.Version));
        Assert.Equal("New things.", Assert.Single(log.Find(AppVersion.TryParse("V0.2.0")!)!.Groups[0].Items));
        Assert.NotNull(log.Find(AppVersion.TryParse("0.2")!));            // 0.2 is 0.2.0
        Assert.Null(log.Find(AppVersion.TryParse("9.9.9")!));
        Assert.Equal("0.2.0", log.Newest!.Version);                       // not "Unreleased"
    }

    [Fact]
    public void The_link_list_at_the_end_is_not_part_of_the_last_version()
    {
        var last = Changelog.Parse(Sample).Sections[^1];

        Assert.DoesNotContain("example.com", last.Body);
    }

    [Fact]
    public void The_changelog_inside_the_app_is_there_and_has_the_versions_released_so_far()
    {
        var log = Changelog.Embedded;

        Assert.NotEmpty(log.Sections);
        Assert.NotNull(log.Find(AppVersion.TryParse("0.1-dev1")!));
        Assert.NotNull(log.Find(AppVersion.TryParse("0.1-dev2")!));
        Assert.All(log.Sections.Where(s => s.Version != "Unreleased"), s => Assert.NotEmpty(s.Groups));
    }
}

[Trait("Area", "Updates")]
public class WhatsNewViewModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-whatsnew-" + Guid.NewGuid().ToString("N"));

    public WhatsNewViewModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    sealed class Fake(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    MainViewModel NewViewModel(string current, string releasesJson = "[]")
    {
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets"));
        vm.ConfigureUpdates(new UpdateChecker(new HttpClient(new Fake(releasesJson)), runtimeId: "win-x64"), AppVersion.TryParse(current)!,
            Path.Combine(_dir, "dl"), _ => { });
        return vm;
    }

    [Fact]
    public void The_installed_version_shows_its_own_notes_from_the_changelog()
    {
        var vm = NewViewModel("0.1-dev2");

        Assert.True(vm.HasInstalledNotes);
        Assert.Equal("What's new in 0.1-dev2", vm.InstalledNotesTitle);
        Assert.Equal("You have this version.", vm.InstalledNotesSubtitle);
        Assert.Contains(vm.InstalledNotes, g => g.Title == "Added");
    }

    [Fact]
    public void A_build_without_notes_of_its_own_says_so_and_shows_the_newest_ones()
    {
        var vm = NewViewModel("9.9.9");                       // a version the changelog has never heard of

        Assert.True(vm.HasInstalledNotes);
        Assert.Contains("9.9.9", vm.InstalledNotesSubtitle);
        Assert.Contains("no notes of its own", vm.InstalledNotesSubtitle);
    }

    [Fact]
    public async Task A_newer_version_found_by_the_update_check_shows_its_release_notes()
    {
        string json = """
            [{"tag_name":"v0.3.0","html_url":"https://github.com/x/y","draft":false,"prerelease":false,
              "body":"### Added\n- A **great** feature.\n### Fixed\n- A bug.",
              "assets":[{"name":"Hofwerk-Setup-0.3.0-win-x64.exe","size":3,"browser_download_url":"https://github.com/x/y/Hofwerk-Setup-0.3.0-win-x64.exe"}]}]
            """;
        var vm = NewViewModel("0.2.0", json);
        Assert.False(vm.ShowUpdateNotes);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(vm.ShowUpdateNotes);
        Assert.Equal("What's new in 0.3.0", vm.UpdateNotesTitle);
        Assert.Equal(["Added", "Fixed"], vm.UpdateNotes.Select(g => g.Title));
        Assert.Equal("A great feature.", vm.UpdateNotes[0].Items[0]);
    }

    [Fact]
    public async Task A_release_without_notes_shows_no_notes_area()
    {
        string json = """
            [{"tag_name":"v0.3.0","html_url":"https://github.com/x/y","draft":false,"prerelease":false,"body":"",
              "assets":[{"name":"Hofwerk-Setup-0.3.0-win-x64.exe","size":3,"browser_download_url":"https://github.com/x/y/Hofwerk-Setup-0.3.0-win-x64.exe"}]}]
            """;
        var vm = NewViewModel("0.2.0", json);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(vm.UpdateAvailable);
        Assert.False(vm.ShowUpdateNotes);
    }
}
