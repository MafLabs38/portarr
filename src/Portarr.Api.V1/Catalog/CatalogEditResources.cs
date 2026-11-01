using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    IReadOnlyList<string> Changes,
    string? Via = null,
    string? User = null);

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
        return new CatalogAuditEntryResource(entry.At, entry.Channel, entry.Client, entry.Operation, entry.Section, entry.Id, entry.VersionBefore, entry.VersionAfter, entry.Changes, entry.Via, entry.User);
    }

    public static CatalogEditResource ToResource(this CatalogEditResult result)
    {
        return new CatalogEditResource(result.Status.ToString().ToLowerInvariant(), result.Version, result.Changes);
    }

    /// <summary>The HTTP answer to an edit's outcome, the same for every endpoint that edits the catalog.</summary>
    public static IActionResult ToActionResult(this ControllerBase controller, CatalogEditResult result, string section, string id)
    {
        var detail = string.Join(" ", result.Errors);
        return result.Status switch
        {
            CatalogEditStatus.Applied or CatalogEditStatus.Unchanged => controller.Ok(result.ToResource()),
            CatalogEditStatus.Invalid => controller.ValidationProblem(new ValidationProblemDetails(result.ToErrors(section, id)) { Extensions = { ["version"] = result.Version } }),
            CatalogEditStatus.NotFound => controller.Problem(title: "Not found", detail: detail, statusCode: StatusCodes.Status404NotFound),
            CatalogEditStatus.ReadOnly => controller.Problem(title: "Catalog read-only", detail: detail, statusCode: StatusCodes.Status403Forbidden),
            _ => Conflict(controller, result),
        };
    }

    private static ObjectResult Conflict(ControllerBase controller, CatalogEditResult result)
    {
        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            StatusCodes.Status409Conflict,
            result.Status == CatalogEditStatus.Conflict ? "Version mismatch" : "Already exists",
            detail: string.Join(" ", result.Errors));
        problem.Extensions["version"] = result.Version;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }

    /// <summary>
    /// The field path an error is about, from the catalog parser's message: <c>apps[blog].check.url</c>,
    /// <c>environments[cluster].machines[node-2].access.ssh.key</c>; the edited item when the message names none.
    /// </summary>
    public static string PathOf(string error, string section, string id)
    {
        if (section == CatalogSections.Portal)
        {
            return PortalPathOf(error);
        }

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

    /// <summary><c>portal.logo</c> for "portal: logo must be…", else <c>portal</c>.</summary>
    private static string PortalPathOf(string error)
    {
        var field = error.StartsWith("portal: ", StringComparison.Ordinal) ? error["portal: ".Length..].Split(' ', 2)[0] : null;
        return field is not null && FieldPattern().IsMatch(field) ? $"portal.{field}" : CatalogSections.Portal;
    }

    /// <summary>The parser's errors grouped by field path, for a validation problem.</summary>
    public static Dictionary<string, string[]> ToErrors(this CatalogEditResult result, string section, string id)
    {
        return result.Errors.GroupBy(e => PathOf(e, section, id), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }
}
