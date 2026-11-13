using System.Text.RegularExpressions;
using Portarr.Core.Infrastructure;
using Portarr.Core.Maintenance;

namespace Portarr.Core.Platforms;

/// <summary>What one machine says about its system, with where it is (D-118).</summary>
public sealed record MachineSystemEntry(string EnvironmentId, string MachineId, string MachineName, MachineSystem System);

/// <summary>
/// A platform a machine runs, as reported to the operator and to Orchestrarr (D-119): what it is, the version it runs, the newer
/// versions that exist (a patch of the same line, then the next line: one line at a time), when its support stops and what that
/// deserves. <c>Level</c> is null when there is nothing to say (up to date, or no date known); <c>Lookup</c> says why a date is missing (<see cref="PlatformLookups"/>).
/// </summary>
public sealed record PlatformStatus(
    string EnvironmentId,
    string MachineId,
    string MachineName,
    string Kind,
    string Name,
    string Version,
    string? NewestPatch,
    string? NextMinor,
    DateOnly? SupportedUntil,
    DateOnly? EndOfLife,
    AttentionLevel? Level,
    string Lookup = PlatformLookups.Pending)
{
    /// <summary>The name and the version to show: <c>K3s v1.31.4+k3s1</c>; a name that already says its version (<c>Ubuntu 25.10</c>) is not followed by it again.</summary>
    public string Label => Name.Contains(Version, StringComparison.Ordinal) ? Name : $"{Name} {Version}";
}

public interface IPlatformService
{
    /// <summary>The platforms the machines run, with what is known of their newer versions and support dates.</summary>
    IReadOnlyList<PlatformStatus> GetPlatforms();
}

/// <summary>Joins what the machines say about their system with what Portarr knows of the releases and support dates.</summary>
public sealed class PlatformService : IPlatformService
{
    private readonly IInfrastructureMonitor _infrastructure;
    private readonly IHostMetricsMonitor _hostMetrics;
    private readonly IPlatformKnowledge _knowledge;
    private readonly TimeProvider _timeProvider;

    public PlatformService(IInfrastructureMonitor infrastructure, IHostMetricsMonitor hostMetrics, IPlatformKnowledge knowledge, TimeProvider timeProvider)
    {
        _infrastructure = infrastructure;
        _hostMetrics = hostMetrics;
        _knowledge = knowledge;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<PlatformStatus> GetPlatforms()
    {
        var systems = PlatformReport.SystemsOf(_infrastructure.Current, _hostMetrics.Current);
        return PlatformReport.Build(systems, _knowledge.Current, _timeProvider.GetUtcNow());
    }
}

/// <summary>The rules that turn what machines say into platform statuses (D-119); pure, so every rule is a test.</summary>
public static partial class PlatformReport
{
    /// <summary>Days before the end of life from which a platform deserves a warning.</summary>
    public const int EndOfLifeWarningDays = 90;

    /// <summary>A product identifier as a lifecycle source can be asked for it: it comes from a machine, so it is never trusted as a path.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    public static partial Regex ProductPattern();

    /// <summary>What every machine says, host agent first, then the inventory (the kubelet only comes from the inventory); machines that say nothing are left out.</summary>
    public static IReadOnlyList<MachineSystemEntry> SystemsOf(IReadOnlyDictionary<string, EnvironmentMetrics> infrastructure, IReadOnlyDictionary<HostMachineKey, HostMetricsView> hosts)
    {
        var entries = new List<MachineSystemEntry>();
        var seen = new HashSet<HostMachineKey>();

        foreach (var environment in infrastructure.Values.OrderBy(e => e.EnvironmentId, StringComparer.Ordinal))
        {
            foreach (var machine in environment.Machines.Select(view => view.Machine))
            {
                var key = new HostMachineKey(environment.EnvironmentId, machine.Id);
                seen.Add(key);
                if (MachineSystem.Merge(hosts.GetValueOrDefault(key)?.Latest?.System, machine.System) is { } system)
                {
                    entries.Add(new MachineSystemEntry(key.EnvironmentId, key.MachineId, machine.Name, system));
                }
            }
        }

        // A machine read by its host agent that the inventory does not list (a Docker host before its first sample).
        foreach (var (key, view) in hosts.Where(h => !seen.Contains(h.Key)).OrderBy(h => h.Key.EnvironmentId, StringComparer.Ordinal).ThenBy(h => h.Key.MachineId, StringComparer.Ordinal))
        {
            if (view.Latest?.System is { IsEmpty: false } system)
            {
                entries.Add(new MachineSystemEntry(key.EnvironmentId, key.MachineId, key.MachineId, system));
            }
        }

        return entries;
    }

    /// <summary>The release lines and the products whose support cycles the machines make worth reading.</summary>
    public static (IReadOnlySet<string> ReleaseLines, IReadOnlySet<string> Products) Wanted(IReadOnlyList<MachineSystemEntry> systems)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);
        var products = new HashSet<string>(StringComparer.Ordinal);

        foreach (var system in systems.Select(s => s.System))
        {
            if (KubernetesVersion.Parse(system.KubeletVersion) is { } kubelet)
            {
                products.Add(PlatformKinds.Kubernetes);
                // The lines of the distribution the kubelet names, or of plain Kubernetes: the source that carries that name, if any, answers.
                var platform = kubelet.Distribution ?? PlatformKinds.Kubernetes;
                if (ProductPattern().IsMatch(platform))
                {
                    lines.Add(platform);
                }
            }

            if (system.OsId?.ToLowerInvariant() is { } id && ProductPattern().IsMatch(id))
            {
                products.Add(id);
            }
        }

        return (lines, products);
    }

    public static IReadOnlyList<PlatformStatus> Build(IReadOnlyList<MachineSystemEntry> systems, PlatformKnowledge knowledge, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var statuses = new List<PlatformStatus>();

        foreach (var entry in systems)
        {
            if (Kubernetes(entry, knowledge, today) is { } kubernetes)
            {
                statuses.Add(kubernetes);
            }

            if (OperatingSystem(entry, knowledge, today) is { } os)
            {
                statuses.Add(os);
            }
        }

        return statuses
            .OrderBy(s => s.EnvironmentId, StringComparer.Ordinal)
            .ThenBy(s => s.MachineId, StringComparer.Ordinal)
            .ThenBy(s => s.Kind, StringComparer.Ordinal)
            .ToList();
    }

    private static PlatformStatus? Kubernetes(MachineSystemEntry entry, PlatformKnowledge knowledge, DateOnly today)
    {
        if (KubernetesVersion.Parse(entry.System.KubeletVersion) is not { } version)
        {
            return null;
        }

        // The platform is the distribution the kubelet names (k3s, rke2r…), or plain Kubernetes: a source that carries that name gives its lines.
        var platform = version.Distribution ?? PlatformKinds.Kubernetes;
        var newer = knowledge.ReleaseLines.GetValueOrDefault(platform) is { } lines ? NewerOf(version, lines) : Newer.None;
        var cycle = knowledge.Cycles.GetValueOrDefault(PlatformKinds.Kubernetes)?.FirstOrDefault(c => c.Cycle == version.Line);
        var lookup = LookupOf(knowledge, PlatformKinds.Kubernetes, platform, cycle);
        return Status(entry, new Described(platform, DisplayNameOf(platform, knowledge), version.Text), newer, cycle, lookup, today);
    }

    /// <summary>The newest version of the line the node is on, when it is newer than the node's, and the first version of the next line (never a jump of two).</summary>
    private static Newer NewerOf(KubernetesVersion version, IReadOnlyList<ReleaseLine> lines)
    {
        var known = lines.Select(line => KubernetesVersion.Parse(line.Latest)).OfType<KubernetesVersion>().ToList();
        return new Newer(
            known.FirstOrDefault(v => v.Line == version.Line && v > version)?.Text,
            known.FirstOrDefault(v => v.Major == version.Major && v.Minor == version.Minor + 1)?.Text);
    }

    /// <summary>The name the source gives its platform; else the platform as it was written, with a capital (<c>k3s</c> is <c>K3s</c>); plain Kubernetes is Kubernetes.</summary>
    private static string DisplayNameOf(string platform, PlatformKnowledge knowledge)
    {
        return knowledge.DisplayNames?.GetValueOrDefault(platform) ?? string.Concat(char.ToUpperInvariant(platform[0]).ToString(), platform.AsSpan(1));
    }

    private static PlatformStatus? OperatingSystem(MachineSystemEntry entry, PlatformKnowledge knowledge, DateOnly today)
    {
        var system = entry.System;
        if (system.OsName is null && system.OsVersion is null)
        {
            return null;
        }

        var cycle = system.OsId?.ToLowerInvariant() is { } id ? knowledge.Cycles.GetValueOrDefault(id)?.FirstOrDefault(c => IsCycleOf(c, system.OsVersion)) : null;
        var name = system.OsName ?? $"{system.OsId} {system.OsVersion}".Trim();
        var lookup = LookupOf(knowledge, system.OsId?.ToLowerInvariant(), null, cycle);
        return Status(entry, new Described(PlatformKinds.Os, name, system.OsVersion ?? system.OsName!), Newer.None, cycle, lookup, today);
    }

    /// <summary>The newer versions that exist: a patch of the same line and the first version of the next line.</summary>
    private sealed record Newer(string? Patch, string? NextMinor)
    {
        public static readonly Newer None = new(null, null);
    }

    /// <summary>A version belongs to a cycle when it is that cycle or a point release of it (<c>13.2</c> is in <c>13</c>).</summary>
    private static bool IsCycleOf(SupportCycle cycle, string? version)
    {
        return version is not null && (version == cycle.Cycle || version.StartsWith(cycle.Cycle + ".", StringComparison.Ordinal));
    }

    /// <summary>What a status says it is: its kind, the name to show and the version it runs.</summary>
    private sealed record Described(string Kind, string Name, string Version);

    private static PlatformStatus Status(MachineSystemEntry entry, Described what, Newer newer, SupportCycle? cycle, string lookup, DateOnly today)
    {
        return new PlatformStatus(
            entry.EnvironmentId,
            entry.MachineId,
            entry.MachineName,
            what.Kind,
            what.Name,
            what.Version,
            newer.Patch,
            newer.NextMinor,
            cycle?.EndOfActiveSupport,
            cycle?.EndOfLife,
            LevelOf(cycle?.EndOfLife, newer.Patch is not null || newer.NextMinor is not null, today),
            lookup);
    }

    /// <summary>
    /// Where the lookup behind a status stands: switched off, failed with nothing kept (the support cycles of <paramref name="product"/>, or the
    /// release lines of <paramref name="platform"/>), known, not listed by any source, or not read yet.
    /// </summary>
    private static string LookupOf(PlatformKnowledge knowledge, string? product, string? platform, SupportCycle? cycle)
    {
        if (!knowledge.Enabled)
        {
            return PlatformLookups.Disabled;
        }

        var cyclesKey = product is null ? null : $"cycles:{product}";
        var cyclesFailed = cycle is null && cyclesKey is not null && knowledge.Failures?.ContainsKey(cyclesKey) == true;
        var linesFailed = platform is not null && knowledge.Failures?.ContainsKey($"lines:{platform}") == true && !knowledge.ReleaseLines.ContainsKey(platform);
        if (cyclesFailed || linesFailed)
        {
            return PlatformLookups.Failed;
        }

        if (cycle is not null)
        {
            return PlatformLookups.Ok;
        }

        var listed = product is not null && (knowledge.Cycles.ContainsKey(product) || knowledge.Unlisted?.Contains(cyclesKey!) == true);
        return product is null || listed ? PlatformLookups.NotListed : PlatformLookups.Pending;
    }

    /// <summary>
    /// End of life reached is a danger (no more security fixes), within <see cref="EndOfLifeWarningDays"/> days a warning, and a newer version
    /// that exists is information; nothing to say otherwise.
    /// </summary>
    internal static AttentionLevel? LevelOf(DateOnly? endOfLife, bool newerExists, DateOnly today)
    {
        if (endOfLife is { } end)
        {
            if (end <= today)
            {
                return AttentionLevel.Danger;
            }

            if (end <= today.AddDays(EndOfLifeWarningDays))
            {
                return AttentionLevel.Warning;
            }
        }

        return newerExists ? AttentionLevel.Info : null;
    }
}
