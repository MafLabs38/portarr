namespace Portarr.Mcp.Digest;

// What the MCP tools return (V5, D-68): small, typed and free of secrets. Field names are the vocabulary of the
// glossary resource (portarr://knowledge/glossary); every "reason" or "error" is a short code or a status
// reason, never a raw exception message. Sizes are in GiB and CPU in cores, as on the Infrastructure page (D-33).

/// <summary>
/// A grouped finding of the overview. <c>Area</c>: apps, infrastructure, data, maintenance or catalog.
/// <c>Level</c>: danger, warning or info. <c>Kind</c>: a stable code (app-down, environment-unreachable,
/// backup-failed, data-missing, severe-alerts…). <c>Subjects</c> names what it concerns (at most 10).
/// </summary>
public sealed record Finding(string Area, string Level, string Kind, int Count, IReadOnlyList<string> Subjects);

public sealed record AppCounts(int Total, int Healthy, int Degraded, int Down, int Unknown, int Paused, int Maintenance);

/// <summary>An environment. <c>Connection</c>: connected, unreachable, pending or not-connected; <c>Error</c>: a reason code.</summary>
public sealed record EnvironmentLine(string Id, string Name, string Runtime, string Connection, string? Error, int Workloads, string? HostedOn);

/// <summary>A machine's load now, as a share of its own capacity. <c>Scope</c>: host, or containers (the sum of a Docker host's containers).</summary>
public sealed record MachineLoad(string Environment, string Machine, double? CpuPercent, double? MemoryPercent, string Scope);

/// <summary>The followed data by state, and "x of y critical assets have an off-site copy" (<c>CriticalOffsite</c> of <c>CriticalAssets</c>).</summary>
public sealed record DataCounts(
    int Followed,
    int Protected,
    int Late,
    int Failed,
    int Missing,
    int Unknown,
    int CriticalAssets,
    int CriticalOffsite,
    int Waiting,
    int NotSetUp,
    int CriticalOffsitePending);

public sealed record MaintenanceCounts(
    int Repositories,
    int CriticalOrHighAlerts,
    int SecretAlerts,
    int CiFailing,
    int DependabotOff,
    int Images,
    int ImagesBehind,
    int ImagesMajor,
    int ImagesUnknown);

/// <summary>"What needs attention" across the four tabs, worst first, with the counts behind it.</summary>
public sealed record Overview(
    DateTimeOffset GeneratedAt,
    AppCounts Apps,
    IReadOnlyList<Finding> NeedsAttention,
    IReadOnlyList<EnvironmentLine> Environments,
    IReadOnlyList<MachineLoad> BusiestMachines,
    DataCounts Data,
    MaintenanceCounts Maintenance,
    int CatalogErrors);

/// <summary>An app in a list. <c>Url</c>: its first public URL, else its LAN URL, without credentials or query.</summary>
public sealed record AppLine(string Id, string Name, string Status, string Reason, string? Stack, string? Role, IReadOnlyList<string> Environments, string? Url);

/// <summary>An app URL. <c>Scope</c>: public (through DNS) or lan (local network only).</summary>
public sealed record UrlLine(string Label, string Href, string Scope);

/// <summary>
/// A workload of an app and where it runs: its environment, its machine (a Kubernetes node or the Docker host),
/// and the hypervisor that machine is a VM on (<c>HostedOn</c>). <c>Found</c> false: no workload matches the
/// app's selector. CPU in cores, memory in GiB, from the live window.
/// </summary>
public sealed record AppWorkloadLine(
    string Environment,
    string Runtime,
    string Name,
    string Role,
    bool Found,
    string Status,
    string Reason,
    string? State,
    string? Health,
    string? Namespace,
    string? Machine,
    string? HostedOn,
    string? Image,
    double? CpuCores,
    double? MemoryGiB);

/// <summary>The app's HTTP check: what it probes, its verdict and its latest result.</summary>
public sealed record CheckLine(string Target, bool Critical, string Status, string Reason, DateTimeOffset? LastAt, int? StatusCode, int? LatencyMs, DateTimeOffset? CertificateExpiresAt);

/// <summary>A data asset of an app and its backup coverage (protected, late, failed, unknown or not-covered).</summary>
public sealed record AppDataLine(string Id, string Name, string Kind, string Importance, string State, string? Local, string? Offsite, DateTimeOffset? LastSuccessAt);

/// <summary>A running image and its update verdict (up-to-date, behind, newer-build, floating, unknown, pending).</summary>
public sealed record ImageLine(string Reference, string Status, string? Current, string? Latest, string? Change, string? Reason, IReadOnlyList<string> Apps);

public sealed record AppDetail(
    string Id,
    string Name,
    string Type,
    string? Description,
    IReadOnlyList<string> Tags,
    string Status,
    string Reason,
    string? Stack,
    string? Role,
    IReadOnlyList<UrlLine> Urls,
    IReadOnlyList<AppWorkloadLine> Workloads,
    CheckLine? Check,
    IReadOnlyList<AppDataLine> Data,
    IReadOnlyList<ImageLine> Images,
    string? Repository);

/// <summary>A value of the live window (about 10 minutes): now, and the lowest and highest points seen.</summary>
public sealed record UsageLine(double? Now, double? Min, double? Max, double? Capacity, double? NowPercent);

/// <summary>A workload on a machine and what it consumes now (CPU in cores, memory in GiB).</summary>
public sealed record ConsumerLine(string Workload, IReadOnlyList<string> Apps, double? CpuCores, double? MemoryGiB);

/// <summary>
/// A machine: a Docker host or a Kubernetes node. <c>Cpu</c> in cores, <c>Memory</c> in GiB. <c>Scope</c>
/// containers: a Docker host only sees its containers (a lower bound of the host's load). <c>HostedOn</c>: the
/// hypervisor it is a VM on; <c>Guests</c>: the VMs it hosts. <c>Scope</c> host-agent: CPU and memory are what
/// the machine's node_exporter reads (D-72), detailed in <c>HostAgent</c>.
/// </summary>
public sealed record MachineLine(
    string Id,
    string Name,
    string Kind,
    bool? Ready,
    IReadOnlyList<string> Roles,
    string Scope,
    UsageLine Cpu,
    UsageLine Memory,
    string? HostedOn,
    IReadOnlyList<string> Guests,
    int Workloads,
    IReadOnlyList<ConsumerLine> TopConsumers,
    HostAgentLine? HostAgent = null);

/// <summary>What a machine's host agent adds (D-72): I/O wait and per-core load in %, load average, CPU temperature, swap in GiB.</summary>
public sealed record HostAgentLine(double? IowaitPercent, IReadOnlyList<double> CorePercents, double? Load1, double? TemperatureCelsius, double? SwapUsedGiB, double? SwapTotalGiB);

/// <summary>An environment with its machines; <c>Error</c> is a reason code; <c>Unplaced</c>: workloads on no known machine.</summary>
public sealed record InfrastructureLine(string Id, string Name, string Runtime, DateTimeOffset? SampledAt, string? Error, IReadOnlyList<MachineLine> Machines, IReadOnlyList<string> Unplaced);

/// <summary>A workload found by name, image or namespace. <c>Stale</c>: its environment cannot be read, this is its last known state.</summary>
public sealed record WorkloadMatch(
    string Environment,
    string Name,
    string? Namespace,
    string? Machine,
    string Image,
    string State,
    string Health,
    string Status,
    string Reason,
    IReadOnlyList<string> Apps,
    bool Stale);

/// <summary>One expected copy: <c>State</c> protected, unknown, late, failed or missing; <c>Reason</c> a code; <c>Sources</c> the jobs behind it.</summary>
public sealed record CopyLine(string State, string? Reason, DateTimeOffset? LastSuccessAt, IReadOnlyList<string> Sources);

/// <summary>
/// A followed data asset. <c>Importance</c>: critical, important or comfort; <c>MaxAgeHours</c> the accepted loss
/// window; <c>State</c> the worst of its declared copies. A copy not declared is null.
/// </summary>
public sealed record DataAssetLine(
    string Id,
    string Environment,
    string Name,
    string App,
    string Kind,
    string Importance,
    double MaxAgeHours,
    string State,
    CopyLine? Local,
    CopyLine? Offsite);

/// <summary>A backup tool read for the data. <c>Error</c>: a reason code (http-403, no-connector…).</summary>
public sealed record DataSourceLine(string Tool, string Environment, string? Error, int Jobs, DateTimeOffset CheckedAt);

/// <summary>Assets at risk first; <c>Truncated</c> when the list was cut.</summary>
public sealed record DataDigest(DataCounts Summary, IReadOnlyList<DataAssetLine> Assets, IReadOnlyList<DataSourceLine> Sources, bool Truncated);

/// <summary>A maintenance finding of the Maintenance page, worst first (severe-alerts, ci-failing, image-major…).</summary>
public sealed record AttentionLine(string Kind, string Level, int Count, IReadOnlyList<string> Subjects);

/// <summary>
/// A repository with something to say. Each security feature reads "on: 1 critical, 2 medium", "on: none",
/// "off" (turned off on GitHub, itself a finding) or "unknown: &lt;reason&gt;" (the token cannot read it).
/// </summary>
public sealed record RepositoryLine(
    string FullName,
    bool Deployed,
    IReadOnlyList<string> Apps,
    string Dependabot,
    string CodeScanning,
    string SecretScanning,
    string? Ci,
    int? DependabotPullRequests,
    string? LatestRelease);

public sealed record MaintenanceDigest(
    MaintenanceCounts Summary,
    IReadOnlyList<AttentionLine> Attention,
    IReadOnlyList<RepositoryLine> Repositories,
    IReadOnlyList<ImageLine> Images,
    string? CodeSourceError,
    bool Truncated);
