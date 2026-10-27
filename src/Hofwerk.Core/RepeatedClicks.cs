namespace Hofwerk.Core;

/// <summary>Counts quick repeated clicks on the same spot. The count is lost after too long a pause, or on <see cref="Reset"/>.</summary>
public sealed class RepeatedClicks(int required = 10, TimeSpan? pause = null)
{
    int _count;
    DateTime _last;

    public int Required { get; } = required;
    public TimeSpan Pause { get; } = pause ?? TimeSpan.FromSeconds(2);

    /// <summary>Counts one click. True on the click that completes the count (which then starts again from zero).</summary>
    public bool Tap(DateTime now)
    {
        if (_count > 0 && now - _last > Pause) _count = 0;
        _last = now;
        _count++;
        if (_count < Required) return false;
        _count = 0;
        return true;
    }

    public void Reset() => _count = 0;
}
