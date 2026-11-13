# 0019 — The GPU is read from a standard exporter

- **Status**: proposed
- **Date**: 2026-11-13
- **Spec**: D-120

## Context

The operator wants the load of the graphics card of a machine (an RTX 2070 that runs a media server's transcodes and an embedding model) on the Infrastructure page, with the workloads around it. Portarr reads a machine's host metrics from node_exporter and its workloads from Docker; neither says anything of a graphics card. A custom probe (the retired reader, [ADR 0017](0017-the-reader-is-retired.md)) was considered and rejected: D-03 says Portarr integrates the tools of the ecosystem and does not reimplement them.

## Decision

- **The card is read from `nvidia_gpu_exporter`** (the `-nvml` flavor: native NVML, no `nvidia-smi` subprocess), one container on the host started with the NVIDIA runtime, which serves the card's figures as Prometheus text. It is declared in the catalog (`gpu: { exporter }` on the Docker environment), owned by its own connector library (ADR 0013), read-only (GET), every 5 seconds because the exporter asks the driver on each read.
- **Two capabilities, each with a contract suite** (rule 8): `IGpuSource` (the cards: load, memory, encoder, decoder, temperature, power) and `IGpuAttachmentSource` (the workloads created with a card attached). Another exporter or another runtime joins by its contract.
- **The Prometheus text parser moves to Core** (`PrometheusText`) so node_exporter and the GPU exporter share it.
- **Which workloads have the card is read from the runtime**: a container created with the NVIDIA runtime or a GPU device request (`docker inspect`), once per container, cached while it exists, at most 20 per pass.
- **Portarr does not measure the load per container.** It shows the card's own load and the containers that can use it.

## Consequences

- One more container on a machine with a card, standard and maintained by its authors; none on the others.
- The card's load per container (which transcode takes how much) is not available. If it is wanted, it needs a probe that maps a process to its container; that would be a new decision.
- The exporter has no authentication: like node_exporter, it is published on the host's LAN address and restricted by network.
- The card is shown for a Docker host only (one machine). A Kubernetes node with a card is not covered yet.

## Alternatives considered

- **Extending the reader with NVML and a process-to-container mapping.** Rejected with the reader (ADR 0017): it reimplements an exporter.
- **DCGM exporter.** Rejected: built for data centers and Kubernetes, heavy, and partial on consumer cards.
- **A script writing a text file for node_exporter's textfile collector.** Rejected: one more moving part outside the container tooling.
