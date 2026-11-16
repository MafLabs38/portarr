using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Proxmox;

/// <summary>A node as Proxmox reports it: its cores, its memory and what is used of them (CPU as a fraction of all the cores).</summary>
internal sealed record ProxmoxNode(string Name, bool Online, double Cores, double CpuFraction, long MemoryBytes, long MemoryMaxBytes);

/// <summary>An LXC container (or a VM) as Proxmox reports it: its limits (<c>Cpus</c>, <c>MemoryMaxBytes</c>) and its use of them.</summary>
internal sealed record ProxmoxGuest(int VmId, string Name, string Status, double Cpus, double CpuFraction, long MemoryBytes, long MemoryMaxBytes, long DiskBytes, long DiskMaxBytes, long UptimeSeconds, string? Tags)
{
    /// <summary>The cores in use: Proxmox gives the use of the guest's own cores as a fraction.</summary>
    public double CpuCores => Math.Round(CpuFraction * Cpus, 3);
}

/// <summary>
/// The Proxmox VE API, read with GET only and a token that is meant to hold the <c>PVEAuditor</c> role: the node and its LXC
/// containers. The token's secret is read from the variable the catalog names, only to be sent to that node (D-128).
/// </summary>
public sealed class ProxmoxClient
{
    private readonly ProxmoxHttpClientFactory _clients;
    private readonly IConfiguration _configuration;

    public ProxmoxClient(ProxmoxHttpClientFactory clients, IConfiguration configuration)
    {
        _clients = clients;
        _configuration = configuration;
    }

    /// <summary>A reason code when the node is declared but cannot be read yet (the variable of its token is empty).</summary>
    public string? GetConfigurationError(CatalogEnvironment environment)
    {
        return environment.Proxmox() is { } endpoint && string.IsNullOrWhiteSpace(_configuration[endpoint.TokenVariable]) ? ReasonCodes.TokenMissing : null;
    }

    internal async Task<ProxmoxNode> GetNodeAsync(ProxmoxEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(endpoint, "nodes", cancellationToken);
        var node = document.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault(n => n.TryGetProperty("node", out var name) && name.GetString() == endpoint.Node);
        return node.ValueKind == JsonValueKind.Undefined
            ? throw new InvalidDataException($"Proxmox has no node '{endpoint.Node}'.")
            : new ProxmoxNode(endpoint.Node, Text(node, "status") == "online", Number(node, "maxcpu"), Number(node, "cpu"), (long)Number(node, "mem"), (long)Number(node, "maxmem"));
    }

    internal async Task<IReadOnlyList<ProxmoxGuest>> ListContainersAsync(ProxmoxEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(endpoint, $"nodes/{endpoint.Node}/lxc", cancellationToken);
        return document.RootElement.GetProperty("data").EnumerateArray().Select(ToGuest).ToList();
    }

    private static ProxmoxGuest ToGuest(JsonElement guest)
    {
        return new ProxmoxGuest(
            (int)Number(guest, "vmid"),
            Text(guest, "name") ?? $"lxc-{(int)Number(guest, "vmid")}",
            Text(guest, "status") ?? "unknown",
            Number(guest, "cpus"),
            Number(guest, "cpu"),
            (long)Number(guest, "mem"),
            (long)Number(guest, "maxmem"),
            (long)Number(guest, "disk"),
            (long)Number(guest, "maxdisk"),
            (long)Number(guest, "uptime"),
            Text(guest, "tags"));
    }

    private async Task<JsonDocument> GetAsync(ProxmoxEndpoint endpoint, string path, CancellationToken cancellationToken)
    {
        var secret = _configuration[endpoint.TokenVariable];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"The variable {endpoint.TokenVariable} is empty.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", $"PVEAPIToken={endpoint.TokenId}={secret}");

        using var response = await _clients.Get(endpoint).SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>A number Proxmox gives as a JSON number, or as text on some versions; 0 when absent.</summary>
    private static double Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }
}
