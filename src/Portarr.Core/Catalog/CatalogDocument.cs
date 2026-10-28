namespace Portarr.Core.Catalog;

// YAML shape of catalog.yaml. Kept separate from the domain records so the file format can
// evolve (shorthands, defaults) without leaking nullable, loosely typed fields into the Core.
#pragma warning disable CA1002, CA2227 // Mutable lists are what the YAML deserializer fills.

internal sealed class CatalogDocument
{
    public List<EnvironmentDocument>? Environments { get; set; }

    public List<StackDocument>? Stacks { get; set; }

    public List<AppDocument>? Apps { get; set; }
}

internal sealed class EnvironmentDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public DockerDocument? Docker { get; set; }
}

internal sealed class DockerDocument
{
    public string? Url { get; set; }
}

internal sealed class StackDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public List<MemberDocument>? Members { get; set; }
}

internal sealed class MemberDocument
{
    public string? App { get; set; }

    public string? Role { get; set; }
}

internal sealed class AppDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Icon { get; set; }

    public string? Description { get; set; }

    /// <summary>Shorthand for a critical membership in that stack.</summary>
    public string? Stack { get; set; }

    public List<string>? Tags { get; set; }

    public List<UrlDocument>? Urls { get; set; }

    public List<SelectorDocument>? Workloads { get; set; }
}

internal sealed class UrlDocument
{
    public string? Label { get; set; }

    public string? Href { get; set; }
}

internal sealed class SelectorDocument
{
    /// <summary><c>docker@&lt;environment id&gt;</c>.</summary>
    public string? Runtime { get; set; }

    public MatchDocument? Match { get; set; }
}

internal sealed class MatchDocument
{
    public string? Name { get; set; }
}
