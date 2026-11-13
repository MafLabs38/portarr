# 0018 — Platforms are reported from the tools Portarr reads and two public sources

- **Status**: proposed
- **Date**: 2026-11-13
- **Spec**: D-118, D-119

## Context

Orchestrarr plans the upgrades of the platforms under the applications (K3s, the operating system of each machine). It must not detect them itself: Portarr observes and describes, Orchestrarr plans and acts. Portarr, until now, knew no version of the system of a machine: its Kubernetes connector read the node list but kept only the capacity, and node_exporter's `node_os_info` was ignored. A machine of the fleet was found running an operating system past its end of life by hand; nothing on the page said so.

## Decision

- **The system of a machine is read where it already is** (D-118): node_exporter's `node_os_info` and `node_uname_info`, and the node information of the Kubernetes API (`kubeletVersion`, `osImage`, `kernelVersion`). A `MachineSystem` carries it, merged from both (the host agent wins for the operating system and the kernel; the kubelet comes only from the inventory).
- **What is newer and when support stops comes from two public services** (D-119), each behind its own capability and contract suite (CLAUDE.md rule 8): `IReleaseLineSource` (K3s channels, `update.k3s.io`) and `ISupportLifecycleSource` (`endoflife.date`). A connector joins by its contract, so another source can be added without touching the rules.
- **The rules are Portarr's, pure, and tested** (`PlatformReport`): one Kubernetes line at a time, the end of life is a danger, within 90 days a warning, a newer version information; a date that is not announced stays empty.
- **A monitor reads only what the machines make worth reading** (`PlatformMonitor`): the K3s lines when a node runs K3s, a product's cycles when a machine runs it; every 6 hours, 1 hour after a failure, keeping the last answer.
- **Reported in the maintenance report** (`platforms[]`, the attention kinds, the MCP section, a Platforms tab), where Orchestrarr already reads.

## Consequences

- Nothing is installed on a machine and no machine is reached: the operating system comes from node_exporter (which must see the host's `/etc/os-release`, as a container with `--path.rootfs` does) or from the cluster.
- Two outbound GETs to public services, without any data about the fleet (a product identifier such as `ubuntu` and nothing else). An air-gapped installation shows the versions and no support dates.
- A product `endoflife.date` does not list (Unraid) is shown as unknown: Portarr says what it does not know.
- The outbound address policy that registries use moves to Core (`OutboundAddressPolicy`), so every connector that reads a public service shares it.

## Alternatives considered

- **Orchestrarr detecting K3s lines itself.** Rejected by the maintainer: it breaks "Portarr observes, Orchestrarr acts".
- **Comparing the K3s version written in the deployment code.** Rejected: the nodes declare what they run; the code is Orchestrarr's concern.
- **A machine-side agent for the operating system** (the retired reader, ADR 0017), or SSH. Rejected: the version is already exported by node_exporter.
