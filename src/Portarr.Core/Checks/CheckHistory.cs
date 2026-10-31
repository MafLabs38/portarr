namespace Portarr.Core.Checks;

/// <summary>
/// The last <see cref="Capacity"/> results of a check, in memory only (D-51): a fixed ring buffer, the
/// oldest result is overwritten. Not thread-safe; <see cref="CheckStore"/> serializes access.
/// </summary>
internal sealed class CheckHistory
{
    public const int Capacity = 90;

    private readonly CheckResult[] _results = new CheckResult[Capacity];
    private int _next;
    private int _count;

    public void Add(CheckResult result)
    {
        _results[_next] = result;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    /// <summary>Oldest first.</summary>
    public CheckResult[] ToArray()
    {
        var start = (_next - _count + Capacity) % Capacity;
        return Enumerable.Range(0, _count).Select(i => _results[(start + i) % Capacity]).ToArray();
    }
}
