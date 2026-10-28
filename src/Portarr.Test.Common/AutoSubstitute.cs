using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Portarr.Test.Common;

/// <summary>
/// Builds a subject by passing a substitute (or a registered instance) for each constructor
/// parameter. The NSubstitute counterpart of the auto-mocker used by the *arr test suites.
/// </summary>
public sealed class AutoSubstitute
{
    private readonly Dictionary<Type, object> _instances = [];

    public AutoSubstitute()
    {
        // Time is never substituted: tests drive it explicitly.
        Use<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>Registers the instance to inject for <typeparamref name="T"/>.</summary>
    public T Use<T>(T instance)
        where T : class
    {
        _instances[typeof(T)] = instance;
        return instance;
    }

    /// <summary>Returns the substitute (or registered instance) injected for <typeparamref name="T"/>.</summary>
    public T Get<T>()
        where T : class
    {
        return (T)Resolve(typeof(T));
    }

    public TSubject Create<TSubject>()
        where TSubject : class
    {
        var constructor = typeof(TSubject)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{typeof(TSubject).Name} has no public constructor.");

        var arguments = constructor.GetParameters().Select(p => Resolve(p.ParameterType)).ToArray();

        return (TSubject)constructor.Invoke(arguments);
    }

    private object Resolve(Type type)
    {
        if (!_instances.TryGetValue(type, out var instance))
        {
            instance = Substitute.For([type], []);
            _instances[type] = instance;
        }

        return instance;
    }
}
