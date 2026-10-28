using System.Globalization;
using System.Text.RegularExpressions;
using Hofwerk.Core;
using Hofwerk.Core.Omsi;
using Hofwerk.Installer.Engine;
using Hofwerk.Localization;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class LanguageTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-language-" + Guid.NewGuid().ToString("N"));

    public LanguageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // ---- which language ------------------------------------------------------------------------------------------

    [Fact]
    public void English_comes_first_and_every_language_is_listed_by_its_own_name()
    {
        Assert.Equal(new Language("en", "English"), Localizer.Languages[0]);
        Assert.Contains(new Language("de", "Deutsch"), Localizer.Languages);
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de", "de")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]       // a language Hofwerk has no file for yet: English
    [InlineData("ja-JP", "en")]
    public void The_system_language_is_matched_to_a_language_Hofwerk_has_whatever_the_region(string culture, string expected)
    {
        Assert.Equal(expected, Localizer.Detect(new CultureInfo(culture)));
    }

    [Theory]
    [InlineData(null, "detect")]
    [InlineData("", "detect")]
    [InlineData("auto", "detect")]
    [InlineData("AUTO", "detect")]
    [InlineData("de", "de")]
    [InlineData("DE", "de")]
    [InlineData(" de ", "de")]
    [InlineData("en", "en")]
    [InlineData("xx", "en")]          // a setting nobody understands falls back to English
    public void A_setting_means_the_system_language_a_known_language_or_English(string? setting, string expected)
    {
        string result = Localizer.Resolve(setting);

        Assert.Equal(expected == "detect" ? Localizer.Detect() : expected, result);
    }

    // ---- the texts -----------------------------------------------------------------------------------------------

    [Fact]
    public void Texts_are_in_German_when_German_is_used_and_in_English_otherwise()
    {
        Assert.Equal("Save as", Localizer.T("Save as"));

        using (Localizer.Override("de"))
        {
            Assert.Equal("de", Localizer.Current);
            Assert.Equal("Speichern unter", Localizer.T("Save as"));
            Assert.Equal("Haltestellen", Localizer.T("Bus stops"));
        }

        Assert.Equal("en", Localizer.Current);
        Assert.Equal("Save as", Localizer.T("Save as"));
    }

    [Fact]
    public void A_text_with_no_translation_is_shown_in_English_not_blank()
    {
        using var german = Localizer.Override("de");

        Assert.Equal("A sentence nobody translated yet.", Localizer.T("A sentence nobody translated yet."));
        Assert.Equal("Hello 42", Localizer.F("Hello {0}", 42));
    }

    [Fact]
    public void Numbers_are_put_into_translated_texts_and_one_and_many_are_told_apart()
    {
        Assert.Equal("1 bus stop", Localizer.Count(1, "{0} bus stop", "{0} bus stops"));
        Assert.Equal("12 bus stops", Localizer.Count(12, "{0} bus stop", "{0} bus stops"));

        using var german = Localizer.Override("de");

        Assert.Equal("1 Haltestelle", Localizer.Count(1, "{0} bus stop", "{0} bus stops"));
        Assert.Equal("12 Haltestellen", Localizer.Count(12, "{0} bus stop", "{0} bus stops"));
        Assert.Equal("Zeile 7: Test", Localizer.F("Line {0}: {1}", 7, "Test"));
    }

    [Fact]
    public void The_messages_of_the_program_follow_the_language()
    {
        var result = new MergeResult(12, 3, 2, 0, 8, 1);
        using var german = Localizer.Override("de");

        Assert.Equal("12 Haltestellen, 2 Ziele und 8 Routen werden hinzugefügt; 3 Haltestellen und 1 Route sind schon in der Datei.", result.Describe());
        Assert.Equal("Bearbeite Ziele, Haltestellen und Routen für OMSI. Ziehe eine .hof-Datei auf dieses Fenster, um sie zu öffnen.",
            Localizer.T("Edit destinations, bus stops and routes for OMSI. Drop a .hof file onto this window to open it."));
    }

    [Fact]
    public void Every_translation_keeps_the_numbers_and_names_the_English_text_has_in_braces()
    {
        // "{0} Haltestellen" must still have its {0}: a lost one would show braces or leave a hole in the text
        static List<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+(?::[^}]*)?\}").Select(m => m.Value).Order().ToList();

        foreach (var language in Localizer.Languages.Where(l => l.Code != Localizer.English))
            foreach (var (english, translated) in Localizer.TextsOf(language.Code))
                Assert.True(Placeholders(english).SequenceEqual(Placeholders(translated)),
                    $"{language.Code}: “{english}” and its translation do not have the same {{0}}, {{1}} ...");
    }

    [Fact]
    public void No_translation_is_empty_and_none_is_left_the_same_as_the_English_where_it_should_not_be()
    {
        foreach (var language in Localizer.Languages.Where(l => l.Code != Localizer.English))
        {
            var texts = Localizer.TextsOf(language.Code);
            Assert.NotEmpty(texts);
            Assert.All(texts, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{language.Code}: “{pair.Key}” has an empty translation"));
        }
    }

    // ---- choosing a language in the app ----------------------------------------------------------------------------

    MainViewModel ViewModel(Action<string>? use = null)
    {
        var vm = new MainViewModel(AppSettings.Load(Path.Combine(_dir, "settings.json")), Path.Combine(_dir, "presets"));
        vm.ConfigureLanguage(use ?? (_ => { }));
        return vm;
    }

    [Fact]
    public void The_language_drop_down_offers_the_system_language_first_with_its_name_then_every_language()
    {
        var vm = ViewModel();

        Assert.Equal("auto", vm.LanguageChoices[0].Id);
        Assert.StartsWith("System default (", vm.LanguageChoices[0].Label);
        Assert.Contains(Localizer.NameOf(Localizer.Detect()), vm.LanguageChoices[0].Label);
        Assert.Equal(["en", "de"], vm.LanguageChoices.Skip(1).Select(c => c.Id).Take(2));
        Assert.Equal("auto", vm.SelectedLanguage.Id);        // nothing chosen yet: follow the system
    }

    [Fact]
    public void Choosing_a_language_switches_to_it_and_is_remembered_in_the_settings_file()
    {
        var used = new List<string>();
        var vm = ViewModel(used.Add);

        vm.SelectedLanguage = vm.LanguageChoices.Single(c => c.Id == "de");

        Assert.Equal(["de"], used);
        Assert.Equal("de", AppSettings.Load(Path.Combine(_dir, "settings.json")).Language);
        Assert.Equal("de", ViewModel().SelectedLanguage.Id);       // and still chosen next time

        vm.SelectedLanguage = vm.LanguageChoices[0];               // back to following the system
        Assert.Equal("auto", AppSettings.Load(Path.Combine(_dir, "settings.json")).Language);
    }

    // ---- the installer ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_language_chosen_in_the_installer_is_saved_where_Hofwerk_reads_it_next_to_what_is_already_there()
    {
        string file = Path.Combine(_dir, "installer-settings.json");
        File.WriteAllText(file, "{ \"OmsiFolder\": \"D:\\\\OMSI 2\", \"Theme\": \"Dark\" }");

        UserSettings.SaveLanguage("de", file);

        Assert.Equal("de", UserSettings.ReadLanguage(file));
        string text = File.ReadAllText(file);
        Assert.Contains("OMSI 2", text);                         // the other settings survive
        Assert.Contains("Dark", text);
        Assert.Equal("de", AppSettings.Load(file).Language);     // and the app reads it
    }

    [Fact]
    public void No_saved_language_reads_as_none()
    {
        Assert.Null(UserSettings.ReadLanguage(Path.Combine(_dir, "nothing.json")));
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        Assert.Null(UserSettings.ReadLanguage(Path.Combine(_dir, "broken.json")));
    }
}
