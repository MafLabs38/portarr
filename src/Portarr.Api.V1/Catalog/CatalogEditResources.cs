using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentValidation;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Api.V1.Catalog;

/// <summary>
/// An item to write: <c>Version</c> is the catalog version you read (<c>GET /api/v1/catalog/status</c> or the
/// item); <c>Item</c> is the item with the keys of the catalog file (docs/catalog.md), as JSON.
/// </summary>
public sealed record CatalogItemRequest(string? Version, JsonObject? Item);

/// <summary>Notes to set (Markdown); empty or null clears them.</summary>
public sealed record CatalogNotesRequest(string? Version, string? Notes);

/// <summary>An item as the catalog file holds it, with the version to send back when changing it.</summary>
public sealed record CatalogItemResource(string Version, string Section, string Id, JsonObject Item);

/// <summary>An edit's outcome: <c>applied</c> or <c>unchanged</c>, the catalog's new version and what changed.</summary>
public sealed record CatalogEditResource(string Status, string Version, IReadOnlyList<string> Changes);

/// <summary>One audited catalog edit (D-85).</summary>
public sealed record CatalogAuditEntryResource(
    DateTimeOffset At,
    string Channel,
    string? Client,
    string Operation,
    string Section,
    string Id,
    string VersionBefore,
    string VersionAfter,
    IReadOnlyList<string> Changes);

public sealed class CatalogItemRequestValidator : AbstractValidator<CatalogItemRequest>
{
    public CatalogItemRequestValidator()
    {
        RuleFor(r => r.Version).NotEmpty().WithMessage("version is required: the catalog version you read.");
        RuleFor(r => r.Item).NotNull().WithMessage("item is required: the item as a JSON object with the catalog's keys.");
    }
}

public sealed class CatalogNotesRequestValidator : AbstractValidator<CatalogNotesRequest>
{
    public CatalogNotesRequestValidator()
    {
        RuleFor(r => r.Version).NotEmpty().WithMessage("version is required: the catalog version you read.");
        RuleFor(r => r.Notes).MaximumLength(CatalogParser.MaxNotesLength);
    }
}

public static partial class CatalogEditMapper
{
    private static readonly Dictionary<string, string> Sections = new(StringComparer.Ordinal)
    {
        ["Environment"] = CatalogSections.Environments,
        ["Stack"] = CatalogSections.Stacks,
        ["App"] = CatalogSections.Apps,
        ["Data"] = CatalogSections.Data,
        ["Link"] = CatalogSections.Links,
        ["Provider"] = CatalogSections.Providers,
    };

    private static readonly HashSet<string> FieldFollowers = new(StringComparer.Ordinal) { "must", "is", "looks", "needs", "maps", "and", "cannot", "runs" };

    [GeneratedRegex(@"^(?<kind>Environment|Stack|App|Data|Link|Provider) '(?<id>[^']+)': (?:machine '(?<machine>[^']+)': )?(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex(@"^[a-z][A-Za-z]*(\.[a-z][A-Za-z]*)*$")]
    private static partial Regex FieldPattern();

    public static CatalogAuditEntryResource ToResource(this CatalogAuditEntry entry)
    {
        return new CatalogAuditEntryResource(entry.At, entry.Channel, entry.Client, entry.Operation, entry.Section, entry.Id, entry.VersionBefore, entry.VersionAfter, entry.Changes);
    }

    public static CatalogEditResource ToResource(this CatalogEditResult result)
    {
        return new CatalogEditResource(result.Status.ToString().ToLowerInvariant(), result.Version, result.Changes);
    }

    /// <summary>
    /// The field path an error is about, from the catalog parser's message: <c>apps[blog].check.url</c>,
    /// <c>environments[cluster].machines[node-2].access.ssh.key</c>; the edited item when the message names none.
    /// </summary>
    public static string PathOf(string error, string section, string id)
    {
        var match = OwnerPattern().Match(error);
        if (!match.Success)
        {
            return error.StartsWith("id:", StringComparison.Ordinal) ? "item.id" : $"{section}[{id}]";
        }

        var path = $"{Sections[match.Groups["kind"].Value]}[{match.Groups["id"].Value}]";
        if (match.Groups["machine"].Success)
        {
            path += $".machines[{match.Groups["machine"].Value}]";
        }

        var words = match.Groups["rest"].Value.Split(' ', 3);
        var field = words.Length > 1 && FieldPattern().IsMatch(words[0]) && (FieldFollowers.Contains(words[1]) || words[1].StartsWith('\'')) ? words[0] : null;
        return field is null ? path : $"{path}.{field}";
    }

    /// <summary>The parser's errors grouped by field path, for a validation problem.</summary>
    public static Dictionary<string, string[]> ToErrors(this CatalogEditResult result, string section, string id)
    {
        return result.Errors.GroupBy(e => PathOf(e, section, id), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }
}
