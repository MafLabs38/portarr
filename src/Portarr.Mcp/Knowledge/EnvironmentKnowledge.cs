using System.Globalization;
using Portarr.Core.Catalog;
using Portarr.Mcp.Digest;

namespace Portarr.Mcp.Knowledge;

// What the operator wrote down about the fleet (D-84), told to an assistant: notes, how to reach each machine,
// the network links between them and the external providers in use. Descriptive only, never a secret: the catalog
// refuses anything that looks like one, and console URLs lose their query like every URL (D-70).

/// <summary>An SSH route; <c>Command</c> is the command line it amounts to (<c>ssh -p 2222 root@10.0.0.10</c>).</summary>
public sealed record SshLine(string? Alias, string? Host, int? Port, string? User, string? Key, string? Jump, string Command);

public sealed record ConsoleLine(string Label, string Url);

/// <summary>How to reach a machine: SSH, web consoles and free notes.</summary>
public sealed record AccessLine(SshLine? Ssh, IReadOnlyList<ConsoleLine> Consoles, string? Notes);

public sealed record MachineKnowledgeLine(string Name, string? HostedOn, string? Notes, AccessLine? Access);

/// <summary>A network link; <c>From</c> and <c>To</c> are environment ids or machine names.</summary>
public sealed record LinkLine(string Id, string From, string To, string Kind, string? Name, string? Network, string? Notes);

public sealed record ProviderLine(string Id, string Name, string Kind, string? Console, string? Account, IReadOnlyList<string> UsedBy, string? Notes);

/// <summary>The notes of an app or a stack. <c>Kind</c>: app or stack.</summary>
public sealed record NoteLine(string Kind, string Id, string Name, string Notes);

/// <summary>One environment as the operator described it, with the links and providers that concern it.</summary>
public sealed record EnvironmentKnowledgeLine(
    string Id,
    string Name,
    string Runtime,
    string? Address,
    string? HostedOn,
    string? Notes,
    AccessLine? Access,
    IReadOnlyList<MachineKnowledgeLine> Machines,
    IReadOnlyList<string> Apps);

public sealed record EnvironmentKnowledge(
    IReadOnlyList<EnvironmentKnowledgeLine> Environments,
    IReadOnlyList<LinkLine> Links,
    IReadOnlyList<ProviderLine> Providers,
    IReadOnlyList<NoteLine> Notes);

public static class EnvironmentKnowledgeBuilder
{
    /// <summary>
    /// The whole description, or what concerns one environment (by id or name): its machines, the links that touch
    /// it or its machines, the providers it or its apps use, and the notes of those apps and their stacks.
    /// </summary>
    public static EnvironmentKnowledge Build(Catalog catalog, string? environment)
    {
        var environments = catalog.Environments
            .Where(e => environment is null || Same(e.Id, environment) || Same(e.Name, environment))
            .ToList();
        var ids = environments.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var machines = environments.SelectMany(MachineNames).ToHashSet(StringComparer.Ordinal);
        var apps = catalog.Apps.Where(a => environment is null || a.Workloads.Any(w => ids.Contains(w.EnvironmentId))).ToList();
        var appIds = apps.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var stacks = catalog.Stacks.Where(s => environment is null || s.Members.Any(m => appIds.Contains(m.AppId))).ToList();

        bool Touches(string end) => ids.Contains(end) || machines.Contains(end) || ids.Contains(end.Split('/')[0]);

        return new EnvironmentKnowledge(
            environments.Select(e => Describe(catalog, e)).ToList(),
            catalog.NetworkLinks.Where(l => environment is null || Touches(l.From) || Touches(l.To))
                .Select(l => new LinkLine(l.Id, l.From, l.To, l.Kind, l.Name, l.Network, l.Notes)).ToList(),
            catalog.ExternalProviders.Where(p => environment is null || p.UsedBy.Any(u => ids.Contains(u) || appIds.Contains(u)))
                .Select(p => new ProviderLine(p.Id, p.Name, p.Kind, p.Console is null ? null : SafeUrl.Of(p.Console), p.Account, p.UsedBy, p.Notes)).ToList(),
            stacks.Where(s => s.Notes is not null).Select(s => new NoteLine("stack", s.Id, s.Name, s.Notes!))
                .Concat(apps.Where(a => a.Notes is not null).Select(a => new NoteLine("app", a.Id, a.Name, a.Notes!)))
                .ToList());
    }

    public static AccessLine? Describe(MachineAccess? access)
    {
        if (access is null)
        {
            return null;
        }

        var ssh = access.Ssh is { } s ? new SshLine(s.Alias, s.Host, s.Port, s.User, s.Key, s.Jump, Command(s)) : null;
        return new AccessLine(ssh, access.Consoles.Select(c => new ConsoleLine(c.Label, SafeUrl.Of(c.Href))).ToList(), access.Notes);
    }

    /// <summary><c>ssh alias</c> when the operator has one, else the full command line.</summary>
    public static string Command(SshAccess ssh)
    {
        if (ssh.Alias is not null)
        {
            return $"ssh {ssh.Alias}";
        }

        var parts = new List<string> { "ssh" };
        if (ssh.Port is { } port)
        {
            parts.Add($"-p {port.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ssh.Key is not null)
        {
            parts.Add($"-i {ssh.Key}");
        }

        if (ssh.Jump is not null)
        {
            parts.Add($"-J {ssh.Jump}");
        }

        parts.Add(ssh.User is null ? ssh.Host! : $"{ssh.User}@{ssh.Host}");
        return string.Join(' ', parts);
    }

    private static EnvironmentKnowledgeLine Describe(Catalog catalog, CatalogEnvironment environment)
    {
        var machines = (environment.Machines ?? [])
            .Select(m => new MachineKnowledgeLine(m.Name, environment.HostOf(m.Name), m.Notes, Describe(m.Access)))
            .ToList();
        var apps = catalog.Apps.Where(a => a.Workloads.Any(w => w.EnvironmentId == environment.Id)).Select(a => a.Id).ToList();
        return new EnvironmentKnowledgeLine(environment.Id, environment.Name, environment.Runtime, environment.Address, environment.Host, environment.Notes, Describe(environment.Access), machines, apps);
    }

    private static IEnumerable<string> MachineNames(CatalogEnvironment environment)
    {
        return (environment.Machines ?? []).Select(m => m.Name).Concat(environment.MachineHosts?.Keys ?? []);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
