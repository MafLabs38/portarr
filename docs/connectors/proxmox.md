# Proxmox VE connector

Reads a **Proxmox VE node** through its API (D-128): the node as one machine, and its **LXC containers as workloads**. Read-only: only `GET` requests, with an API token that is meant to hold the `PVEAuditor` role (the connector never tries a write, and Proxmox would refuse it).

## Why LXC containers are workloads, not machines

An LXC container has **no kernel of its own**: it shares its host's, and its CPU and memory are already in the host's figures. Its "RAM max" is a limit, not a reservation. It is the same thing as a Docker container on a Docker host, so Portarr shows it the same way: a line in the host's **Top consumers**, counted as a container, never a machine card of its own. A **VM** has its own kernel and its own disks and network: it stays an environment that names the node as its `host` (D-59), read by its own node_exporter.

## Declaring a node

```yaml
environments:
  - id: server-2
    name: server-2 (Proxmox)
    address: 10.0.0.30
    runtime: proxmox
    proxmox:
      url: "https://10.0.0.30:8006"
      node: server-2                       # the node's name in Proxmox; the environment id when omitted
      tokenId: portarr@pve!readonly      # the API token's id: not a secret
      tokenEnv: PORTARR_PROXMOX_TOKEN    # the variable that holds the token's secret, never the secret
      fingerprint: "B4:F2:…:C3:65"       # the certificate's SHA-256 (Proxmox's own certificate is self-signed)
    hostMetrics: { nodeExporter: "http://10.0.0.30:9100/metrics" }   # the node's own CPU, memory, disks, network
```

- `url` is `https` without credentials (the token travels in the request). `fingerprint` pins the certificate: that one is accepted and no other; without it the system's trust applies, which a self-signed certificate fails.
- `tokenEnv` must start with `PORTARR_PROXMOX_` (or be an operator-allowed name, as for the other connectors), so that a catalog cannot point Portarr at an unrelated secret.
- An app reaches an LXC with `{ runtime: proxmox@server-2, match: { name: jellyfin } }`.

## What it reads

| Call | Use |
|---|---|
| `GET /nodes` | The node's cores, memory and what is used of them |
| `GET /nodes/<node>/lxc` | Each container: state, cores and memory limits, use of them, disk, uptime, tags |

A container is `Running` (running), `Exited` (stopped) or `Paused`. Its CPU is the fraction of its own cores in use, shown in cores; its memory is shown against its limit.

## Creating the token (once, on the node)

```bash
pveum user add portarr@pve --comment "Portarr: read-only view"
pveum acl modify / --users portarr@pve --roles PVEAuditor
pveum user token add portarr@pve readonly --privsep 0     # prints the secret once: put it in the variable
```

The VMs' state and the node's own disks and network are not read here: the VMs have their node_exporter, and the node has its own (`hostMetrics`).
