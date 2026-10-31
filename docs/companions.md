# Companion tools

Portarr is a tool that uses other tools (D-03, D-30): it does not collect metrics, run backups or scan code itself. For each capability it names **one recommended companion tool**, reads what that tool already knows, and shows it next to the rest of the fleet. This page says, for each companion, what Portarr gets from it, how to install it on a Docker host and in Kubernetes, how to declare it in the [catalog](catalog.md), and how to check that Portarr reads it.

Generic deployment examples live in [`deploy/companions/`](../deploy/companions/). They are starting points: pin versions, adapt tolerations, ports and firewall rules to your own infrastructure.

| Capability | Companion | Portarr reads |
|---|---|---|
| Host metrics (CPU per core, I/O wait, memory, swap) | [node_exporter](#node_exporter) | `/metrics`, every second |

Companions are read and never changed by Portarr (D-31): GET only.

## node_exporter

[node_exporter](https://github.com/prometheus/node_exporter) is the Prometheus exporter for Linux hosts: it publishes the kernel's counters (`/proc/stat`, `/proc/meminfo`, `/sys/class/hwmon`…) as plain text on port 9100.

### What it gives Portarr

Without it, a Docker host's card only knows the **sum of its containers** (D-57) and a Kubernetes node's card waits ~15 s for each metrics-server value. A hypervisor's own processes, its VMs as the host runs them and the time it waits for its disks are invisible. With node_exporter, Portarr reads the machine **every second** (D-72) and the Infrastructure card shows, host truth first:

- the **overall load**, 100 − idle, I/O wait included (the figure `top` and Unraid show), and the **I/O wait** part on its own;
- one **bar per logical core** (CPU 0…N);
- the **memory ring**: used (total − available) split into containers or pods, VMs and the host's own apps, then reclaimable cache and free, parts that add up to the total;
- **swap** (used of total, or "No swap");
- the **load average** and the **CPU temperature** when a CPU sensor is exposed.

Formulas and details: [connectors/node-exporter.md](connectors/node-exporter.md).

### On a Docker host

Run it as a container on the host's network and PID namespace, with the host's `/proc`, `/sys` and `/` mounted **read-only** ([`deploy/companions/node-exporter/docker-compose.yml`](../deploy/companions/node-exporter/docker-compose.yml)):

```yaml
services:
  node-exporter:
    image: prom/node-exporter:v1.10.2
    restart: unless-stopped
    network_mode: host
    pid: host
    read_only: true
    user: "65534:65534"
    cap_drop: [ALL]
    command: [--path.procfs=/host/proc, --path.sysfs=/host/sys, --path.rootfs=/host/root]
    volumes:
      - /proc:/host/proc:ro
      - /sys:/host/sys:ro
      - /:/host/root:ro,rslave
```

On an appliance that ships its own exporter (an Unraid plugin, a NAS package), use that one: Portarr only needs the standard metric names.

node_exporter has **no authentication**. What it publishes (process counts, mount points, network interfaces, hardware) helps an attacker map the host: like the [Docker socket proxy](connectors/docker.md), allow only Portarr's host on port 9100. For example with iptables, Portarr at `10.0.0.5`:

```bash
iptables -A INPUT -p tcp --dport 9100 -s 10.0.0.5 -j ACCEPT
iptables -A INPUT -p tcp --dport 9100 -j DROP
```

or with ufw: `ufw allow from 10.0.0.5 to any port 9100 proto tcp` (and no other rule opening 9100). A Prometheus that scrapes the same exporter needs its own rule.

### In Kubernetes

Run it as a **DaemonSet**, one pod per node, with `hostNetwork` (it answers at the node's InternalIP), `hostPID`, the host's `/proc`, `/sys` and `/` read-only, a non-root user, all capabilities dropped and small resource limits ([`deploy/companions/node-exporter/kubernetes.yaml`](../deploy/companions/node-exporter/kubernetes.yaml)). Add a toleration for every taint of your nodes (control plane, dedicated nodes), or those nodes stay without an exporter.

Portarr reads each node at its InternalIP. A node it cannot reach (a cloud worker behind a VPN tunnel, a node on another network) can be read **through the API server**: Portarr finds the node_exporter pod running on that node (it lists the pods of the exporter's namespace, which its read-only ServiceAccount may already do) and the API server proxies `GET /api/v1/namespaces/monitoring/pods/<pod>:9100/proxy/metrics` to it. That needs one more rule, **in the exporter's namespace only**:

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata: { name: portarr-node-exporter, namespace: monitoring }
rules:
  - apiGroups: [""]
    resources: [pods/proxy]
    verbs: [get]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata: { name: portarr-node-exporter, namespace: monitoring }
roleRef: { apiGroup: rbac.authorization.k8s.io, kind: Role, name: portarr-node-exporter }
subjects:
  - { kind: ServiceAccount, name: portarr-reader, namespace: portarr }
```

Why the pod proxy and not the node proxy: `nodes/proxy` reaches **any port of any node** through the API server (kubelet included), cluster-wide; `pods/proxy` in one namespace only reaches the pods of that namespace.

### Catalog

```yaml
environments:
  - id: server-1
    docker: { url: "http://10.0.0.10:2375" }
    hostMetrics: { nodeExporter: "http://10.0.0.10:9100/metrics" }
  - id: cluster
    runtime: kubernetes
    kubernetes:
      server: "https://10.0.0.20:6443"
      tokenEnv: K8S_TOKEN
      nodeExporter:
        port: 9100                     # every node, at its InternalIP
        nodes: { node-2: api-server }  # this one through the API server's pod proxy; also: off, or a URL
        # namespace: monitoring                         # where the DaemonSet runs (api-server access; this is the default)
        # selector: app.kubernetes.io/name=node-exporter  # its pods' labels (this is the default)
```

Details: [catalog.md](catalog.md#host-metrics-node_exporter).

### Verification

1. From Portarr's host: `curl -s http://10.0.0.10:9100/metrics | grep -E '^node_(cpu_seconds_total|memory_MemAvailable_bytes)' | head` lists counters. In Kubernetes: `kubectl -n monitoring get pods -o wide` shows one ready pod per node.
2. On the Infrastructure page, the machine's card says "node_exporter every 1 s", its live indicator stays at 0–1 s and the per-core bars move every second.
3. The figures match the host: `free -b` "used" equals the card's used memory; the load matches `top` (100 − id) over the same seconds.
4. A card that says "node_exporter: …" names why it is not read (`connection refused or host down`, `access refused (missing permission)` for a node read through the API server without `pods/proxy` in the exporter's namespace…). It keeps its connector's values meanwhile.
