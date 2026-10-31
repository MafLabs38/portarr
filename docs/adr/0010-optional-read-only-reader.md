# 0010 — An optional, read-only Portarr reader as the simple way to connect a machine

- **Status**: accepted (the open questions below carry a provisional answer, to confirm when the first slice is reviewed)
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

Adding one ordinary Docker machine means at least three things to install, two ports to open and two sections to write. Portarr is meant to be a **simple** configuration tool: if connecting a machine is complicated, it fails at its first job. The spec ruled an agent out for now (§6.2 "no mandatory agent", Q-20, D-38 "the collector kit is not part of Portarr"). The maintainer's reference is Portainer: one agent container on each machine, one address to paste, and the containers and metrics show up.

The name: the chat runner is already called `portarr-agent` (D-87, ADR 0008), so the machine-side component is the **reader**, which also says what it does.

## Decision

Add an **optional reader, read-only by construction**, as the simple path. The existing direct connectors stay for people who already run those tools.

- `portarr-reader`: a small .NET service (chiseled image, non-root), one per machine, published by the release pipeline.
- **It speaks the two protocols Portarr already reads**, so the existing connectors read it unchanged and the numbers are the ones node_exporter gives today:
  - `GET /v1/host`: the node_exporter text exposition limited to the families Portarr uses (per-cpu seconds by mode, a few `/proc/meminfo` fields, load, the CPU temperature). Raw counters: Portarr turns them into rates (D-72, D-73), so D-30 holds.
  - `GET /v1/docker/...`: a filtered, read-only subset of the Docker Engine API (container list, usage, logs, host capacity, and an inspect reduced to the id and the TTY flag). Anything else is refused.
  - `GET /v1/info` (version, host name, capabilities) and an open `GET /healthz`.
- One credential: a bearer token per reader (32 characters or more, compared by hash in constant time, never logged).
- In the catalog: `reader: { url, tokenEnv }` on an environment (D-101), read as `docker:` and `hostMetrics:` with the token as a bearer. No new connector: the Docker and node_exporter connectors gain an optional token.
- Kubernetes needs no reader: its API already serves every node. It gets in-cluster authentication and one install manifest so that adding a cluster has the same shape as adding a machine.
- Pull only in the first version (Portarr calls the reader).

## Consequences

- Adding a machine is one container and one URL and token. The firewall needs one port, not two.
- The live metrics keep their one-second cadence and their values: same format, same sampler.
- **The reader needs the Docker socket, which is root-equivalent on the host.** A read-only mount does not make a socket read-only: the protection is that the reader's code only routes `GET`, allows a short list of Docker paths and parameters, strips what it forwards (no container environment variables), is small enough to audit, runs as a non-root user and is token-protected. Whoever compromises the reader can act on the host; Portainer's agent has the same exposure. The documentation says so, the reader can be pointed at a socket proxy, and the direct connectors remain for people who do not accept it.
- A new component to build, publish, secure and version (an image in the release pipeline, a token).
- D-38 changes: the reader is the one component the product ships; installing the other tools stays the operator's.
- node_exporter stays a valid source; the reader is a second one speaking the same format.
- The reader is a mini node_exporter plus a filtered Docker window: it is not a general agent and does not grow into one (no actions, no history, no backup tools).

## Open questions (provisional answers)

1. **Image**: .NET chiseled first; native AOT if the size matters later.
2. **Kubernetes nodes**: keep the node_exporter DaemonSet for now; a reader DaemonSet behind the pod proxy is a later option.
3. **Transport**: plain HTTP on a trusted network with a startup warning, TLS through a reverse proxy; a self-signed certificate with a pinned fingerprint later.
4. **Docker access**: the reader mounts the socket directly by default; it can be pointed at a socket proxy.
5. **Direction**: pull only; a reader that connects out (machines behind NAT, a remote VPS) is a second step.
6. **Versions**: the `/v1` prefix and `capabilities` in `/v1/info` are the starting point for compatibility.
7. **Scope**: containers and host only; backup tools keep their own connector.

## Implementation slices

| Slice | Content | Tests |
|---|---|---|
| A1 | `portarr-reader` (host and Docker window), its image in the release pipeline, the `reader:` catalog key and the optional token on the Docker and node_exporter connectors | The reader's own tests, the existing host-metrics contract suite and the Docker tests against a real reader, an integration test over HTTP |
| A2 | The "Add environment" screen, documentation, migration notes | API and E2E smoke |
| A3 | Kubernetes in-cluster authentication and the single install manifest | Unit, and an integration test against a fake API server |

A1 is shippable alone: a machine with a reader shows its containers and live metrics.
