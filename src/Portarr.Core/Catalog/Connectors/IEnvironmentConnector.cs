using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Portarr.Core.Catalog;

/// <summary>
/// A connector's side of the catalog (D-105): the section of an environment it owns (<c>backrest:</c>), read and validated by
/// the connector's own library. Core never looks inside what <see cref="Parse"/> returns; the connector reads it back with an
/// extension on <see cref="CatalogEnvironment"/> (<c>environment.Backrest()</c>).
/// </summary>
public interface IEnvironmentConnector
{
    /// <summary>The key of the environment this connector owns: <c>docker</c>, <c>kubernetes</c>, <c>backrest</c>…</summary>
    string Section { get; }

    /// <summary>
    /// True for a runtime (docker, kubernetes, cloudflare…): the connector an environment's workloads are read from. At most one per
    /// environment; <c>hostMetrics</c> and <c>backrest</c> are auxiliary.
    /// </summary>
    bool IsRuntime => false;

    /// <summary>
    /// Whether an environment of this runtime is one machine (a Docker host: it can be a VM host, carry a Backrest). False for a cluster
    /// or an account. Meaningful for a runtime only.
    /// </summary>
    bool SingleMachine => true;

    /// <summary>Validates the section and returns its typed settings, or null after adding the errors to the context.</summary>
    EnvironmentConnectorResult? Parse(EnvironmentSectionContext context);
}

/// <summary>
/// What a connector made of its section: its typed <paramref name="Settings"/> (opaque to Core) and, when it knows them, the
/// machines of the environment that are VMs on another environment (<paramref name="MachineHosts"/>, machine to environment id, D-59).
/// </summary>
public sealed record EnvironmentConnectorResult(object Settings, IReadOnlyDictionary<string, string>? MachineHosts = null);

/// <summary>What a connector gets to read its section: the section itself, read strictly, and where it sits.</summary>
public sealed class EnvironmentSectionContext
{
    private readonly IReadOnlyList<ParsingEvent> _events;
    private readonly IDeserializer _deserializer;
    private readonly Action<string> _addError;

    internal EnvironmentSectionContext(string environmentId, string section, bool singleMachine, IReadOnlyList<ParsingEvent> events, IDeserializer deserializer, Action<string> addError)
    {
        EnvironmentId = environmentId;
        Section = section;
        SingleMachine = singleMachine;
        _events = events;
        _deserializer = deserializer;
        _addError = addError;
    }

    public string EnvironmentId { get; }

    /// <summary>The key being read.</summary>
    public string Section { get; }

    /// <summary>
    /// Whether the environment is one machine, which a VM host, a Backrest or a host agent need: true for a Docker host and for
    /// an environment without a runtime (a launcher-only one, D-32), false for a cluster or an account.
    /// </summary>
    public bool SingleMachine { get; }

    /// <summary>The section as a document of the connector's own type, read strictly: an unknown key is an error naming its line.</summary>
    public T? Read<T>()
        where T : class
    {
        try
        {
            return _deserializer.Deserialize<T?>(new EventReplayParser(_events));
        }
        catch (YamlException exception)
        {
            Error($"{Section}: Invalid YAML at line {exception.Start.Line}: {Innermost(exception).Message}");
            return null;
        }
    }

    /// <summary>Adds an error, prefixed with the environment it concerns: the connector's message names the key it means.</summary>
    public void Error(string message)
    {
        _addError($"Environment '{EnvironmentId}': {message}");
    }

    private static Exception Innermost(Exception exception)
    {
        return exception.InnerException is null ? exception : Innermost(exception.InnerException);
    }
}
