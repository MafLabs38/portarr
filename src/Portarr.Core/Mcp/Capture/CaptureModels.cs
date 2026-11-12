namespace Portarr.Core.Mcp.Capture;

/// <summary>
/// A header (remote server) or an environment entry (local server) of a captured server: its name and what the assistant knows of its value,
/// which is never a secret. <c>Variable</c> is the name of the variable that holds the value on the workstation (written by the assistant, or
/// proposed by it in place of a literal secret it saw and did not send); <c>Literal</c> a harmless text; with neither, the value was redacted.
/// </summary>
public sealed record CapturedValue(string Name, string? Variable, string? Literal);

/// <summary>An MCP server found in the configuration of a client on a workstation, as an assistant describes it (D-116).</summary>
public sealed record CapturedServer(
    string Name,
    string Transport,
    string? Url,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyList<CapturedValue> Headers,
    IReadOnlyList<CapturedValue> Env);

/// <summary>What an assistant hands over: the servers configured in one client, at one scope, on one workstation.</summary>
public sealed record CaptureRequest(string Workstation, string Client, string Scope, IReadOnlyList<CapturedServer> Servers);

/// <summary>A capture was refused: <c>Errors</c> name the field, never the value.</summary>
public sealed class McpCaptureException : Exception
{
    public McpCaptureException(IReadOnlyList<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors;
    }

    public McpCaptureException()
        : this(["The capture is invalid."])
    {
    }

    public McpCaptureException(string message)
        : this([message])
    {
    }

    public McpCaptureException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Where a captured server stands against the catalog.</summary>
public static class McpCaptureStates
{
    /// <summary>Nothing in the catalog is the same server.</summary>
    public const string New = "new";

    /// <summary>The catalog already describes this server, identically.</summary>
    public const string Same = "same";

    /// <summary>The catalog describes this server, differently.</summary>
    public const string Differs = "differs";
}

/// <summary>What the operator may do with a group of captured servers (<see cref="AcceptRequest.Mode"/>).</summary>
public static class McpAcceptModes
{
    /// <summary>Add it to the catalog as a new definition.</summary>
    public const string New = "new";

    /// <summary>Replace the definition of the server it matches, keeping its notes, scope, check and app.</summary>
    public const string Replace = "replace";

    /// <summary>The catalog already has it: only add it to the wanted lists of the workstations it was seen on.</summary>
    public const string Existing = "existing";
}

/// <summary>A server seen on a workstation: one row of the inbox.</summary>
public sealed record McpCaptureSighting(int RowId, string Workstation, bool WorkstationKnown, string Client, string Scope, string Name, DateTimeOffset ReceivedAt);

/// <summary>A value of the proposed definition that needs a variable name: the capture had no variable for it.</summary>
public sealed record McpProposedVariable(string Kind, string Name, string Variable, bool FromCapture);

/// <summary>
/// The same server seen on one or several workstations (same identity), compared with the catalog. <c>Server</c> is the latest description;
/// <c>Differences</c> says, for <c>differs</c>, what is not the same as <c>MatchId</c>; <c>Proposal</c> is what accepting would write.
/// </summary>
public sealed record McpCaptureGroup(
    string Key,
    string State,
    string? MatchId,
    string? MatchName,
    CapturedServer Server,
    IReadOnlyList<McpCaptureSighting> Sightings,
    IReadOnlyList<string> Differences,
    McpCaptureProposal Proposal);

/// <summary>The id and name a new definition would get, the variables to name, and the YAML to paste into a catalog that is read-only.</summary>
public sealed record McpCaptureProposal(string Id, string Name, string Scope, IReadOnlyList<McpProposedVariable> Variables, string Yaml);

/// <summary>What the operator decided for a group (D-116). <c>Variables</c> maps <c>kind:name</c> (<c>header:X-Api-Key</c>, <c>env:GITHUB_TOKEN</c>) to the variable that holds it.</summary>
public sealed record AcceptRequest(string Mode, string? Id, string? Name, IReadOnlyDictionary<string, string>? Variables, bool AddToWorkstations);

/// <summary>
/// The outcome of accepting. <c>Written</c> is false when the catalog is read-only (nothing is written, the group stays in the inbox
/// and <c>Yaml</c> is what to paste); <c>Errors</c> says why a write was refused.
/// </summary>
public sealed record AcceptResult(bool Written, bool ReadOnly, string? ServerId, IReadOnlyList<string> WorkstationsUpdated, IReadOnlyList<string> Skipped, IReadOnlyList<string> Errors, string? Yaml);

public sealed record CaptureReceipt(int CaptureId, int Servers, int Replaced);
