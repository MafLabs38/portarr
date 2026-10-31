# V3 — Data and backups

> Status: **in progress** (sandbox, phase 1), reworked on 2026-10-31 ("Declarative data", step 4 of the accepted plan). Merges and pulls forward plan entries V10 Data and V11 Backups; the plan entry V3 Routes keeps its row. Features: F-BAK-01 (register of expected backups, now per data asset), F-BAK-02 level 1 (the job ran), F-BAK-03 (coverage). Decisions: **D-77 to D-82** (D-60 to D-63 are superseded). Tools chosen (Q-50): restic through [Backrest](../companions.md#backrest) and [K8up](../companions.md#k8up).

## Need

> "If all my servers crash, I want to get my state back. I want backups on my local RAID array and on S3." — "Data must be managed by the user like Apps and Infrastructure: a declared source, not a scan of every possible database. I don't want to follow everything; some apps matter more than others."

The first demo of this page discovered every database and volume of the fleet. The maintainer rejected it as too generic: a list of everything cannot say what matters, and an unimportant volume without a backup is noise. The page is now **declarative**, like Apps and Infrastructure: the operator writes in the catalog **which data he follows**, how much losing it hurts, which copies must exist, and which tool proves each one. Portarr compares that policy with what the backup tools report (D-43: it reads, it never backs up).

## Scope

| In | Out (later) |
|---|---|
| A `data:` catalog section: followed data linked to an app, with kind, place, importance (accepted loss window) and expected copies (local, off-site) | Discovering data (removed, see below); sizes, growth and saturation forecasts (F-DAT-02) |
| Connectors, read-only: **Backrest** (plans, operations), **K8up** (Schedules, Backups, Snapshots), Kubernetes **CronJobs** (with their Jobs) | S3 bucket verification (F-BAK-02 level 2), restore tests (Q-45), CloudNativePG / Velero connectors |
| State per copy (Protected, Waiting for first run, Unknown, Not set up, Late, Failed, Missing) and per asset; summary with "x of y critical assets have an off-site copy" | Issues and notifications on a late or failed backup (V5, V6) |
| **Data** page grouped by importance then app, at-risk first, summary figures as filters, freshness of the tools read | Configuring backups from Portarr (D-31, D-43) |

## The declared model (D-77, D-78)

```yaml
data:
  - id: shop-db
    app: shop                          # an app of the catalog (required)
    name: Shop database
    kind: postgresql                   # postgresql, mariadb, influxdb, vault, sqlite-app-config, volume, files, cluster-state…
    where: { environment: cluster, namespace: shop, workload: shop-db-0, claim: data-shop-db-0 }   # informative
    importance: critical               # critical | important | comfort
    maxAge: 12h                        # optional: overrides the importance's window
    targets:
      local: { cronjob: shop/db-dump }
      offsite: { k8up: { schedule: shop/offsite, item: data-shop-db-0 } }
```

Full reference: [catalog.md](../catalog.md#data).

- **Only declared assets appear.** Nothing is discovered: a volume the operator did not list does not exist for this page.
- **Importance** gives the accepted loss window, the age beyond which the newest copy is *late*: **critical 24 h**, **important 7 days**, **comfort 30 days**. `maxAge` overrides it per asset. A grace of one hour absorbs runs that drift.
- **Targets**: `local` (the array) and `offsite` (a B2 or S3 bucket). A target absent from the entry is a copy the operator does not want (shown "not followed"); a declared target without source is a copy nothing produces yet (**Missing**).
- **Sources** of a target, any of which proving a recent copy is enough: `backrest: { plan }` (a Backrest plan), `k8up: { schedule: <ns>/<name>, item? }` (a K8up Schedule, narrowed by `item` to the snapshots whose path holds it, e.g. a claim name), `cronjob: <ns>/<name>` (a Kubernetes CronJob), `manual` (a copy the operator makes himself: `true`, or the date of the last one). The environment of a source is the data's own unless the source says `environment:`.

### Coverage (D-79)

Per copy, from the sources' reports: **Protected** (last success within the window), **Late** (older), **Failed** (the last attempt failed after the last success, even inside the window: a broken job is actionable), **Missing** (declared, and no job found: no source, or the tool answers without that job, or the item is absent from the snapshots), **Waiting for first run** (the job exists in the tool and has not run yet; the page shows its next run when the schedule is a cron expression, read in UTC; pending, neither a failure nor a risk), **Unknown** (the tool cannot be read: refused, unreachable, unexpected answer), **Not set up** (the tool is declared but not configured on that environment: `no-connector`, `credentials-missing`, `token-missing`, `ca-missing`, `ca-invalid`; a configuration the operator must fix, in its own color). With several sources the best counts. An asset takes the **worst** of its copies, in the order above; the page puts at-risk first. Waiting, Unknown and Not set up are not "at risk": nothing proves a loss, they are not proven yet.

Summary: assets followed, protected, at risk (late + failed + missing), waiting for a first run, not set up, unknown, and **offsite coverage**: "x of y critical assets have an off-site copy" (a copy that succeeded at least once), with "z pending first run" for the critical assets whose off-site job has not run yet: they are not protected (not in x) but they are not missing either. Each figure filters the list (`?filter=at-risk`, `late`, `failed`, `missing`, `waiting`, `not-set-up`, `unknown`, `protected`, `offsite-gap`).

## Connectors (D-80, D-81, D-62)

`IBackupSource` is the seam: `ListJobsAsync` returns every job a tool defines on an environment (reference, name, schedule, repositories, last run, snapshots). The `BackupMonitor` reads, every minute and in memory only, the tools the declared data names. A tool that is not configured marks its copies Not set up (`no-connector`, `credentials-missing`…), one that cannot be read marks them Unknown (`http-403`, `unreachable`…), both with the reason code; neither hides what other sources say.

| Tool | Reads | Version checked | Needs |
|---|---|---|---|
| **Backrest** (`backrest`) | `POST /v1.Backrest/GetConfig` (plans, their repository and schedule), `POST /v1.Backrest/GetOperations` per plan (last 200 operations → last backup, last success, snapshot, bytes) | v1.14.1 (proto `v1`, Connect protocol, JSON) | the environment's `backrest: { url, userEnv, passwordEnv }` |
| **K8up** (`k8up`) | `k8up.io/v1` `Schedule` (cron, backend kind), `Backup` (owned by a Schedule; `Completed` condition `Succeeded`/`Failed`; a failed pre-backup pod), `Snapshot` (id, date, paths, repository) | K8up operator v2.16.1, chart 4.10.1 | `get, list` on `schedules, backups, snapshots` (group `k8up.io`) |
| **CronJob** (`cronjob`) | `batch/v1` `CronJob` (last successful time) and `Job` (completed / failed attempts) | `batch/v1` | `get, list` on `cronjobs, jobs` (group `batch`) |

Notes that shaped the connectors:
- Backrest has **no read-only account** (one basic-auth login, full access) and its Connect API only accepts POST for these calls. Portarr calls the two read methods and none of the mutating ones (`Backup`, `Forget`, `Restore`, `Cancel`…); restrict network access to Portarr's host. `GetConfig` answers with repository passwords and hook tokens: the connector keeps plan ids, repository ids and schedules only.
- A Backrest plan has **one repository**: a copy in two repositories is two plans (`x-local`, `x-offsite`).
- Backrest statuses: success and warning count as a snapshot made; error is a failure; pending, in-progress, cancelled and dry runs are not attempts.
- K8up's `Snapshot` objects are matched to a Schedule through its backend (`s3:endpoint/bucket`, `b2:bucket:path`, a local mount path) when it is spelled out in the Schedule; a Schedule relying on operator-wide defaults is judged from its Backups alone. Snapshots are optional (a 403 on them is ignored).
- Until the maintainer applies the RBAC rules, the Kubernetes sources answer `http-403`: the copies are Unknown, not failed.

## Data page (D-82)

Route `/data`, after Infrastructure. From top: the tools read (name, environment, jobs, age of the reading, or why not), the summary figures (filters), then critical / important / comfort sections, each grouped by app (icon and name). A row: the asset's state (icon and text), name, kind, accepted window, place; then the **local copy** and the **off-site copy** side by side, each with its state, last success, and the job(s) behind it with the reason when a tool could not prove it. A copy not expected is dashed "not followed".

## What was removed

The discovery code and everything that only served it: `DataAssetDiscovery`, `DataEngines` (engine recognition by image or data path), the Docker/Kubernetes asset ids, the `backups:` catalog section (jobs declared by hand with `covers`, `lastRun`, `source: kubernetes-cronjob@…`) and its parser, the 3-2-1 "same machine" analysis, the backup job rows of the page. Kept: the workload `Mounts` of the inventory (Docker mounts, Kubernetes claims), used by the Infrastructure model and its tests, and the Kubernetes CronJob source (now `cronjob:`). An existing `backups:` section is a catalog error pointing at the line; migrate each entry to a `data:` target.

## Statuses

Data coverage is not an app status yet: a late or failed backup becomes an issue in V5 (Issues). The MCP overview lists `backup-failed`, `data-missing`, `backup-late` and `critical-without-offsite` findings.

## Acceptance criteria

- [ ] Only the data declared in `data:` is listed, grouped by importance then app, at-risk first; no discovered asset appears
- [ ] Each asset shows its local and off-site copy with state, last success and the job behind it; a copy not declared is "not followed"
- [ ] A copy declared with no source, or whose job a reachable tool does not have, is Missing; a tool that cannot be read makes it Unknown with the reason; a tool not configured on the environment makes it Not set up; a job that never ran makes it Waiting for first run (with the next run when known) and is not counted at risk
- [ ] Late is relative to the importance's window (24 h / 7 days / 30 days) or `maxAge`; a failed last attempt is Failed
- [ ] The summary gives followed, protected, at risk, waiting, not set up, unknown and "x of y critical assets have an off-site copy" (with the pending ones apart); every figure filters the list through `?filter=`
- [ ] Backrest plans and operations, K8up Schedules, Backups and Snapshots, and CronJobs with their Jobs are read with GET (POST for the two Backrest reads) and never written; credentials come from environment variables the catalog names
- [ ] The Kubernetes sources show `http-403` while the ServiceAccount lacks the rules
- [ ] Invalid `data:` entries (unknown app or environment, no importance, no target, a K8up source on a machine, a credential in the catalog) are catalog errors that name the entry

## Appendix: the tool comparison (Q-50)

> Chosen on 2026-10-31: restic through **Backrest** (hosts, VMs) and **K8up** (Kubernetes). The study below is kept as the reasoning behind the choice; the Rollout subsection is superseded by [companions.md](../companions.md).

### The fleet to protect
- A **hypervisor host** (Unraid) with ~40 Docker containers whose state is in an appdata share, several databases (PostgreSQL, MariaDB), and VMs; it has an array (parity-protected) that is the natural **local target**.
- **Docker VMs** with their own databases (named volumes) and, for the infrastructure state, an S3-compatible bucket.
- A **K3s cluster** with StatefulSets (PostgreSQL, InfluxDB, Vault) and Deployments with claims, on local-path storage (data lives on one node's disk).
- An **S3 bucket** off-site (any S3-compatible provider).

### Needs
3-2-1 (array + S3), encryption at rest on S3, deduplication (daily copies of multi-GB time series), **consistent database copies** (a dump through a hook, not a copy of live files), a **status Portarr can read** (API or Kubernetes resources, D-17), Kubernetes-native for the cluster, popular and maintained.

### Options

| Option | Scope | Targets | Encryption · dedup | Database consistency | What Portarr could read | Verdict |
|---|---|---|---|---|---|---|
| **Unraid Appdata Backup** (plugin, in use) | Unraid containers | Local folder only | No · no (tar.gz per run) | Stops each container during the copy: consistent, with downtime | Log files only | Keep during transition; no S3, no API |
| **Kubernetes CronJobs + scripts** (in use) | Anything | Whatever the script does | Script-dependent | `pg_dumpall` in the script | CronJob status (last success only) | No dedup, no off-site, no retention view |
| **restic + Backrest** | Hosts, VMs (Docker or not) | Local folder, S3, B2, SFTP, rest-server; several repos per plan | Yes · yes | Pre/post hooks (`docker exec … pg_dump`) | Backrest HTTP API: plans, operations (status, time, size) | **Recommended for hosts and VMs** |
| **K8up** (restic operator) | Kubernetes | S3, B2, GCS, Azure, local path, rest-server | Yes · yes | `k8up.io/backupcommand` annotation streams `pg_dump` / `influx backup` from the pod | `Schedule`, `Backup`, `Snapshot` resources (status, time); Prometheus metrics | **Recommended for the cluster** |
| **Velero** (+ Kopia file-system backup) | Kubernetes | S3-compatible (one per location) | Yes (Kopia) · yes | Pre/post exec hooks by annotation (dump to a file, then copy) | `Backup`, `Schedule` resources; metrics | Strong alternative for the cluster, heavier; also saves the manifests (already in Git here) |
| **Kopia** (server) | Hosts | Local, S3, B2, SFTP… | Yes · yes | Actions (before/after snapshot commands) | Server API (less documented), UI | Good engine; less scheduling and hook UX than Backrest |
| **Borg + borgmatic** | Hosts | SSH/borg repos (no native S3) | Yes · yes | Native database hooks (PostgreSQL, MariaDB, MongoDB…) | None (healthchecks pings) | No S3, no API |
| **Duplicati** | Hosts | Local, S3, many clouds | Yes · yes | Pre/post scripts | Web API (unofficial) | Known local-database fragility on large sets |
| **CloudNativePG** | PostgreSQL on Kubernetes | S3 (base backup + WAL) | Yes · n/a | Native, point-in-time recovery | `Backup`, `ScheduledBackup`, `Cluster` status | Best for PostgreSQL, but means migrating each database to an operator-managed cluster; later, per database |

### Recommendation (chosen)
**One engine, restic, with two front-ends: Backrest on the hosts and VMs, K8up in the cluster.**

- **Why restic**: encrypted, deduplicated, incremental snapshots to any target (local folder on the array, S3), one restore tool and one repository format for the whole fleet; very widespread.
- **Backrest** (on the Unraid host, and on each Docker VM still running databases) schedules restic with a UI and an API. One plan per data set, each with two repositories: one on the array (local), one in S3 (off-site). Database hooks run `docker exec <db> pg_dump` (or `mariadb-dump`) before the snapshot, so copies are consistent without stopping the containers; app folders are snapshotted as files.
- **K8up** is Kubernetes-native: a `Schedule` per namespace backs up every claim of the namespace, and pods annotated with `k8up.io/backupcommand` (e.g. `pg_dumpall`, `influx backup`) are dumped through their own process, consistent and without a file copy of live data. Two schedules (or one plus a restic `copy`) give the local and the S3 copies.
- **Portarr reads, never writes** (D-43): K8up's `Schedule`, `Backup` and `Snapshot` resources with the read-only ServiceAccount (plus `get, list` on `k8up.io` resources), and Backrest's plans and operations through its API with a read-only account. Both connectors implement `IBackupSource.ListJobsAsync`: their jobs, coverage (Backrest plan paths matched to bind mounts; K8up namespaces matched to claims, annotated pods to their databases) and runs replace the hand-written catalog entries. An S3 connector can then verify the newest snapshot independently (F-BAK-02 level 2).
