# 0017 — The reader is retired: node_exporter is the host source

- **Status**: proposed
- **Date**: 2026-11-13
- **Spec**: D-117 (supersedes D-100, D-101 and D-102)
- **Supersedes**: [0011](0011-reader-speaking-the-existing-protocols.md) and [0010](0010-optional-read-only-agent.md); retires points 8 and 10 of [0013](0013-connectors-own-their-environment-settings.md) and the third published image of [0014](0014-releases-with-semantic-release.md), as far as they concern the reader (an accepted ADR is not edited)

## Context

[ADR 0011](0011-reader-speaking-the-existing-protocols.md) added `portarr-reader`, a small Rust program that serves the two protocols Portarr already reads: node_exporter's text format for the host and a filtered subset of the Docker Engine API. Its reasons were a simple connection (one container, one URL, one token instead of a socket proxy, its firewall and node_exporter) and a very small footprint.

It was built, merged and tried once in a test environment. It was never put in service: the production and development catalogs kept `docker:` (a socket proxy) and `hostMetrics:` (node_exporter), which gave the same figures every second and which the operator was satisfied with. Two further needs then came up: the operating-system version of every machine (for Orchestrarr's platform upgrades) and the load of an NVIDIA GPU. Both could only be added to the reader by writing, in Rust, what the ecosystem already provides:

- node_exporter already exports `node_os_info` and `node_uname_info` with the real host's values, as soon as it mounts the root file system (checked on two machines: Unraid 7.3 and Ubuntu 25.10);
- an NVIDIA exporter (`nvidia_gpu_exporter`, a wrapper of `nvidia-smi`) serves the GPU figures as Prometheus text, which Portarr already knows how to parse.

D-03 says Portarr integrates the tools it connects to and does not reimplement them. A second implementation of an exporter, in a second language, with its own image, CI job, token namespace and catalog key, goes against it.

## Decision

- **The reader is removed**: the `reader/` Rust project, its image and its publication, its CI job, its Dependabot entries, its compose example and its documentation.
- **The catalog loses `reader:`** and the bearer-token plumbing that only the reader used: `docker.tokenEnv`, `hostMetrics.tokenEnv`, the `PORTARR_READER_` namespace and the `token-missing` reason for these two sections. The other connectors keep their own credentials rules unchanged (Kubernetes, Backrest, Cloudflare, GitHub). A catalog that still carries a removed key is refused by the strict parser, with the name of the key.
- **node_exporter stays the host source** for a Docker machine, and for the nodes of a cluster through the Kubernetes connector (ADR 0012). The Docker API, directly or behind a read-only socket proxy, stays the workload source.
- **New data comes from standard tools**: the OS and the kernel from node_exporter's own families; a GPU from a standard exporter (D-118 onward); the Kubernetes version from the nodes' `nodeInfo`.

## Consequences

- The repository goes back to one language. No more Cargo project, Rust checks, Rust Dependabot entries or second image.
- The images already published as `portarr-reader` stay on the registry; nothing new is published.
- Nobody has to migrate: no catalog in use declares `reader:`.
- What the reader would have offered for connecting a machine in one step (a single container, a token) is not available. A machine is connected through the tools it normally runs; the connector pages say what to install.
- Per-container GPU load cannot be measured with a standard exporter. Portarr shows the load of the card and the containers that have it attached. A custom probe stays possible later if that figure is wanted, as a new decision.

## Alternatives considered

- **Keep the reader frozen and optional.** Rejected: an unused component is a cost (a language, a pipeline, an image, a token rule) with no user.
- **Extend the reader with the OS version, NVML and a stream.** Rejected: it reimplements exporters, and the stream does not improve what the load calculation needs (it uses counter deltas, not arrival times).
