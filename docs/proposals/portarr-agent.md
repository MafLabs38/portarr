# Proposal: a read-only Portarr agent, one way to connect a machine

> **Status: proposed (2026-10-31), not accepted.** Written from a design discussion with the maintainer. Nothing here is
> implemented. When accepted, it becomes an ADR and D-rows in [SPEC.md](../SPEC.md), and this file is deleted or reduced
> to a pointer.

## 1. Problem

Connecting an environment to Portarr is a different recipe for each kind of environment:

| Environment | What the operator installs and declares today |
|---|---|
| Docker host | a read-only Docker socket proxy (firewalled to Portarr), its URL in `docker:` |
| Host metrics | node_exporter on the machine, a second URL in `hostMetrics.nodeExporter` |
| Kubernetes | a read-only ServiceAccount and RBAC, the API server URL, a token variable, the cluster CA, plus node_exporter for the nodes |
| Backups | the backup tool's own URL and account |

Adding one ordinary Docker machine means at least three things to install, two ports to open and two sections to write.
A new connector also means editing Core (the architecture review's first finding on the infrastructure vertical).
Portarr is meant to be a **simple** configuration tool: if connecting a machine is complicated, the product fails at its
first job.

The reference the maintainer points at is Portainer: one agent container on each machine, one address to paste, and
the containers and metrics show up. Portarr has no such concept, and the spec rules it out for now
([§ Collection](../SPEC.md): "pull rather than push, no mandatory agent", Q-20, D-38 "the collector kit is not part of
Portarr").

## 2. Goal

**Adding a machine is one container to run and one address and token to paste.** Everything else stays as decided:

- Portarr **informs, the operator acts** (D-16, D-31): the agent is read-only **by construction**, not by configuration.
- Portarr **collects and reports, it does not compute** (D-30): the agent returns raw readings (counters, states).
- Generic public product: nothing specific to one infrastructure.
- The existing direct connectors keep working (Docker proxy, node_exporter, Prometheus later) for people who already
  run those tools. The agent is the simple path, not the only path.

## 3. Proposal

### 3.1 The agent

A small service, `portarr-agent`, written in .NET (the project's stack), published as an image by the release pipeline
(chiseled base, non-root, a few MB), one instance per machine. It reads and exposes:

- **Containers**: list, state, health check, Compose project, labels that the catalog already matches on, CPU and
  memory usage, a log tail and a live log follow. Source: the Docker Engine API, `GET` requests only.
- **Host metrics**: raw `/proc` and `/sys` counters (per-core CPU times, memory and swap, load, I/O wait, temperature
  when the machine has a sensor). Portarr turns counters into rates exactly as it does today with node_exporter
  (D-72, D-73), so D-30 is respected.

Out of scope for the first version: anything that acts (start, stop, exec), backup tools (they keep their own connector),
a metrics history (Portarr stores none, D-03).

### 3.2 One endpoint, one credential

HTTP(S) with a bearer token per agent (32 characters or more, compared by hash in constant time, never logged or
returned, rotatable by restarting the agent with a new one). The surface is small and versioned:

```text
GET /v1/info              agent version, host name, capabilities
GET /v1/containers        containers with state, health, usage
GET /v1/containers/{id}/logs?tail=&follow=
GET /v1/host              one reading of the raw host counters
GET /v1/host/stream       the same, pushed every second (SSE)
```

No other verb, no shell, no `exec`. Environment variables of containers are never returned (same rule as Portarr's own
API, D-84 and the MCP answers).

### 3.3 In the catalog

```yaml
environments:
  - id: server-1
    name: Server 1
    agent: { url: "https://server-1.homelab.example:8485", tokenEnv: AGENT_SERVER_1 }
```

`agent:` replaces `docker:` and `hostMetrics:` for that machine. The token is read from the named environment variable
like every other credential (never in the file). A machine that also declares `docker:` or `hostMetrics:` keeps using
them: the agent is one more source behind the same capabilities.

### 3.4 Where it fits in the code

A new connector assembly, `Portarr.Providers.Agent` (D-35), implementing `IWorkloadSource` and `IHostMetricsSource`.
It must pass the capability contract suites of both (rule 8), like the Docker and node_exporter connectors. Core learns
nothing about the agent: it sees the same capabilities as today.

### 3.5 Kubernetes

The Kubernetes API is already the "agent" for every node at once, so no agent runs per node. Two changes make it as
simple as the others:

- **In-cluster authentication**: when Portarr runs inside the cluster it uses the mounted ServiceAccount token and
  the in-cluster API address, so `kubernetes:` needs no URL, token variable or CA.
- **One manifest** (namespace, ServiceAccount, read-only RBAC, optional node metrics) documented as a single
  `kubectl apply`, so adding a cluster has the same shape as adding a machine.

Whether node metrics keep using node_exporter or move to the agent as a DaemonSet is an open question (section 6).

### 3.6 Adding a machine in the UI

An "Add environment" screen (the first-run wizard of F-SET-07 already plans one) generates the token, shows the one
`docker run` command to execute on the machine and the YAML to add. The catalog stays the source of truth (D-44):
Portarr shows the snippet, it does not write it behind the operator's back unless writes are enabled (D-83).

## 4. Security

- **The agent needs access to the Docker socket, which is root-equivalent on the host.** A read-only mount does not
  make a socket read-only. The protection is that the agent's code has no write path, is small enough to audit, runs
  as a non-root user inside its container, and the endpoint is token-protected. Anyone who compromises the agent can
  act on the host. The same is true of Portainer's agent. For people who do not accept that, the direct connectors
  (a socket proxy that filters at the API level) stay available.
- Token: random, long, per agent, constant-time comparison, never in logs, never in the catalog file.
- Transport: TLS recommended (self-signed with a pinned fingerprint is an open question), plain HTTP only on a
  trusted network with a startup warning, like the rest of Portarr.
- Rate limiting and a response size cap on every endpoint, as for the other tools.
- The agent exposes nothing about secrets: no environment variables, no mounts' contents.

## 5. Spec impact (when accepted)

| Where | Change |
|---|---|
| SPEC "Collection" principles | "No mandatory agent" becomes "no mandatory agent: an optional read-only agent is the simple path" |
| Q-20 (will an agent be needed?) | Resolved: yes, optional, read-only |
| D-38 (collector kit is not part of Portarr) | Amended: the agent is part of the product; the deployment of other tools stays the operator's |
| D-72 (host metrics from node_exporter) | Amended: node_exporter is one source, the agent is another, behind the same capability |
| D-30 (no computation) | Restated for the agent: it returns raw readings only |
| New D-rows and an ADR | The agent, its API, the `agent:` catalog key, the in-cluster mode |
| `docs/catalog.md`, `docs/companions.md`, `docs/connectors/` | The `agent:` key and one page for the agent |

## 6. Open questions

1. **Language and size**: .NET chiseled image, or native AOT for a smaller footprint.
2. **Kubernetes nodes**: keep node_exporter as a DaemonSet, or run the agent as a DaemonSet exposed through the pod
   proxy.
3. **Transport security**: plain HTTP on a trusted LAN only, or TLS with a self-signed certificate and a pinned
   fingerprint by default.
4. **Docker access**: the agent mounts the socket directly, or can be pointed at a socket proxy (more setup, less
   exposure).
5. **Direction**: pull only in the first version. An agent that connects out to Portarr ("edge" mode) would solve
   machines behind NAT (the remote VPS case of Q-20) and can be a second step.
6. **Compatibility**: how Portarr and an agent of another version negotiate (the `/v1` prefix and `capabilities` in
   `/v1/info` are the starting point).
7. **Scope creep**: whether the agent should one day relay a local backup tool's status, or whether that stays a
   separate connector.

## 7. Implementation slices

| Slice | Content | Tests |
|---|---|---|
| A1 | `portarr-agent` (containers and host), image in the release pipeline, `Portarr.Providers.Agent` | Both contract suites against the real agent; integration over HTTP |
| A2 | `agent:` in the catalog and its validation, documentation, the "Add environment" screen | Parser, API and E2E smoke |
| A3 | Kubernetes in-cluster authentication and the single install manifest | Unit and an integration test against a fake API server |
| A4 | Spec and ADR updates, migration notes | Documentation review |

A1 comes first and is shippable alone: a machine with an agent already shows its containers and live metrics.
