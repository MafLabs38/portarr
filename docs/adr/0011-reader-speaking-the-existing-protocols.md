# 0011 — The reader: a read-only probe, written in Rust, that speaks the protocols Portarr already reads

- **Status**: accepted
- **Date**: 2026-10-31
- **Spec**: D-102 (amends D-100 and D-101)
- **Supersedes**: [0010](0010-optional-read-only-agent.md), for the component's name, its API, its connector and its language

## Context

[ADR 0010](0010-optional-read-only-agent.md) decided to add an optional, read-only agent so that connecting a machine is one container and one URL, and sketched it with its own API read by a new connector, `Portarr.Providers.Agent`. Building the first slice changed three things:

- **The name.** The chat runner is already `portarr-agent` (D-87, ADR 0008). The machine-side component is the **reader**.
- **The API.** Portarr already reads two protocols well: the Docker Engine API (the Docker connector) and node_exporter's text format (the host metrics connector, with its sampler and its contract suite). A reader that speaks those two needs no new connector, and the values and the one-second cadence are the ones node_exporter gives today.
- **The cost.** The reader runs on every machine, next to node_exporter (about 15 MB) and a socket proxy (about 10 MB). A first implementation in .NET was measured on a real host: about 76 MB of its own memory, and still 70 to 90 MB once compiled ahead of time (native AOT), almost all of it one block of the garbage collector's heap that no setting reduced reliably. It was abandoned.

## Decision

- **Name and form**: `portarr-reader`, one container per machine, read-only by construction (D-100 stands).
- **It speaks the two protocols Portarr already reads**: `GET /v1/host` in node_exporter's text format limited to the families Portarr uses, and `GET /v1/docker/` as a filtered, read-only subset of the Docker Engine API, plus `/v1/info` and an open `/healthz`. The existing Docker and node_exporter connectors read it unchanged, with an optional bearer token.
- **The catalog key** `reader: { url, tokenEnv }` is read as `docker:` and `hostMetrics:`; no new connector (D-101's `Portarr.Providers.Agent` is not built).
- **The token variable** named by `tokenEnv` (on `reader:`, `docker:` and `hostMetrics:`) must start with `PORTARR_READER_`. A catalog can be edited through the API, and the value of the variable is sent to the URL the same entry names: letting it name any variable of the process would turn the permission to edit the catalog into read access to every secret.
- **A missing token** is one reason code, `token-missing`, whichever half of the reader is read: the host metrics capability gets the same configuration-error hook as the workload sources.
- **Written in Rust** (`reader/`). Portarr is a .NET application; the tools it connects to are free in their technology, like node_exporter, Backrest or K8up. The reader is one of them: a small probe with a fixed HTTP contract that rarely changes, where footprint matters more than the language. It is a static binary of under 1 MB, serves from well under 2 MB of memory, and ships in an image that holds nothing else (no shell, no libc, no runtime). It has four dependencies: an HTTP server (`tiny_http`), `serde_json`, `sha2` and `subtle`; the client for Docker's socket is a few dozen lines.
- **How it is held to its contract.** The Rust tests run the real server against a fake Docker over TCP and over a Unix socket (the real mode), including a followed log that must arrive line by line. Its host text for two fixed kernel states is written to golden files that Portarr's own node_exporter sampler reads in a .NET test, so the format has one pair of files, not two opinions. A .NET integration test runs Portarr's real Docker sources, and a Portarr that reads a machine through a `reader:` line, against the real binary (`PORTARR_READER_BIN`); a request the Docker connector learns to make, or makes with another parameter, fails there. CI builds the image and runs a smoke script against it with a real Docker (`tests/reader/smoke.sh`).

## Consequences

- Core and the connectors learn nothing about the reader beyond an optional token and the `reader:` shorthand.
- The repository gains a second language and a Cargo project (`reader/`), with their own checks in CI (`rustfmt`, `clippy`, tests) and Dependabot entries. The .NET tests that need the binary are skipped when it is not built.
- The reader reaches Docker over a Unix socket or plain HTTP only; a Docker endpoint in HTTPS is not supported (a socket proxy on the same network is the documented way).
- Compatibility between Portarr and a reader of another version is still open: Portarr does not read `/v1/info` yet.
- The reader still needs the Docker socket, which is root-equivalent on the host (ADR 0010, stated in the documentation); it can be pointed at a socket proxy instead.
- It serves with one thread per request (at most 128 at once) and has no TLS and no request timeout of its own: it belongs on a private network or behind a reverse proxy, like the other tools Portarr reads.

## Open questions

1. **Compatibility**: should Portarr read `/v1/info` (`version`, `capabilities`) and say "reader too old" instead of a per-container 404?
2. **Transport**: plain HTTP on a trusted network, TLS through a reverse proxy (the catalog accepts a path under it); a self-signed certificate with a pinned fingerprint later.
3. **Direction**: pull only; a reader that connects out (NAT, a remote VPS) is a later step.
4. **Kubernetes nodes**: keep the node_exporter DaemonSet for now.
5. **Architectures**: the image is built for x86-64 first; a static Rust binary makes ARM a matter of one more target.

## Implementation slices

| Slice | Content |
|---|---|
| A1 | The reader in Rust (host and Docker window), its image and CI job, the `reader:` key and the optional token on the two connectors |
| A2 | The "Add environment" screen, documentation, migration notes |
| A3 | Kubernetes in-cluster authentication and the single install manifest |
