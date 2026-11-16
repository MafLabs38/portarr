# 0021 — A host without an engine and a Proxmox node are runtimes

- **Status**: proposed
- **Date**: 2026-11-16
- **Spec**: D-126 to D-128 (follows [0020](0020-the-catalog-is-writable-and-a-connection-change-waits-for-approval.md) for the writable catalog)

## Context

A staging hypervisor had to be described: a Proxmox node with its GPU, two virtual machines and a Jellyfin container in an LXC. Portarr could only measure a machine that runs Docker (`hostMetrics` and `nvidiaGpu` needed a Docker host), so a hypervisor had no card; and the LXC was shown as a virtual machine of its own, its memory limit counted as reserved by the VMs, and four containers would have been four cards.

## Decision

- **`runtime: machine` (D-126)**: an environment that is one machine with no engine and no API (a NAS, a bare KVM host). It lists no workload and is measured by its `hostMetrics` (node_exporter) and, when it has one, its `nvidiaGpu` card. It lives with the node_exporter library, which already reads the machine.
- **`runtime: proxmox` (D-128)**: a Proxmox VE node read through its API. The node is one machine; its LXC containers are its **workloads**, consumers of the host like Docker containers on a Docker host (an LXC shares the host's kernel and its memory maximum is a limit). A virtual machine keeps its own environment that names the node as `host` (D-59), because it has its own kernel and its own agent.
- **Read-only and narrow**: `GET` requests with an API token holding the `PVEAuditor` role, its secret in a variable of the `PORTARR_PROXMOX_` namespace (never in the catalog), the certificate pinned by its SHA-256 fingerprint (Proxmox ships a self-signed one), no redirect, the address policy checked when it connects, the answer capped at 4 MiB.
- **Disks and network (D-127)** come from the host agent already read (node_exporter): one saturation gauge per data filesystem (a ZFS pool counts once) and the traffic of the physical cards.
- **An LXC's kind is not an image**: its workload's image field says "Proxmox LXC", which is not an image reference, so the maintenance pass does not look it up.

## Consequences

- A hypervisor appears with its VMs and containers the way an Unraid host does, and four containers are four lines in one card.
- The GPU box does not yet say which LXC uses the card (attachments are read from Docker only).
- A VM is measured by its own exporter; Portarr does not list the VMs of a node from the API.
- Another hypervisor (libvirt, VMware) joins the same way: its own runtime, read-only, a token in its own namespace.

## Alternatives considered

- **Each LXC as an environment with its own Docker and exporter** (what was done first). Rejected: one card per container and a wrong memory count.
- **Listing the VMs from the API as workloads.** Rejected: a VM has its own kernel, its own exporter and its own card, like on Unraid (D-59).
- **A Proxmox agent on the node.** Rejected: the API with a read-only token is the standard tool (D-03).
