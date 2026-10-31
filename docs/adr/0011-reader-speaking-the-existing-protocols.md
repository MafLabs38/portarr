# 0011 — The reader: a read-only component that speaks the protocols Portarr already reads

- **Status**: accepted
- **Date**: 2026-10-31
- **Spec**: D-102 (amends D-100 and D-101)
- **Supersedes**: [0010](0010-optional-read-only-agent.md), for the component's name, its API and its connector

## Context

[ADR 0010](0010-optional-read-only-agent.md) decided to add an optional, read-only agent so that connecting a machine is one container and one URL, and sketched it with its own API (`/v1/containers`, `/v1/host/stream`) read by a new connector, `Portarr.Providers.Agent`. Building the first slice showed three things:

- **The name.** The chat runner is already `portarr-agent` (D-87, ADR 0008). The machine-side component is the **reader**.
- **The API.** Portarr already reads two protocols well: the Docker Engine API (the Docker connector) and node_exporter's text format (the host metrics connector, with its sampler and its contract suite). A reader that speaks those two needs no new connector, and the values and the one-second cadence are the ones node_exporter gives today.
- **The cost.** A normal .NET build measured about 76 MB of its own memory on a real host, against 15 MB for node_exporter: too much for a component that runs on every machine.

## Decision

- **Name and form**: `portarr-reader`, one container per machine, read-only by construction (D-100 stands).
- **It speaks the two protocols Portarr already reads**: `GET /v1/host` in node_exporter's text format limited to the families Portarr uses, and `GET /v1/docker/` as a filtered, read-only subset of the Docker Engine API, plus `/v1/info` and an open `/healthz`. The existing Docker and node_exporter connectors read it unchanged, with an optional bearer token.
- **The catalog key** `reader: { url, tokenEnv }` is read as `docker:` and `hostMetrics:`; no new connector (D-101's `Portarr.Providers.Agent` is not built). The reader's contract is held by the existing suites and by a test that runs Portarr's real Docker sources against a real reader.
- **The token variable** named by `tokenEnv` (on `reader:`, `docker:` and `hostMetrics:`) must start with `PORTARR_READER_`. A catalog can be edited through the API, and the value of the variable is sent to the URL the same entry names: letting it name any variable of the process would turn the permission to edit the catalog into read access to every secret.
- **Native AOT, as the interim.** The reader is compiled ahead of time (one binary, no .NET runtime in the image, a chiseled `runtime-deps` base). The AOT and trimming analyzers are on, so code that needs reflection fails the build, and CI builds the image and runs a smoke test on the native binary (`tests/reader/smoke.sh`) because the in-process tests run on the normal runtime and cannot see what only a native build gets wrong. **It does not solve the memory**: measured again on the same host, the native reader keeps about 70 to 90 MB of its own memory, almost all of it one block of the garbage collector's heap touched at start-up, which no setting reduced reliably (a first measurement of 18 MB could not be reproduced and is not relied on).
- **Rust next.** The reader is a probe with a small, stable HTTP contract and rarely changes, so its footprint matters more than its language. It is rewritten in Rust behind the same contract in the next slice (A1b), for a few MB of memory and a static binary. The .NET reader stays until then as the reference implementation, and the black-box tests (the smoke script, the contract against Portarr's own Docker sources) are what any implementation must pass. Portarr is unchanged by the swap.
- **A missing token** is one reason code, `token-missing`, whichever half of the reader is read: the host metrics capability gets the same configuration-error hook as the workload sources.

## Consequences

- Core and the connectors learn nothing about the reader beyond an optional token and the `reader:` shorthand.
- The reader forwards a fixed list of Docker requests. That list is tied to what the Docker connector asks for by a contract test: a request the connector learns to make, or makes with another parameter (it first dropped `one-shot`, which made every usage sample wait a second), fails the test instead of failing for reader users only.
- The reader is a mini node_exporter plus a filtered Docker window: it is not a general agent and does not grow into one.
- Compatibility between Portarr and a reader of another version is still open: Portarr does not read `/v1/info` yet.
- The reader still needs the Docker socket, which is root-equivalent on the host (ADR 0010, stated in the documentation); it can be pointed at a socket proxy instead.

## Open questions

1. **Compatibility**: should Portarr read `/v1/info` (`version`, `capabilities`) and say "reader too old" instead of a per-container 404?
2. **Transport**: plain HTTP on a trusted network, TLS through a reverse proxy (the catalog accepts a path under it); a self-signed certificate with a pinned fingerprint later.
3. **Direction**: pull only; a reader that connects out (NAT, a remote VPS) is a later step.
4. **Kubernetes nodes**: keep the node_exporter DaemonSet for now.
5. **Architectures**: one binary per CPU architecture; the image is built for x86-64 first (a static Rust binary makes ARM straightforward).

## Implementation slices

| Slice | Content |
|---|---|
| A1 | The reader in .NET (host and Docker window), its AOT image and CI smoke test, the `reader:` key and the optional token on the two connectors |
| A1b | The same reader rewritten in Rust behind the same contract, replacing the .NET one |
| A2 | The "Add environment" screen, documentation, migration notes |
| A3 | Kubernetes in-cluster authentication and the single install manifest |
