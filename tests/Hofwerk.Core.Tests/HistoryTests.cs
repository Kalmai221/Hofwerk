using Hofwerk.Core;
using Hofwerk.Core.Model;
using Hofwerk.ViewModels;

namespace Hofwerk.Core.Tests;

[Trait("Area", "App")]
public class HistoryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hofwerk-history-" + Guid.NewGuid().ToString("N"));
    string SettingsFile => Path.Combine(_dir, "settings.json");

    public HistoryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    const string Hof =
        "[name]\nCotterell\n\n[servicetrip]\nNot in Service\n\n[global_strings]\n6\nCotterell\nCotterell v1.1\nCotterell v1.1\n00\n00\n00\n\n" +
        "stringcount_terminus\n1\n\nstringcount_busstop\n0\n\n" +
        "[addterminus_allexit]\n1001\nNot in Service\nNot in Service\n\n[addterminus]\n1002\nSpecial\nSpecial\n\n[addterminus]\n1003\nThird\nThird\n";

    /// <summary>A view model with an open file; every change is its own undo step unless a test says otherwise.</summary>
    MainViewModel Open(bool settleAtOnce = true, Action<AppSettings>? configure = null)
    {
        var settings = AppSettings.Load(SettingsFile);
        configure?.Invoke(settings);
        var vm = new MainViewModel(settings, Path.Combine(_dir, "presets"));
        if (settleAtOnce) vm.EditSettleTime = TimeSpan.Zero;
        string file = Path.Combine(_dir, "test.hof");
        File.WriteAllText(file, Hof, HofEncodings.Windows1252);
        Assert.True(vm.Open(file));
        return vm;
    }

    // ---- undo and redo -------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_that_was_just_opened_has_nothing_to_undo()
    {
        var vm = Open();

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Undo_takes_an_edit_back_and_redo_puts_it_back()
    {
        var vm = Open();
        vm.Name = "Changed";
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Equal("Cotterell", vm.Name);
        Assert.True(vm.CanRedo);
        Assert.False(vm.CanUndo);

        vm.Redo();
        Assert.Equal("Changed", vm.Name);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Several_edits_are_undone_one_step_at_a_time_in_reverse_order()
    {
        var vm = Open();
        vm.Name = "One";
        vm.Name = "Two";
        vm.AddTerminusCommand.Execute(null);
        int withNew = vm.Termini.Count;

        vm.Undo();
        Assert.Equal(withNew - 1, vm.Termini.Count);
        Assert.Equal("Two", vm.Name);
        vm.Undo();
        Assert.Equal("One", vm.Name);
        vm.Undo();
        Assert.Equal("Cotterell", vm.Name);
        Assert.False(vm.CanUndo);

        vm.Redo(); vm.Redo(); vm.Redo();
        Assert.Equal(withNew, vm.Termini.Count);
        Assert.Equal("Two", vm.Name);
    }

    [Fact]
    public void Quick_typing_is_one_step_not_one_per_letter()
    {
        var vm = Open(settleAtOnce: false);
        vm.EditSettleTime = TimeSpan.FromHours(1);          // never settles by itself: Undo settles what is pending

        foreach (string typed in new[] { "N", "Ne", "New", "New name" }) vm.Name = typed;
        Assert.True(vm.CanUndo);
        vm.Undo();

        Assert.Equal("Cotterell", vm.Name);                 // all four letters in one step
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Edits_settle_by_themselves_after_a_pause()
    {
        var vm = Open(settleAtOnce: false);
        vm.EditSettleTime = TimeSpan.FromMilliseconds(30);
        vm.Name = "A";
        Thread.Sleep(250);
        vm.Name = "B";
        Thread.Sleep(250);

        vm.Undo();
        Assert.Equal("A", vm.Name);                         // two pauses, two steps
    }

    [Fact]
    public void A_new_edit_after_an_undo_ends_the_redo_branch()
    {
        var vm = Open();
        vm.Name = "First";
        vm.Undo();
        Assert.True(vm.CanRedo);

        vm.Name = "Other";

        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void The_settings_page_destinations_bus_stops_routes_and_export_locations_can_all_be_undone()
    {
        var vm = Open();
        vm.Ibis9xx = "42";
        vm.AddBusStopCommand.Execute(null);
        vm.AddRouteCommand.Execute(null);
        int stops = vm.BusStops.Count, routes = vm.Routes.Count;

        vm.Undo();
        Assert.Equal(routes - 1, vm.Routes.Count);
        vm.Undo();
        Assert.Equal(stops - 1, vm.BusStops.Count);
        vm.Undo();
        Assert.Equal("00", vm.Ibis9xx);
    }

    [Fact]
    public void An_undone_destination_comes_back_with_what_was_typed_in_it()
    {
        var vm = Open();
        var second = vm.Termini[1];
        string text = second.Ident;
        vm.SelectedTerminus = second;
        vm.RemoveTerminusCommand.Execute(null);
        Assert.DoesNotContain(vm.Termini, t => t.Ident == text);

        vm.Undo();

        Assert.Contains(vm.Termini, t => t.Ident == text);
    }

    [Fact]
    public void The_same_entry_stays_selected_after_an_undo()
    {
        var vm = Open();
        vm.SelectedTerminus = vm.Termini[2];
        vm.Name = "Changed";

        vm.Undo();

        Assert.Equal(2, vm.Termini.IndexOf(vm.SelectedTerminus!));
    }

    // ---- unsaved or not ------------------------------------------------------------------------------------------

    [Fact]
    public void Undoing_back_to_what_is_on_disk_means_nothing_is_unsaved_and_redo_means_it_is_again()
    {
        var vm = Open();
        vm.Name = "Changed";
        Assert.True(vm.IsDirty);

        vm.Undo();
        Assert.False(vm.IsDirty);

        vm.Redo();
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Typing_something_and_deleting_it_again_does_not_leave_the_file_changed()
    {
        var vm = Open();
        vm.Name = "Cotterell!";
        vm.Name = "Cotterell";

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void After_saving_you_can_still_undo_past_the_save_and_the_file_counts_as_changed_again()
    {
        var vm = Open();
        vm.Name = "Saved name";
        vm.Save(vm.FilePath!);
        Assert.False(vm.IsDirty);
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Equal("Cotterell", vm.Name);
        Assert.True(vm.IsDirty);                  // the disk has "Saved name"

        vm.Redo();
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Opening_another_file_starts_a_new_history()
    {
        var vm = Open();
        vm.Name = "Changed";
        string other = Path.Combine(_dir, "other.hof");
        File.WriteAllText(other, Hof.Replace("Cotterell", "Other"), HofEncodings.Windows1252);

        Assert.True(vm.Open(other));

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.Equal("Other", vm.Name);
    }

    [Fact]
    public void An_undo_does_not_change_how_the_file_is_written_the_encoding_stays()
    {
        var vm = Open();
        vm.Name = "Köln";
        vm.Undo();
        vm.Redo();

        vm.Save(vm.FilePath!);

        Assert.Contains("Köln", File.ReadAllText(vm.FilePath!, HofEncodings.Windows1252));
    }

    [Fact]
    public void Only_the_last_hundred_steps_are_kept()
    {
        var vm = Open();
        for (int i = 0; i < 130; i++) vm.Name = $"Name {i}";

        int undone = 0;
        while (vm.CanUndo) { vm.Undo(); undone++; }

        Assert.Equal(100, undone);
    }

    // ---- autosave ------------------------------------------------------------------------------------------------

    static bool WaitFor(Func<bool> condition, int milliseconds = 4000)
    {
        for (int waited = 0; waited < milliseconds; waited += 25)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    [Fact]
    public void With_autosave_on_the_file_is_saved_by_itself_after_a_pause()
    {
        var vm = Open(configure: s => s.AutoSave = true);
        vm.AutoSaveDelayOverride = TimeSpan.FromMilliseconds(80);

        vm.Name = "Autosaved name";

        Assert.True(WaitFor(() => File.ReadAllText(vm.FilePath!, HofEncodings.Windows1252).Contains("Autosaved name")));
        Assert.True(WaitFor(() => !vm.IsDirty));
        Assert.True(WaitFor(() => vm.Status.Contains("Autosaved")));    // the message is set just after the file is written
        vm.Undo();                                  // an autosave can still be taken back
        Assert.Equal("Cotterell", vm.Name);
    }

    [Fact]
    public void With_autosave_off_nothing_is_written_until_the_person_saves()
    {
        var vm = Open();
        vm.AutoSaveDelayOverride = TimeSpan.FromMilliseconds(50);

        vm.Name = "Not saved";
        Thread.Sleep(400);

        Assert.DoesNotContain("Not saved", File.ReadAllText(vm.FilePath!, HofEncodings.Windows1252));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Each_new_change_restarts_the_autosave_wait()
    {
        var vm = Open(configure: s => s.AutoSave = true);
        vm.AutoSaveDelayOverride = TimeSpan.FromMilliseconds(400);

        for (int i = 0; i < 5; i++) { vm.Name = $"Typing {i}"; Thread.Sleep(120); }
        Assert.True(vm.IsDirty);                    // still typing, so not yet

        Assert.True(WaitFor(() => !vm.IsDirty));
    }

    [Fact]
    public void Autosave_choices_are_remembered_in_the_settings_file()
    {
        var vm = Open();
        Assert.False(vm.AutoSave);
        Assert.Equal(1, vm.AutoSaveChoice);          // 30 seconds unless chosen

        vm.AutoSave = true;
        vm.AutoSaveChoice = 2;

        var reloaded = AppSettings.Load(SettingsFile);
        Assert.True(reloaded.AutoSave);
        Assert.Equal(120, reloaded.AutoSaveSeconds);
        Assert.Equal(3, MainViewModel.AutoSaveChoices.Count);
    }

    // ---- closing -------------------------------------------------------------------------------------------------

    [Fact]
    public void Closing_with_nothing_unsaved_needs_no_question()
    {
        var vm = Open();

        Assert.True(vm.EverythingSavedForClose());
    }

    [Fact]
    public void Closing_with_unsaved_changes_and_no_autosave_says_the_question_is_needed_and_keeps_the_changes()
    {
        var vm = Open();
        vm.Name = "Unsaved";

        Assert.False(vm.EverythingSavedForClose());
        Assert.True(vm.IsDirty);
        Assert.DoesNotContain("Unsaved", File.ReadAllText(vm.FilePath!, HofEncodings.Windows1252));
    }

    [Fact]
    public void Closing_with_unsaved_changes_and_autosave_on_just_saves_them()
    {
        var vm = Open(configure: s => s.AutoSave = true);
        vm.AutoSaveDelayOverride = TimeSpan.FromHours(1);
        vm.Name = "Saved on close";

        Assert.True(vm.EverythingSavedForClose());
        Assert.Contains("Saved on close", File.ReadAllText(vm.FilePath!, HofEncodings.Windows1252));
    }

    [Fact]
    public void Edits_still_waiting_to_settle_count_as_unsaved_when_closing_and_edits_that_cancelled_out_do_not()
    {
        var vm = Open(settleAtOnce: false);
        vm.EditSettleTime = TimeSpan.FromHours(1);

        vm.Name = "Typed";
        Assert.False(vm.EverythingSavedForClose());

        vm.Name = "Cotterell";                       // put back by hand
        Assert.True(vm.EverythingSavedForClose());
    }
}
