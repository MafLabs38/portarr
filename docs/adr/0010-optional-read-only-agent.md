# 0010 — An optional, read-only Portarr agent as the simple way to connect a machine

- **Status**: superseded by [0011](0011-reader-speaking-the-existing-protocols.md) in what it decides about the component's name, API and connector; its context and the decision to add an optional read-only component stand
- **Date**: 2026-10-31
- **Spec**: D-100, D-101; resolves Q-20, amends D-30, D-38, D-72

## Context

Connecting an environment to Portarr is a different recipe for each kind of environment:

| Environment | What the operator installs and declares |
|---|---|
| Docker host | a read-only Docker socket proxy (firewalled to Portarr), its URL in `docker:` |
| Host metrics | node_exporter on the machine, a second URL in `hostMetrics.nodeExporter` |
| Kubernetes | a read-only ServiceAccount and RBAC, the API server URL, a token variable, the cluster CA, plus node_exporter for the nodes |
| Backups | the backup tool's own URL and account |

Adding one ordinary Docker machine means at least three things to install, two ports to open and two sections to write. A new connector also means editing Core (the architecture review's first finding on the infrastructure vertical). Portarr is meant to be a **simple** configuration tool: if connecting a machine is complicated, it fails at its first job. The spec ruled an agent out for now (§6.2 "no mandatory agent", Q-20, D-38 "the collector kit is not part of Portarr"). The maintainer's reference is Portainer: one agent container on each machine, one address to paste, and the containers and metrics show up.

## Decision

Add an **optional agent, read-only by construction**, as the simple path. The existing direct connectors stay for people who already run those tools.

- `portarr-agent`: a small .NET service (chiseled image, non-root), one per machine, published by the release pipeline. It reads containers (state, health, Compose project, labels, usage, log tail and follow) from the Docker Engine API with `GET` requests only, and raw host counters from `/proc` and `/sys`. Portarr turns counters into rates exactly as it does for node_exporter (D-72, D-73), so D-30 holds.
- One endpoint, one credential: HTTP(S) with a bearer token per agent; a small versioned surface (`/v1/info`, `/v1/containers`, logs, `/v1/host`, `/v1/host/stream`); no other verb, no shell, no exec, no container environment variables.
- In the catalog: `agent: { url, tokenEnv }` on an environment (D-101). A connector assembly `Portarr.Providers.Agent` implements `IWorkloadSource` and `IHostMetricsSource` and passes their contract suites; Core learns nothing about it.
- Kubernetes needs no agent: its API already serves every node. It gets in-cluster authentication and one install manifest so that adding a cluster has the same shape as adding a machine.
- Pull only in the first version (Portarr calls the agent).

## Consequences

- Adding a machine is one container and one URL and token. The firewall needs one port, not two.
- **The agent needs the Docker socket, which is root-equivalent on the host.** A read-only mount does not make a socket read-only: the protection is that the agent's code has no write path, is small enough to audit, runs as a non-root user and is token-protected. Whoever compromises the agent can act on the host; Portainer's agent has the same exposure. The documentation says so, the agent can be pointed at a socket proxy, and the direct connectors remain for people who do not accept it.
- A new component to build, publish, secure and version (an image in the release pipeline, a token, compatibility between Portarr and agents of other versions).
- D-38 changes: the agent is the one component the product ships; installing the other tools stays the operator's.
- node_exporter stays a valid source; the agent is a second one behind the same capability.

## Open questions (provisional answers)

1. **Image**: .NET chiseled first; native AOT if the size matters later.
2. **Kubernetes nodes**: keep the node_exporter DaemonSet for now; an agent DaemonSet behind the pod proxy is a later option.
3. **Transport**: plain HTTP on a trusted network with a startup warning, TLS through a reverse proxy; a self-signed certificate with a pinned fingerprint later.
4. **Docker access**: the agent mounts the socket directly by default; it can be pointed at a socket proxy.
5. **Direction**: pull only; an agent that connects out (machines behind NAT, a remote VPS) is a second step.
6. **Versions**: the `/v1` prefix and `capabilities` in `/v1/info` are the starting point for compatibility.
7. **Scope**: containers and host only; backup tools keep their own connector.

## Implementation slices

| Slice | Content | Tests |
|---|---|---|
| A1 | `portarr-agent` (containers and host), its image in the release pipeline, `Portarr.Providers.Agent` | Both contract suites against the real agent; integration over HTTP |
| A2 | `agent:` in the catalog and its validation, documentation, the "Add environment" screen | Parser, API and E2E smoke |
| A3 | Kubernetes in-cluster authentication and the single install manifest | Unit, and an integration test against a fake API server |
| A4 | Migration notes, companions documentation | Documentation review |

A1 comes first and is shippable alone: a machine with an agent already shows its containers and live metrics.
