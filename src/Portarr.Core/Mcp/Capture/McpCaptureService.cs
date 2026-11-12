using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Core.Mcp.Capture;

public interface IMcpCaptureService
{
    /// <summary>Validates and stores what an assistant handed over. Throws <see cref="McpCaptureException"/> naming the fields when it is refused.</summary>
    Task<CaptureReceipt> SubmitAsync(CaptureRequest request, string? user, CancellationToken cancellationToken);

    /// <summary>The servers waiting in the inbox, grouped by identity and compared with the catalog.</summary>
    Task<IReadOnlyList<McpCaptureGroup>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Accepts a group on behalf of a person (D-112, D-116): writes the definition and the wanted lists through the editor, versioned and
    /// audited, then resolves the group. Throws <see cref="McpCaptureException"/> when the request makes no sense (no such group, no match).
    /// </summary>
    Task<AcceptResult> AcceptAsync(string key, AcceptRequest request, string? user, string? client, CancellationToken cancellationToken);

    /// <summary>Dismisses a group; the number of servers dismissed.</summary>
    Task<int> DismissAsync(string key, string? user, CancellationToken cancellationToken);
}

/// <summary>
/// The inbox of captured MCP servers (D-116): an assistant hands over what a workstation has, the operator reviews it against the
/// catalog and accepts it. Nothing an assistant sends reaches the catalog by itself: accepting is a person's act, written with the editor
/// (version, backup, audit), and a catalog that is read-only gets the YAML to paste instead.
/// </summary>
public sealed partial class McpCaptureService : IMcpCaptureService
{
    private const string Channel = "ui";
    private const int Attempts = 3;

    private readonly ICatalogService _catalogService;
    private readonly ICatalogEditor _editor;
    private readonly IMcpCaptureStore _store;
    private readonly TimeProvider _timeProvider;

    public McpCaptureService(ICatalogService catalogService, ICatalogEditor editor, IMcpCaptureStore store, TimeProvider timeProvider)
    {
        _catalogService = catalogService;
        _editor = editor;
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<CaptureReceipt> SubmitAsync(CaptureRequest request, string? user, CancellationToken cancellationToken)
    {
        if (McpCaptureValidator.Validate(request) is { Count: > 0 } errors)
        {
            throw new McpCaptureException(errors);
        }

        return await _store.SubmitAsync(request, user, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<IReadOnlyList<McpCaptureGroup>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _store.PendingAsync(cancellationToken);
        var catalog = _catalogService.Current.Catalog;
        return rows
            .GroupBy(r => r.Identity, StringComparer.Ordinal)
            .Select(group => Describe(group.Key, group.OrderBy(r => r.RowId).ToList(), catalog))
            .OrderBy(g => g.Sightings.Min(s => s.RowId))
            .ToList();
    }

    public async Task<AcceptResult> AcceptAsync(string key, AcceptRequest request, string? user, string? client, CancellationToken cancellationToken)
    {
        var rows = await RowsOf(key, cancellationToken);
        var catalog = _catalogService.Current.Catalog;
        var group = Describe(rows[0].Identity, rows, catalog);
        var actor = new CatalogActor(Channel, client, null, user);

        if (!string.Equals(request.Version, group.Version, StringComparison.Ordinal))
        {
            throw new McpCaptureException("This server was handed over again since you opened it: reload the inbox and review it again.");
        }

        if (request.Mode is not (McpAcceptModes.New or McpAcceptModes.Replace or McpAcceptModes.Existing))
        {
            throw new McpCaptureException($"mode must be {McpAcceptModes.New}, {McpAcceptModes.Replace} or {McpAcceptModes.Existing}.");
        }

        if (request.Mode != McpAcceptModes.New && group.MatchId is null)
        {
            throw new McpCaptureException("The catalog has no server like this one: add it as a new definition.");
        }

        var serverId = ChooseId(request, group);
        var variables = group.Proposal.Variables.ToDictionary(v => $"{v.Kind}:{v.Name}", v => v.Variable, StringComparer.Ordinal);
        foreach (var (name, variable) in request.Variables ?? new Dictionary<string, string>())
        {
            if (variables.ContainsKey(name))
            {
                variables[name] = variable.Trim();
            }
        }

        var errors = new List<string>();
        if (request.Mode != McpAcceptModes.Existing)
        {
            var written = await WriteServerAsync(group, request, serverId, variables, actor, cancellationToken);
            if (written is not null)
            {
                return written;
            }
        }

        var updated = new List<string>();
        var skipped = new List<string>();
        if (request.AddToWorkstations)
        {
            await AddToWorkstationsAsync(group, serverId, actor, updated, skipped, errors, cancellationToken);
        }

        await _store.ResolveAsync(rows.Select(r => r.RowId).ToList(), McpCaptureStore.Accepted, user, serverId, _timeProvider.GetUtcNow(), cancellationToken);
        return new AcceptResult(true, false, serverId, updated, skipped, errors, null);
    }

    public async Task<int> DismissAsync(string key, string? user, CancellationToken cancellationToken)
    {
        var rows = await RowsOf(key, cancellationToken);
        await _store.ResolveAsync(rows.Select(r => r.RowId).ToList(), McpCaptureStore.Dismissed, user, null, _timeProvider.GetUtcNow(), cancellationToken);
        return rows.Count;
    }

    /// <summary>The id the definition gets: the one chosen for a new server, else the id of the server it replaces or matches.</summary>
    private static string ChooseId(AcceptRequest request, McpCaptureGroup group)
    {
        if (request.Mode != McpAcceptModes.New)
        {
            return group.MatchId!;
        }

        return string.IsNullOrWhiteSpace(request.Id) ? group.Proposal.Id : request.Id.Trim();
    }

    private async Task<List<McpCaptureRow>> RowsOf(string key, CancellationToken cancellationToken)
    {
        var rows = (await _store.PendingAsync(cancellationToken)).Where(r => McpIdentity.KeyOf(r.Identity) == key).OrderBy(r => r.RowId).ToList();
        return rows.Count > 0 ? rows : throw new McpCaptureException("No such captured server waiting in the inbox: it was already decided, or replaced by a newer capture.");
    }

    /// <summary>Writes the definition. Null when it was written; the result to return when it was not (read-only, refused).</summary>
    private async Task<AcceptResult?> WriteServerAsync(
        McpCaptureGroup group,
        AcceptRequest request,
        string serverId,
        Dictionary<string, string> variables,
        CatalogActor actor,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? group.Proposal.Name : request.Name.Trim();
        var isNew = request.Mode == McpAcceptModes.New;
        var yaml = ProposalYaml(BuildItem(group.Server, serverId, name, group.Proposal.Scope, variables, null));

        JsonObject? Change(JsonObject? current)
        {
            if (!isNew)
            {
                return BuildItem(group.Server, serverId, name, group.Proposal.Scope, variables, current);
            }

            return current is null
                ? BuildItem(group.Server, serverId, name, group.Proposal.Scope, variables, null)
                : throw new CatalogEditException(CatalogEditStatus.AlreadyExists, $"The catalog already has a server '{serverId}'.");
        }

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var version = await _editor.GetVersionAsync(cancellationToken);
            var result = await _editor.EditAsPersonAsync(
                new CatalogEdit(
                    CatalogSections.McpServers,
                    serverId,
                    isNew ? "capture-add" : "capture-replace",
                    version,
                    actor,
                    Change),
                cancellationToken);

            switch (result.Status)
            {
                case CatalogEditStatus.Applied or CatalogEditStatus.Unchanged:
                    return null;
                case CatalogEditStatus.ReadOnly:
                    return new AcceptResult(false, true, serverId, [], [], result.Errors, yaml);
                case CatalogEditStatus.Conflict:
                    continue;
                default:
                    return new AcceptResult(false, false, serverId, [], [], result.Errors, yaml);
            }
        }

        return new AcceptResult(false, false, serverId, [], [], ["The catalog kept changing while it was written: try again."], yaml);
    }

    private async Task AddToWorkstationsAsync(
        McpCaptureGroup group,
        string serverId,
        CatalogActor actor,
        List<string> updated,
        List<string> skipped,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var known = _catalogService.Current.Catalog.WorkstationList.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var workstation in group.Sightings.Select(s => s.Workstation).Distinct(StringComparer.Ordinal))
        {
            var clients = group.Sightings.Where(s => s.Workstation == workstation).Select(s => s.Client).Distinct(StringComparer.Ordinal).ToList();
            if (!known.Contains(workstation))
            {
                skipped.Add($"{workstation}: not a workstation of the catalog (declare it first).");
                continue;
            }

            var error = await AddToWorkstationAsync(workstation, serverId, clients, actor, cancellationToken);
            if (error is null)
            {
                updated.Add(workstation);
            }
            else
            {
                errors.AddRange(error);
            }
        }
    }

    /// <summary>Writes the server into one workstation, trying again when the catalog changed meanwhile. Null when it succeeded, else what went wrong.</summary>
    private async Task<IReadOnlyList<string>?> AddToWorkstationAsync(string workstation, string serverId, IReadOnlyList<string> clients, CatalogActor actor, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var result = await _editor.EditAsPersonAsync(
                new CatalogEdit(
                    CatalogSections.Workstations,
                    workstation,
                    "capture-wanted",
                    await _editor.GetVersionAsync(cancellationToken),
                    actor,
                    current => current is null ? throw new CatalogEditException(CatalogEditStatus.NotFound, $"No workstation '{workstation}'.") : WithWanted(current, serverId, clients)),
                cancellationToken);
            if (result.Succeeded)
            {
                return null;
            }

            if (result.Status != CatalogEditStatus.Conflict)
            {
                return result.Errors.Select(e => $"{workstation}: {e}").ToList();
            }
        }

        return [$"{workstation}: the catalog kept changing while the server was added; add it to this workstation again."];
    }

    /// <summary>
    /// The workstation with the server wanted in these clients (D-111). A client the machine did not list is added, and the entries written
    /// as a bare id (every client) are made explicit first, so that a new client never silently inherits the servers wanted in the others.
    /// </summary>
    internal static JsonObject WithWanted(JsonObject workstation, string serverId, IReadOnlyList<string> clients)
    {
        var oldClients = StringsOf(workstation["clients"]);
        var allClients = oldClients.Union(clients).ToList();
        var wanted = (workstation["mcp"] as JsonArray)?.DeepClone() as JsonArray ?? [];

        if (allClients.Count != oldClients.Count && oldClients.Count > 0)
        {
            MakeExplicit(wanted, oldClients);
        }

        var index = Enumerable.Range(0, wanted.Count).FirstOrDefault(i => EntryServer(wanted[i]) == serverId, -1);
        var existing = index >= 0 ? wanted[index] : null;
        var desired = ClientsOf(existing, oldClients).Union(clients).ToList();
        var scope = (existing as JsonObject)?["scope"]?.GetValue<string>();

        // Every client of the machine, at the server's own scope: the bare id says it.
        JsonNode entry = desired.Count == allClients.Count && scope is null ? JsonValue.Create(serverId)! : WantedObject(serverId, desired, scope);
        if (index >= 0)
        {
            wanted[index] = entry;
        }
        else
        {
            wanted.Add(entry);
        }

        var result = (JsonObject)workstation.DeepClone();
        result["clients"] = Strings(allClients);
        result["mcp"] = wanted;
        return result;
    }

    /// <summary>Entries written as a bare id mean every client of the machine; once a client is added they would mean more, so they name their clients.</summary>
    private static void MakeExplicit(JsonArray wanted, List<string> clients)
    {
        for (var i = 0; i < wanted.Count; i++)
        {
            if (wanted[i] is JsonValue value && value.TryGetValue<string>(out var id))
            {
                wanted[i] = new JsonObject { ["server"] = id, ["clients"] = Strings(clients) };
            }
        }
    }

    /// <summary>The clients an entry covers: the ones it names, every client of the machine for a bare id, none when there is no entry.</summary>
    private static List<string> ClientsOf(JsonNode? entry, List<string> machineClients)
    {
        if (entry is JsonObject { } mapping && mapping["clients"] is JsonArray)
        {
            return StringsOf(mapping["clients"]);
        }

        return entry is null ? [] : machineClients;
    }

    private static List<string> StringsOf(JsonNode? node) => (node as JsonArray)?.Select(c => c?.GetValue<string>()).OfType<string>().ToList() ?? [];

    private static JsonObject WantedObject(string serverId, List<string> clients, string? scope)
    {
        var entry = new JsonObject { ["server"] = serverId, ["clients"] = Strings(clients) };
        if (scope is not null)
        {
            entry["scope"] = scope;
        }

        return entry;
    }

    private static string? EntryServer(JsonNode? entry) => entry switch
    {
        JsonValue value when value.TryGetValue<string>(out var id) => id,
        JsonObject o => o["server"]?.GetValue<string>(),
        _ => null,
    };

    private static JsonArray Strings(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotAlphanumeric();
}
