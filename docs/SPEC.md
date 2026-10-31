# Portarr — Functional and Technical Specification

> **Status**: draft v0.3, requirements gathering well advanced.
> **Related documents**: [DESIGN.md](DESIGN.md) (UI guidelines) · [TESTING.md](TESTING.md) (testing strategy) · [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) (plan by verticals)
> **Last updated**: 2026-10-30
> **Audience**: the project owner, contributors, and mockup tools (this file is self-contained enough to produce mockups from).
>
> ⚠️ This document is intended for the **public repo**. It contains **no real infrastructure values**. All names (`server-1`, `homelab.example`, "Media" stack…) are examples.

## Legend

| Marker | Meaning |
|---|---|
| ✅ | **Frozen**: decided, does not change without a new ADR |
| 🟡 | **Proposed**: drafted by the analyst, pending validation |
| ❓ | **Open**: unanswered question, see §14 |
| ♻️ | **Superseded**: replaced by a later decision, named in the row |

Identifiers: `D-xx` decision · `F-XXX-nn` feature · `NF-nn` non-functional requirement · `Q-xx` question.

---

## Table of contents

1. [Vision](#1-vision)
2. [Frozen decisions](#2-frozen-decisions)
3. [Users and use cases](#3-users-and-use-cases)
4. [Domain model](#4-domain-model)
5. [Status model](#5-status-model)
6. [Architecture](#6-architecture)
7. [Providers and capabilities](#7-providers-and-capabilities)
8. [Navigation and screens](#8-navigation-and-screens)
9. [Feature catalog](#9-feature-catalog)
10. [Non-functional requirements](#10-non-functional-requirements)
11. [Security](#11-security)
12. [Engineering and delivery](#12-engineering-and-delivery)
13. [Roadmap](#13-roadmap)
14. [Question register](#14-question-register)
15. [Glossary](#15-glossary)
16. [Changelog](#16-changelog)

---

## 1. Vision

### 1.1 Pitch

**Portarr** is a self-hosted portal for homelabs and small infrastructures. It brings together in a single application:

- a **launcher**: all your web applications, organized by theme, one click away;
- an **operations console**: health, live logs, metrics, updates, security vulnerabilities, logins.

It works the same way wherever your services run: Docker host, VM, hypervisor or Kubernetes cluster.

It is designed in the spirit of the *arr suite (Sonarr, Radarr, Prowlarr…): a standalone, API-driven application, extensible through **providers**, that **builds on existing tools** instead of rewriting them.

### 1.2 Problem

A typical homelab accumulates dozens of services across several machines. To answer "is everything OK?", you currently have to juggle:
- a launcher (Heimdall, Homepage);
- a container tool (Portainer, Dozzle);
- an uptime tool (Uptime Kuma);
- a metrics tool (Grafana);
- an update tool (Diun, What's Up Docker);
- a security scanner (Trivy).

None of these tools knows that *Sonarr is part of the Media stack, runs on server-1, is deployed by a given IaC repo, has 2 critical vulnerabilities and a pending update*.

### 1.2.1 Field observations (gathered 2026-10-26)

The original need comes from real-life situations:

- **We are flying blind.** Many small applications have been deployed over time. We don't know when they go down or when they need maintenance, and we have to hop from tool to tool to understand what is going on.
- **The hypervisor UI only sees the outside.** It shows disks, the array and VM state well, but nothing of what runs **inside** the VMs. The reverse is also true: from inside a VM, you cannot see the pressure the hypervisor is under.
- **Silent failures.** A real example: VMs were shutting down "at random". The probable cause was hypervisor RAM exhaustion (several VMs with 12 GB each), which killed a VM to reclaim memory. Nothing reported it.
- **Data is a blind spot.** The number of databases keeps growing, with no way of knowing whether there is enough space left to store them, or whether backups (to S3) are actually performed and valid.
- **No alerting.** Information only arrives if you go looking for it. We want to be notified (email, push notification) when something is wrong.
- **Too many URLs.** Between the applications and their admin consoles (e.g. the identity provider's console), it is hard to remember which link to open.

### 1.3 Value proposition

1. **A unified model** (§4) that links applications, stacks, workloads, environments and IaC sources.
2. **A single view** of health, with navigation familiar to *arr users.
3. **The conductor rather than the Swiss Army knife**: Portarr aggregates and links; it delegates the storage of metrics, logs and scans to specialized tools.
4. **A DevOps approach**: configuration as code, deployment via IaC, API first.

### 1.4 Out of scope

| Portarr **is not** | It delegates to |
|---|---|
| A metrics database (TSDB) | Prometheus, VictoriaMetrics… |
| A log store | Loki, or the Docker/Kubernetes APIs for real time |
| An advanced dashboarding tool | Grafana (Portarr deep-links to it) |
| A continuous deployment tool | Terraform, Ansible, ArgoCD, Flux… (Portarr can *trigger* and *display*) |
| A vulnerability scanner | Trivy, Grype |
| An identity provider | Keycloak, Authentik, Authelia… |
| A full replacement for Portainer or the Kubernetes dashboard | Portarr covers day-to-day operations, not fine-grained administration |

---

## 2. Frozen decisions

| # | Decision | Rationale |
|---|---|---|
| ✅ D-01 | **Name: Portarr.** Public repo `maflabs38/portarr`. | A nod to the *arr suite (*portal* + *arr*). Existing namesakes: small projects with no direct relation (see §14, Q-40). |
| ✅ D-02 | ***arr spirit**: standalone application in **a single container**, REST API first (the UI consumes it), extensible through providers, **navigation inspired by the *arr apps**. | Proven, familiar model that is simple to deploy. |
| ✅ D-03 | **Conductor**: integrates existing tools, does not reimplement them, stores neither metrics nor logs. | The value lies in the model and the links. |
| ✅ D-04 | **.NET (C#) backend**, ASP.NET Core, latest LTS (.NET 10). | .NET showcase, same foundation as Sonarr and Radarr. |
| ✅ D-05 | **React frontend** (TypeScript, Vite). | Like the *arr apps. |
| ✅ D-06 | **SQLite by default, PostgreSQL as an option.** | Zero dependencies to get started. |
| ✅ D-07 | **Public, generic and configurable project.** The product (public repo) is strictly separated from any personal configuration (private repo). | Portfolio and reusability. |
| ✅ D-08 | **Reference: Sonarr and Radarr.** We adopt their patterns in modern .NET, without their NzbDrone legacy. | Proven best practices and learning. |
| ✅ D-09 | **The spec is organized by feature** (§9): each feature describes the need, the display and the data sources **for each environment type**. | Miss no feature, and be able to build mockups from the spec. |
| ✅ D-10 | **Priority: observability first, launcher second.** The main value is to *see* the state of the whole fleet and to be *notified*. The launcher is a convenience: essential, but secondary. | Observations §1.2.1: the real problem is flying blind. |
| ✅ D-11 | **Connect the outside and the inside.** A VM (or a node) is seen both from its host (allocation, state, hypervisor memory pressure) and from the inside (guest OS, workloads), and the two views are correlated. | This is precisely what the hypervisor UI does not do. |
| ✅ D-12 | **Data is in scope**: databases, storage space and backups are first-class objects, not a detail. | Current blind spot (§1.2.1). |
| ✅ D-13 | **Three first-class execution families**: **VMs** (via their hypervisor and from the inside), **Kubernetes** (multi-node clusters, including remote nodes connected through a tunnel) and standalone **Docker**. None of them is a late add-on. | Real mixed fleet, migrating from VMs to a k3s cluster with an off-site worker node. |
| ✅ D-14 | **Maintenance includes the code**: an app can be linked to its source code repository. Portarr then surfaces its dependency alerts (e.g. Dependabot), its published releases and the end of life of its frameworks and runtimes. | "Knowing when to maintain" = knowing when a dependency or runtime becomes dangerous (§9.6, §9.7). |
| ✅ D-15 | **Every application in the fleet is tracked, not just the ones you launch**: web apps (openable in one click), thick clients / desktop applications, headless services. The launcher shows **frontends**; backends (databases, internal APIs) remain workloads. | A thick client also has dependencies, vulnerabilities and versions to track. |
| ✅ D-16 | **Portarr informs, the operator acts.** In v1, no automatic remediation: no Portarr-driven GitOps, no PR creation, no automatic updates. A problem translates into a clear signal (⚠ "vulnerability detected" badge, issue, notification), and the operator decides and acts with their own tools. | The scope is already vast: the first goal is an accurate picture. Automation is a possible evolution, not a v1 goal. |
| ✅ D-17 | **One reference tool per capability, chosen among the most widely used.** For each need, we pick **one** proven, popular tool (maintained container, solid API), integrate it properly, then broaden. No exotic tools, no "just in case" connectors. See §7.0. | Start minimal: one tool that works well beats ten half-finished connectors. |
| ✅ D-18 | **Per-feature method: what to watch → with which tool → how Portarr connects to it.** Each feature documents these three steps before being implemented. | The main work of the project is integration, feature by feature. |
| ✅ D-19 | **Two fundamental concepts: the Application and the Stack.** An **application** is most often materialized by a container, sometimes by a bare-metal service (or by nothing at all, for a thick client). A **stack** is a constellation of applications that form a whole. Both can be created **in two equivalent ways**: from the **UI** (novice user) or through **configuration** (YAML manifest, labels, Terraform). | Core of the product, confirmed by interview #4. |
| ✅ D-20 | **The reverse proxy is a special component.** It is the one that assigns subdomains: Portarr reads its routes to automatically link an application to its URL, know whether it is protected by SSO and detect broken routes (F-NET). | Source of truth for URLs in a homelab. |
| ✅ D-21 | **The spec is validated against a real fleet.** The inventory of a real infrastructure (kept in the private repo) serves as a set of concrete cases; every generic finding is carried over into the spec. | Pragmatism: start from what exists. |
| ✅ D-22 | **Reference observability: the Grafana stack.** Prometheus for metrics, Loki for logs, Grafana for exploration, with collection based on the **OpenTelemetry** standard (Grafana Alloy). ELK / OpenSearch is ruled out as the v1 reference and may come later as an additional provider. | Much lighter (Loki only indexes labels, versus Elasticsearch's full-text indexing and JVM), the Kubernetes standard for metrics, already in place. The OpenTelemetry collector avoids lock-in. |
| ✅ D-23 | **Full, mandatory test pyramid**: unit, integration (real services via Testcontainers) and end-to-end (Playwright), with blocking thresholds in CI. Details: [TESTING.md](TESTING.md). | Professional quality, portfolio showcase, fearless refactoring. |
| ✅ D-24 | **Implementation as a core then verticals**, one vertical = one PR with its definition of "done" (three levels of tests, compliant UI, demo mode, docs, dogfooding). Details: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md). | Modular, provider-based application: each integration ships and is validated on its own. |
| ✅ D-25 | **Formalized design guidelines**: mandatory *arr patterns for structure, Portarr theme through tokens only. Details: [DESIGN.md](DESIGN.md). | Strict adherence to the *arr pattern, including by AI agents. |
| ✅ D-26 | **English everywhere in the repository**: code, comments, documentation, commits, issues and PRs are written in English. The UI ships in English with a French translation (i18n). | Public, international project; maximize the potential audience and contributors. |
| ✅ D-27 | **License: GPL-3.0**, like the *arr apps. Code adapted from Sonarr/Radarr is allowed, with attribution. | Consistency with the *arr ecosystem and freedom to reuse its proven components. |
| ✅ D-28 | **Data access: Dapper + FluentMigrator** (like Sonarr), no EF Core. Explicit SQL, migrations replayed on SQLite and PostgreSQL. | Data is not central to Portarr (configuration, inventory, issue history); explicit SQL keeps queries visible and lightweight; stays in the *arr philosophy. |
| ✅ D-29 | **Backend tests: xUnit v3 + NSubstitute + AwesomeAssertions** (all free licenses). Replaces the NUnit + Moq of the first draft. | Most common choice in modern .NET, consistent with the organization's .NET template. |
| ✅ D-30 | **Portarr collects and reports, it does not compute.** Thresholds, aggregations, alert evaluation and any calculation live in the connected tools (Prometheus, Alertmanager, Kubernetes, backup tools…). Portarr's own logic is limited to **orchestration**: linking entities, combining the states reported by the tools, presenting them. Reinforces D-03. The optional agent (D-100, D-102) returns raw readings only. | A clean application is a relay that relies heavily on the tools, not a second monitoring engine. |
| ✅ D-31 | **Portarr never acts on the infrastructure.** No imperative action (start, stop, restart, scale, IaC job triggering). The only write allowed: **declarative configuration pushed to a tool through its API or its configuration files**, when a connector declares it (e.g. a backup schedule, a scrape target). Rules only raise alerts, never automatic actions. Reinforces D-16. | Portarr is a reporter; acting stays with the operator and their tools. |
| ✅ D-32 | **Standalone first.** With no connector at all, Portarr works as a launcher and an inventory (apps, links, stacks, which environment they run on), like a Heimdall-type portal. Each connector plugged in adds capabilities (metrics, logs, health, updates…); missing data is shown as "not connected", never as an error. | Low entry barrier; the ecosystem is opt-in, connector by connector. |
| ✅ D-33 | **Resources expressed in vCPU (cores) and RAM consumed**, the same way across every environment type (Docker host, VM, hypervisor container, Kubernetes pod and node). The percentage of the parent's capacity is secondary; Kubernetes adds request and limit. | Uniform units make a container on the hypervisor, a VM and a pod comparable. |
| ✅ D-34 | **Portarr runs on the hypervisor host**, next to the reverse proxy, as the place with the most height; it understands VMs and their workloads through connectors. | The central view belongs at the top of the hierarchy. |
| ✅ D-35 | **One class library per connector** (`Portarr.Providers.Docker`, `Portarr.Providers.Prometheus`…), compiled into the application. Loading external DLLs may come later at little cost, since connectors are already isolated. No plugin marketplace or third-party contributor model is designed for now. | Solo project; maximum decoupling is the goal, a plugin ecosystem is not. |
| ✅ D-36 | **Configuring tools is allowed, tool logic is not.** A connector may push configuration to its tool through the tool's API or configuration files (alert rules, scrape targets, a backup policy…): this is the preferred way to get the right information. What is forbidden is re-implementing in Portarr business logic tightly coupled to a tool (own threshold evaluation, own query engine). Any tool-specific code stays inside its connector library; the Core knows only capabilities. Refines D-30 and D-31. | Portarr steers the tools, it does not replace them. |
| ✅ D-37 | **Alerts are evaluated by the tools** (Prometheus/Alertmanager, Kubernetes, backup tools…) and received by Portarr, which links them to apps, stacks and environments. Rules can be edited from Portarr only when the connector can push them to the tool (D-36). Portarr's own signals are limited to orchestration: combining reported states (a stack degraded because a critical member is down). | No second alerting engine; one source of truth per rule. |
| ✅ D-38 | **The collector kit is not part of Portarr.** The product documents, per connector, what must be installed and how to connect it; deploying the tools is each operator's own IaC (for this project: the private config repo). The only compose files in the repo are test fixtures (E2E stack) and a minimal example. **Amended by D-100 and D-102:** the Portarr reader is the one component the product does ship (image and documentation); installing the other tools stays the operator's. | An installation kit would mirror one particular infrastructure; Portarr stays generic. |
| ✅ D-39 | **Forge: GitHub only** for v1, organization repositories included (private ones through a read-only token). Version and vulnerability information comes from Dependabot and the GitHub APIs. | Single forge actually used; the forge connector stays behind a capability so another one can be added later. |
| ✅ D-40 | **The hypervisor is a first-class environment**: its own resources, its VMs and its containers are shown like any other environment. v1 reference: Unraid (dogfooding); Proxmox later. If the hypervisor goes down, Portarr goes down with it: accepted, the outage is obvious anyway (no external heartbeat required). | It is the top of the hierarchy and where Portarr runs (D-34). |
| ✅ D-41 | **Vulnerabilities of running images are in scope**, not only those of the operator's own code: a critical CVE in a third-party image (identity provider, media server…) must be visible so the operator updates it. Portarr queries a **Trivy server** through a connector; severities below a configurable threshold (HIGH by default) are hidden. | Knowing about a critical flaw in what actually runs is a core reason for the tool. |
| ✅ D-42 | **No fixed access topology.** Several reverse proxies may coexist and chain (a proxy on the hypervisor forwarding to a VM, then to a cluster ingress). Portarr supports several route connectors at once and resolves a chain of routes down to the workload. First route connector: SWAG/nginx (dogfooding); Traefik / Kubernetes Ingress next. | Real homelabs mix containers, VMs and clusters in many layouts; the model must not assume one. |
| ✅ D-43 | **Backups: observe and report only** for now. Portarr shows which databases and volumes exist, whether they have a backup, and the last successful one (date, size, destination); it creates or configures nothing. The reference tool is chosen when V11 starts. | The first need is visibility: today nobody knows whether the backup scripts actually run. |
| ✅ D-44 | **The catalog file is the primary source** of environments, stacks and apps: it is written and maintained as code (by hand, by IaC or by an AI agent). The UI shows it read-only, with its origin; Settings shows the configuration rather than editing it. Creating apps from the UI stays a later option for operators who want it (F-CAT-04). | Automation first: one versioned file is easier to generate, review and rebuild than state edited in a UI. |
| ✅ D-45 | **Tiles show the container state and the healthcheck separately**, like Portainer: "Up 3 h" / "Exited (1)" on one line, "healthy" / "unhealthy" / "starting" / "no healthcheck" on the other; the app's overall status is the tile's frame color. | The two signals answer different questions (is it running? does it work?) and must both be visible at a glance. |
| ✅ D-46 | **Public and LAN URLs, picked automatically.** An app may have public (DNS) and LAN URLs; a URL whose host is a private IP is a LAN URL. When no LAN URL is declared, Portarr derives one from the environment's `address` and the container's published port. The launcher opens LAN URLs when Portarr itself is opened on a LAN address, public ones otherwise, with a Public/Local switch to force either. Resolves Q-07. | One-click access from home and from the Internet, without writing every URL twice. |
| ✅ D-47 | **Icons resolve in this order**: the operator's own icon library (an `icons/` folder next to the catalog, versioned with it), then the dashboard-icons community set by name, then a name guessed from the container image, then the app's initial. Portarr serves every icon from its own API and caches community icons in its data folder: browsers never call a third-party CDN, and icons keep working offline. | Custom apps get their real logo; common apps need no configuration; no tracking, offline homelabs supported. |
| ✅ D-48 | **An app can be made of several workloads** (micro-services, database), each `critical` (default) or `optional` for the app; the tile summarizes them ("7/8 up"). A stack groups the apps that form one solution, including back-end services used by a single product. | A product built as micro-services is one thing for its operator, while each service keeps its own status. |
| ✅ D-49 | **One spec page per vertical** (`docs/verticals/`), written before or with the vertical: need, scope, behavior, acceptance criteria, decisions. SPEC.md stays the global reference and links to them. | The spec evolves with the product, vertical by vertical, close to the code that implements it. |
| ✅ D-50 | **HTTP checks on by default, with opt-out.** Every web app with a URL is checked (GET on its public URL, else its LAN URL, every 60 s, 10 s timeout, 2xx/3xx up, redirects not followed). `check:` in the catalog overrides it per app (URL, expected statuses, keyword, interval, timeout, critical) and `check: false` turns it off. A check is critical by default: failing, it turns the app Down even when its container is Up; `critical: false` only degrades it. | "Up" does not mean the app answers; most containers have no healthcheck, so the useful signal must not depend on configuration. |
| ✅ D-51 | **Check history in memory only**: the last 90 results per check; a restart starts a new history. Uptime over 24 h / 7 d / 30 d comes later, from a tool or a persistent store. | Enough to see flapping and recent incidents without making Portarr a metrics store (D-03). |
| ✅ D-52 | **TLS certificate expiry read during the HTTP check**, from the handshake, validation kept on: less than 14 days before expiry the app is Degraded, expired or invalid it is Down. | Expired certificates are a classic homelab outage; the data comes for free with the check. |
| ✅ D-53 | **Cloudflare as an environment** (`runtime: cloudflare`): its Pages projects and Workers are workloads (state from the latest production deployment). The API token is read from an environment variable named in the catalog, never written in it; read-only permissions only (Pages Read, Workers Scripts Read). | Apps hosted on a CDN are part of the fleet; the catalog stays versionable without secrets. |
| ✅ D-54 | **Infrastructure is a live dashboard, not a list.** One compact card per machine (a Docker host, a Kubernetes node), side by side: CPU line chart over the last 10 minutes, load bar, memory ring, and the top running consumers as bars (scaled to the heaviest, share of the machine as text), each opening its workload page. Each connector is read as fast as its data changes (Docker every 3 s; Kubernetes every 5 s, a point only when metrics-server measured again, ~15–20 s) and the page refreshes every 2 s with the age of the last sample; values are kept **in memory only** (10-minute ring buffer per machine, lost on restart). It replaces the Workloads list and the Checks page; statuses stay on the Apps page. | D-03 holds: a short live window is not metrics storage. Long history and trends are Prometheus' job (V8). The operator asked "how is my machine running and what consumes it", not for another table. |
| ✅ D-55 | **Kubernetes connector over plain HTTPS**, no SDK: four read-only endpoints (`/api/v1/nodes`, `/api/v1/pods`, metrics.k8s.io nodes and pods). Token of a read-only ServiceAccount from an environment variable named in the catalog, cluster CA from a file next to the catalog (the only trusted root, host name checked). Pods are workloads (namespace, node, readiness), nodes are machines. Usage and logs are separate connector capabilities: pods serve usage, not logs yet. | Small dependency surface and the same idioms as the Docker and Cloudflare connectors; minimal RBAC (D-31, rule 9). |
| ✅ D-56 | **Kubernetes selectors survive rollouts**: an app matches pods by `namespace` and `app` label (or name prefix, or exact name); when several pods match, the running and ready one is shown. | Pod names change on every rollout; a catalog must not be edited for each deployment. |
| ✅ D-57 | **A Docker host's usage is the sum of its containers**, labelled as such: Docker cannot see the host. Capacity comes from `/info` (or the containers' stats when a socket proxy refuses it). A host metrics seam (`IHostMetricsSource`) lets a node_exporter or Prometheus reader replace it later; none is installed or shipped yet. | Honest numbers without installing anything on the hosts; the upgrade path is an integration, not a collector (D-38). |
| ✅ D-59 | **A machine can be a VM on another environment of the catalog**: `host: <environment id>` on an environment, `kubernetes.nodeHosts: { <node>: <environment id> }` per node. The host must be a single machine (a Docker or launcher-only environment). A hypervisor's card then shows its VMs as they report themselves: memory split into Containers, VMs (the memory each guest reports, i.e. its allocation) and Free; CPU as Containers plus the VMs' guest-reported load, labelled as such and as a lower bound when a guest only sees its containers. Guests say "VM on <host>". Nothing is installed on the hypervisor. | A hypervisor's Docker engine sees neither its VMs nor its own processes: "62 GB free" on a host whose memory is mostly allocated to VMs was wrong. D-11 asks to correlate the outside and the inside; the catalog already knows both sides, and a host agent (node_exporter, D-57) can replace the estimate later. |
| ✅ D-58 | **Charts with Chart.js** (`react-chartjs-2`), colors read from the design tokens at render time, values always written next to the chart; the page opts out of Dark Reader-style recoloring (`darkreader-lock`). | The stack chosen in DESIGN.md §1.1; canvas charts cannot read CSS variables, and recoloring extensions hid bars and rings. |
| ♻️ D-60 | **Data assets are discovered from storage, not declared.** *Superseded by D-77: the maintainer rejected discovery as too generic.* A Docker container with a bind mount or named volume that runs a known engine (recognized by image, else by data path), belongs to a catalog app, or is named by a backup job; every PersistentVolumeClaim mounted by a pod (its engine and apps from the running pods that mount it). Ids `docker@<env>/<container>`, `kubernetes@<env>/<namespace>/<claim>`. System mounts (socket, `/proc`, `/etc`, a single file) are not data. Recognition names the engine; Portarr connects to no database. | The maintainer does not know which databases exist; a list that must be written by hand would miss exactly the forgotten ones. |
| ♻️ D-61 | **Known backup jobs in the catalog until a tool reports them.** *Superseded by D-78 (targets and sources of followed data) and D-80/D-81 (tool connectors).* `backups:` entries (what runs, `covers` by app or asset id, `target` local or s3, `every`, `storedOn`, last run written by hand, optional `source`). A job is Late after 1.5 × `every` without success, Failed when its last attempt failed after its last success. An asset takes the best state of its jobs, Not covered without any. 3-2-1 view: local copy, off-site copy, every copy on the physical machine of the data (through VMs and nodes, D-59). | Visibility first (D-43): the jobs that exist today are scripts, a plugin and CronJobs, none with an API; the register is the bridge until a backup tool is chosen (Q-50). |
| ✅ D-62 | **`IBackupSource` is the backup seam**: `ListJobsAsync` returns every job a tool defines on an environment (reference, name, schedule, repositories, last run, snapshots). The `BackupMonitor` reads, every minute and in memory only, the tools the declared data names; a tool that cannot be read marks its copies Unknown with a reason code. Sources: `backrest` (D-80), `k8up` and `cronjob` (D-81). | Backrest and K8up plug in without changing the Core; a refused connector must not hide what the operator knows. |
| ✅ D-63 | **A Data page, after Infrastructure** *(reworked by D-82)*: coverage and 3-2-1 summary, assets grouped by environment with the unprotected first, then the backup jobs. Proposal for the backup tool in [verticals/V3-data-backups.md](verticals/V3-data-backups.md#tool-proposal-q-50--proposal-awaiting-the-maintainers-choice) (restic: Backrest for hosts and VMs, K8up for the cluster), awaiting the maintainer's choice. | "If everything crashes, can I get my state back?" needs one answer per piece of data, not a list of jobs. |
| ✅ D-77 | **Data is declared, not discovered.** The catalog's `data:` section lists the data the operator follows, each linked to an app: `id`, `name`, `kind` (postgresql, mariadb, influxdb, vault, sqlite-app-config, volume, files, cluster-state…), `where` (environment, namespace, workload, claim, path: informative), `importance` and `targets`. Only declared assets appear on the Data page. The discovery code (Docker mounts, claims, engine recognition) and the `backups:` section are removed. | The maintainer: "Data must be managed by the user like Apps and Infrastructure: a declared source, not a scan of every possible database. I don't want to follow everything; some apps matter more than others." A scan listed every scratch volume and could not say what matters. |
| ✅ D-78 | **Importance is the accepted loss window; targets name the copies and the tools that prove them.** `critical` 24 h, `important` 7 days, `comfort` 30 days, overridable per asset with `maxAge`; a copy is late once its newest success is older than the window plus one hour of grace. `targets` are `local` (the array) and `offsite` (B2 or S3); a target left out is not wanted, a target without source is Missing. A target's sources, any of which proving a copy: `backrest: { plan }`, `k8up: { schedule, item? }`, `cronjob: <ns>/<name>`, `manual` (true or a date). | Importance is a decision about loss, not about the app's health; one number per level keeps the policy readable, and the tools (not Portarr) decide how often to back up (D-30, D-43). |
| ✅ D-79 | **Seven states per copy, the worst per asset** *(amended 2026-10-31: "Unknown" mixed three situations)*. Protected (success within the window); **Waiting for first run** (the job exists in the tool and has not run yet, with its next run when its schedule is a cron expression, read in UTC); Unknown (the tool cannot be read: unreachable, refused, unexpected answer); **Not set up** (the tool is declared but not configured on that environment: no connector, missing credentials, token or cluster CA, a configuration the operator must fix); Late; Failed (the last attempt failed after the last success, even inside the window); Missing (declared and no job found, or the item absent from the snapshots). Severity grows in that order. With several sources the best counts; an asset takes the worst of its copies. **At risk** is late + failed + missing: Waiting, Unknown and Not set up are *not proven*, not *lost*, and have their own counters. Summary: followed, protected, at risk, waiting, not set up, unknown, and "x of y critical assets have an off-site copy" (a copy that succeeded at least once) with **z pending**: a critical asset whose off-site job has not run yet is still not protected, so it is not in x, but it is shown as pending (blue) rather than as a gap (red), and the MCP overview reports it as an `info` finding (`critical-offsite-pending`) instead of the `critical-without-offsite` warning. | An unreachable tool is not a failed backup: saying Failed there would be a lie, saying Protected a worse one. A job that has not had its first run yet is not at risk, and a tool nobody set up is not "unknown": it is a task for the operator, in its own color. Off-site coverage of what is critical is the figure that answers "can I get my state back?". |
| ✅ D-80 | **Backrest connector (`backrest`).** Per environment, `backrest: { url, userEnv, passwordEnv }` (the variable names, never the credentials). Portarr POSTs `GetConfig` and `GetOperations` per plan (Connect protocol, JSON; Backrest v1.14.1) and calls no mutating method; a plan is a job (schedule, one repository), its operations give the last backup, last success, snapshot and bytes (success and warning are snapshots, error a failure). Backrest has no read-only account: restrict the network, and the connector keeps plan and repository ids only, never the passwords `GetConfig` carries. | Backrest is the chosen tool for hosts and VMs (Q-50). Its API is the only way to read the real result of each run. |
| ✅ D-81 | **K8up and CronJob connectors (`k8up`, `cronjob`).** Through the existing Kubernetes connector: `k8up.io/v1` `Schedule` (cron, backend kind), `Backup` (owned by a Schedule, `Completed` condition) and `Snapshot` (date, paths, repository; optional), operator v2.16 / chart 4.10; and `batch/v1` `CronJob` with its `Job`s. `item` narrows a Schedule to the snapshots whose path holds it. The read-only ServiceAccount needs `get, list` on those resources (group `k8up.io`, `batch`); until granted they answer `http-403`. | K8up is the chosen tool for the cluster; existing CronJobs keep the local copy meanwhile. |
| ✅ D-82 | **The Data page is the policy against the facts.** Sources read (tool, environment, age of the reading, or why not), summary figures that filter the list (`?filter=at-risk`, `late`, `failed`, `missing`, `waiting`, `not-set-up`, `unknown`, `protected`, `offsite-gap`), then critical / important / comfort sections grouped by app, at-risk first; each row shows the local and the off-site copy side by side with their state, last success and the job behind them. The "same machine" 3-2-1 analysis is dropped: off-site is a target of its own. | Same pattern as Maintenance (D-75); the maintainer reads one page to know what to fix first. |
| ✅ D-83 | **The API edits the catalog file, at the text level** (refines D-44). The file stays the source of truth; one Core editor (`ICatalogEditor`) serves the API and the MCP write tools. An edit names an item of a top-level list (`environments`, `stacks`, `apps`, `data`, `links`, `providers`) and carries the version the caller read (16 hex digits of the text's SHA-256): a different version is refused (409), writes are serialized. The whole resulting file goes through the catalog parser (400 with field paths, file untouched), is written to a temporary file in the same folder, flushed and renamed over the catalog (rewritten in place when a single-file bind mount refuses the rename), after copying the previous version to `catalog.yaml.bak-<timestamp>` (the last `PORTARR__CATALOG__BACKUPS`, 10 by default); the catalog is reloaded at once and the watcher's later event finds the same version, so the own write is never reported. **Comments are kept by splicing text**: YamlDotNet's representation model drops comments but keeps each node's start; the editor diffs the old item with the new one and rewrites only what changed (a value in place, a new key or list item after the last one in the file's indentation and the majority style of the list, flow `- { … }` or block, a removed key or item with its lines); a flow item is one line and is rewritten whole, in block style only when it gains multi-line text, and a comment that followed it moves to its own line. Limits: comments inside a rewritten node are lost, comment lines above a removed item stay. Every result is read back and compared with the intended document; on a mismatch the whole item is rewritten, and if that fails too nothing is written. No database; splitting the file later stays possible (the editor addresses items, not the file). | The maintainer wants to describe or change the environment by asking an assistant instead of editing complicated YAML, without losing the file as code (D-44) nor the comments that explain it. |
| ✅ D-84 | **The catalog carries descriptive knowledge** for people and AI assistants: `notes` (Markdown, at most 8000 characters) on environments, machines, stacks, apps, links and providers; `access` on an environment or a machine (`ssh: { alias, host, port, user, key, jump }` with the key file's name only, `consoles: [{ label, href }]`, `notes`); `machines: [{ name, notes, access }]` under an environment (cluster nodes, VPS); `links: [{ id, from, to, kind, name, network, notes }]` between environments or machines (`wireguard-tunnel`, `vpn`, `lan`, `peering`, `ssh-tunnel`, `other`; ends checked, `environment/machine` when a name is ambiguous); `providers: [{ id, name, kind, console, account, usedBy, notes }]` (cdn, paas, dns, registrar, cloud, vps, storage, backup, email, git, registry, monitoring, other; `usedBy` checked against apps and environments). **Never secrets**: a heuristic refuses private keys and certificates, SSH key material, well-known token formats, `password=…` assignments, credentials in URLs and long random strings, naming the field, never the value. Portarr never uses this knowledge to connect anywhere (D-31). It is rendered in the knowledge base (D-69) and by the read tool `get_environment_knowledge`. | Assistants must understand the environment (which machines are tunnelled together, how to reach them, which providers are involved) to help; the catalog is where the operator already describes it. |
| ✅ D-85 | **Catalog writes are off by default and need `catalog.write`** (amended by V7, D-97: they were keyed by `PORTARR__APIKEY` until accounts). `PORTARR__CATALOG__WRITABLE=true` turns them on and requires a caller whose role holds `catalog.write` (a signed-in user, a per-user key of such a user, or the deprecated `PORTARR__APIKEY`, 32 characters at least, which acts as the `system` principal). Off: 403; not signed in or a wrong key: 401; a role without the permission: 403. 30 writes per minute per client, counted before the key is checked. **Every write is audited in an append-only JSON Lines file next to the catalog** (`catalog.audit.jsonl`, rotated once past 1 MiB): time, channel (`api`, `mcp`), client address, user name (`system` for the environment keys, D-96), operation, item, versions before and after, which keys changed (never values, never keys); `GET /api/v1/catalog/audit` lists the latest. A file rather than the database: the history belongs with the file it describes, survives a change of database and is readable with any tool; the accounts' own audit (D-96) is in the database. | Writing the operator's source of truth is a deliberate choice; until accounts exist, a key and a trace are the minimum. |
| ✅ D-86 | **MCP write tools, enabled separately** (amends D-68): with `PORTARR__MCP__WRITETOOLS=true` the server adds `get_catalog_item` (read the item and the version first), `upsert_app`, `remove_app`, `upsert_stack`, `upsert_environment`, `upsert_machine`, `upsert_data_asset`, `add_link`, `upsert_provider`, `remove_catalog_item` and `set_notes`, annotated not read-only (destructive when they replace or remove), and instructions to read first, change only what was asked and never write a secret. They go through the same editor as the API (D-83, same validation, versions and audit, channel `mcp`) and need the catalog to be writable. Off, they are not even listed. They edit Portarr's description of the fleet, never the infrastructure (D-31). The Assistant page shows whether they are on and the latest audited edits. | The assistant in the IDE, and the future built-in chat, describe the fleet for the operator; read tools stay unchanged. |
| ✅ D-87 | **The built-in chat runs an agent CLI headless, behind an adapter, in an optional container** (`portarr-agent`, [ADR 0008](adr/0008-headless-agent-cli-behind-an-adapter.md)). Claude Code first: `claude -p` with stream-json in and out, one process per active conversation (new: `--session-id`, later: `--resume`), at most `PORTARR_AGENT_MAX_SESSIONS` (3) at once, stopped after `PORTARR_AGENT_IDLE_MINUTES` (15) idle, interrupted then killed on cancel. An adapter per CLI maps its protocol to the runner's events (text deltas and blocks, thinking, tool calls and results, permission requests, turn end with usage, errors); conversations are JSON files in the runner's data folder, replayed on the event stream. Node.js 22, TypeScript run without a build, no runtime dependency. Authenticated by a shared secret (bearer, compared by hash in constant time); Claude credentials from `CLAUDE_CODE_OAUTH_TOKEN` or `ANTHROPIC_API_KEY`, never logged. No PTY, no screen scraping. | Structured events give exact tool calls and permissions; the CLI stays swappable; Portarr's image stays free of agent CLIs and model credentials. |
| ✅ D-88 | **The chat's only tool is Portarr's MCP server; every write waits for the operator.** The CLI runs with `--strict-mcp-config` (Portarr's `/mcp` only, the key in a per-process file of mode 600), `--tools ""` plus the built-ins disallowed, `--setting-sources ""`, its own config directory and an empty working directory (no settings, hooks or `CLAUDE.md`), its own short system prompt, Portarr's read tools pre-allowed and `--permission-prompt-tool stdio` for the rest; never `--dangerously-skip-permissions`. The runner decides: `mcp__portarr__get_*`, `list_*`, `find_*` run; any other Portarr tool becomes an "Apply this change?" card (tool and arguments) answered by Approve (the input sent unchanged) or Reject (denied, the agent told not to retry); anything else is denied without asking; cancel or a stopped process refuses pending writes. The CLI's environment is an allow-list (never the runner's secret nor the MCP key). | The operator wanted a chat that describes and edits Portarr's configuration with confirmation, without a shell or access to the machines (D-31). |
| ✅ D-89 | **Portarr proxies the chat, off by default.** `PORTARR__AGENT__URL` and `PORTARR__AGENT__SECRET` (32+ characters; startup fails on a malformed URL or a weak secret); without the URL `/api/v1/assistant/chat` says `enabled: false` and its other endpoints are 404. Endpoints: state and agents, conversations (list, create, read, delete), messages, events (SSE, counted with the log streams), permission answers, cancel. Ids and names are checked before forwarding, nothing of the browser's request is forwarded, the runner's 401 or silence is a 502. Writes go through the MCP write tools and so the catalog editor (D-83, D-86); the runner sends `X-Portarr-Origin: chat:<conversation>`, kept in the audit as `via` (informative). Per user since V7 (D-98): `chat.use`, own conversations, approving needs `catalog.write`, a per-turn token instead of the shared MCP key. | One place for credentials and access, the browser never talks to the runner; the audit tells chat edits apart. |
| ✅ D-90 | **The chat panel follows an IDE chat.** A header icon (shown only when the chat is on) opens a side panel on the right, resizable (320 to 960 px, pointer or arrow keys, kept per browser), full screen under 768 px: conversations (latest first, delete, "waiting for your approval"), new chat, the messages (`role="log"`; Markdown through the safe built-in renderer; thinking and tool calls collapsed with their state, arguments and shortened result; permission cards with Approve and Reject; a usage line per turn; the answer shown live while written), and a prompt that takes typing and pasting on several lines (Enter sends, Shift+Enter breaks the line), agent and model selectors, Stop while a turn runs. | The maintainer dictates by pasting and wants the IDE experience inside Portarr. |
| ✅ D-91 | **The portal has an identity: a name, a logo and the version, in the sidebar.** A top-level `portal: { name, logo, colors: { name, version } }` section of the catalog (all optional; defaults: "Portarr", Portarr's own porthole and the accent token, the yellow of its logo, for both texts). A color is `#rgb`, `#rrggbb` or one of the theme tokens `accent`, `accentDeep`, `secondary`, `ink`, `inkSoft`, `inkMuted`, validated strictly by the parser, the API and the MCP tool (no other CSS can get through; the UI re-checks it before putting it in a style). `name` is one line of at most 40 characters; `logo` is a path **relative to the catalog folder** (`branding/logo.png`): forward slashes, no `..`, no absolute or drive path, no hidden segment, png, webp or svg, at most 1 MiB, checked by its content (signature of a png or webp; an svg must be well-formed without script, event handler, `foreignObject` or DTD), and never followed through a link out of the folder. `GET /api/v1/portal` returns the name, the resolved `colors`, the logo URL (`/api/v1/portal/logo?v=<hash of the file>`: immutable cache once the hash is in the URL) and the running version (open, like the rest of the UI's data); `GET /api/v1/portal/logo` serves the file with an ETag, `nosniff` and the icons' policy (`default-src 'none'; sandbox`, D-47); a declared logo that fails the checks falls back to Portarr's own and `logoProblem` says why. Writes are catalog writes (D-83, D-85): `PUT /api/v1/portal` (`name` and/or `colors`; `version` optional since the identity is all it changes), `PUT /api/v1/portal/logo` (multipart upload, checked and written atomically to `branding/` next to the catalog, a previous file of that name kept as `.bak-<timestamp>`, audited as `upload-logo`, then `portal.logo` set through the editor) and `DELETE /api/v1/portal/logo`: catalog writable, API key, rate limit, 1 MiB. The editor edits the `portal` mapping like an item (section `portal`, id `portal`), at the text level, comments kept. MCP: `get_portal` (read; name, logo, version to send, the logos already in `branding/`) and `set_portal` (write, so it waits for the operator's approval in the chat) change the name and the two colors and choose a logo among the files already in `branding/`; an image is uploaded by the operator in the UI. UI: a badge at the bottom of the menu, with no card or border around it (name and `v<version>` in the configured colors, the image alone clipped to rounded corners, the logo with the name as alt text, version refreshed with the system status query). The sidebar is a full-height column pinned under a pinned header: its menu scrolls on its own and the badge stays at the bottom of the visible window while the page content scrolls; under 768 px the menu is a bar ending with the logo only. An Identity card in System, Status (name, colors, logo, API key typed per save and never stored; read-only with a hint when writes are off). |
| ✅ D-92 | **Local accounts with a cookie session; login required** (step 1 of C2, pulled forward as V7, [ADR 0009](adr/0009-local-accounts-and-permission-based-roles.md)). A `Users` table (SQLite and PostgreSQL migrations): user name (unique, case-insensitive), display name, password hash (`PasswordHasher<T>`, PBKDF2, from `Microsoft.Extensions.Identity.Core`, not full Identity), role, avatar file, created, last sign-in, disabled, failed attempts, lockout end, a security stamp, and nullable `ExternalIssuer`/`ExternalSubject` for OIDC (D-99). Forms login, *arr style: `POST /api/v1/auth/login` sets the `portarr_session` cookie (HttpOnly, `SameSite=Strict`, Secure whenever the request is HTTPS, sliding expiry `PORTARR__AUTH__SESSIONDAYS`, 7 by default; a browser-session cookie unless "keep me signed in"). The cookie names the account only: the role and the permissions are read at every request, so an edit applies at once; a password change or a disable ends the sessions (security stamp). **Sign-in failures are one generic answer** (unknown user, wrong password, disabled and locked look the same, and each costs one hash check); 5 failures lock the account for 5 minutes (`PORTARR__AUTH__MAXFAILEDATTEMPTS`, `LOCKOUTMINUTES`); 10 sign-in attempts per minute per client address (429); failures are logged and audited without the password. **First run** (F-SET-07, partial): while no user exists every page shows a setup screen; `POST /api/v1/auth/setup` creates the first Admin through one `INSERT … WHERE NOT EXISTS` inside a transaction (the table locked on PostgreSQL), so racing requests cannot both win, and answers 409 forever after. Optional unattended first run: `PORTARR__AUTH__ADMINUSER` with `PORTARR__AUTH__ADMINPASSWORDFILE` (a file, never an environment variable), read once at startup while the table is empty. Passwords are 10 to 128 characters. | The portal is about to show real infrastructure to people other than its operator; everything later (roles, scoped keys, per-user chat) checks against an identity. Constant-cost generic failures and an atomic first run close the usual account-takeover paths of a self-hosted app. |
| ✅ D-93 | **Roles are named sets of permissions.** The permissions are a closed list defined in code, one authorization policy each: `apps.view`, `infrastructure.view`, `data.view`, `maintenance.view`, `assistant.view` (the MCP page), `chat.use`, `catalog.write` (includes approving chat writes), `system.view`, `settings.manage`, `users.manage`, and the reserved `secrets.view` (held by Admin only, nothing uses it yet). A permission is added with the feature it protects, never before. Built-in roles, seeded, never deleted, their sets **defined in code** (a new permission reaches the right ones with the code that adds it) and not editable: **Admin** (all), **Operator** (all but `users.manage`, `settings.manage`, `secrets.view`), **Observer** (the view permissions: no write, no chat), **Guest** (`apps.view`). **Custom roles** are created, renamed, given other permissions (a non-empty subset of the assignable ones) and deleted when no user has them; "Duplicate" starts one from a built-in role. Guarantees, server side: at least one enabled Admin always remains (demoting, disabling or deleting the last one is a 409), a user cannot change their own role nor disable or delete themselves (403). Admins and role editors are the only ones who see the roles (`users.manage`). | The maintainer wants several "styles of view" and says RBAC must not be an afterthought: a role is exactly the set of pages and actions it allows, so the sidebar, the routes and the API all derive from the same list. Fixed built-ins keep "Observer" meaning one thing across installations. |
| ✅ D-94 | **Every endpoint declares the permission it needs; the rest requires a sign-in.** A fallback policy requires an authenticated principal everywhere, so an endpoint is protected unless it says otherwise; each controller or action names its permission (`[Authorize(Policy = "apps.view")]`). Anonymous: `/api/v1/auth/status`, `setup`, `login`, `logout`, `GET /api/v1/portal/logo` (the login page shows it), `/health/*`, `/openapi`, `/docs`, the UI's own files and unknown paths under `/.well-known` (404). `/metrics` needs `system.view` (a scraper presents an API key). An unauthenticated API call is a **401, never a redirect**; the SPA shows the login page. Mapping: apps, stacks, icons, catalog status → `apps.view`; environments, workloads (logs included) and infrastructure → `infrastructure.view`; data → `data.view`; maintenance (refresh included) → `maintenance.view`; the Assistant page and the catalog audit → `assistant.view`; the chat → `chat.use`; catalog items and every catalog write (and the portal name and logo) → `catalog.write` (plus the catalog's own `WRITABLE` switch); system status and the log level's read → `system.view`; changing the log level → `settings.manage`; users, roles and their activity → `users.manage`; the portal identity's read, the avatar and the account endpoints → any signed-in user. **CSRF**: a request that changes something (`/api`, any method but GET, HEAD, OPTIONS, TRACE) authenticated by the cookie, and the sign-in endpoints, must carry `X-Requested-With: portarr`, on top of `SameSite=Strict`; requests with an API key are exempt. Account changes and API-key management accept a browser session only (not a key). | Deny by default; one source of truth (the permission) for the menu, the routes and the API; a header a cross-site form cannot send is a cheap, robust second line behind `SameSite`. |
| ✅ D-95 | **The user menu and the profile.** A round button at the top right of the header, like a Google account: the uploaded picture clipped to a circle, or the initials on a theme color derived from the user name. It opens a menu with the display name, user name and role, **Profile**, **API keys** and **Log out**. The Account page (Profile, API keys tabs) lets a user change their display name, **password** (the current one required; other sessions end), and **avatar**: png, webp or jpeg of at most 1 MiB, checked by signature (an SVG is refused: it is user-supplied and shown to others), stored in the data folder under a name made of the user and the image's hash, served at `/api/v1/avatars/{id}?v=<hash>` with `nosniff`, the icons' policy (`default-src 'none'; sandbox`), an ETag and a one-year cache once the hash is in the URL (the same serving as the portal logo, D-91). The sidebar, the routes and the page actions show what the role allows; a hidden route redirects to the first allowed page, and the API still enforces everything. **Settings, Users & Auth** (`users.manage`; one page with tabs while the sidebar has no second level): the users (create, edit the display name and role, reset a password, disable, delete), the roles (permissions as checkboxes grouped by area) and the activity (D-96). | The maintainer's own words: "un petit rond avec une image qu'on pourra setter pour l'utilisateur". |
| ✅ D-96 | **Audit of accounts, and the user in the catalog audit.** A database table keeps the latest 5000 events (the Activity tab shows 200): sign-in success and failure (with the reason: unknown user, wrong password, disabled, locked), first setup, logout, user create, role change, disable, enable, delete, password reset and change, profile and avatar changes, role create, update and delete, API key create and revoke. Text is cut and stripped of control characters (a user name typed on the login page is untrusted); **never a password, a key or a token**. The catalog audit lines (D-85) gain the user (`system` for the deprecated environment keys). | Who did what, and who tried to get in, is the first question after an incident. |
| ✅ D-97 | **Per-user API keys; the shared keys are deprecated bootstrap keys.** A user creates named keys (Account, API keys; at most 20 active): `ptr_` plus 256 random bits, **shown once**, stored as SHA-256, with a prefix to recognize them, last used (kept to the 5 minutes), an optional expiry (within 5 years) and revoke. A key is **read-only** (the view permissions of the owner's role) or **full** (the owner's permissions); the permissions are those of the role *as it is now*, so a key never exceeds its owner, and a **disabled owner's keys stop working**. Accepted as `X-Api-Key` or `Authorization: Bearer` on `/api/v1` and `/mcp` (a request with a key never uses the cookie). `PORTARR__APIKEY` and `PORTARR__MCP__APIKEY` keep working as the built-in **`system`** principal with Admin permissions (the sandbox and the agent runner need them): compared by SHA-256 in constant time, optional (32 characters at least when set), a warning logged at startup for each one set, documented as deprecated. | Accounts replace the shared secrets over time without breaking the automation that exists. |
| ✅ D-98 | **The chat and the MCP tools are per user.** `chat.use` opens the chat; a conversation belongs to the user who created it (ownership kept in Portarr's database, the runner stays unaware): list, read, send, cancel, events and delete are the owner's (another user's conversation is a 404), user managers (`users.manage`) also **list** every conversation with its owner and look after those without owner (made before accounts, or whose owner was deleted). **Approving a change needs `catalog.write`** (rejecting does not). With each message Portarr mints a **token** tied to the user and the conversation (`ptc_`, 256 random bits, stored as SHA-256 in the database, 30 minutes from its last use or message, 12 hours at most) and passes it as `mcpToken`; the runner writes it, instead of the shared MCP key, into the process's MCP configuration (a process already running keeps its own; without a token the configured key applies, so an older Portarr or a non-user caller still works). The token opens **`/mcp` only**, never the REST API, and its permissions are the user's as they are at each call. The MCP tools run as the caller: each read tool needs the permission of the page it mirrors (`get_overview` and the knowledge base: all four view permissions), every catalog tool needs `catalog.write`; the audit's `via` is taken from the token, not from the header. | D-89 said "until C2, whoever can open Portarr can use the chat". An agent acting for a person must never hold more than that person. |
| ✅ D-99 | **OIDC is step 2 of the same vertical, planned, not built.** Generic OIDC (Keycloak, Authentik…): authorization-code flow with PKCE, an issuer, client id and secret, claim or group → role mapping, optionally "trusted header" mode behind an SSO proxy; an identity provider's user maps to a `Users` row by (`ExternalIssuer`, `ExternalSubject`), columns that exist (nullable, uniquely indexed) so adding it needs no migration of data. The principal is already built from an identity (claims) whatever proves it, so a new authentication scheme only has to produce an `AccountIdentity`. | Most homelabs already run an SSO; designing the model now avoids reworking users later. |
| ✅ D-100 | **An optional, read-only Portarr agent is the simple way to connect a machine** ([ADR 0010](adr/0010-optional-read-only-agent.md); resolves Q-20, amends D-38 and D-72). One `portarr-agent` container (.NET, chiseled, non-root) runs on a machine and Portarr reads it through one HTTP endpoint with one bearer token (32 characters or more, compared by hash in constant time, never logged or returned): `GET /v1/info` (version, host name, capabilities), `/v1/containers` (state, health, Compose project, labels, usage) with logs (tail, follow), `/v1/host` and `/v1/host/stream` (SSE) carrying raw `/proc` and `/sys` counters (per-core CPU times, memory, swap, load, I/O wait, temperature when a sensor exists), which Portarr turns into rates as it does for node_exporter (D-72, D-73). **Read-only by construction**: the agent only issues `GET` requests to the Docker Engine API, has no write path, no shell and no exec, and never returns container environment variables. It needs the Docker socket, which is root-equivalent on the host: the documentation says so, and the agent can be pointed at a socket proxy instead. Pull only in the first version; an agent that connects out (NAT, remote VPS) is a later step. **Amended by D-102:** the component is named the reader, it speaks the two protocols Portarr already reads, and it is compiled ahead of time. The direct connectors (socket proxy, node_exporter, Prometheus) stay for those who already run them. | Connecting a machine took a different recipe per kind of environment (socket proxy, node_exporter, token and CA, backup account): three installs and two open ports for a plain Docker host. The maintainer's reference is Portainer: one agent, one address. Portarr keeps its principle (it informs, the operator acts, D-16 and D-31) by making the agent unable to write. |
| ✅ D-101 | **The catalog declares an agent with one key; Kubernetes needs none.** `environments[].agent: { url, tokenEnv }` replaces `docker:` and `hostMetrics:` on that machine (the token is read from the named variable, like every credential). A machine that also declares `docker:` or `hostMetrics:` keeps using them: the agent is one more source behind `IWorkloadSource` and `IHostMetricsSource`, in a new connector assembly `Portarr.Providers.Agent` (D-35) that must pass both capability contract suites (rule 8); Core learns nothing about it. Kubernetes keeps its API as the single source for every node and gets two changes to be as simple: **in-cluster authentication** (Portarr running inside the cluster uses the mounted ServiceAccount token and the in-cluster API address, so `kubernetes:` needs no URL, token variable or CA) and **one documented manifest** (namespace, ServiceAccount, read-only RBAC, optional node metrics). An "Add environment" screen shows the generated token, the one `docker run` command and the YAML to add; the catalog stays the source of truth (D-44). **Amended by D-102:** no `Portarr.Providers.Agent` connector; `reader:` is read as `docker:` and `hostMetrics:` with a token, the key is `reader`, not `agent`, and the token variable must start with `PORTARR_READER_`. | One shape for "add a machine" whatever the machine, and no edit of Core for each new connector (the architecture review's first finding on the infrastructure vertical). |
| ✅ D-102 | **The reader speaks the two protocols Portarr already reads, in the `PORTARR_READER_` token namespace, compiled ahead of time** ([ADR 0011](adr/0011-reader-speaking-the-existing-protocols.md); amends D-100 and D-101). The machine-side component is named **reader** (`portarr-reader`; the chat runner is already `portarr-agent`, D-87). One bearer token (32 characters or more, compared by hash in constant time, never logged or returned) guards every route but `GET /healthz`: `GET /v1/host` is the node_exporter text format limited to the families Portarr uses (per-cpu seconds by mode, `MemTotal`, `MemFree`, `MemAvailable`, `Buffers`, `Cached`, `SReclaimable`, `SwapTotal`, `SwapFree`, `node_load1`, the CPU temperature), raw counters turned into rates by Portarr (D-72, D-73); `GET /v1/docker/` is a filtered, read-only subset of the Docker Engine API (host capacity reduced to the CPU count and memory, the container list reduced to the fields Portarr reads, an inspect reduced to the id and the TTY flag, one-shot or two-reading usage as asked, logs with at most 5000 lines); `GET /v1/info` gives the version, host name and capabilities. Only GET is routed, the Docker paths and parameters are an allowlist, every Docker call has a time limit, and container environment variables are never forwarded. **The catalog key is `reader: { url, tokenEnv }`**, read as `docker:` (`<url>/v1/docker/`) and `hostMetrics:` (`<url>/v1/host`) with the token as a bearer: no new connector, the Docker and node_exporter connectors only gain an optional `tokenEnv` (the token is read at each request, so a rotated one is used without a restart). `url` may carry a path (a reader behind a reverse proxy); `reader:` excludes `docker:` and `hostMetrics:` on the same environment; an invalid reader drops its environment with one error. **Every `tokenEnv` (on `reader:`, `docker:`, `hostMetrics:`) must start with `PORTARR_READER_`**: the value of the variable is sent to the entry's URL, and the catalog can be edited through the API. A declared token whose variable is empty is `token-missing` for the Docker half and the host metrics half alike (`IHostMetricsSource.GetConfigurationError`). The reader is **compiled ahead of time** in this first slice (native AOT, one binary, a chiseled `runtime-deps` image, CI builds the image and runs a smoke test on the native binary); it still keeps about 70 to 90 MB of its own memory, so the next slice rewrites it in Rust behind the same contract (the black-box tests any implementation must pass). | A normal .NET build cost 76 MB per machine and AOT did not bring it down enough; the protocols Portarr already reads made a new connector unnecessary, kept the values and the cadence, and make the reader replaceable by a smaller implementation. The token namespace closes a secret-exfiltration path through the editable catalog, and one reason code for one cause keeps the operator from looking for a mismatch that does not exist. |
| ✅ D-64 | **The apps the operator makes are code repositories, scanned from the catalog's `sources: github:`** (owners, `pushedWithinMonths`, `include`, `exclude`, forks and archived switches; token from the variable `tokenEnv` names). Per repository: Dependabot, code scanning (by security severity, quality rules apart) and secret scanning alerts, each **on** (counts), **off** (GitHub says disabled or never set up: itself a finding) or **unknown** (the token cannot read it, with the reason); latest release or tag; Dependabot pull requests waiting; last push-triggered CI run of the default branch. GET only, every 30 min, ETag revalidation, a 250-request reserve. Production uses a fine-grained read-only token. `ICodeRepositorySource` is the seam (GitLab, Gitea later). | Half of the fleet is code that never runs here (desktop apps, tools): the maintainer asked to see it with the deployed half. Security scanning that is off hides vulnerabilities: saying "0 alerts" there would be a lie. |
| ✅ D-65 | **A repository is linked to the fleet** by `repository: owner/name` on a catalog app, by a running image's OCI label `org.opencontainers.image.source`, or by a GHCR / Docker Hub image named like a scanned repository; linked repositories are "running in the fleet", the others "not deployed here", both first-class. | Explicit when the operator knows, automatic when the image says it; no repository is hidden because it is not deployed. |
| ✅ D-66 | **Image updates are read anonymously from the registries** (Docker Hub web API; any OCI registry through its anonymous token flow, HTTPS realms only), each image repository every 6 h, in memory. A tag is compared only with tags of the **same shape** (prefix, count of numbers, suffix with digits masked): major / minor / patch from the first differing number, build when only the suffix changes. A floating tag is compared by running digest (Kubernetes `imageID`), else by its OCI version label only when that label is a tag of the repository, else "cannot tell". Images started from an id, private or local are **unknown, with the reason**. `IImageRegistry` is the seam. | Best effort and honest: a wrong "update available" costs more trust than "cannot tell". Same-shape comparison avoids the classic `16-alpine` → `latest` / `18-rc1` false positives; Docker Hub's quotas are respected. |
| ✅ D-67 | **One Maintenance page, after Data**: summary tiles for both halves, a "needs attention" list mixing them (one line per kind of finding, worst first, naming its subjects as links), then two sub-tabs, *My code* (repositories, running in the fleet first) and *Deployed images* (identical running images merged, worst first). Findings are not app statuses. | The operator's question is one ("what is vulnerable, outdated or late?"); the attention list answers it, the tabs are the drill-down. |
| ✅ D-68 | **An MCP server inside Portarr, read-only tools only** (resolves Q-32, F-API-03): the official C# SDK (`ModelContextProtocol.AspNetCore`), Streamable HTTP, stateless, at `/mcp`. Seven tools (`get_overview`, `list_apps`, `get_app`, `get_infrastructure`, `find_workload`, `get_data_coverage`, `get_maintenance`), all annotated read-only, idempotent and closed-world. They re-read the views the pages show (fleet, Infrastructure, Data, Maintenance) through the same Core services and API mappers: no logic of their own beyond picking and ordering, and the live window of a machine is told as now, lowest and highest point (a description of the chart, not a metric store, D-30). **No tool acts** (D-31): the "if authorized, act" of F-API-03 is out of scope. | An assistant answers "what is down and why?" from Portarr's live view instead of guessing; acting stays with the operator. |
| ✅ D-69 | **The knowledge base is generated, never stored**: two Markdown MCP resources, `portarr://knowledge/infrastructure` (environments and machines with their hierarchy, VMs under their hypervisor; stacks and apps with URLs, roles and where they run; data assets and backup jobs; repositories and images) built from the live state on every read, and `portarr://knowledge/glossary` (statuses, roles, coverage, verdicts, reason codes). No vector store, no history. | One read gives an assistant the whole map; regenerating it costs milliseconds and can never be stale. |
| ✅ D-70 | **The MCP endpoint is off by default and keyed** (amended by D-97: per-user keys): `PORTARR__MCP__ENABLED=true` maps it; a key is required on every call (since V7 a per-user API key, the chat runner's token, or the deprecated `PORTARR__MCP__APIKEY` of at least 32 characters, which acts as the `system` principal, D-97). The key is sent as `X-Api-Key` or `Authorization: Bearer`, compared by SHA-256 hash in constant time, never returned or logged; 401 with `WWW-Authenticate: Bearer` otherwise. 120 requests per minute per client, counted before authentication. Answers carry no secret: URLs lose credentials, query and fragment; workload labels, environment variables, connector endpoints and token variable names are never exposed; errors are reason codes. Off, `/mcp` and `/.well-known` are plain 404s, never the UI. Scoped API keys replaced the shared key in V7 (D-97, D-98): each tool asks for the permission of the page it mirrors. | An assistant sees the whole map at once: the endpoint must be a deliberate choice and must not be open on the LAN like the pages are until accounts exist. |
| ✅ D-71 | **An Assistant page, its own sidebar entry after Maintenance**: the MCP server's state (on/off, endpoint, transport, authentication, tools and resources), how to connect Claude Code and Claude Desktop (key never shown, a placeholder), and the knowledge base rendered with collapsible sections. It moves under Settings or System when the sidebar gets a second level (C3). | Visible while the sidebar has one level; it is the operator's way to ask the four tabs' questions from an AI. |
| ✅ D-72 | **Host metrics come from node_exporter, read every second.** A machine declares its node_exporter in the catalog (`hostMetrics.nodeExporter` on a Docker environment; `kubernetes.nodeExporter` for the nodes: port at the InternalIP, or the API server's pod proxy to the exporter's pod on the node (`namespace`, `selector`), which needs `get` on `pods/proxy` in that namespace only, not the cluster-wide `nodes/proxy`; per-node `direct`, `api-server`, `off` or a URL). GET only, gzip, 3 s timeout, 8 M-character cap, only the needed families parsed. Load = 100 − idle over the delta of `node_cpu_seconds_total` (I/O wait included, like top and Unraid), I/O wait and per-core load apart; memory used = MemTotal − MemAvailable, swap used = SwapTotal − SwapFree. 10 minutes in memory per machine; a failing agent is retried with backoff (1 s to 30 s) and the machine keeps its connector's values with the reason. Host values win over container sums and metrics-server; `IHostMetricsSource` replaces the D-57 seam. node_exporter is a companion installed next to Portarr ([companions.md](companions.md)), never shipped inside it. **Amended by D-100 and D-102:** node_exporter is one source of host metrics; the Portarr reader is another, speaking the same format. | Container sums plus guest-reported VM load read 4 % on a hypervisor that Unraid showed at 30–38 %: the VMs as the host runs them, the host's processes and I/O wait were missing. node_exporter is the standard, read-only source of those counters; reading it is integration, not collection (D-03). |
| ✅ D-73 | **Live by push: server-sent events at 1 s.** `GET /api/v1/infrastructure/stream` sends one `host` event per machine and read (no history); the REST snapshot is the initial state and is polled every 5 s while the stream is open (consumers, VMs), every 2 s without it, its history carrying the 1 s points. EventSource reconnects by itself; a slow client loses reads, never delays the monitor; the stream counts with the log streams' limits. Charts redraw without animation, straight segments, unparsed data. | Unraid's dashboard publishes /proc/stat every second over websockets; the maintainer wanted the same reactivity. SSE is one-way, plain HTTP, already used for logs; no hub exists yet (SignalR comes with C3). |
| ✅ D-74 | **The host memory ring sums to the total.** Used (total − available) holds the containers or pods (their measured usage), then on a hypervisor the VMs' allocation as the guests report it and the host's own "system and apps"; when the allocations exceed what is left of used (a VM takes only the memory it touched), the two are one part, "VMs and system". Then reclaimable cache (available − free) and free. Buffers + Cached is not used: it counts tmpfs and shared memory. Swap is a separate bar, "No swap" when there is none. | Unraid's legend reads as a breakdown of the machine; parts that do not add up, or a VM allocation shown as resident when it is not, would mislead. |
| ✅ D-75 | **Maintenance is filtered from its own figures and refreshed on request.** Every figure of the summary and every line of "Needs attention" is a link that filters the page to the items it counts (`?tab=images&filter=behind`, in the URL so it is shareable and the back button works); the active filter is shown as a pill with a clear button. The page says when GitHub and the registries were last read and when GitHub is read next. `POST /api/v1/maintenance/refresh` rescans now (202, the scan runs in the background; 429 with `Retry-After` when one was accepted less than 60 s ago): it ignores the 30 min and 6 h cadences, never GitHub's rate-limit guard (a stop below 250 remaining requests, applied to every request by the connector) nor the 25 registry lookups per minute. A read of external APIs, so allowed by D-31. | The operator asked "what are these 22?" and "is this fresh?": a figure that cannot be clicked is a dead end, and a cadence that cannot be forced is a wait. The cooldown protects the shared GitHub quota from an impatient click. |
| ✅ D-76 | **The API documents itself: OpenAPI at `/openapi/v1.json`, a Scalar reference at `/docs`** (F-API-01). The document is generated by ASP.NET Core's built-in `Microsoft.AspNetCore.OpenApi` from the `/api/v1` controllers (summaries from XML doc comments, response types and status codes declared on the endpoints); health probes, `/metrics` and `/mcp` have their own contracts and are left out. The reference is `Scalar.AspNetCore`, whose script is served by Portarr itself (no CDN, no third-party request: default fonts, telemetry, AI agent and MCP buttons are off). **Exception to the global CSP**, for the `/docs` page only: its inline starter script carries a per-request nonce (`script-src 'self' 'nonce-…'`) and styles allow `'unsafe-inline'` because Scalar injects them at run time; everything else stays `'self'`. The global policy (no `'unsafe-inline'`) is untouched. | One documented, versioned contract for scripts and integrations, without a hosted service. Nonce rather than a hash or `'unsafe-inline'` for scripts: the page renders text from the OpenAPI document, so scripts stay locked down. |

### 2.1 *arr patterns adopted (D-08)

| Sonarr pattern | Use in Portarr |
|---|---|
| `ThingiProvider` (`ProviderFactory`, `ProviderDefinition`, `IProviderConfig`) | Configurable provider instances (§7) |
| `[FieldDefinition]` | Configuration forms generated on the UI side from C# classes |
| `HealthCheck` (`HealthCheckBase`, `[CheckOn]`, `EventDrivenHealthCheck`) | Health of Portarr itself (System → Health) and inspiration for app checks |
| Event aggregator (`IHandle<T>`) and command queue | Scheduled tasks, actions, Activity page |
| SignalR | Real time: logs, status changes |
| Versioned API with API key | Public API `/api/v1` |
| Core / Common / Host / Http / Api split and test projects | Solution structure (§12) |
| Built-in Backup / Restore (System → Backup) | Same (F-SYS-04) |

---

## 3. Users and use cases

### 3.1 Personas 🟡

| Persona | Description | What they expect |
|---|---|---|
| **The operator** (primary persona) | Single-handedly runs a homelab of several machines (hypervisor, VMs, soon a cluster). Developer or DevOps profile. | See everything at a glance, diagnose quickly, act without switching tools. |
| **The visitor / observer** (secondary) | Someone the operator **shows** their infrastructure to: recruiter, peer, curious friend. They have an account with view-only rights. | Watch the fleet live (health, metrics, topology) without being able to change anything, and without access to sensitive information ❓ Q-46. |
| **The launcher guest** (secondary) | Someone the operator shares a **selection of applications** with: they click, that's all. | A simple page with the links chosen for them. Low priority: most apps already have their own address. |
| **The recruiter on GitHub** | Discovers the project on GitHub. | A demo, a clear README, a readable architecture. |

> Gathered 2026-10-26: the main use is **a single operator maintaining their own environment**. The collaborative angle is secondary but must be planned for, notably to **show** one's system (live demo) thanks to several permission levels.

### 3.2 Key use cases 🟡

| # | Use case |
|---|---|
| UC-01 | I open Portarr and launch an application in one click, or via keyboard search. |
| UC-02 | I open Portarr in the morning and immediately see whether something is wrong, and what. |
| UC-03 | An application stops responding: I see which workload and environment it runs on, its state, its latest logs and its metrics, then I restart it. |
| UC-04 | I want to know the CPU and RAM usage of a host, a VM, a container, a pod or an entire cluster over the last few hours. |
| UC-05 | I want to know which images have an update available, and which IaC repo to change to apply it. |
| UC-06 | I want to know which images contain critical vulnerabilities. |
| UC-07 | I want to see recent logins (SSH, SSO), especially failed ones. |
| UC-08 | I get notified on my phone when something goes down or recovers. |
| UC-09 | I add a new service: it appears automatically (labels) or via a commit to the YAML catalog. |
| UC-10 | I migrate a stack from a VM to the cluster: the application in Portarr stays the same, only its workload changes. |
| UC-11 | A VM stopped without my asking: Portarr flags it and tells me the probable cause (e.g. the hypervisor was out of memory and killed it). |
| UC-12 | I see at a glance whether my hypervisors' RAM, CPU and disk are overloaded, including when the sum of resources allocated to VMs exceeds the physical capacity. |
| UC-13 | I see all my databases, their size, their growth, and how long before they run out of space. |
| UC-14 | For each important database or volume, I know when the last successful backup was taken, where it is stored (e.g. S3), and I am alerted if it is too old or has failed. |
| UC-15 | I receive an email or a notification when something is wrong, and optionally a periodic summary of the fleet's state. |
| UC-16 | I open a service's admin console in one click (e.g. the identity provider's console), not just its user interface. |

---

## 4. Domain model

> 🟡 This section is the backbone of the spec. Among other things, it addresses the principle "VM or cluster, the need is the same".

### 4.1 The entities

```
Environment ──1..n── Runtime ──1..n── Workload
                                         ▲
                                         │ n..n (via selectors)
Stack ──1..n── App ──────────────────────┘
  │             │
  └── Source    └── Check (1..n)
     (IaC repo)
```

| Entity | Definition | Examples |
|---|---|---|
| **Environment** | A physical or logical execution location. Can contain other environments. | `hypervisor-1` (hypervisor), `vm-prod` (VM hosted by `hypervisor-1`), `cluster-a` (Kubernetes cluster), `vps-1` |
| **Runtime** | An execution technology within an environment, exposed by a provider. | `docker@vm-prod`, `kubernetes@cluster-a`, `libvirt@hypervisor-1`, `systemd@vps-1` |
| **Workload** | A running unit. Discovered automatically by providers, never entered by hand. | Container `sonarr`, Deployment `grafana`, Pod `grafana-7f9c…`, VM `vm-prod`, systemd service `trading-worker` |
| **App** | An application in the fleet: name, icon, description, **type** (`web`, `desktop`, `service`), URL(s) if it has any. **Declared** (catalog or labels). Origin: `self-built` (developed by the operator) or `third-party`. | "Sonarr" (web, third-party), "Grafana" (web), "My trading app" (service, self-built), "A home-made Windows client" (desktop, self-built) |
| **Stack** | A coherent group of apps and workloads, deployed together. Also serves as a visual grouping. | "Media", "Monitoring", "Trading" |
| **Source** | The repositories linked to an app or a stack, of two kinds: **code** (the application's code, its dependencies, its releases) and **deployment** (the IaC that deploys it). | Code: `github.com/org/my-app`. Deployment: repo + path + tool (Terraform, Ansible, Helm, Compose) |
| **Version** | The version of an app in three places: **deployed** (what is running), **declared** (what the IaC says), **available** (latest release). | `deployed 1.4.2 · declared 1.4.2 · available 1.5.0` |
| **Check** | A health probe attached to an app, a workload or an environment. | HTTP 200 on `/ping`, open TCP port, Docker healthcheck, Kubernetes readiness |
| **Provider** | A configured connector instance (§7). | "Docker @ vm-prod", "Main Prometheus" |
| **Issue** | A detected problem, aggregated and deduplicated. | "Sonarr down for 12 min", "3 critical CVEs in `jellyfin:10.10`" |
| **Event** | A timestamped fact. | Status change, restart, login, user action |
| **Task / Command** | Work executed by Portarr, scheduled or manual. | Refresh the inventory, scan images, restart a container |

### 4.2 Linking App and Workload (reconciliation)

An App is declared; Workloads are discovered. The link is made through **selectors** 🟡:

```yaml
apps:
  - id: sonarr
    name: Sonarr
    stack: media
    urls:
      - { label: Public, href: "https://sonarr.homelab.example" }
      - { label: LAN, href: "http://10.0.0.10:8989", lanOnly: true }
    workloads:
      - runtime: docker@server-1
        match: { name: sonarr }               # container name
      - runtime: kubernetes@cluster-a
        match: { labels: { app.kubernetes.io/name: sonarr }, namespace: media }
```

Or, from the workload itself (label-based discovery):

```yaml
# docker-compose / Terraform
labels:
  portarr.enable: "true"
  portarr.app: sonarr
  portarr.stack: media
  portarr.url: https://sonarr.homelab.example
  portarr.icon: sonarr
```

A stack declares its members with a **role** 🟡:

```yaml
stacks:
  - id: media
    name: Media
    members:
      - { app: sonarr,      role: critical }   # if Sonarr goes down, the stack no longer works
      - { app: radarr,      role: critical }
      - { app: prowlarr,    role: critical }
      - { app: jellyfin,    role: critical }
      - { app: recyclarr,   role: optional }   # its failure degrades the stack without breaking it
      - { app: flaresolverr, role: optional }
```

**Viability rule**: the stack is `Down` as soon as a `critical` member is `Down` ("Media is not working: Sonarr is down"); it is `Degraded` if an `optional` member is failing. The Overview and notifications talk about the stack **and** name the faulty member.

Rules:
- An app can have **several workloads** (e.g. app + database, or a migration in progress between a VM and the cluster).
- A workload with no associated app is visible in the inventory, but not in the launcher.
- An app with no workload (external service, SaaS site) is a **plain link**, optionally with an HTTP check.

### 4.3 Environment hierarchy

An environment can host another one (hypervisor → VM → Docker runtime). This enables:
- **correlation**: if `hypervisor-1` goes down, the issues of the VMs and apps it hosts are attached to the root cause instead of producing 40 alerts;
- **both points of view on a VM**: the view *from the outside* (hypervisor: powered on, allocated vCPUs, usage as seen by the host) and the view *from the inside* (exporter in the VM: guest OS CPU, RAM, disk). ✅ D-11

### 4.4 The distributed cluster case 🟡

A Kubernetes cluster can have nodes **in different places**:

```
cluster-a (k3s)
├── node cp-1      → hosted by the VM vm-k3s, itself on hypervisor-1
├── node worker-1  → hosted by the same VM, or another one
└── node worker-2  → a VPS at a hosting provider, connected through a tunnel (WireGuard, Tailscale…)
```

Consequences for the model:
- a **node** is both a cluster member (Kubernetes view: Ready, pods, pressure) and a **host** (system view: CPU, RAM, disk, reachability). Portarr links the two, as for VMs (D-11);
- each node indicates **where it is hosted** (VM on a hypervisor, external VPS, physical machine);
- the **network link** between a remote node and the cluster (the tunnel) is a monitored object in its own right (F-HLT-10): if the tunnel goes down, the node becomes NotReady and its pods are evicted, and Portarr must point to the tunnel as the root cause;
- collection from the remote node preferably goes **through the cluster** (exporter DaemonSet, Kubernetes API), which avoids exposing the VPS directly ❓ Q-20.

---

## 5. Status model

🟡 A single vocabulary for all entities:

| Status | Color | Meaning |
|---|---|---|
| `Healthy` | green | All checks pass |
| `Degraded` | orange | Partially working (a non-critical check fails, one replica out of two, high latency) |
| `Down` | red | A critical check fails |
| `Unknown` | gray | No data (provider unreachable, not checked yet) |
| `Maintenance` | blue | Declared maintenance: no alerts |
| `Paused` | light gray | Stopped on purpose (container deliberately stopped, planned VM shutdown) ❓ Q-12 |

**Aggregation** 🟡:
- App = worst status of its **critical** checks; a failing non-critical check yields `Degraded` at most.
- Stack = `Down` if a critical app is `Down`, `Degraded` if at least one app is not `Healthy`.
- Environment = status of its reachability and its resources (full disk → `Degraded`).
- **Anti-flapping**: a status change is only confirmed after N consecutive failures (configurable per check, 2 by default).

---

## 6. Architecture

### 6.1 Overview 🟡

```
 Browser ──HTTPS──► reverse proxy ──► ┌────────────────── Portarr (1 container) ─────────────────┐
                                         │  React frontend (static files)                            │
 API clients ──API key──────────────────►│  Http: REST /api/v1 + OpenAPI · SignalR /hub              │
                                         │  Core: catalog · status · issues · tasks · actions        │
                                         │  Providers (plugins) ────────────────────────────────────┼──► Docker API (socket-proxy)
                                         │  Database: SQLite | PostgreSQL                            │──► Kubernetes API
                                         └───────────────────────────────────────────────────────────┘──► Hypervisor API
                                              │ OIDC                                                ──► Prometheus / Loki
                                              ▼                                                     ──► Trivy · registries
                                         Identity provider                                          ──► ntfy / Telegram / webhook
```

### 6.2 Principles

1. **Pull rather than push**: Portarr polls the APIs at regular intervals, with a cache. No mandatory agent: the simple way to connect a machine is the optional read-only agent, named the reader (D-100, D-102); the direct connectors stay. ✅ Q-20
2. **Data persisted in the database**: configuration, catalog, cached inventory, status and check history, issues, events, audit log, scan cache. **No metrics or logs.**
3. **Real time**: SignalR pushes status changes, events and log streams.
4. **Everything goes through the API**: the UI has no privileged access. Anything the UI does can be scripted.
5. **Degraded mode**: a failing provider puts its entities into `Unknown` without blocking the rest.

### 6.3 Solution components 🟡

| Project | Role |
|---|---|
| `Portarr.Common` | Cross-cutting utilities |
| `Portarr.Core` | Domain, services, provider framework, tasks, statuses |
| `Portarr.Providers.*` | One assembly per provider family (Docker, Kubernetes, Prometheus…) ❓ Q-21 |
| `Portarr.Http` | Authentication, middlewares, SignalR |
| `Portarr.Api.V1` | REST controllers and resources |
| `Portarr.Host` | Entry point, configuration, DI |
| `frontend/` | React application |
| `*.Test` | Unit and integration tests (Testcontainers) |

### 6.4 The ecosystem: what plugs in around Portarr 🟡

Portarr is a **hub**: it collects almost nothing by itself and relies on standard components (D-03, D-17). These components form three circles.

```
                       ┌─────────────────────────── Circle 2: shared backends ────────────────────────────┐
                       │  Prometheus (metrics)     Loki (logs)   Trivy (vulns)     Backup tool              │
                       │  Grafana (exploration)    GitHub (code)  endoflife.date    SMTP / notifications    │
                       └───────▲──────────────────────▲─────────────────────▲──────────────────────────────┘
                               │ remote write / scrape │ push logs            │ API
   Circle 1: collectors ───────┼───────────────────────┼──────────────────────┼─────────────────────────────
   per environment             │                       │                      │
   ┌───────────────┐  ┌────────┴──────┐  ┌─────────────┴──┐  ┌────────────────┴────────────────────────────┐
   │ Docker host   │  │ VM / machine  │  │ Hypervisor     │  │ Kubernetes cluster (all nodes, even remote)   │
   │ socket-proxy  │  │ node_exporter │  │ API (token)    │  │ Read-only ServiceAccount                      │
   │ cAdvisor      │  │ Alloy         │  │ node_exporter  │  │ metrics-server, kube-state-metrics            │
   │ node_exporter │  └───────────────┘  │ Alloy (kernel) │  │ node_exporter + Alloy as a DaemonSet          │
   │ Alloy         │                     └────────────────┘  └───────────────────────────────────────────────┘
   └───────────────┘
                               ▲ Docker API, hypervisor API, Kubernetes API (direct read)
                    ┌──────────┴───────────┐
   Circle 0:        │       PORTARR        │  SQLite/Postgres database · built-in probes (HTTP, TCP, ping, heartbeat)
   the core         │                      │  behind the reverse proxy · OIDC identity (or local accounts)
                    └──────────────────────┘
```

**Collector kit per environment type**

| Environment | To install | What it brings to Portarr |
|---|---|---|
| Docker host | **docker-socket-proxy** (read-only), **cAdvisor**, **node_exporter**, **Grafana Alloy** | Inventory, state, live logs, container and host metrics, historical logs |
| VM or machine without Docker | **node_exporter**, **Alloy** | OS metrics and logs (inside view, D-11) |
| Hypervisor | **API token** (Unraid, Proxmox), **node_exporter**, **Alloy** (kernel logs, to detect the OOM killer) | Outside view of VMs, capacity, shutdown causes |
| Kubernetes cluster | **ServiceAccount** + read-only RBAC, **metrics-server**, **kube-state-metrics**, **node_exporter** and **Alloy** as a DaemonSet | Pods, nodes (including remote ones via tunnel), metrics, logs |

**Operating levels: Portarr stays useful with very little**

| Level | What is connected | Available features |
|---|---|---|
| **0 · Launcher** | Nothing | Apps, links, stacks and environments described by hand (D-32) |
| **0+ · Runtime APIs** | Portarr + socket-proxy and/or Kubernetes API | Launcher, inventory, state, live logs, **real-time** metrics, HTTP/TCP/heartbeat checks, routes, email alerts |
| **1 · + metrics** | + Prometheus and exporters | History, capacity and overcommitment, disk forecasts, sparklines |
| **2 · + logs** | + Loki and Alloy | Log search, **shutdown causes** (OOM), SSH logins |
| **3 · + maintenance** | + Trivy, GitHub, registries, backup tool | Vulnerabilities, dependencies, versions, end of life, backups |

For each unavailable feature, the UI shows **which component to connect** to enable it ("Connect Prometheus to see history"), like System → Health in the *arr apps.

**The kit is not shipped by Portarr** (D-38): each connector's documentation lists what to install and how to connect it. Operators deploy these tools with their own IaC.

**Integrations in the other direction** 🟡:
- **Prometheus discovery**: Portarr exposes an *HTTP service discovery* endpoint (`/api/v1/prometheus/sd`). Prometheus then automatically scrapes everything Portarr knows about, without maintaining the target list by hand (F-API-06).
- **Alertmanager**: Portarr can receive alerts from an existing Alertmanager (webhook) and turn them into issues ❓ Q-57.

---

## 7. Providers and capabilities

### 7.0 v1 reference stack (D-17)

> For each capability, **a single** reference tool in v1, chosen among the most widely used in homelabs and enterprises. The other providers in §7.4 come later, once the reference works.

| Need | v1 reference tool | Status | Why this one |
|---|---|---|---|
| Docker containers | **Docker Engine API** via **docker-socket-proxy** (Tecnativa) | 🟡 | De facto standard; the proxy limits permissions |
| Kubernetes | **Kubernetes API** + **metrics-server** (included in k3s) | 🟡 | Native, no dependencies |
| Hypervisor | **Unraid** (used by the project) then **Proxmox VE** (the most widespread) | ❓ Q-51 | Dogfooding first, then the most common platform |
| Metrics | **Prometheus** + **node_exporter** + **cAdvisor** (+ **kube-state-metrics** in a cluster) | 🟡 | CNCF standard |
| Logs | **Loki** + **Grafana Alloy** (successor to Promtail) | 🟡 | Same ecosystem as Grafana, lightweight |
| Advanced dashboards | **Grafana** (deep links, no heavy integration) | 🟡 | Already indispensable |
| Image vulnerabilities | **Trivy** (server mode) | 🟡 | The most widely used open-source scanner |
| Code dependencies (vulnerabilities and updates) | **GitHub**: Dependabot + dependency graph | 🟡 | Already enabled on the repositories |
| Image versions | **Registry v2 API** (Docker Hub, GHCR) | 🟡 | OCI standard |
| Runtime end of life | **endoflife.date** | 🟡 | Public reference, free API |
| Identity | Generic **OIDC**, tested with **Keycloak** | 🟡 | Standard |
| Notifications | **Email (SMTP)** | 🟡 | Universal; other channels later |
| Backups | restic through Backrest (hosts, VMs) and K8up (Kubernetes), read-only (D-80, D-81); Q-50 | ✅ | One engine, two front-ends, local array + B2 |
| Reverse proxy (routes) | SWAG/nginx (config files) or Traefik (API) ❓ Q-54 | ❓ | SWAG: used by the project. Traefik: the most widespread, clean API |
| Code dependencies: prerequisites | GitHub token with the *Dependabot alerts: read* permission; *dependency graph* enabled on the repositories | 🟡 | Observed on a real fleet: without it, the API returns 403 / 404 |

### 7.1 Principle

A **provider** is a connector to an external system. As with Sonarr's Indexers, you can configure **zero, one or several of each type**. Each provider type **declares the capabilities** it offers. Features (§9) request a capability, never a specific provider.

This is what answers the question "how do we display the CPU of a VM or a cluster?": the *Metrics* feature requests the `Metrics` capability for an entity, and Portarr resolves which provider supplies it for that entity (§7.3).

### 7.2 Capabilities 🟡

| Capability | What it provides |
|---|---|
| `Inventory` | Discovery of environments and workloads, with their attributes |
| `State` | Current state of a workload (running, exited, restarting, ready 2/3…) |
| `Health` | Result of a probe (HTTP, TCP, native healthcheck) |
| `Logs.Live` | Real-time log stream |
| `Logs.Query` | Search in log history |
| `Metrics` | Time series (CPU, RAM, disk, network) |
| `Actions` | Operations (start, stop, restart, rollout, trigger a job) |
| `Updates` | Available versions for an image, a chart, a package |
| `Vulnerabilities` | Vulnerabilities (CVEs) of an image or a host |
| `AuthEvents` | Logins, failures, sessions |
| `Notify` | Sending notifications |
| `Identity` | Authentication of Portarr users |

### 7.3 Resolving a capability for an entity

For a given entity and capability, Portarr picks the source in this order 🟡:
1. a source **explicitly configured** on the entity (e.g. a custom PromQL query);
2. the **richest** provider that covers the entity (e.g. Prometheus rather than the Docker API for metrics, because it has history);
3. a native **fallback** source (e.g. the Docker `stats` API: real time only, no history).

The UI always shows **where the data comes from** (small "via Prometheus", "via Docker API" badge).

### 7.4 Provider type catalog 🟡

| Provider | Capabilities | Connection | Phase |
|---|---|---|---|
| **Docker** | Inventory, State, Health (native healthcheck), Logs.Live, Metrics (real time, `stats`), Actions | Socket via **docker-socket-proxy** (TCP), or read-only local socket, or TCP + TLS | v0.2 |
| **Kubernetes** | Inventory (nodes, namespaces, deployments, statefulsets, daemonsets, pods), State, Health (readiness/liveness), Logs.Live, Metrics (metrics-server, real time), Actions (rollout restart, scale) | kubeconfig or ServiceAccount (in-cluster) | v0.3 |
| **Prometheus** (and compatibles: VictoriaMetrics, Mimir, Thanos) | Metrics (history), Health (state of `up` targets) | URL + optional auth | v0.4 |
| **Loki** | Logs.Query, Logs.Live (tail), AuthEvents (via LogQL queries) | URL + optional auth | v0.3 |
| **Elasticsearch / OpenSearch** | Logs.Query, AuthEvents, Metrics (if Metricbeat) | URL + API key | ❓ Q-59 |
| **Unraid** | Inventory (VMs, containers, array, disks), State, Metrics (real time), Actions (VM start/stop) | Unraid API (GraphQL) ❓ to be verified | v0.3 |
| **Proxmox VE** | Inventory (nodes, VMs, LXC), State, Metrics, Actions | REST API + token | Later |
| **libvirt** | Inventory (VMs), State, Actions | SSH or libvirt socket | Later |
| **HTTP / TCP / DNS / Ping** (built-in probes) | Health | Directly from Portarr | v0.2 |
| **OCI registries** (Docker Hub, GHCR, private registry) | Updates | Registry v2 API + credentials | v0.5 |
| **Trivy** (server mode) | Vulnerabilities | Trivy server URL | v0.6 |
| **Keycloak** | AuthEvents (login events), Identity | Admin API + OIDC client | v0.6 |
| **Generic OIDC** | Identity | Discovery URL, client id/secret | v0.1 |
| ***arr** (Sonarr, Radarr, Prowlarr…) | Health (their own System → Health alerts), widgets (queue) | URL + API key | ❓ Q-31 |
| **Uptime Kuma** | Health (import of existing monitors) | API / metrics | ❓ |
| **Jobs**: webhook, GitHub Actions (`workflow_dispatch`), Semaphore / AWX (Ansible) | Actions (trigger an IaC job) | URL + token | v0.7 |
| **Notifications**: email (SMTP), ntfy, Telegram, Discord, Gotify, webhook | Notify | Depends on the service | v0.3 (email), then the others |
| **GitHub** (and later GitLab, Gitea) | Vulnerabilities (Dependabot alerts, code scanning), Updates (releases, tags), published versions, CI/CD pipeline status, images published to GHCR | Token (fine-grained, read-only) or GitHub App | v0.5 |
| **endoflife.date** | End of life of runtimes, frameworks and OSes (PHP, .NET, Node.js, Python, PostgreSQL, Debian, Kubernetes…) | Public API, no key | v0.5 |
| **Tunnels** (WireGuard, Tailscale, Netbird…) | Health: link state, last handshake, latency | Prometheus metrics from the tunnel tool, or a probe from the cluster ❓ Q-49 | v0.4 |
| **Heartbeat** (built-in probes, *push* mode) | Health: a job (backup, cron) calls a Portarr URL when it finishes; the absence of a call within the expected window triggers an alert | Unique URL per heartbeat | v0.3 |
| **S3 and compatibles** (AWS, MinIO, Backblaze B2, Wasabi…) | Backups: list of objects by prefix, date and size of the latest backup, total volume | Endpoint, bucket, read-only access keys | v0.4 |
| **Databases** (PostgreSQL, MySQL/MariaDB, Redis, MongoDB…) | Inventory (databases), Metrics (size, growth, connections) | Prometheus + exporters preferably; read-only direct connection as a fallback ❓ Q-42 | v0.4 |
| **Backup tools** (restic, Kopia, pgBackRest, Velero…) | Backups: snapshots, status of the last job | API, metrics or reading the repository ❓ Q-41 | Later |

### 7.5 Configuring a provider

As in the *arr apps:
- the form is **generated from the C# class** (`[FieldDefinition]`);
- a **Test** button before saving;
- secrets are **encrypted in the database** and never returned in clear text by the API;
- each provider has its own **health status**, visible in System → Health ("Prometheus provider unreachable for 5 min").

---

## 8. Navigation and screens

### 8.1 Menu structure (inspired by the *arr apps) 🟡

| Menu | *arr equivalent | Sub-pages | Content |
|---|---|---|---|
| **Overview** | — (Portarr-specific) | — | Home page 🟡 ❓ Q-43: global view of the fleet, at several levels (E-00) |
| **Apps** | Series / Movies | All · by Stack · Favorites | The launcher: application tiles, with their status |
| **Infrastructure** | — (Portarr-specific) | Environments · Workloads · Capacity · Map | Technical inventory: hosts, VMs, clusters, containers, pods; capacity |
| **Data** | — (Portarr-specific) | Databases · Storage · Backups | Databases, disk space, backups (D-12) |
| **Activity** | Activity | Queue · History | Running tasks and actions, then the event history |
| **Issues** | Wanted | Active · Snoozed · Resolved | Everything that is wrong, prioritized |
| **Maintenance** | Calendar | Versions · Updates · End of life · Deployments · Ignored | Deployed / declared / available versions, updates, end of life, deployment history 🟡 (formerly "Updates", broadened by D-14) |
| **Security** | — | Vulnerabilities · Logins | Vulnerabilities (images **and** code dependencies) and logins |
| **Settings** | Settings | General · Catalog · Providers · Checks · Notifications · Users & Auth · UI | Configuration |
| **System** | System | Status · Health · Tasks · Backup · Updates · Events · Logs | Health and maintenance of Portarr itself |

Persistent header: global search (Ctrl+K), global health indicator, issue counter, user menu.

### 8.2 Main screens (description for mockups) 🟡

**E-00 · Overview (proposed home page)**
- Goal: in 5 seconds, know whether everything is fine and, if not, *where* and *at what level*.
- **Verdict banner**: "All good" or "2 critical problems, 3 warnings", with a link to Issues.
- **Layered view**, from top to bottom, each level with its counters (✓ / ⚠ / ✗):
  1. **Hypervisors and clusters**: CPU, RAM and disk pressure (gauges), allocated vs physical RAM.
  2. **VMs and nodes**: powered on / expected, recent unexpected shutdowns.
  3. **Workloads**: running / failing / crash-looping.
  4. **Apps and stacks**: aggregated status per stack ("Media: 9/10").
  5. **Data**: databases (space left), oldest latest backup, failed backups.
- **Recent events feed**: shutdowns, restarts, recoveries, alerts sent.
- **Quick access**: favorite apps (compact launcher) and Ctrl+K search.

**E-01 · Apps (the launcher)**
- Summary banner: "42 apps · 40 ✓ · 1 ⚠ · 1 ✗", active issues, last refresh.
- Tiles grouped by stack, in collapsible sections or in tabs ❓ Q-05. Each tile: icon, name, status indicator, short description, quick actions on hover (open, logs, details).
- *arr view modes: **Posters** (tiles), **Table** (dense list), **Overview** (tile + mini-metrics).
- *arr toolbar: filter (status, stack, environment, tag), sort, view options.

**E-02 · App detail**
- Header: icon, name, status, URLs, stack, tags, link to the IaC source.
- Tabs: **Overview** (checks and their history, associated workloads with their environment, CPU/RAM mini-charts) · **Logs** (live, merged workloads) · **Metrics** · **Updates** · **Security** · **Events**.
- Actions: restart, maintenance, edit (if the app comes from the UI).

**E-03 · Infrastructure → Environments**
- Tree or cards: hypervisor → VM → runtimes; cluster → nodes.
- Per environment: status, CPU/RAM/disk (gauges + sparkline), number of workloads, uptime.

**E-04 · Infrastructure → Machines** (D-54, replaces the former Workloads table)
- Unraid-style dashboard: one card per machine (Docker host, Kubernetes node) with a live CPU chart (10 min, in memory, refreshed every 2 s), load bar, memory ring and the top running consumers as bars, side by side; a hypervisor followed by its VMs, with their allocation and guest load (D-59). No status table.
- With a node_exporter (D-72 to D-74): host values every second, pushed over SSE: load and I/O wait, per-core bars, load average, CPU temperature, memory ring that sums to the total, swap bar.
- Workload detail (reached from a consumer or an app): inspect (ports, volumes, masked environment variables, labels), logs, metrics, actions.

**E-05 · Issues**
- Prioritized list: severity, entity, description, since when, source. Actions: open the entity, snooze, acknowledge.
- Grouping by root cause ("hypervisor-1 unreachable → 12 related issues").

**E-06 · Maintenance** (first slice: [verticals/V4-maintenance.md](verticals/V4-maintenance.md), D-64 to D-67)
- **Versions**: one row per app: deployed version · declared in the IaC · latest available, with a gap indicator (up to date, behind by patch/minor/major, **drift** if deployed ≠ declared). Self-built / third-party filter.
- **Updates**: app/workload, current version → available, type (patch/minor/major), date, release notes, IaC source to change.
- **End of life**: runtimes and frameworks at or near end of life ("PHP 5.6: out of support since 2018", ".NET 6: out of support"), per app.
- **Deployments**: chronological feed of detected version changes ("app-x 1.4.2 → 1.5.0, 2 h ago, via CI pipeline").
- Timeline view (Calendar-style) of new versions and end-of-life deadlines.

**E-07 · Security**
- Vulnerabilities: per image, Critical/High/Medium/Low counters, CVE details with or without an available fix.
- Logins: login feed (SSH, SSO), failures highlighted, source IP, geolocation ❓.

**E-08 · Logs (full-screen view)**
- Live stream, pause/resume, search, level filter, multi-selection of workloads (a stack's logs merged and color-coded), timestamps, download.

**E-09 · Settings → Providers**
- Grid of cards by type, "+" button, modal generated from `[FieldDefinition]`, Test button. Identical to the *arr Indexers.

**E-10 · System → Status / Health / Tasks**
- Taken from the *arr apps: version, uptime, database; internal checks; scheduled tasks with interval, last and next run, "run now" button.

### 8.3 Usability 🟡

- Dark theme by default, light theme available (like the *arr apps).
- Responsive: usable on a phone, at least Apps, Issues and app detail ❓ Q-06.
- Keyboard shortcuts: Ctrl+K (search), `g a` (Apps), `g i` (Issues)…
- **Icons**: each app has **its real icon**, resolved automatically from a community set (*dashboard-icons*) based on the image or the name, and editable (pick from the set or upload). Fallback: a letter on a colored background.
- **Art direction** (feedback on the v1 mockups, deemed too "Bootstrap"): a bold, playful, "cartoon" visual identity. Thick cards with very rounded corners and a strong drop shadow, rounded fonts (Fredoka-style headings), vivid colors on a midnight-blue background, a pirate wink (*arr* → "Ahoy", porthole logo). Resource visualizations must be at least as rich as those of a hypervisor UI (CPU per core, memory broken down by consumer, disks). To be validated on the v2 mockups.

---

## 9. Feature catalog

> Format of each feature: **Need** → **Display** → **Sources** (per environment type) → **Rules** → **Phase** → **Questions**.
> The "Sources" table answers "how do we obtain and display this data depending on whether it is a host, a VM, a container or a cluster".

### 9.1 F-CAT — Catalog and launcher

**F-CAT-01 · Application tiles** 🟡
- **Need**: access any app in one click.
- **Display**: E-01. Click = open the main URL (same tab or new tab, configurable).
- **Rules**: several URLs per app (Public, LAN, Admin…); default URL chosen according to the address Portarr is opened on (D-46).
- **Phase**: v0.1

**F-CAT-02 · Grouping by stack, groups and tags** 🟡
- **Need**: organize apps by theme (Media, Monitoring, Trading…).
- **Rules**: an app belongs to **one stack** (primary grouping) and has **0..n tags** (cross-cutting grouping: "admin", "family"). ❓ Q-05: stacks = tabs, sections, or pages?
- **Phase**: v0.1

**F-CAT-03 · Global search (Ctrl+K)** 🟡
- **Need**: find and open an app, a workload or a page without a mouse.
- **Display**: command palette; typed results (App, Workload, Environment, Action, Page).
- **Phase**: v0.1

**F-CAT-04 · Catalog sources** 🟡
- **Need**: declare apps in the most DevOps way possible.
- **Sources**:

| Source | Mechanism | Phase |
|---|---|---|
| YAML file | `catalog.yaml` mounted into the container, hot-reloaded, published JSON schema | v0.1 |
| Labels | `portarr.*` on Docker containers and Kubernetes objects | v0.2 |
| UI | Creating and editing apps and stacks from the UI (wizard for novices: pick a discovered workload, Portarr pre-fills name, icon, URL from the reverse proxy, check) | v0.2 (D-19) |
| Terraform | `portarr` Terraform provider (resources `portarr_app`, `portarr_stack`, `portarr_check`) that calls the API | v0.5 (F-API-05) |
| Git repository | Portarr clones a repo and reads the catalog (GitOps) | Later |
| IaC import | Ansible inventory, Terraform state | Later |

- **Rules**: in case of conflict, the priority order is to be defined ❓ Q-09. Each app shows its origin ("from catalog.yaml", "from labels", "created in the UI", "Terraform"). An app managed through configuration is **read-only** in the UI, so that a manual change is not overwritten by the next `apply`.

**F-CAT-05 · Favorites, ordering, customization** 🟡
- Pin apps, reorder (drag and drop), hide. Per user or global ❓ Q-03.
- **Phase**: v0.2

**F-CAT-06 · External links and bookmarks** 🟡
- Apps without a workload (external sites, documentation, cloud consoles) with an optional HTTP check.
- **Phase**: v0.1

**F-CAT-07 · Application types and non-launchable apps** 🟡 (D-15)
- **Need**: reference *all* the applications in the fleet, including those that cannot be opened in a browser.

| Type | In the launcher | What Portarr tracks |
|---|---|---|
| `web` | Clickable tile (main URL + secondary URLs: Admin, API, docs) | Health, workloads, metrics, versions, vulnerabilities, dependencies |
| `desktop` (thick client, Windows/macOS/Linux app, mobile) | Non-clickable tile, "Desktop" badge; links to the repository, the releases, the download page ❓ Q-47 | Dependencies (Dependabot), framework end of life, published versions vs installed version ❓ |
| `service` (bot, worker, headless API, job) | Absent from the launcher by default, visible in Apps → All | Health (heartbeat, workload), metrics, versions, vulnerabilities |

- **Rule**: technical backends (databases, caches, brokers) are **not** apps; they are workloads attached to the app they serve, and databases (F-DAT).
- **Phase**: v0.1 (types), v0.5 (dependency tracking)

### 9.2 F-HLT — Health

**F-HLT-01 · HTTP(S) check** 🟡
- **Need**: know whether an app responds.
- **Parameters**: URL, method, expected status codes, expected content (keyword or regex), timeout, interval, headers, TLS verification on/off, critical or not.
- **Display**: indicator on the tile; in the detail view, bar history (last 90 checks, Uptime Kuma-style), latency, uptime over 24 h / 7 d / 30 d.
- **Phase**: v0.2

**F-HLT-02 · TCP, DNS, ICMP checks** 🟡
- Open port, DNS resolution, ping (for hosts and network devices).
- **Phase**: v0.2

**F-HLT-03 · Native workload state** 🟡

| Type | Source | What we display |
|---|---|---|
| Docker container | `inspect`: `State.Status`, `State.Health.Status` (if `HEALTHCHECK` is defined), `RestartCount` | running / exited / restarting; healthy / unhealthy / starting; recent restarts |
| Kubernetes Deployment / StatefulSet | `status.readyReplicas` / `spec.replicas`, conditions | 2/3 ready → `Degraded`; 0/3 → `Down` |
| Kubernetes Pod | phase, `Ready` conditions, `restartCount`, reasons (`CrashLoopBackOff`, `OOMKilled`, `ImagePullBackOff`) | Readable state and reason |
| VM (Unraid, Proxmox, libvirt) | power state via the hypervisor API | running / stopped / paused |
| systemd service | `systemctl is-active` (via SSH or exporter) ❓ Q-22 | active / failed |

- **Rules**: a native `unhealthy` healthcheck is equivalent to a failing critical check.
- **Phase**: v0.2 (Docker), v0.3 (Kubernetes, VM)

**F-HLT-04 · Health of hosts and environments** 🟡

| Type | Source |
|---|---|
| Host / VM (inside view) | Prometheus `up{job="node"}` + thresholds on disk, RAM and load, or ping/TCP as a fallback |
| VM (hypervisor view) | Hypervisor API |
| Kubernetes cluster | API server reachability, `Ready` nodes, failing system pods |
| Hypervisor | Hypervisor API (array, disks, temperatures if available) |

- **Phase**: v0.2

**F-HLT-08 · Heartbeats (push-mode checks)** 🟡
- **Need**: monitor things that *do not answer requests*: backup jobs, cron, scripts, ETL. The "dead man's switch" principle (healthchecks.io-style).
- **Mechanism**: Portarr generates one URL per heartbeat. The job calls it on each run (`/start`, `/success`, `/fail`, with an optional log excerpt). No call within the period + grace delay → `Down`.
- **Display**: list of heartbeats, last run, duration, bar history; can be attached to an app, a stack or a database (F-BAK).
- **Phase**: v0.3

**F-HLT-09 · Unexpected shutdowns and probable cause** 🟡
- **Need**: no longer put up with VMs or containers that "go down at random" without explanation (UC-11).
- **Detection**: a workload goes from running to stopped/exited **without any action** recorded in Portarr and without declared maintenance → "unexpected shutdown" event.
- **Root-cause search**, cross-referencing the data available around the time of the shutdown:

| Clue | Source |
|---|---|
| Host OOM killer (`Out of memory: Killed process … qemu`) | Hypervisor kernel logs via Loki, or hypervisor API |
| Container `OOMKilled` / exit code 137 | Docker `inspect`, Kubernetes pod status |
| Host RAM close to 100% just before | Host metrics (Prometheus) |
| Host reboot | Host uptime |
| Full disk | Disk metrics |

- **Display**: in the event and the issue, "Probable cause: out of memory on hypervisor-1 (RAM at 98% at 03:12, OOM killer killed vm-app-2)", with the chart for the period.
- **Phase**: v0.4

**F-HLT-11 · Automatically suggested checks** 🟡
- **Observation on a real fleet**: the vast majority of containers have **no** Docker `HEALTHCHECK`. Their "running" state does not tell whether they respond.
- **Mechanism**: when an app is onboarded, Portarr suggests a default check: HTTP on the URL taken from the reverse proxy (F-NET-01), or on the exposed port, or a heartbeat for a job. The operator approves it in one click.
- **Display**: an "unmonitored" indicator on apps without a real check; a counter in the Overview ("12 apps without a check").
- **Phase**: v0.2

**F-HLT-10 · Network links and tunnels** 🟡
- **Need**: monitor the links the fleet depends on, in particular the tunnel connecting a remote node (VPS) to the cluster (§4.4).
- **Sources**: tunnel tool metrics (last WireGuard handshake, Tailscale peer state), ping/TCP from the cluster, node `Ready` state.
- **Display**: in Infrastructure → Map, the links between environments with their state and latency; issue "tunnel to worker-2 down for 4 min → node NotReady, 6 pods rescheduled".
- **Phase**: v0.4

**F-HLT-05 · Correlation and root cause** 🟡
- If a parent environment is `Down`, its children switch to `Unknown (parent down)` and do not generate separate issues.
- **Phase**: v0.3

**F-HLT-06 · Maintenance** 🟡
- Declare a maintenance window (immediate or scheduled) on an app, a stack or an environment: `Maintenance` status, alerts muted.
- **Phase**: v0.3

**F-HLT-07 · TLS certificates** 🟡
- Expiry date of the certificates of public URLs; issue 14 days before expiry (configurable threshold).
- **Phase**: v0.2

### 9.3 F-INV — Inventory

**F-INV-01 · Environment list** 🟡
- Tree hypervisor → VM → runtime; cluster → nodes. Status, resources, number of workloads. (E-03)
- **Phase**: v0.2

**F-INV-02 · Workload list** 🟡
- Table filterable by environment, type, status, stack, linked app or not. (E-04)
- **Phase**: v0.2

**F-INV-03 · Workload detail** 🟡

| Type | Information |
|---|---|
| Container | image and digest, command, ports, volumes, networks, environment variables (**values masked** by default), labels, restart policy, creation date |
| Pod / Deployment | namespace, containers, images, requests/limits, node, recent Kubernetes events, owner |
| VM | vCPUs, allocated RAM, disks, network, OS ❓ |

- **Phase**: v0.2

**F-INV-04 · Orphan workloads** 🟡
- Highlight workloads that run without an associated app, or that do not come from any known IaC source ("drift").
- **Phase**: v0.5 ❓ Q-27

**F-INV-06 · Inferred origin and dependencies** 🟡
- **Observation on a real fleet**: a container's origin can almost always be read from its labels, and its dependencies from its configuration.
- **Origin**: `com.docker.compose.project` / `working_dir` (Compose), `managed-by=terraform` + `project=<repo>` (Terraform, convention to be documented), `net.unraid.docker.managed` (Unraid), Helm labels and `app.kubernetes.io/*` (Kubernetes). No trace = "started by hand", flagged as such.
- **Dependencies**: a workload that references another workload's name in its configuration (environment variables, without ever reading or displaying secret values), or that shares a dedicated network with it, is suggested as a **dependency**. Portarr uses this to suggest stack members and to draw the map (Infrastructure → Map).
- **Signal**: a database that no workload references is flagged ("database with no known consumer").
- **Phase**: v0.2

**F-INV-05 · Abandoned workloads** 🟡
- **Observation on a real fleet**: dozens of containers have been stopped for months, even years, cluttering the views and the disk.
- **Mechanism**: list of workloads stopped for more than N days (30 by default), with their size (image + volumes) and the reclaimable space. They can be marked "archived" to remove them from the views. No deletion by Portarr (D-16).
- **Phase**: v0.3

### 9.4 F-LOG — Logs

**F-LOG-01 · Live logs** 🟡
- **Need**: see a container's or a pod's logs in real time, like `docker logs -f`.

| Type | Source |
|---|---|
| Docker container | Docker API `GET /containers/{id}/logs?follow=true` via socket-proxy |
| Kubernetes Pod | Kubernetes API `pods/log?follow=true` (container selection if there are several) |
| Host / VM / systemd service | Loki (`journal`, `/var/log/*`), if collected |
| Fallback | Loki for any workload whose logs are shipped there |

- **Display**: E-08, or the Logs tab. Browser transport via SignalR. The last 500 lines, then the stream ❓ Q-16.
- **Phase**: v0.3

**F-LOG-02 · Merged logs of a stack** 🟡
- Select several workloads (or an entire stack): merged stream, one color per source.
- **Phase**: v0.3

**F-LOG-03 · Historical search** 🟡
- Via Loki: time range, text, level. "Open in Grafana Explore" link with the query pre-filled.
- **Phase**: v0.4

### 9.5 F-MET — Metrics

**F-MET-01 · Normalized metrics** 🟡
- **Need**: see the CPU, RAM, disk and network of any entity, with the same chart regardless of its type.
- **Principle**: Portarr defines **canonical metrics** (`cpu.usage`, `memory.used`, `memory.limit`, `disk.used`, `disk.size`, `net.rx`, `net.tx`, `restarts`). Each provider knows how to translate them for each entity type.

| Entity | Preferred source (history) | Typical query | Fallback (real time only) |
|---|---|---|---|
| Physical host or VM (inside view) | Prometheus + node_exporter | `1 - avg(rate(node_cpu_seconds_total{mode="idle",instance=~"$host.*"}[5m]))` | — |
| VM (hypervisor view) | Hypervisor API, or Prometheus + libvirt/Proxmox exporter | `rate(libvirt_domain_info_cpu_time_seconds_total{domain="$vm"}[5m])` | Hypervisor API |
| Docker container | Prometheus + cAdvisor | `rate(container_cpu_usage_seconds_total{name="$container"}[5m])` | Docker `stats` API |
| Kubernetes Pod | Prometheus + cAdvisor (kubelet) | `sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="$ns",pod="$pod"}[5m]))` | metrics-server (`metrics.k8s.io`) |
| Kubernetes node | Prometheus + node_exporter | same as host | metrics-server |
| Kubernetes cluster (aggregated) | Prometheus | sum of the nodes; *usage* vs *requests* vs *allocatable* | metrics-server (sum) |
| Stack / App | Sum of the linked workloads | — | — |

- **Rules**:
  - queries are **overridable templates** per provider (label names vary from one installation to another);
  - CPU is expressed in **cores** and as a **% of capacity** (of the host, of the container limit, or of the cluster's allocatable) ❓ Q-15;
  - the source is always shown (§7.3).
- **Display**: sparklines on tiles (Overview mode) and lists; charts with 1 h / 6 h / 24 h / 7 d ranges in detail views; gauges for the current snapshot; "open in Grafana" button.
- **Phase**: v0.4 (Prometheus), v0.2 (real time via Docker `stats`)

**F-MET-02 · Capacity and overcommitment view** 🟡
- **Need**: understand whether the infrastructure is properly sized and anticipate saturation (UC-12). Promoted to a major feature after observing VMs killed for lack of RAM.
- **Display** (Infrastructure → Capacity): per hypervisor or cluster, three stacked bars per resource: **physical**, **allocated** (sum of the VMs' RAM and vCPUs, or of the requests in the cluster), **actually used**. Visual alert when allocated exceeds physical (overcommitment), and when usage approaches physical.
- **Sources**: hypervisor API (VM allocation), Prometheus (host usage), Kubernetes API (requests/limits, allocatable).
- **Rules**: configurable thresholds; a `Warning` issue if the host's used RAM stays above 90% for 10 min (default values).
- **Phase**: v0.4

**F-MET-05 · "Who uses what" resource explorer** 🟡
- **Need** (mockup feedback): for a given environment (e.g. a VM with 4 vCPUs and 12 GB hosting a k3s cluster), see **which containers or pods use how much**, in a very visual and interactive way, with direct access to logs.
- **Display**: a **proportional map** (treemap) of the environment's capacity, split by stack or namespace then by workload, plus "system" and "free". Memory / CPU toggle. Clicking a workload opens a panel: usage versus limit, chart, restarts, node, **latest logs**, "live logs" and "open in Grafana" buttons. Below the map, a stacked area chart of CPU per namespace over time, with the environment's limit and deployment markers.
- **Navigation**: breadcrumb hypervisor → VM → namespace → pod. The same screen serves a Docker host (split by compose stack).
- **Phase**: v0.4

**F-MET-04 · Trends and forecasting** 🟡
- For disk (hosts, volumes, databases): linear projection of growth, "full in ~23 days". Issue if the deadline is below a threshold.
- **Phase**: v0.5

**F-MET-03 · Custom metrics** 🟡
- Add app-specific indicators to an app through a PromQL query (e.g. "orders executed / h" for a trading app), displayed as a widget.
- **Phase**: later ❓ Q-30

### 9.6 F-UPD — Updates

**F-UPD-01 · Detecting outdated images** 🟡
- **Need**: know which images have a new version.

| Case | Method |
|---|---|
| Versioned tag (`10.9.1`) | List the registry tags, compare using semver; classify as patch / minor / major |
| Floating tag (`latest`, `release`) | Compare the local **digest** with the remote digest of the same tag |
| Kubernetes image | Same, from the pod specs |
| Helm chart | Compare with the chart repository ❓ later |
| Host OS packages | ❓ Q-26 (out of scope?) |

- **Rules**: tag filters per app (regex, e.g. ignore `-beta`); ignore a version; limited check frequency (respecting Docker Hub quotas).
- **Display**: E-06; "update" badge on the tile; link to the release notes (GitHub releases, if the source repository is known).
- **Phase**: v0.5

**F-UPD-02 · Link to the IaC source** 🟡
- For each update, indicate **where to change it**: repo, file, variable (e.g. `terraform/media/variables.tf → sonarr_image_tag`). Portarr **does not open** a PR and changes nothing (D-16).
- **Phase**: v0.5

**F-UPD-04 · Version inventory** 🟡 (D-14)
- **Need**: have an overview of each app's version, whether you manage it yourself (CI/CD with automatic versioning) or not.

| Version | Source |
|---|---|
| **Deployed** | Tag or digest of the running image; OCI label `org.opencontainers.image.version`; the app's version endpoint if declared (e.g. `/api/version`); for a VM or a host, the OS version |
| **Declared** | Value read from the deployment repository (Terraform variable, Helm value, Compose file) ❓ Q-27 |
| **Available** | Releases and tags of the code repository (GitHub), registry tags |

- **Rules**: deployed ≠ declared = **drift** (the deployment was not applied, or a manual change was made); deployed < available = update possible (F-UPD-01).
- **Phase**: v0.5

**F-UPD-05 · Deployment detection** 🟡
- **Need**: be aware when a version changes, even if it was changed by a GitOps pipeline or an automated tool rather than by yourself.
- **Mechanism**: comparison of the deployed version between two inventories → "Deployment" event (before → after, time, workload). If a CI/CD pipeline notifies Portarr (webhook), the event is enriched (commit, author, link to the run).
- **Display**: Maintenance → Deployments, Activity feed, vertical marker on metric charts ("deployment here") to see the effect of a new version.
- **Notification**: "On Deployment" trigger (F-NOT-02).
- **Phase**: v0.5

**F-UPD-06 · End of life of runtimes and frameworks** 🟡
- **Need**: be warned that an app relies on an obsolete runtime ("this container runs on PHP 5, move to PHP 8").
- Runtime **detection**: base image (`php:5.6-apache`), packages detected by the scanner (Trivy lists OSes and runtimes), code repository files (`*.csproj` → `TargetFramework`, `package.json` → `engines`, `composer.json`), or declaration in the catalog.
- **Reference data**: endoflife.date.
- **Display**: Maintenance → End of life; badge on the app tile; `Warning` issue as end of life approaches, `Critical` once it has passed (configurable thresholds).
- **Phase**: v0.5

**F-UPD-07 · Code dependency evolution** 🟡 (D-14)
- **Need**: a **developer view** of one's applications (web and desktop alike). Know that a library in use has released a new version, and judge whether it is worth evolving the application. This is **not** a need for usage statistics.
- **Example**: a WinUI desktop app uses a video playback library (NuGet package). A new major version comes out: Portarr displays it with a link to its release notes.
- **Sources** 🟡:

| Step | Source |
|---|---|
| List of a repository's dependencies | GitHub dependency graph (repository SBOM export) |
| Latest version of each dependency | Package registries (NuGet, npm, PyPI…) or an aggregator such as deps.dev ❓ to be validated |
| Release notes | GitHub releases of the dependency's project, if known |

- **Display**: in the app detail, **Dependencies** tab: package, version in use, latest version, gap (patch/minor/major), age, link to the release notes. Ability to **follow** some key dependencies ("notify me when this library releases a version") and ignore the others.
- **Rules**: no issue for a mere update (it is information); an issue only if there is a vulnerability (F-SEC-04) or an end of life (F-UPD-06).
- **Phase**: v0.5

**F-UPD-03 · Updates of Portarr itself** 🟡
- Like the *arr apps: System → Updates, list of versions and changelog. No automatic in-container update (you change the image tag).
- **Phase**: v0.5

### 9.7 F-SEC — Security

**F-SEC-01 · Image vulnerabilities** 🟡
- **Source**: Trivy in server mode (Portarr sends it the image references) ❓ Q-25.
- **Display**: E-07; counters by severity on an app's detail view; issue if a Critical CVE has an available fix.
- **Rules**: scan on every new image and daily; results cached in the database; ability to ignore a CVE with a justification.
- **Phase**: v0.6

**F-SEC-02 · Logins and authentication failures** 🟡

| Source | Data |
|---|---|
| Host SSH | Loki (`sshd` / `auth.log` / journal logs): successes, failures, IP, user |
| Identity provider | Keycloak: events API (LOGIN, LOGIN_ERROR) |
| Portarr itself | Its own audit log |
| Third-party apps | ❓ |

- **Display**: chronological feed, failures highlighted, grouping by IP, issue on a burst of failures (configurable threshold).
- **Phase**: v0.6

**F-SEC-04 · Vulnerabilities in code dependencies** 🟡 (D-14)
- **Need**: be notified when a dependency of one of your applications has a vulnerability (e.g. a Dependabot alert), for web apps and thick clients alike.
- **Sources**: Dependabot and code scanning alerts from the GitHub repository linked to the app; later GitLab and Gitea; as a fallback, a Trivy scan of the repository (`trivy repo`) ❓.
- **Display**: a highly visible **⚠ "vulnerability detected" badge** on the app tile and in the Overview: this is the main expectation. The details are in Security → Vulnerabilities, with an **origin** column (container image / code dependency), the severity, the package, the fixed version and a link to the Dependabot alert. The operator then acts on their own (D-16).
- **Rules**: a critical vulnerability with a fix available for more than N days → issue; alerts closed on GitHub are closed in Portarr.
- **Phase**: v0.5

**F-SEC-03 · Exposure** 🟡
- List what is exposed: ports published on hosts, public URLs, apps without authentication in front of them ❓ Q-29.
- **Phase**: later

### 9.8 F-ISS — Issues

**F-ISS-01 · Problem aggregation** 🟡
- **Sources**: failing checks, failing workloads, resources above a threshold (disk > 90%), certificates close to expiry, critical CVEs, unreachable providers, overdue major updates ❓.
- **Rules**: deduplication (one issue per cause, not per check), `Critical / Warning / Info` severities, attachment to the root cause (F-HLT-05), automatic resolution when the condition disappears.
- **Actions**: acknowledge, snooze (duration), open the entity.
- **Phase**: v0.3

### 9.9 F-ACT — Activity and actions

**F-ACT-01 · Workload actions** ❌ Out of scope (D-31)

| Type | Actions |
|---|---|
| Container | start, stop, restart, pull + recreate ❓ |
| Deployment / StatefulSet | rollout restart, scale |
| Pod | delete (recreated by its controller) |
| VM | start, shutdown, force stop, reboot |
| Stack | restart all its workloads, in order ❓ |

- Kept for the record only: Portarr never acts on workloads (D-31). The operator uses their own tools.

**F-ACT-02 · Triggering IaC jobs** ❌ Out of scope (D-31)
- Declare **jobs** (webhook, GitHub Actions `workflow_dispatch`, Semaphore/AWX template…) attached to a stack: "redeploy Media", "terraform plan monitoring".
- **Display**: button on the stack; tracking in Activity → Queue; link to the job logs.
- **Phase**: v0.7

**F-ACT-03 · Activity** 🟡
- **Queue**: running tasks and actions, with progress. **History**: all events, filterable.
- **Phase**: v0.3

### 9.10 F-NOT — Notifications

**F-NOT-01 · Channels** 🟡
- `Notify` providers (§7.4), configured like the *arr *Connections*, with a Test button.
- **Phase**: v0.7 ❓ Q-17 (move up to v0.3?)

**F-NOT-02 · Rules** 🟡
- Triggers to tick per channel, as in the *arr apps: On Down · On Recovery · On Degraded · On Unexpected Stop · On Deployment · On Update Available · On End Of Life · On Critical CVE · On Backup Missed · On Login Failure Burst · On Action.
- Filters by stack, tag and severity; delay before notification.
- **Phase**: v0.3 (email), v0.7 (advanced rules)

**F-NOT-04 · In-app and browser notifications** 🟡
- Real-time toasts in Portarr (SignalR) when an app or a VM goes down or recovers, and **browser push** notifications (Web Push, Portarr installable as a PWA) even with the tab closed.
- **Phase**: v0.3

**F-NOT-03 · Periodic summary** 🟡
- A summary email (or message): what went down and recovered, open issues, pending updates, overdue backups, disk space. Frequency ❓ Q-44.
- **Phase**: v0.4

### 9.11 F-SYS — System (Portarr itself)

| ID | Feature | Phase |
|---|---|---|
| F-SYS-01 | **Status**: version, uptime, database, .NET runtime, paths | v0.1 |
| F-SYS-02 | **Health**: internal health (unreachable providers, database, disk space, invalid configuration) | v0.2 |
| F-SYS-03 | **Tasks**: scheduled tasks (refresh inventory, checks, scans, update check, housekeeping), intervals, manual run | v0.2 |
| F-SYS-04 | **Backup / Restore**: scheduled backups of the database and configuration, restore from the UI | v0.3 |
| F-SYS-05 | Portarr **logs**, level adjustable at runtime | v0.1 |
| F-SYS-06 | **Portarr metrics**: Prometheus `/metrics` endpoint, OpenTelemetry traces | v0.2 |

### 9.12 F-SET — Settings

| ID | Feature | Phase |
|---|---|---|
| F-SET-01 | **General**: display name, logo, base URL, API key, time zone | v0.1 |
| F-SET-02 | **Providers** (E-09) | v0.2 |
| F-SET-03 | **Catalog**: sources, editing ❓ Q-08 | v0.1 |
| F-SET-04 | **Checks**: default values (interval, timeout, anti-flapping) | v0.2 |
| F-SET-05 | **Users & Auth**: local accounts, roles, API keys, activity (V7, D-92 to D-98; ✅ step 1); OIDC (D-99, planned step 2) | v0.1 |
| F-SET-06 | **UI**: theme, language, date format, default view mode | v0.1 |
| F-SET-07 | **First-run wizard**: create the admin (✅ V7: setup screen, or `PORTARR__AUTH__ADMINUSER` + `ADMINPASSWORDFILE`), add a first provider, import a catalog | v0.2 |

### 9.13 F-API — API and integrations

| ID | Feature | Phase |
|---|---|---|
| F-API-01 | REST API `/api/v1`, API key (`X-Api-Key` header), published OpenAPI spec (`/openapi/v1.json`) and interactive reference (`/docs`, D-76) | v0.1 |
| F-API-02 | Documented SignalR hub for real-time clients | v0.3 |
| F-API-03 | **MCP server**: an AI assistant can query Portarr ("what is down?") (D-68 to D-70, [verticals/V5-ai-mcp.md](verticals/V5-ai-mcp.md)) and, when enabled, describe the fleet by editing the catalog (D-86); acting on the infrastructure is out of scope (D-31) | v0.2 (V5) |
| F-API-04 | Generic outgoing webhooks (Portarr events → third-party system) | v0.7 |
| F-API-06 | **Prometheus discovery endpoint** (*HTTP SD*): Prometheus automatically scrapes the exporters of the environments Portarr knows about (§6.4) | v0.4 |
| F-API-05 | **`portarr` Terraform provider**: declare apps, stacks and checks in the same code as the infrastructure they describe (D-19). Published on the Terraform Registry | v0.5 |

### 9.14 F-DAT — Databases and storage (D-12)

**F-DAT-01 · Database inventory** 🟡
- **Need**: know which databases exist, where they run and how large they are (UC-13).
- **Display** (Data → Databases): table database server → databases; engine, version, host workload, size, growth over 7 d / 30 d, number of connections, app(s) using it, last backup (F-BAK).

| Engine | Preferred source | Fallback |
|---|---|---|
| PostgreSQL | postgres_exporter (`pg_database_size_bytes`), or CloudNativePG metrics in a cluster | Read-only connection: `pg_database_size()` |
| InfluxDB (time series) | InfluxDB's native `/metrics` endpoint | InfluxDB API (buckets, size) |
| MySQL / MariaDB | mysqld_exporter | `information_schema.tables` |
| Redis | redis_exporter (`redis_memory_used_bytes`) | `INFO memory` |
| SQLite (databases embedded in apps, e.g. the *arr apps) | File size in the volume | — |
| MongoDB | mongodb_exporter | `dbStats` |

- **Discovery**: labels (`portarr.database=postgres`), known images (`postgres:*`, `mariadb:*`), or declaration in the catalog ❓ Q-42.
- **Phase**: v0.4

**F-DAT-02 · Storage space** 🟡
- **Display** (Data → Storage): per host, per disk or array, per volume or share: used / free, trend, forecast saturation date (F-MET-04). Who uses the most (top volumes, top databases).

| Level | Source |
|---|---|
| Hypervisor disks and array | Hypervisor API, node_exporter |
| A VM's file system | node_exporter in the VM |
| Docker volumes | Docker API (`system df`) ❓ cost |
| Kubernetes PersistentVolumes | kubelet (`kubelet_volume_stats_*`) via Prometheus |

- **Phase**: v0.4

### 9.15 F-BAK — Backups (D-12)

> **Gathered 2026-10-26.** Data is considered **precious**: a PostgreSQL database often comes with each new application, and some databases are business-critical (e.g. a time-series database that accumulates market data). Today, if a VM or a volume is lost, everything is lost. An S3 bucket exists, but backups are not organized. The wish: **proper management of databases and their backups**, relying on an existing, popular tool with a good API (D-17).

**Tooling study to be carried out (D-18)** 🟡 ❓ Q-50

| Context | Widespread candidates | What they bring | Integration point for Portarr |
|---|---|---|---|
| PostgreSQL on Kubernetes | **CloudNativePG** (CNCF operator) | Continuous backups to S3 (base + WAL), point-in-time recovery, everything declared in YAML | Kubernetes resources (`Backup`, `ScheduledBackup`, `Cluster` status) + Prometheus metrics |
| Kubernetes volumes and resources | **Velero** (CNCF) | Backup of namespaces and volumes to S3 | Kubernetes resources (`Backup`, `Schedule`) + Prometheus metrics |
| Files, volumes, dumps on Docker / VMs | **restic** (engine) with **Backrest** (UI + API) or **Kopia** (server with UI + API) | Encrypted, deduplicated backups to S3 | Backrest or Kopia API, or heartbeats |
| Database dumps on Docker | Generic "db-backup" containers (pg_dump, mysqldump, `influx backup` → S3) | Easy to plug in next to each database | Heartbeats (F-HLT-08) + S3 bucket verification (F-BAK-02) |

- **The key point of the study**: which tool exposes a **proper API** that Portarr can connect to, for each context (Kubernetes, Docker, VM).
- **Natural direction**: part of the fleet is migrating to Kubernetes. CloudNativePG + Velero for the cluster, and a restic-based tool for the rest, would cover most needs with very widespread tools. To be confirmed.
- **Study result** (2026-10-30), **choice** (2026-10-31): restic through Backrest and K8up (Q-50), see [verticals/V3-data-backups.md](verticals/V3-data-backups.md). Declared per data asset in the catalog (D-77 to D-82).
- **Portarr's role** ❓ Q-52: *observe* the backups made by the tool (consistent with D-03 and D-16), or also *configure* them (create a backup policy from Portarr when a database is added)?

**F-BAK-01 · Register of expected backups** 🟡
- **Need**: declare *what must be backed up, where and how often*, in order to verify that it is done (UC-14).
- **Model**: a **backup policy** = target (database, volume, VM, repo) + destination (S3 bucket + prefix, restic repository…) + expected frequency + expected retention.

```yaml
backups:
  - id: app-db
    target: { database: postgres@server-1/appdb }
    destination: { provider: s3-main, prefix: "backups/appdb/" }
    expectEvery: 24h
    keep: 14
    heartbeat: true        # the job also reports its execution (F-HLT-08)
```

- **Phase**: v0.4

**F-BAK-02 · Backup verification** 🟡
- **Checks**, from the simplest to the most reliable:

| Level | Check | Source |
|---|---|---|
| 1 | The job ran and succeeded | Heartbeat (F-HLT-08) |
| 2 | A new file is present at the destination, with a plausible size (not 0 bytes, not −50% compared with the previous one) | S3 provider: prefix listing |
| 3 | The number of retained backups matches the retention | S3 provider / backup tool |
| 4 | The backup can be restored (restore test) | ❓ Q-45: out of scope, or an external job that reports a heartbeat? |

- **Display** (Data → Backups): one row per policy: last backup ("26 h ago", in orange if beyond the expected frequency), size and trend, number retained, check status. Total volume per destination (and estimated cost ❓).
- **Rules**: `Critical` issue if there has been no valid backup for twice the expected frequency; `Warning` on a size anomaly.
- **Phase**: v0.4

**F-BAK-03 · Coverage** 🟡
- Highlight **databases and volumes without a backup policy** ("4 databases are not backed up").
- **Phase**: v0.5

**F-BAK-04 · Co-located backups** 🟡
- **Observation on a real fleet**: a daily backup existed, but it was stored **on the same machine** as the database. Losing the machine takes both with it.
- **Rule**: a backup whose destination is on the same environment (or the same physical host) as its source is flagged `Warning`: "backup not stored off-site". The 3-2-1 principle is recalled in the UI.
- **Phase**: v0.4

### 9.16 F-NET — Reverse proxy and routes (D-20)

**F-NET-01 · Route discovery** 🟡
- **Need**: know which subdomain serves which application, without entering it by hand.
- **Mechanism**: the reverse proxy provider supplies the list of routes: host name → upstream (container name, or IP:port) → options (auth, TLS). Portarr **resolves the upstream to a workload** (by container name on the same network, or by IP:port → environment → published port) and offers to link the route to the app.

| Reverse proxy | Method | Status |
|---|---|---|
| SWAG / nginx | Reading the `*.subdomain.conf` files (mounted read-only): `server_name`, `$upstream_app`/`$upstream_port` or `proxy_pass`, authentication `include` | ❓ Q-54 |
| Traefik | `/api/http/routers` and `/api/http/services` API | ❓ Q-54 |
| Nginx Proxy Manager | REST API | Later |
| Caddy | `/config/` admin API | Later |
| Kubernetes Ingress | Kubernetes API (`Ingress`, `IngressRoute`) | v0.3 |

- **Observation on a real fleet**: a single instance mixes several upstream styles (container name, hard-coded IP, direct `proxy_pass`). The parser must handle all three.
- **Phase**: v0.2

**F-NET-02 · Route status** 🟡
- For each route: does the upstream respond; is the route protected by SSO; is the TLS certificate valid (F-HLT-07).
- **Display**: "Exposure" column in Apps (🌐 public, 🔒 SSO, 🏠 LAN only); Infrastructure → Routes screen.
- **Phase**: v0.2

**F-NET-03 · Routing anomalies** 🟡
- **Observation on a real fleet**: routes point to non-existent or "placeholder" upstreams, duplicates serve the same app, backup files (`.bak`, `.save`) are left lying around, upstreams target IPs that are not in the inventory.
- **Detection**: dead route (upstream unreachable, or pointing to a stopped workload), orphan route (no linked app), app without a route, duplicates, upstream to an unknown host, hard-coded IP where a container name is possible.
- **Phase**: v0.3

### 9.17 F-DEMO — Demo mode

- `PORTARR_DEMO=true`: simulated providers, realistic fake data (outages, CVEs, updates), actions with no effect.
- **Use**: README screenshots, online demo, end-to-end tests, **mockups**.
- **Phase**: v0.2

---

## 10. Non-functional requirements

| ID | Requirement | Target 🟡 |
|---|---|---|
| NF-01 | Footprint | < 250 MB of RAM, image < 200 MB, idle < 2% of a core for ~100 workloads |
| NF-02 | Responsiveness | Apps page < 1 s; status change visible < 5 s after detection |
| NF-03 | Target scale | Up to ~10 environments, ~500 workloads, ~200 apps |
| NF-04 | Resilience | A failing provider only affects its own entities; Portarr restarts without losing history |
| NF-05 | Retention | Check and event history: 90 days by default, configurable; automatic purge |
| NF-06 | Multi-architecture | amd64 and arm64 images |
| NF-07 | Configuration | Every startup setting can be overridden by an environment variable (`PORTARR__SECTION__KEY`) |
| NF-08 | Observability | `/metrics`, `/health/live`, `/health/ready`, optional structured JSON logs |
| NF-09 | Internationalization | English by default, French translation included; community translation files (D-26) |
| NF-10 | Accessibility | AA contrast, keyboard navigation, status never conveyed by color alone (icon + text) |
| NF-11 | Quality | Core test coverage > 70%; zero critical vulnerabilities at release |

---

## 11. Security

### 11.1 Threat model (summary) 🟡

| Risk | Mitigation |
|---|---|
| Portarr has access to the Docker socket, which is equivalent to being root on the host | **docker-socket-proxy** with only the required endpoints; read-only as long as actions are disabled |
| Provider secrets (tokens, kubeconfig) | Encrypted in the database (Data Protection API); never returned by the API; masked in logs |
| Container environment variables (often secrets) | Masked by default; clear-text display restricted to the admin, and audited ❓ |
| Account takeover | OIDC recommended; MFA delegated to the identity provider; short sessions |
| Destructive actions | Dedicated role, confirmation, audit log |
| Kubernetes access | Minimal RBAC provided (read-only ClusterRole + optional role for actions) |
| Internet exposure | Documented as possible behind a reverse proxy and SSO; security headers, CSP |
| An AI assistant connected over MCP reads the whole map at once | Endpoint off by default, per-user API keys scoped to the owner's role (D-97), per-tool permissions (D-98), read-only tools unless the write tools are enabled (D-86), answers without secrets (URLs without credentials or query, no labels, environment variables or connector endpoints), rate limit (D-70) |
| The API or an assistant rewrites the catalog | Off by default, `catalog.write` (D-92 to D-97), versioned writes (409), whole-file validation, atomic write with previous versions kept, audit file, rate limit, no secrets accepted in the knowledge fields (D-83 to D-86) |

### 11.2 Authentication and roles ✅ (step 1, V7)

- Modes: **local account** (form, cookie session; ✅ D-92), **OIDC** (planned step 2, D-99), **none** (trusted network): *removed*, login is always required.
- Roles are named sets of permissions (D-93); the four built-in ones, fixed:

| Role | Sees | Can act | Use |
|---|---|---|---|
| `Admin` | Everything (secrets are reserved: `secrets.view`) | Everything, including Settings and users | The operator |
| `Operator` | Every page except users and settings | Catalog changes, chat | A possible co-administrator |
| `Observer` | Every view permission: apps, infrastructure, data, maintenance, assistant, system; Q-46 (logs, variables, internal addresses, masking) is open | Nothing | Showing off one's infra (live demo) |
| `Guest` | The apps launcher | Nothing | Sharing a few links |

- **Custom roles** (D-93) answer "several styles of view": the menu, the routes and the actions show what the role allows; the API refuses the rest.
- Per-user **scoped API keys**, read-only or full, never beyond the owner's role (D-97). The environment keys are deprecated.
- Open follow-ups: **masking for observers** ("showcase" mode that replaces IPs, internal host names and domains with aliases, Q-46); a **role's scope** restricted to stacks or tags (e.g. a `Guest` that only sees the `shared` tag); role mapping from OIDC groups or claims (D-99); confirm whether workload logs and environment variables (when they exist) stay under `infrastructure.view`.

---

## 12. Engineering and delivery

### 12.1 Repository structure 🟡

```
portarr/
├── src/                 # .NET solution (§6.3)
├── frontend/            # React + Vite + TypeScript
├── docs/                # SPEC, adr/, guides
├── deploy/
│   ├── compose/         # example docker-compose (+ socket-proxy)
│   ├── helm/            # Helm chart
│   └── terraform/       # example Terraform module
├── schemas/             # catalog JSON Schema
└── .github/             # CI/CD workflows, issue templates
```

### 12.2 CI/CD 🟡

> Detailed thresholds and checks are in [TESTING.md](TESTING.md) §5–6.

- **Pull request**: build, tests (unit + integration with Testcontainers), lint, SonarQube analysis, gitleaks, image build.
- **Main branch**: semantic-release (Conventional Commits) → tag, CHANGELOG, multi-arch image published to GHCR, Trivy scan of the published image, SBOM, cosign signature ❓.
- **Confidentiality guardrail**: pre-commit hook + CI job that rejects commits containing values from a deny-list (list stored outside the public repo).

### 12.3 Deliverables 🟡

| Deliverable | Phase |
|---|---|
| Image `ghcr.io/maflabs38/portarr` | v0.1 |
| Example `docker-compose.yml` | v0.1 |
| Helm chart | v0.3 |
| Terraform module | ❓ |
| Documentation site | ❓ |

---

## 13. Roadmap

🟡 Proposal, pending validation. Reordered on 2026-10-26 according to D-10: observability and alerting before convenience features. The launcher stays in v0.1 because it is cheap and serves as the foundation (catalog, auth, UI).

| Version | Theme | Features |
|---|---|---|
| **v0.1** | Foundation and launcher | F-CAT-01/02/03/06, F-CAT-04 (YAML), F-SET-01/05/06, F-SYS-01/05, F-API-01, OIDC + local auth |
| **v0.2** | See: health and inventory | Docker provider, **Kubernetes provider (inventory and pod state)**, reverse proxy provider, **F-NET-01/02**, **F-HLT-11**, **F-INV-06**, F-HLT-01/02/03/04/07, F-INV-01/02/03, real-time F-MET, F-CAT-04 (labels), F-SYS-02/03/06, F-SET-02/04/07, **E-00 Overview**, demo mode |
| **v0.3** | Get notified and diagnose | Hypervisor providers, **multi-node Kubernetes** (logs, remote nodes) and Loki, **F-NOT-01/02 (email)**, **F-NOT-04**, **F-HLT-08 heartbeats**, F-ISS-01, F-HLT-05/06, F-LOG-01/02, F-ACT-03, F-SYS-04, Helm chart |
| **v0.4** | Resources and data | Prometheus provider, F-MET-01, **F-MET-02 capacity**, **F-HLT-09 unexpected shutdowns**, **F-HLT-10 tunnels**, **F-DAT-01/02**, **F-BAK-01/02**, F-NOT-03, F-LOG-03 |
| **v0.5** | Maintenance | Registry, GitHub and endoflife.date providers, F-UPD-01/02/03, **F-UPD-04/05/06/07**, **F-SEC-04**, F-CAT-07 (dependency tracking), F-MET-04, F-BAK-03, F-INV-04, F-CAT-05 |
| **v0.6** | Security | Trivy, identity provider, F-SEC-01/02 |
| **v0.7** | Extend | Other notification channels, F-API-04 |
| **Never** | Act | Workload actions, IaC jobs, GitOps, PR creation, remediation (D-16, D-31) |

> **Slicing rule (D-17)**: each version only integrates the §7.0 reference tool for the capabilities it covers. The other §7.4 providers are future evolutions.
| **v1.0** | Stabilization | Frozen v1 API, complete documentation, online demo |

---

## 14. Question register

> Filled in as the interviews progress. Status: ❓ open · 💬 under discussion · ✅ resolved (with a reference to the decision).

| # | Topic | Question | Status |
|---|---|---|---|
| Q-01 | License | GPL-3.0 (like the *arr apps) or a more permissive license? | ✅ GPL-3.0 (D-27) |
| Q-02 | Language | English for code and docs; which languages for the UI? Should this spec be translated into English? | ✅ English for code and docs; UI in English + French (D-26) |
| Q-03 | Users | Who besides the operator will use Portarr, and for what? | ✅ A single operator first and foremost; secondarily observers (showing off one's system) and launcher guests (§3.1) |
| Q-04 | Roles | Which permission levels are actually useful? | ✅ For now (V7, D-93): permissions are a closed list; Admin, Operator, Observer and Guest are built in, custom roles cover the rest. Observer masking stays Q-46, per-role scope by stack or tag stays open (§11.2) |
| Q-05 | Launcher | How to navigate between stacks: tabs, sections, pages, customizable dashboard? | 💬 The launcher is secondary (D-10); the home page becomes the Overview (Q-43) |
| Q-06 | Mobile | What use on a phone? | ❓ |
| Q-07 | URLs | How to automatically pick the LAN or public URL? | ✅ By the address Portarr is opened on, with a Public/Local switch (D-46) |
| Q-08 | Catalog | Can the UI edit the catalog, or is it *as code* only? | ✅ Both (D-19); an app managed through config is read-only in the UI |
| Q-09 | Catalog | Priority between sources in case of conflict | 💬 The catalog file is the primary source (D-44); labels and UI come later |
| Q-10 | Actions | Which actions from the first version, and which ones never? | ✅ None: Portarr never acts on the infrastructure (D-31) |
| Q-12 | Status | How to distinguish "stopped on purpose" from "went down"? | 💬 F-HLT-09 approach: any shutdown with no recorded action or maintenance is "unexpected" |
| Q-14 | VM | Which view of a VM matters: inside, hypervisor, or both? | ✅ Both, correlated (D-11) |
| Q-15 | Metrics | How to express CPU (cores, %, relative to what)? | ✅ vCPU and RAM consumed, everywhere (D-33) |
| Q-16 | Logs | How much history in the live view; do we need logs persisted outside Loki? | ❓ |
| Q-17 | Notifications | Priority of notifications in the roadmap | ✅ High: email from v0.3 (D-10) |
| Q-20 | Collection | Will an agent ever be needed (unreachable hosts, NAT, VPS)? | ✅ Yes, optional and read-only, as the simple way to connect a machine (D-100, D-101, [ADR 0010](adr/0010-optional-read-only-agent.md)). A worker VPS behind a tunnel is still collected through the cluster (§4.4); an agent that connects out (NAT) is a later step |
| Q-21 | Extensibility | Providers compiled into the application or dynamically loaded plugins? | ✅ One class library per connector, compiled in (D-35) |
| Q-22 | systemd | Should services outside containers be monitored, and how? | ❓ |
| Q-23 | Data | Dapper + FluentMigrator (like Sonarr) or EF Core? | ✅ Dapper + FluentMigrator (D-28) |
| Q-24 | UI | Faithful *arr look or a modern design system? | ❓ |
| Q-25 | Security | Does Portarr embed Trivy, or require an external Trivy server? | ✅ External Trivy server through a connector (D-41) |
| Q-26 | Updates | Are host OS packages in scope? | ❓ |
| Q-27 | Drift | Should we detect what runs outside IaC? | ❓ |
| Q-28 | Updates | Should Portarr open PRs on IaC repos (Renovate's role)? | ✅ Not in v1 (D-16); possible evolution |
| Q-29 | Security | Should exposure be mapped (ports, apps without authentication)? | ❓ |
| Q-30 | Metrics | Do we need business widgets per app? | ❓ |
| Q-31 | *arr | Do we need a specific integration with the *arr apps (queue widgets…)? | ❓ |
| Q-32 | AI | Is an MCP server part of the vision? | ✅ Yes, read-only, with a generated knowledge base (D-68, D-69) |
| Q-40 | Name | Are the existing `portarr` namesakes (Plex portal, qBittorrent utility) acceptable? | ❓ |
| Q-41 | Backups | What are backups made (or will they be made) with, and to where? | 💬 Today: a hypervisor plugin (weekly, local), Kubernetes CronJobs and scripts (daily, local); nothing off-site. Target: local array + S3 (D-61, Q-50) |
| Q-42 | Databases | Which engines, how they run (dedicated container per app, shared server), and how should Portarr discover them? | ✅ Discovered from storage: known images or data paths, Kubernetes claims (D-60) |
| Q-43 | Home page | Is the home page the Overview (global view) or the launcher with a status banner? | ❓ |
| Q-44 | Summary | What cadence and what content for the periodic summary? | ❓ |
| Q-45 | Backups | Are restore tests part of the need? | ❓ |
| Q-46 | Observer | What is an observer allowed to see of your real system, and what must stay hidden? | 💬 Settled when C2 is built |
| Q-47 | Desktop | For a desktop app, what do we want to track: only the repository (dependencies, releases), or also the version actually installed on workstations? | ✅ Developer view only: dependencies, vulnerabilities, library evolution, framework end of life. No usage statistics (F-UPD-07) |
| Q-48 | Forges | GitHub only, or also GitLab / Gitea? Private repositories included? | ✅ GitHub only, private repositories included (D-39) |
| Q-49 | Tunnels | Which tunnel technology, and does it expose metrics? | ❓ |
| Q-50 | Backups | Which reference backup tool(s) (study §9.15)? | ✅ restic, with Backrest (hosts, VMs) and K8up (cluster), chosen 2026-10-31; existing CronJobs keep the local copy; see [verticals/V3-data-backups.md](verticals/V3-data-backups.md) and [companions.md](companions.md) (D-77 to D-82) |
| Q-51 | Hypervisor | v1 reference hypervisor: the project's own (Unraid) or the most widespread (Proxmox)? | ✅ Unraid in v1, Proxmox later (D-40) |
| Q-52 | Backups | Does Portarr only observe backups, or does it also configure them? | ✅ Observe and report only (D-43) |
| Q-53 | Rules | Do "rules" on app and stack state only trigger alerts, or also automatic actions (restart)? This touches on D-16 | ✅ Alerts only, never automatic actions (D-31) |
| Q-54 | Reverse proxy | v1 reference reverse proxy: SWAG/nginx (dogfooding) or Traefik (the most widespread)? | ✅ Several chained proxies; SWAG/nginx first (D-42) |
| Q-56 | Ecosystem | Does the project ship the collector kit (Ansible role, Helm, companion stack), or only document what needs to be installed? | ✅ Not shipped; documented per connector (D-38) |
| Q-57 | Alerts | Who owns the alerting rules: Portarr alone (a single place), or Prometheus/Alertmanager, whose alerts Portarr receives? | ✅ Evaluated by the tools, received by Portarr; editable only by pushing to the tool (D-37) |
| Q-58 | Hosting | Where does Portarr itself run, given that it must not go down along with what it monitors? | ✅ On the hypervisor host (D-34); going down with it is accepted (D-40) |
| Q-59 | Logs | v1 reference log backend: Loki or Elasticsearch / OpenSearch? | ✅ Loki + Prometheus + Grafana (D-22); Elasticsearch / OpenSearch as a later provider |
| Q-60 | Maintenance | Private images (GHCR packages of the operator's own apps) cannot be checked anonymously: reuse the GitHub token (needs *packages: read*) or declare registry credentials per registry? | ❓ |
| Q-55 | Positioning | Should Portarr replace an existing launcher (Homarr, Homepage) and a container tool (Portainer), or coexist with them? | ❓ |

---

## 15. Glossary

| Term | Definition |
|---|---|
| *arr | Family of media management applications (Sonarr, Radarr, Prowlarr…) sharing the same architecture |
| Provider | Configurable connector to an external system (§7) |
| Capability | Type of data or operation a provider can supply (§7.2) |
| Workload | A running unit: container, pod, VM, service |
| Stack | Group of apps and workloads deployed together |
| Socket-proxy | Filtering proxy in front of the Docker socket, which limits the accessible endpoints |
| IaC | *Infrastructure as Code* (Terraform, Ansible, Helm…) |
| ADR | *Architecture Decision Record*: a short document recording a decision and its rationale |

---

## 16. Changelog

| Date | Change |
|---|---|
| 2026-10-26 | Created. Decisions D-01 to D-09. Renamed Labarr → Portarr. First complete draft of sections 1 to 15. |
| 2026-10-26 | Interview #1 (original need). Added the §1.2.1 observations, D-10 (observability first), D-11 (outside and inside correlated), D-12 (data in scope). UC-11 to UC-16. New Overview and Data menus, E-00 screen. New features F-HLT-08 (heartbeats), F-HLT-09 (unexpected shutdowns), F-MET-02 extended (overcommitment), F-MET-04 (forecasting), F-NOT-03 (summary), F-DAT, F-BAK. Email, heartbeat, S3, database and backup tool providers. Roadmap reordered. Q-41 to Q-45. |
| 2026-10-26 | Interview #1 (continued, users). Observer and Guest personas, Admin / Operator / Observer / Guest roles, "showcase" mode with masking. Q-03 resolved, Q-46 added. |
| 2026-10-26 | Interview #2 (fleet and maintenance). D-13 (VMs, Kubernetes and Docker as first-class), D-14 (maintenance includes the code), D-15 (all apps tracked, including thick clients). Model: App type and origin, code / deployment Source, Version, distributed cluster §4.4. GitHub, endoflife.date and tunnel providers. Features F-CAT-07, F-HLT-10, F-UPD-04/05/06, F-SEC-04, F-NOT-04. "Updates" menu broadened into "Maintenance". Q-47 to Q-49. |
| 2026-10-26 | Interview #3 (scope and tooling). D-16 (Portarr informs, the operator acts: no GitOps or automation in v1), D-17 (one popular reference tool per capability, §7.0), D-18 (what → tool → integration method). F-UPD-07 (dependency evolution, developer view). Backup tooling study (§9.15). InfluxDB added to databases. Q-28 and Q-47 resolved, Q-50 to Q-52 added. |
| 2026-10-26 | Interview #4 (core of the model) and inventory of a real fleet. D-19 (Application and Stack, created via UI or config), D-20 (reverse proxy as the source of URLs), D-21 (validation against a real fleet). critical/optional roles and stack viability rule. New F-NET domain (routes). F-HLT-11, F-INV-05, F-BAK-04 and F-API-05 (Terraform provider) derived from the observations. Q-08 resolved, Q-53 to Q-55 added. |
| 2026-10-26 | Triage of the containers of a real fleet (90 containers). F-INV-06 (origin from labels, inferred dependencies, database with no consumer), dead route to a stopped workload (F-NET-03). Kubernetes inventory moves to v0.2: a large part of the fleet is migrating to a cluster. |
| 2026-10-26 | Ecosystem (§6.4): three circles (core, per-environment collectors, shared backends), collector kit per environment type, operating levels 0 to 3, kit delivered as IaC, Prometheus discovery (F-API-06). Q-56 to Q-58. |
| 2026-10-26 | ELK / Grafana stack comparison. D-22: Grafana stack (Prometheus, Loki, Grafana, OpenTelemetry collection via Alloy) as the reference; Elasticsearch / OpenSearch as a later provider. Q-59 resolved. |
| 2026-10-26 | Feedback on the v1 mockups. New art direction (§8.3), real icons per app, F-MET-05 (interactive resource explorer VM → pods, with access to logs). |
| 2026-10-28 | Design guidelines (DESIGN.md, based on Sonarr's frontend code), testing strategy (TESTING.md), implementation plan (IMPLEMENTATION_PLAN.md), repo CLAUDE.md. D-23 to D-25. |
| 2026-10-28 | Translated to English (D-26). |
| 2026-10-28 | Interview #5 (technical choices). D-27 (GPL-3.0), D-28 (Dapper + FluentMigrator), D-29 (xUnit v3 + NSubstitute + AwesomeAssertions), D-30 (collect and report, no computation), D-31 (never acts; declarative configuration through tool APIs only), D-32 (standalone first), D-33 (vCPU and RAM everywhere), D-34 (runs on the hypervisor host). F-ACT-01/02 out of scope. Q-01, Q-10, Q-15, Q-23, Q-53, Q-58 resolved; Q-21, Q-56, Q-57 under discussion. |
| 2026-10-28 | Interview #6 (technical choices, continued). D-35 (one library per connector), D-36 (pushing configuration to tools allowed, tool logic forbidden), D-37 (alerts evaluated by the tools), D-38 (collector kit not shipped), D-39 (GitHub only), D-40 (hypervisor as a first-class environment, Unraid first). Q-21, Q-48, Q-51, Q-56, Q-57 resolved. |
| 2026-10-28 | Interview #7. D-41 (vulnerabilities of running images in scope, Trivy server), D-42 (no fixed access topology, chained reverse proxies), D-43 (backups observed only). Q-25, Q-52, Q-54 resolved; remaining questions are settled when the vertical that needs them starts. |
| 2026-10-29 | Feedback on the first MVP preview. D-44 (catalog file as primary source), D-45 (container state and healthcheck on tiles), D-46 (public and LAN URLs, LAN derived from published ports), D-47 (icon resolution and server-side cache), D-48 (apps made of several workloads), D-49 (one spec page per vertical). Q-07 resolved. First vertical page: [verticals/V1-launcher-docker.md](verticals/V1-launcher-docker.md). |
| 2026-10-29 | Second vertical: [verticals/V2-checks-external-apps.md](verticals/V2-checks-external-apps.md) (Checks, infrastructure and external apps). D-50 (HTTP checks on by default, with opt-out), D-51 (check history in memory), D-52 (TLS expiry thresholds), D-53 (Cloudflare environments, token by environment variable). |
| 2026-10-30 | Maintainer feedback on V2 (renamed "Checks, infrastructure and external apps"). D-54 (live Infrastructure dashboard, in-memory window), D-55 (Kubernetes connector over HTTPS), D-56 (rollout-proof Kubernetes selectors), D-57 (Docker host usage = sum of containers, host metrics seam), D-58 (Chart.js with design tokens). Checks page and Workloads list removed. |
| 2026-10-30 | Maintainer feedback on the Infrastructure page: live cadence (Docker 3 s, Kubernetes at metrics-server's resolution, 2 s refresh, 10-minute window, age of the last sample), compact side-by-side cards, consumer bars scaled to the heaviest. D-59 (machines hosted as VMs on another environment: `host`, `nodeHosts`; hypervisor memory and CPU split into containers, VMs and free). |
| 2026-10-30 | Third vertical page: [verticals/V3-data-backups.md](verticals/V3-data-backups.md) (Data and backups, V10 + V11 merged and pulled forward). D-60 (data assets discovered from storage), D-61 (known backup jobs in the catalog, coverage and 3-2-1), D-62 (`IBackupSource`, Kubernetes CronJob source), D-63 (Data page). Backup tool proposal for Q-50. Q-41 and Q-42 answered. |
| 2026-10-30 | Fourth vertical page: [verticals/V4-maintenance.md](verticals/V4-maintenance.md) (Maintenance, V12 pulled forward). D-64 (code repositories scanned from `sources: github:`, on / off / unknown security features), D-65 (repository ↔ app links), D-66 (anonymous registry lookups, same-shape tag comparison, honest unknowns), D-67 (Maintenance page: attention list and two sub-tabs). Q-60 added. |
| 2026-10-30 | Fifth vertical page: [verticals/V5-ai-mcp.md](verticals/V5-ai-mcp.md) (AI, plan entry V16 pulled forward). D-68 (MCP server, read-only tools over the existing views), D-69 (generated Markdown knowledge base), D-70 (off by default, API key until C2, no secrets), D-71 (Assistant page). Q-32 resolved. |
| 2026-10-31 | Accepted plan, step 1 (host truth): D-72 (node_exporter read every second, formulas, `hostMetrics` and `kubernetes.nodeExporter`), D-73 (SSE stream at 1 s), D-74 (host memory ring summing to the total). Companion tools page ([companions.md](companions.md)) and [connectors/node-exporter.md](connectors/node-exporter.md). |
| 2026-10-31 | Accepted plan, steps 2 and 3: D-75 (Maintenance filters from its figures, freshness and a rate-limited manual refresh), D-76 (OpenAPI document and Scalar reference at `/docs`, CSP exception). F-API-01 gains its published OpenAPI document. |
| 2026-10-31 | Accepted plan, step 4 (declarative data): D-77 (data declared in `data:`, discovery and `backups:` removed), D-78 (importance = accepted loss window, targets and sources), D-79 (seven states since the 2026-10-31 amendment, worst per asset, critical off-site coverage), D-80 (Backrest connector), D-81 (K8up and CronJob connectors), D-82 (Data page). D-60 and D-61 are superseded, D-62 and D-63 amended. Q-50 resolved (restic: Backrest + K8up). Companions: [companions.md](companions.md#backrest). |
| 2026-10-31 | Accepted plan, step 5 (writable catalog): D-83 (text-level editing of the catalog file, versions, backups, comments kept), D-84 (knowledge: notes, access, machines, links, providers; no secrets), D-85 (writes off by default, API key, rate limit, audit file), D-86 (MCP write tools). D-44 refined, D-68 amended. ADR [0007](adr/0007-text-level-catalog-editing.md). |
| 2026-10-31 | Identity badge: D-91 (`portal:` section of the catalog, name and logo in the branding folder, `/api/v1/portal`, `get_portal` and `set_portal`, sidebar badge and Identity card). |
| 2026-10-31 | C2 step 1 pulled forward as [verticals/V7-accounts.md](verticals/V7-accounts.md): D-92 (local accounts, cookie session, lockout, first run), D-93 (roles = permission sets, built-ins, custom roles), D-94 (policy per endpoint, CSRF), D-95 (user menu, profile, avatar, Users & Auth), D-96 (audit), D-97 (per-user API keys, deprecated shared keys), D-98 (chat and MCP per user), D-99 (OIDC planned). D-70, D-85, D-89 amended; Q-04 settled for now. ADR [0009](adr/0009-local-accounts-and-permission-based-roles.md). |
| 2026-10-31 | Consolidation, data states: D-79 amended (Waiting for first run, Not set up and Unknown are three different situations; none of them is "at risk"; critical off-site copies pending a first run are counted apart) and D-82 filters (`waiting`, `not-set-up`). |
| 2026-10-31 | The optional read-only agent: D-100 (the agent, its endpoints, read-only by construction), D-101 (the agent catalog key, Kubernetes in-cluster authentication and manifest), [ADR 0010](adr/0010-optional-read-only-agent.md); Q-20 resolved; D-30, D-38 and D-72 amended; §6.2 principle 1. Replaces the design proposal. |
| 2026-10-31 | The reader: D-102 amends D-100 and D-101 ([ADR 0011](adr/0011-reader-speaking-the-existing-protocols.md) supersedes ADR 0010 for the name, the API and the connector): it speaks the two protocols Portarr already reads, `reader:` is read as `docker:` and `hostMetrics:`, `tokenEnv` is confined to the `PORTARR_READER_` namespace, one reason code for a missing token, native AOT as the interim and a Rust rewrite planned. |
