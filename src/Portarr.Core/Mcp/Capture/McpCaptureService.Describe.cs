using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Core.Mcp.Capture;

// The comparison of a captured server with the catalog, and the definition that accepting it would write.
public sealed partial class McpCaptureService
{
    private const string HeaderKind = "header";
    private const string EnvKind = "env";

    private static McpCaptureGroup Describe(string identity, IReadOnlyList<McpCaptureRow> rows, Catalog.Catalog catalog)
    {
        var latest = rows.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.RowId).First();
        var match = catalog.McpServerList.FirstOrDefault(s => McpIdentity.Of(s) == identity);
        var differences = match is null ? [] : Compare(match, latest.Server);

        var state = McpCaptureStates.New;
        if (match is not null)
        {
            state = differences.Count == 0 ? McpCaptureStates.Same : McpCaptureStates.Differs;
        }

        var knownWorkstations = catalog.WorkstationList.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var sightings = rows
            .Select(r => new McpCaptureSighting(r.RowId, r.Workstation, knownWorkstations.Contains(r.Workstation), r.Client, r.Scope, r.Name, r.ReceivedAt))
            .ToList();

        var id = match?.Id ?? Slug(latest.Server.Name, catalog.McpServerList.Select(s => s.Id));
        var name = match?.Name ?? latest.Server.Name;
        var scope = match?.Scope ?? latest.Scope;
        var variables = ProposeVariables(latest.Server, id, match);
        var map = variables.ToDictionary(v => $"{v.Kind}:{v.Name}", v => v.Variable, StringComparer.Ordinal);
        var yaml = ProposalYaml(BuildItem(latest.Server, id, name, scope, map, null));

        return new McpCaptureGroup(McpIdentity.KeyOf(identity), state, match?.Id, match?.Name, latest.Server, sightings, differences, new McpCaptureProposal(id, name, scope, variables, yaml), VersionOf(rows));
    }

    /// <summary>What is not the same between the catalog's definition and what was captured; empty when they agree. A redacted value is never a difference.</summary>
    internal static List<string> Compare(McpServerDefinition known, CapturedServer seen)
    {
        var differences = new List<string>();
        if (known.Stdio is { } stdio)
        {
            if (!string.Equals(stdio.Command, seen.Command?.Trim(), StringComparison.Ordinal))
            {
                differences.Add($"command: {stdio.Command} → {seen.Command}");
            }

            if (!stdio.Args.SequenceEqual(seen.Args))
            {
                differences.Add($"args: {string.Join(' ', stdio.Args)} → {string.Join(' ', seen.Args)}");
            }
        }

        differences.AddRange(CompareValues(HeaderKind, known.Http?.Headers, seen.Headers));
        differences.AddRange(CompareValues(EnvKind, known.Stdio?.Env, seen.Env));
        return differences;
    }

    private static List<string> CompareValues(string kind, IReadOnlyDictionary<string, McpValue>? known, IReadOnlyList<CapturedValue> seen)
    {
        var differences = new List<string>();
        known ??= new Dictionary<string, McpValue>();

        foreach (var value in seen)
        {
            if (!known.TryGetValue(value.Name, out var catalogValue))
            {
                differences.Add($"{kind} {value.Name}: not in the catalog");
            }
            else if (DescribeDifference(catalogValue, value) is { } difference)
            {
                differences.Add($"{kind} {value.Name}: {difference}");
            }
        }

        differences.AddRange(known.Keys.Where(name => seen.All(v => v.Name != name)).Select(name => $"{kind} {name}: in the catalog, not captured"));
        return differences;
    }

    private static string? DescribeDifference(McpValue catalog, CapturedValue seen)
    {
        if (seen.Variable is not null)
        {
            if (!catalog.IsEnv)
            {
                return $"a literal in the catalog, the variable {seen.Variable} captured";
            }

            return catalog.EnvName == seen.Variable ? null : $"variable {catalog.EnvName} → {seen.Variable}";
        }

        if (seen.Literal is not null)
        {
            if (catalog.IsEnv)
            {
                return $"the variable {catalog.EnvName} in the catalog, a literal captured";
            }

            return catalog.Literal == seen.Literal ? null : "the literal changed";
        }

        return null;
    }

    /// <summary>The variables a definition from this capture needs: one per header or environment entry that has no harmless literal.</summary>
    private static List<McpProposedVariable> ProposeVariables(CapturedServer server, string serverId, McpServerDefinition? match)
    {
        var result = new List<McpProposedVariable>();
        result.AddRange(server.Headers.Where(v => v.Literal is null).Select(v => Propose(HeaderKind, v, serverId, match?.Http?.Headers)));
        result.AddRange(server.Env.Where(v => v.Literal is null).Select(v => Propose(EnvKind, v, serverId, match?.Stdio?.Env)));
        return result;
    }

    private static McpProposedVariable Propose(string kind, CapturedValue value, string serverId, IReadOnlyDictionary<string, McpValue>? known)
    {
        if (value.Variable is not null)
        {
            return new McpProposedVariable(kind, value.Name, value.Variable, true);
        }

        // Redacted: keep the variable the catalog already uses, else the name of an environment entry itself, else one made from the server and the header.
        if (known is not null && known.TryGetValue(value.Name, out var catalogValue) && catalogValue.IsEnv)
        {
            return new McpProposedVariable(kind, value.Name, catalogValue.EnvName!, false);
        }

        return new McpProposedVariable(kind, value.Name, kind == EnvKind ? value.Name : VariableName($"{serverId}_{value.Name}"), false);
    }

    /// <summary>An upper-case variable name from any text: <c>memory-x-api-key</c> becomes <c>MEMORY_X_API_KEY</c>.</summary>
    internal static string VariableName(string text)
    {
        var name = new string(text.ToUpperInvariant().Select(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) ? c : '_').ToArray()).Trim('_');
        return name.Length == 0 || char.IsAsciiDigit(name[0]) ? "_" + name : name;
    }

    /// <summary>
    /// What the operator reviewed, as a token: the hand-overs of the group, which never change once stored. A machine that hands the server over again
    /// adds a row, so the token changes and an acceptance of the old card is refused (D-112: a proposal never takes effect by itself).
    /// </summary>
    internal static string VersionOf(IEnumerable<McpCaptureRow> rows) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', rows.Select(r => r.RowId))))).ToLowerInvariant()[..16];

    /// <summary>An id from a name: lower case, letters and digits, dashes between; made unique among the ids of the catalog.</summary>
    internal static string Slug(string name, IEnumerable<string> taken)
    {
        var slug = NotAlphanumeric().Replace(name.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length == 0)
        {
            slug = "server";
        }

        var used = taken.ToHashSet(StringComparer.Ordinal);
        var candidate = slug;
        var n = 1;
        while (used.Contains(candidate))
        {
            n++;
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }

    /// <summary>
    /// The catalog item for a captured server. Without <paramref name="current"/>, a new item; with it, the existing item whose transport,
    /// address or command, headers and environment are replaced (its notes, scope, check and app stay). Never a secret value: a variable, or a harmless literal.
    /// </summary>
    internal static JsonObject BuildItem(CapturedServer server, string id, string name, string scope, IReadOnlyDictionary<string, string> variables, JsonObject? current)
    {
        var item = current is null ? new JsonObject() : (JsonObject)current.DeepClone();
        item["id"] = id;
        if (string.Equals(name, id, StringComparison.Ordinal))
        {
            item.Remove("name");
        }
        else
        {
            item["name"] = name;
        }

        item["transport"] = server.Transport;
        if (server.Transport == McpTransports.Http)
        {
            item.Remove("stdio");
            var http = item["http"] as JsonObject ?? new JsonObject();
            http["url"] = server.Url!.Trim();
            if (server.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                http["allowInsecure"] = true;
            }

            http.Remove("headers");
            Values(http, "headers", HeaderKind, server.Headers, variables);
            item["http"] = http;
        }
        else
        {
            item.Remove("http");
            var stdio = new JsonObject { ["command"] = server.Command!.Trim() };
            if (server.Args.Count > 0)
            {
                stdio["args"] = Strings(server.Args);
            }

            Values(stdio, "env", EnvKind, server.Env, variables);
            item["stdio"] = stdio;
        }

        if (current is null && scope != McpScopes.User)
        {
            item["scope"] = scope;
        }

        return item;
    }

    private static void Values(JsonObject parent, string key, string kind, IReadOnlyList<CapturedValue> values, IReadOnlyDictionary<string, string> variables)
    {
        if (values.Count == 0)
        {
            return;
        }

        var map = new JsonObject();
        foreach (var value in values.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            if (value.Literal is not null)
            {
                map[value.Name] = value.Literal;
            }
            else
            {
                var variable = variables.GetValueOrDefault($"{kind}:{value.Name}") ?? value.Variable ?? VariableName(value.Name);
                map[value.Name] = new JsonObject { ["env"] = variable };
            }
        }

        parent[key] = map;
    }

    /// <summary>The definition as the catalog file would hold it, to paste when Portarr cannot write the file.</summary>
    internal static string ProposalYaml(JsonObject item)
    {
        return "mcpServers:\n  - " + YamlEmitter.Item(item, 4, flow: false);
    }
}
