using Hofwerk.Localization;
using CommunityToolkit.Mvvm.Input;
using Hofwerk.Core;
using Hofwerk.Core.Model;

namespace Hofwerk.ViewModels;

// Undo and redo, and autosave. Both work on snapshots: the file as text, taken whenever the person stops editing for a moment.
// An undo puts an earlier snapshot back on the screen, so everything that is part of the file can be undone (destinations, bus
// stops, routes, the settings page, the export locations) without each edit having to know how to take itself back.
public partial class MainViewModel
{
    const int MaxUndoSteps = 100;
    const long MaxUndoChars = 40_000_000;   // a very big file keeps fewer steps instead of using a lot of memory

    readonly List<string> _undo = [];
    readonly List<string> _redo = [];
    long _undoChars;
    string _current = "";   // the file as it was when the last edit settled
    string _saved = "";     // the file as it is on disk
    bool _pendingEdit;      // something changed after _current was taken
    CancellationTokenSource? _settleTimer;
    CancellationTokenSource? _autoSaveTimer;

    /// <summary>
    /// How long after the last keystroke or click an edit counts as one step: typing a word is one undo, not one per letter.
    /// Zero makes every change a step of its own (for tests).
    /// </summary>
    public TimeSpan EditSettleTime { get; set; } = TimeSpan.FromMilliseconds(700);

    public bool CanUndo => _pendingEdit || _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    string Snapshot() => string.Join('\n', HofWriter.ToLines(ToDocument()));

    /// <summary>A new file was loaded: it is the only state there is, saved and unchanged.</summary>
    void ResetHistory()
    {
        _settleTimer?.Cancel();
        _undo.Clear();
        _redo.Clear();
        _undoChars = 0;
        _pendingEdit = false;
        _current = _saved = Snapshot();
        RaiseHistoryChanged();
    }

    /// <summary>The file was just written with the current state, so "unsaved" now means "different from this".</summary>
    void MarkSaved()
    {
        _current = _saved = Snapshot();
        _pendingEdit = false;
        RaiseHistoryChanged();
    }

    void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // Called for every change the person makes to the file.
    void OnEdited()
    {
        _pendingEdit = true;
        RaiseHistoryChanged();
        ScheduleSettle();
        ScheduleAutoSave();
    }

    void ScheduleSettle()
    {
        _settleTimer?.Cancel();
        if (EditSettleTime <= TimeSpan.Zero) { CommitEdit(); return; }

        var timer = _settleTimer = new CancellationTokenSource();
        _ = Task.Delay(EditSettleTime, timer.Token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;
            void Commit() { if (!timer.IsCancellationRequested) CommitEdit(); }
            if (_uiContext is null) Commit(); else _uiContext.Post(_ => Commit(), null);
        }, TaskScheduler.Default);
    }

    /// <summary>Makes the edits so far one undo step now (instead of waiting for the person to pause). Also what decides whether the file counts as changed.</summary>
    public void CommitEdit()
    {
        _settleTimer?.Cancel();
        if (!_pendingEdit) return;
        _pendingEdit = false;

        string now = Snapshot();
        if (now != _current)
        {
            Push(_undo, _current);
            _redo.Clear();
            _current = now;
        }
        IsDirty = now != _saved;   // typed and then deleted again: nothing to save
        RaiseHistoryChanged();
    }

    void Push(List<string> stack, string snapshot)
    {
        stack.Add(snapshot);
        if (!ReferenceEquals(stack, _undo)) return;
        _undoChars += snapshot.Length;
        while (_undo.Count > 1 && (_undo.Count > MaxUndoSteps || _undoChars > MaxUndoChars))
        {
            _undoChars -= _undo[0].Length;
            _undo.RemoveAt(0);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        CommitEdit();
        if (_undo.Count == 0) return;

        _redo.Add(_current);
        _current = _undo[^1];
        _undoChars -= _current.Length;
        _undo.RemoveAt(_undo.Count - 1);
        Restore("Undone");
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo()
    {
        CommitEdit();
        if (_redo.Count == 0) return;

        Push(_undo, _current);
        _current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore("Redone");
    }

    void Restore(string what)
    {
        var old = _source;
        var doc = HofParser.Parse(_current.Split('\n'));
        // What the text cannot say is kept from the open file.
        doc.Encoding = old.Encoding;
        doc.HelpComments = old.HelpComments;
        doc.Preamble.Clear();
        doc.Preamble.AddRange(old.Preamble);

        var keep = (Termini.IndexOf(SelectedTerminus!), BusStops.IndexOf(SelectedBusStop!), Routes.IndexOf(SelectedRoute!));
        ApplyDocument(doc, refreshProblems: false, keep);
        IsDirty = _current != _saved;
        ScheduleAutoSave();
        RaiseHistoryChanged();
        Status = what;
    }

    // ---- Autosave ------------------------------------------------------------------------------------------------

    /// <summary>For tests: wait this long instead of the chosen time.</summary>
    public TimeSpan? AutoSaveDelayOverride { get; set; }

    void ScheduleAutoSave()
    {
        _autoSaveTimer?.Cancel();
        if (!_settings.AutoSave || FilePath is null) return;

        var timer = _autoSaveTimer = new CancellationTokenSource();
        var delay = AutoSaveDelayOverride ?? TimeSpan.FromSeconds(Math.Max(1, _settings.AutoSaveSeconds));
        _ = Task.Delay(delay, timer.Token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;
            void Run() { if (!timer.IsCancellationRequested) AutoSaveNow(); }
            if (_uiContext is null) Run(); else _uiContext.Post(_ => Run(), null);
        }, TaskScheduler.Default);
    }

    /// <summary>Saves the open file to its own path if it has unsaved changes. False, with the reason in the status bar, when that fails.</summary>
    public bool AutoSaveNow()
    {
        _autoSaveTimer?.Cancel();
        CommitEdit();
        if (!IsDirty || FilePath is not { } path) return true;
        try
        {
            Save(path);
            Status = Localizer.F("Autosaved {0} at {1}", Path.GetFileName(path), DateTime.Now.ToString("t"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Localizer.F("Autosave failed: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>Called when the window is closing: with autosave on the work is saved and nothing needs asking. True when nothing is left unsaved.</summary>
    public bool EverythingSavedForClose()
    {
        CommitEdit();
        if (!IsDirty) return true;
        return _settings.AutoSave && AutoSaveNow() && !IsDirty;
    }

    public bool AutoSave
    {
        get => _settings.AutoSave;
        set
        {
            if (_settings.AutoSave == value) return;
            _settings.AutoSave = value;
            _settings.Save();
            OnPropertyChanged();
            if (value) ScheduleAutoSave(); else _autoSaveTimer?.Cancel();
        }
    }

    /// <summary>How long after the last change the file is saved by itself.</summary>
    public static IReadOnlyList<string> AutoSaveChoices { get; } =
        ["5 seconds after I stop editing", "30 seconds after I stop editing", "2 minutes after I stop editing"];

    static readonly int[] AutoSaveSecondsByChoice = [5, 30, 120];

    public int AutoSaveChoice
    {
        get { int i = Array.IndexOf(AutoSaveSecondsByChoice, _settings.AutoSaveSeconds); return i < 0 ? 1 : i; }
        set
        {
            value = Math.Clamp(value, 0, AutoSaveSecondsByChoice.Length - 1);
            if (AutoSaveSecondsByChoice[value] == _settings.AutoSaveSeconds) return;
            _settings.AutoSaveSeconds = AutoSaveSecondsByChoice[value];
            _settings.Save();
            OnPropertyChanged();
            if (_settings.AutoSave) ScheduleAutoSave();
        }
    }
}
