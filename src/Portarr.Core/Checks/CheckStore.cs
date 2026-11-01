using System.Collections.Concurrent;
using Portarr.Core.Catalog;

namespace Portarr.Core.Checks;

/// <summary>
/// One check of an app: its app and its name. An app has one check today, named <see cref="MainName"/>; the store is keyed by the
/// check, not by the app, so a second check of an app is a new key, not a reshape (D-50).
/// </summary>
public readonly record struct CheckId(string AppId, string Name = CheckId.MainName)
{
    public const string MainName = "main";

    /// <summary>The check of an app, as a bare app id says it.</summary>
    public static implicit operator CheckId(string appId) => new(appId);
}

/// <summary>A check and its history, oldest result first. A published, immutable snapshot.</summary>
public sealed record CheckState(CheckId Id, Uri Target, CheckDefinition Definition, IReadOnlyList<CheckResult> History)
{
    public string AppId => Id.AppId;

    public CheckResult? Latest => History.Count > 0 ? History[^1] : null;
}

public interface ICheckStore
{
    CheckState? Get(CheckId id);

    void Record(CheckId id, Uri target, CheckDefinition definition, CheckResult result);

    /// <summary>Forgets the checks that are no longer declared (app removed, <c>check: false</c>).</summary>
    void Retain(IReadOnlySet<CheckId> ids);
}

/// <summary>
/// Check results kept in memory (D-51): a restart starts a new history. A new target URL or a new
/// definition starts a new history too, so old results never describe another check.
/// </summary>
public sealed class CheckStore : ICheckStore
{
    private readonly ConcurrentDictionary<CheckId, CheckState> _states = new();
    private readonly Dictionary<CheckId, CheckHistory> _histories = [];
    private readonly Lock _lock = new();

    public CheckState? Get(CheckId id) => _states.GetValueOrDefault(id);

    public void Record(CheckId id, Uri target, CheckDefinition definition, CheckResult result)
    {
        lock (_lock)
        {
            var current = _states.GetValueOrDefault(id);
            if (current is null || current.Target != target || current.Definition != definition || !_histories.TryGetValue(id, out var history))
            {
                history = new CheckHistory();
                _histories[id] = history;
            }

            history.Add(result);
            _states[id] = new CheckState(id, target, definition, history.ToArray());
        }
    }

    public void Retain(IReadOnlySet<CheckId> ids)
    {
        lock (_lock)
        {
            foreach (var id in _states.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _states.TryRemove(id, out _);
                _histories.Remove(id);
            }
        }
    }
}
