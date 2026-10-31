# Companion tools

Portarr is a tool that uses other tools (D-03, D-30): it does not collect metrics, run backups or scan code itself. For each capability it names **one recommended companion tool**, reads what that tool already knows, and shows it next to the rest of the fleet. This page says, for each companion, what Portarr gets from it, how to install it on a Docker host and in Kubernetes, how to declare it in the [catalog](catalog.md), and how to check that Portarr reads it.

Generic deployment examples live in [`deploy/companions/`](../deploy/companions/). They are starting points: pin versions, adapt tolerations, ports and firewall rules to your own infrastructure.

| Capability | Companion | Portarr reads |
|---|---|---|
| Host metrics (CPU per core, I/O wait, memory, swap) | [node_exporter](#node_exporter) | `/metrics`, every second |
| Backups of hosts and VMs | [Backrest](#backrest) | plans and their last backups (its API) |
| Backups in Kubernetes | [K8up](#k8up) | `Schedule`, `Backup` and `Snapshot` resources |

Companions are read and never changed by Portarr (D-31): GET only, except Backrest, whose API takes POST for the two read calls Portarr makes.

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

## Backrest

[Backrest](https://github.com/garethgeorge/backrest) is a web UI and scheduler for [restic](https://restic.net): encrypted, deduplicated, incremental snapshots of folders and database dumps to any restic target (a local folder, S3, Backblaze B2, SFTP…). Portarr targets **v1.14.x**.

### What it gives Portarr

On the hosts and VMs (an Unraid hypervisor, Docker VMs with their own databases), Backrest is **the tool that makes the backups** and the only one that knows how each run went. Portarr's Data page reads, for every plan the catalog names (`backrest: { plan: <id> }`, [catalog.md](catalog.md#data)): its schedule and repository, the last backup, its result (success, failure), the snapshot id, the bytes protected, and the last success, which is compared with the data's accepted loss window ([verticals/V3-data-backups.md](verticals/V3-data-backups.md)).

### Install on a Docker host

[`deploy/companions/backrest/`](../deploy/companions/backrest/) has a Dockerfile (the stock image plus `pg_dump` and `mariadb-dump`), a Compose file and an illustrative `config.example.json`:

```yaml
services:
  backrest:
    build: .                               # FROM garethgeorge/backrest:v1.14.1 + postgresql17-client mariadb-client
    restart: unless-stopped
    ports: ["10.0.0.10:9898:9898"]         # the LAN address only
    environment: { BACKREST_PORT: "0.0.0.0:9898", BACKREST_DATA: /data, BACKREST_CONFIG: /config/config.json, XDG_CACHE_HOME: /cache }
    env_file: [backrest.env]               # B2_ACCOUNT_ID, B2_ACCOUNT_KEY… never committed
    volumes:
      - ./config:/config
      - ./data:/data
      - ./cache:/cache
      - /srv/backups/restic:/repos/local   # repository 1: the local array
      - /srv/appdata:/userdata:ro          # what the plans back up, read-only
      - ./dumps:/dumps                     # where the dump hooks write
```

On Unraid, run it as a container the same way (the appdata share mounted read-only, the array share as the local repository); there is no need to install anything else on the host.

### Two repositories, plans and hooks

A Backrest **plan has one repository**. A copy on the local array and a copy in a bucket are therefore **two plans** with the same paths and hooks: `shop-db-local` (repository `local`, a folder on the array) and `shop-db-offsite` (repository `offsite`, `b2:example-backups:server-1` with the B2 application key in the repository's `env`). Name them after the data and the target, since the catalog refers to plan ids:

```yaml
data:
  - id: shop-db
    app: shop
    kind: postgresql
    importance: critical
    where: { environment: server-1, workload: shop-db }
    targets:
      local: { backrest: { plan: shop-db-local } }
      offsite: { backrest: { plan: shop-db-offsite } }
```

- **Database dumps** are made by a **pre-backup hook** (condition *Snapshot start*) that writes a dump to a folder the plan backs up: `pg_dump -h shop-db -U backup -Fc -Z 0 shop > /dumps/shop/shop.dump`, or `mariadb-dump --single-transaction -h mariadb -u backup shop > /dumps/shop/shop.sql`. The dump goes over the network with a read-only database role (credentials in a `.pgpass` or option file mounted into the container), which avoids giving the backup container the Docker socket (root on the host). Keep dumps **uncompressed** (`-Z 0` for the custom format, plain SQL for `mariadb-dump`, `--compression none` for `influx backup`): restic deduplicates and compresses on its own, and a compressed dump changes entirely at every run, so each backup would upload the whole database again. Set the hook's *on error* to **fail the operation**: with *cancel*, a failed dump cancels the backup silently and Portarr, which ignores cancelled operations, would not see a failure.
- **Files** (application configuration, SQLite): a plan on the folder, read-only. Use `excludes` for what is rebuildable (`cache`, `transcodes`, logs). The *arr applications write their own consistent `Backups/` archives; include that folder.
- **Retention** is a policy of the plan (`daily 7, weekly 4, monthly 6`; more months for critical data) and a prune schedule on the repository. Portarr does not read the retention.
- **Schedule**: a cron expression, or "at most every N hours/days"; Portarr shows it as the plan states it.
- A **restore test** of one critical database, monthly, is still on you.

### How Portarr reads it

The catalog declares the instance on the environment that runs it:

```yaml
environments:
  - id: server-1
    docker: { url: "http://10.0.0.10:2375" }
    backrest: { url: "http://10.0.0.10:9898", userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD }
```

Backrest speaks the [Connect protocol](https://connectrpc.com): every call is a `POST` of JSON to `/v1.Backrest/<Method>`. Portarr calls exactly two methods, **`GetConfig`** (plan ids, their repository id and schedule) and **`GetOperations`** (the last 200 operations of one plan), and never a method that starts, forgets, restores, changes the configuration or cancels. A backup operation is a success or a warning (a snapshot was made), or an error; pending, running, cancelled and dry-run operations are not attempts. Details: [verticals/V3-data-backups.md](verticals/V3-data-backups.md#connectors-d-80-d-81-d-62).

### Read-only credentials

**Backrest has no read-only role**: it has users with a password (basic authentication), all with full access to its API. Create a dedicated user for Portarr (so its calls can be told apart and revoked), put the user and password in two environment variables of Portarr's process (`userEnv`, `passwordEnv` name them; the values never go in the catalog), and **restrict the network**: Backrest listens on the LAN address, and only Portarr's host may reach port 9898, for example with `ufw allow from 10.0.0.5 to any port 9898 proto tcp`. Backrest's answer to `GetConfig` contains the repository passwords and hook tokens: Portarr's connector keeps plan ids, repository ids and schedules, and nothing else. A Backrest without authentication (a trusted network only) takes `backrest: { url }` with no variables.

### Verification

1. `curl -s -u portarr:… -H 'Content-Type: application/json' -d '{}' http://10.0.0.10:9898/v1.Backrest/GetConfig | jq '.plans[].id'` lists the plans.
2. In the Backrest UI, run a plan once; within a minute the Data page shows that copy "last success a few minutes ago", with the plan as its job.
3. Break a dump on purpose (a wrong role): the plan fails, the copy turns **Failed** and its last success stays visible.
4. The tool line at the top of the Data page says "Backrest · server-1: N jobs, read … ago". "credentials-missing" means the two variables are not set in Portarr's environment, "http-401" that Backrest rejected them, "connection refused or host down" that the address or the firewall is wrong.

## K8up

[K8up](https://k8up.io) is a Kubernetes operator that runs restic: a `Schedule` custom resource backs up the claims of its namespace and the dumps of annotated pods to an S3-compatible bucket, with retention, prune and checks. Portarr targets **K8up v2.16** (Helm chart 4.10, API `k8up.io/v1`).

### What it gives Portarr

In the cluster, K8up is the tool that makes the off-site backups, and everything it does is a Kubernetes resource Portarr can read with the ServiceAccount it already has: the **Schedules** (cron, backend), a **Backup** object per run (its `Completed` condition says succeeded or failed) and a **Snapshot** object per restic snapshot (date, paths, repository). The Data page shows, for each copy a K8up Schedule should produce, the last success, a failed run, and, with `item`, whether *this* claim is in the snapshots.

### Install in Kubernetes

[`deploy/companions/k8up/`](../deploy/companions/k8up/) has the Helm values, a Schedule, an annotated PostgreSQL StatefulSet and the RBAC for Portarr:

```bash
helm repo add k8up-io https://k8up-io.github.io/k8up
kubectl apply -f https://github.com/k8up-io/k8up/releases/download/k8up-4.10.1/k8up-crd.yaml --server-side
helm install k8up k8up-io/k8up --namespace k8up --create-namespace --version 4.10.1 -f values.yaml
```

### Schedules, repositories, retention

A `Schedule` lives in the namespace it backs up and carries the repository (`backend`), the backup, prune and check schedules and the retention. Here, to a Backblaze B2 bucket ([`schedule.yaml`](../deploy/companions/k8up/schedule.yaml)):

```yaml
apiVersion: k8up.io/v1
kind: Schedule
metadata: { name: offsite, namespace: shop }
spec:
  backend:
    repoPasswordSecretRef: { name: backup-credentials, key: repo-password }
    b2:
      bucket: example-backups
      path: shop
      accountIDSecretRef: { name: backup-credentials, key: b2-account-id }
      accountKeySecretRef: { name: backup-credentials, key: account-key }
  backup: { schedule: "0 4 * * *", failedJobsHistoryLimit: 3, successfulJobsHistoryLimit: 3 }
  prune: { schedule: "0 5 * * 0", retention: { keepDaily: 7, keepWeekly: 4, keepMonthly: 12 } }
  check: { schedule: "0 6 1 * *" }
```

- **Two copies.** K8up writes to the repository its Schedule names. The usual split: **K8up to the bucket** (the off-site copy) and **the cluster's existing CronJobs** (a dump to the array) as the local copy; Portarr reads both (`cronjob:` and `k8up:` under the same data). A second Schedule with a `local` backend (a mounted volume) is possible when the CronJobs are retired.
- **PVC backups.** Every claim of the namespace is backed up as `/data/<claim>`; exclude one with the annotation `k8up.io/backup: "false"`.
- **Databases** are dumped by the pod's own process, not copied while running: annotate the pod template with `k8up.io/backupcommand` (e.g. `sh -c 'pg_dumpall --clean'`, `influx backup --compression none …`; never pre-compress a dump, restic deduplicates and compresses itself) and `k8up.io/file-extension` (`.sql`); K8up runs the command in the pod and stores its output. Exclude the database's claim (`k8up.io/backup: "false"` on its volumeClaimTemplate) when the dump is the copy.
- **Retention** is the prune job's `retention` (keepLast, keepDaily, keepWeekly, keepMonthly…). Portarr does not read it.
- **Backup objects are pruned** by `successfulJobsHistoryLimit` and `failedJobsHistoryLimit`; keep a few so a recent failure stays visible. The Snapshot objects show what the repository holds, independently of that history.

### How Portarr reads it

No new connector configuration: the cluster is already an environment with its read-only token. A data target names the Schedule, and optionally the claim it must prove:

```yaml
data:
  - id: n8n-data
    app: n8n
    kind: volume
    importance: critical
    where: { environment: cluster, namespace: shop, claim: n8n-data }
    targets:
      local: { cronjob: shop/backup-n8n }
      offsite: { k8up: { schedule: shop/offsite, item: n8n-data } }
```

A Schedule's Backups are those it owns (`ownerReferences`); its Snapshots are those of the same namespace and repository (rebuilt from the Schedule's backend: `s3:endpoint/bucket`, `b2:bucket:path`), which a Schedule relying on operator-wide defaults does not allow, in which case Portarr judges it from its Backups alone. `item` is a piece of the snapshot path (`/data/n8n-data`).

### Read-only credentials

`get` and `list`, no write verb, no Secrets ([`portarr-reader.yaml`](../deploy/companions/k8up/portarr-reader.yaml)):

```yaml
rules:
  - { apiGroups: [k8up.io], resources: [schedules, backups, snapshots], verbs: [get, list] }
  - { apiGroups: [batch], resources: [cronjobs, jobs], verbs: [get, list] }
  - { apiGroups: [""], resources: [persistentvolumeclaims], verbs: [get, list] }
```

Until they are granted, the sources answer `http-403`: the copies they should prove show **Unknown** ("access refused (missing permission)"), not failed. A cluster without the K8up CRDs answers 404: no job, the copy is **Missing**.

### Verification

1. `kubectl -n shop get schedules,backups,snapshots.k8up.io` lists the Schedule, a Backup per run (`COMPLETION` Succeeded) and the snapshots.
2. `kubectl auth can-i list schedules.k8up.io --as=system:serviceaccount:portarr:portarr-reader` answers `yes`.
3. On the Data page, the tool line says "K8up · cluster: N jobs, read … ago" and the copy lists "K8up · offsite" with its last success. Run a backup by hand (`kubectl create -f` a `Backup`): the success moves forward.
4. A deleted Schedule turns the copy **Missing** (job not found); a failing dump command turns it **Failed**.
