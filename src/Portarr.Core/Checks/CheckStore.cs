using System.Collections.Concurrent;
using Portarr.Core.Catalog;

namespace Portarr.Core.Checks;

/// <summary>A check and its history, oldest result first. A published, immutable snapshot.</summary>
public sealed record CheckState(string AppId, Uri Target, CheckDefinition Definition, IReadOnlyList<CheckResult> History)
{
    public CheckResult? Latest => History.Count > 0 ? History[^1] : null;
}

public interface ICheckStore
{
    CheckState? Get(string appId);

    void Record(string appId, Uri target, CheckDefinition definition, CheckResult result);

    /// <summary>Forgets the checks of apps that are no longer checked (removed, <c>check: false</c>).</summary>
    void Retain(IReadOnlySet<string> appIds);
}

/// <summary>
/// Check results kept in memory (D-51): a restart starts a new history. A new target URL or a new
/// definition starts a new history too, so old results never describe another check.
/// </summary>
public sealed class CheckStore : ICheckStore
{
    private readonly ConcurrentDictionary<string, CheckState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckHistory> _histories = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public CheckState? Get(string appId) => _states.GetValueOrDefault(appId);

    public void Record(string appId, Uri target, CheckDefinition definition, CheckResult result)
    {
        lock (_lock)
        {
            var current = _states.GetValueOrDefault(appId);
            if (current is null || current.Target != target || current.Definition != definition || !_histories.TryGetValue(appId, out var history))
            {
                history = new CheckHistory();
                _histories[appId] = history;
            }

            history.Add(result);
            _states[appId] = new CheckState(appId, target, definition, history.ToArray());
        }
    }

    public void Retain(IReadOnlySet<string> appIds)
    {
        lock (_lock)
        {
            foreach (var appId in _states.Keys.Where(id => !appIds.Contains(id)).ToList())
            {
                _states.TryRemove(appId, out _);
                _histories.Remove(appId);
            }
        }
    }
}
