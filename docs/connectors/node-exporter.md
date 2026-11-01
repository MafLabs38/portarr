# node_exporter connector

Reads a machine's **node_exporter** every second (D-72): the machine as its kernel counts it, which its runtime connector cannot see (a Docker host only sees its containers, metrics-server refreshes every ~15 s). Installation: [companions.md](../companions.md#node_exporter). Catalog: [catalog.md](../catalog.md#host-metrics-node_exporter).

## What is read

One `GET` of the `/metrics` URL per machine and second, `Accept-Encoding: gzip`, 3 s timeout, no redirects, no credentials. The answer is streamed line by line; only these families are parsed, every other line is skipped unread, a malformed line is skipped, and an answer over 8 M characters is refused (a large host answers a few hundred kB):

| Metric | Use |
|---|---|
| `node_cpu_seconds_total{cpu,mode}` | Load, I/O wait, load per core |
| `node_memory_MemTotal_bytes`, `MemAvailable`, `MemFree` | Memory ring (required: without `MemTotal` and `MemAvailable` the read fails) |
| `node_memory_Buffers_bytes`, `Cached`, `SReclaimable` | Kernel cache, informative (MCP, API) |
| `node_memory_SwapTotal_bytes`, `SwapFree` | Swap bar |
| `node_load1` | Load average |
| `node_hwmon_temp_celsius{chip}` | CPU temperature: the hottest sensor of a CPU chip (`coretemp`, `k10temp`, `zenpower`, `cpu_thermal`); disks and boards are ignored |

A Kubernetes node is read at `http://<InternalIP>:<port>/metrics`, or through the API server's **pod proxy** (`/api/v1/namespaces/<namespace>/pods/<pod>:<port>/proxy/metrics`, with the cluster's token). The pod is the running node_exporter pod on that node, found by listing the pods of `kubernetes.nodeExporter.namespace` that match `selector` with the field selector `spec.nodeName` (listing pods is already allowed), and remembered per node (looked up again on a 404 or 503 and every 5 minutes). It needs `get` on `pods/proxy` in that namespace only: the node proxy (`nodes/proxy`) reaches any port of any node, kubelet included, and Portarr does not use it. A refused read (`http-403`) shows as "access refused" and the node keeps metrics-server's values.

## Behind a proxy that asks for a token

`hostMetrics.tokenEnv` names the environment variable that holds a bearer token, sent to the machine's own `nodeExporter` URL only (never to a node's exporter reached through Kubernetes). The variable must start with `PORTARR_READER_` ([the reader](reader.md), D-102). An empty variable shows the machine as `token-missing`. A machine with a [reader](reader.md) declares `reader:` instead, which sets this and the Docker connection.

## Formulas

`node_cpu_seconds_total` is cumulative (seconds since boot per cpu and mode: user, nice, system, idle, iowait, irq, softirq, steal; guest time is already inside user). A load exists only between two reads, *a* then *b*:

- per core *c*: Δtotal = Σ<sub>modes</sub>(b − a), **load<sub>c</sub> = 100 × (1 − Δidle / Δtotal)**;
- machine: **load = 100 × (1 − ΣΔidle / ΣΔtotal)** over the cores present in both reads, **I/O wait = 100 × ΣΔiowait / ΣΔtotal**.

The load includes I/O wait (and steal), like `top`'s 100 − `id` and Unraid's dashboard: a host stuck on its disks is busy. The first read of a machine, and a read whose counters went backwards (reboot, a CPU taken offline), have no CPU yet.

Memory, from /proc/meminfo:

- **used = MemTotal − MemAvailable** (what `free` calls used: cannot be given back without swapping);
- **reclaimable = MemAvailable − MemFree** (cache the kernel gives back on demand);
- **free = MemFree**; used + reclaimable + free = total;
- **swap used = SwapTotal − SwapFree**.

`Buffers + Cached + SReclaimable` is not used for the ring: `Cached` includes tmpfs and shared memory (a RAM root filesystem, for instance), which are not reclaimable.

## The memory ring

Parts that sum to the total, like Unraid's legend:

| Part | Value |
|---|---|
| Containers / Pods | Sum of the running workloads' memory on the machine, as their connector measures it (Docker stats, metrics-server), capped by used |
| VMs (allocated) | On a hypervisor (D-59): the memory the guests report (their allocation), when it fits in what is left of used |
| System and apps | The rest of used: the host's own processes and kernel |
| VMs and system | Replaces the two above when the allocations exceed what is left of used: a VM only takes the host memory it has touched, so the split cannot be told |
| Cache (reclaimable) | MemAvailable − MemFree |
| Free | MemFree |

## Cadence and memory

Every machine is read every second (or at the `interval` its catalog entry gives, up to every 5 minutes, D-107), at most one read in flight per machine; a failing machine is retried after 1, 2, 4… up to 30 s, with its reason code (`unreachable`, `timeout`, `http-403`…) and its last values kept. Points are kept **in memory only**, 10 minutes per machine (D-54).

The browser gets the snapshot (`GET /api/v1/infrastructure`, `hostMetrics` per machine with its 10-minute history), then each read as it happens over **server-sent events**: `GET /api/v1/infrastructure/stream`, one `host` event per machine and second (the same object without history), a keep-alive comment every 15 s, `retry: 2000`. While the stream is open the snapshot is polled every 5 s (consumers, VMs); without it, every 2 s, and the snapshot's history still carries the 1 s points.

## Limits

- No authentication on node_exporter: restrict port 9100 to Portarr's host.
- `hostMetrics` is for Docker environments (the only single machine with a card); a launcher-only environment cannot carry it yet.
- Disks, filesystems and network are not shown yet; long history is Prometheus' job (V8).
