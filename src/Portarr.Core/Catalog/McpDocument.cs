using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Portarr.Core.Catalog;

// YAML shape of the mcpServers section (V9). Kept apart from the domain records, like the rest of the catalog document.
#pragma warning disable CA1002, CA2227 // Mutable lists and maps are what the YAML deserializer fills.

/// <summary>See <see cref="McpServerDefinition"/>.</summary>
internal sealed class McpServerDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>An app of the catalog.</summary>
    public string? App { get; set; }

    public string? Transport { get; set; }

    public McpHttpDocument? Http { get; set; }

    public McpStdioDocument? Stdio { get; set; }

    public string? Scope { get; set; }

    /// <summary><c>false</c>, <c>true</c> or <c>{ interval, timeout }</c>; the same reader as an app's check.</summary>
    public CheckDocument? Check { get; set; }

    public string? Notes { get; set; }
}

/// <summary>See <see cref="McpHttpSettings"/>.</summary>
internal sealed class McpHttpDocument
{
    public string? Url { get; set; }

    public bool? AllowInsecure { get; set; }

    public Dictionary<string, McpValueDocument>? Headers { get; set; }
}

/// <summary>See <see cref="McpStdioSettings"/>.</summary>
internal sealed class McpStdioDocument
{
    public string? Command { get; set; }

    public List<string>? Args { get; set; }

    public Dictionary<string, McpValueDocument>? Env { get; set; }
}

/// <summary>A header or environment value: a text, or <c>{ env: NAME }</c>; read by <see cref="McpValueDocumentConverter"/>.</summary>
internal sealed class McpValueDocument
{
    public string? Literal { get; init; }

    public string? Env { get; init; }

    /// <summary>The key, in the secret store, of the secret behind <see cref="Env"/>.</summary>
    public string? Secret { get; init; }

    public bool IsEmpty => Literal is null && Env is null && Secret is null;
}

/// <summary>The mapping form of a value. Only <c>env</c> exists today; the strict reader rejects any other key.</summary>
internal sealed class McpValueBodyDocument
{
    public string? Env { get; set; }

    public string? Secret { get; set; }
}

/// <summary>See <see cref="McpSecretStore"/>.</summary>
internal sealed class SecretStoreDocument
{
    public string? Kind { get; set; }

    public string? Address { get; set; }

    public string? Prefix { get; set; }

    public bool? AllowInsecure { get; set; }
}

/// <summary>Reads <c>X-Api-Key: { env: NAME }</c> and <c>Accept: text/plain</c> into one document type.</summary>
internal sealed class McpValueDocumentConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(McpValueDocument);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new McpValueDocument { Literal = scalar.Value };
        }

        var body = (McpValueBodyDocument?)rootDeserializer(typeof(McpValueBodyDocument));
        return new McpValueDocument { Env = body?.Env, Secret = body?.Secret };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException("The catalog is never written by Portarr.");
    }
}

/// <summary>See <see cref="WorkstationDefinition"/>.</summary>
internal sealed class WorkstationDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>windows, linux or macos.</summary>
    public string? Os { get; set; }

    /// <summary>An environment of the catalog this machine is too.</summary>
    public string? Environment { get; set; }

    public List<string>? Clients { get; set; }

    /// <summary>The servers wanted here: an id, or <c>{ server, clients, scope }</c>.</summary>
    public List<WantedDocument>? Mcp { get; set; }

    public string? Notes { get; set; }
}

/// <summary>An entry of <c>mcp:</c>: a server id, or a mapping; read by <see cref="WantedDocumentConverter"/>.</summary>
internal sealed class WantedDocument
{
    public string? Server { get; init; }

    public List<string>? Clients { get; init; }

    public string? Scope { get; init; }
}

internal sealed class WantedBodyDocument
{
    public string? Server { get; set; }

    public List<string>? Clients { get; set; }

    public string? Scope { get; set; }
}

/// <summary>Reads <c>- memory</c> and <c>- { server: memory, clients: [cursor], scope: project }</c> into one document type.</summary>
internal sealed class WantedDocumentConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(WantedDocument);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new WantedDocument { Server = scalar.Value };
        }

        var body = (WantedBodyDocument?)rootDeserializer(typeof(WantedBodyDocument));
        return new WantedDocument { Server = body?.Server, Clients = body?.Clients, Scope = body?.Scope };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException("The catalog is never written by Portarr.");
    }
}
