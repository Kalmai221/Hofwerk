using System.Collections.ObjectModel;

namespace Hofwerk.ViewModels;

/// <summary>
/// A search-as-you-type view of a collection. <see cref="View"/> follows the source and the text, and is updated in place
/// (items are removed, inserted or moved rather than rebuilt), so a list keeps the selection of items that stay visible.
/// </summary>
public sealed class FilteredCollection<T> where T : class
{
    readonly ObservableCollection<T> _source;
    readonly Func<T, string, bool> _matches;
    string _text = "";

    public FilteredCollection(ObservableCollection<T> source, Func<T, string, bool> matches)
    {
        _source = source;
        _matches = matches;
        source.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<T> View { get; } = [];

    public string Text
    {
        get => _text;
        set
        {
            value = (value ?? "").Trim();
            if (_text == value) return;
            _text = value;
            Refresh();
        }
    }

    public void Refresh()
    {
        var wanted = _source.Where(x => _text.Length == 0 || _matches(x, _text)).ToList();
        var wantedSet = new HashSet<T>(wanted);

        for (int i = View.Count - 1; i >= 0; i--)
            if (!wantedSet.Contains(View[i])) View.RemoveAt(i);

        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < View.Count && ReferenceEquals(View[i], wanted[i])) continue;

            int existing = View.IndexOf(wanted[i]);
            if (existing >= 0) View.Move(existing, i);
            else View.Insert(i, wanted[i]);
        }
    }
}
