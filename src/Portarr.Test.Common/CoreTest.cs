using Microsoft.Extensions.Time.Testing;

namespace Portarr.Test.Common;

/// <summary>
/// Base class for unit tests of one class: <see cref="Subject"/> is built lazily with
/// substitutes for its dependencies, available through <see cref="Mocker"/>.
/// </summary>
public abstract class CoreTest<TSubject>
    where TSubject : class
{
    private TSubject? _subject;

    protected AutoSubstitute Mocker { get; } = new();

    protected FakeTimeProvider Clock => (FakeTimeProvider)Mocker.Get<TimeProvider>();

    protected TSubject Subject => _subject ??= Mocker.Create<TSubject>();
}
