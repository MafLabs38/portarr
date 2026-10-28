# Portarr — Functional and Technical Specification

> **Status**: draft v0.3, requirements gathering well advanced.
> **Related documents**: [DESIGN.md](DESIGN.md) (UI guidelines) · [TESTING.md](TESTING.md) (testing strategy) · [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) (plan by verticals)
> **Last updated**: 2026-10-26
> **Audience**: the project owner, contributors, and mockup tools (this file is self-contained enough to produce mockups from).
>
> ⚠️ This document is intended for the **public repo**. It contains **no real infrastructure values**. All names (`server-1`, `homelab.example`, "Media" stack…) are examples.

## Legend

| Marker | Meaning |
|---|---|
| ✅ | **Frozen**: decided, does not change without a new ADR |
| 🟡 | **Proposed**: drafted by the analyst, pending validation |
| ❓ | **Open**: unanswered question, see §14 |

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
| ✅ D-30 | **Portarr collects and reports, it does not compute.** Thresholds, aggregations, alert evaluation and any calculation live in the connected tools (Prometheus, Alertmanager, Kubernetes, backup tools…). Portarr's own logic is limited to **orchestration**: linking entities, combining the states reported by the tools, presenting them. Reinforces D-03. | A clean application is a relay that relies heavily on the tools, not a second monitoring engine. |
| ✅ D-31 | **Portarr never acts on the infrastructure.** No imperative action (start, stop, restart, scale, IaC job triggering). The only write allowed: **declarative configuration pushed to a tool through its API or its configuration files**, when a connector declares it (e.g. a backup schedule, a scrape target). Rules only raise alerts, never automatic actions. Reinforces D-16. | Portarr is a reporter; acting stays with the operator and their tools. |
| ✅ D-32 | **Standalone first.** With no connector at all, Portarr works as a launcher and an inventory (apps, links, stacks, which environment they run on), like a Heimdall-type portal. Each connector plugged in adds capabilities (metrics, logs, health, updates…); missing data is shown as "not connected", never as an error. | Low entry barrier; the ecosystem is opt-in, connector by connector. |
| ✅ D-33 | **Resources expressed in vCPU (cores) and RAM consumed**, the same way across every environment type (Docker host, VM, hypervisor container, Kubernetes pod and node). The percentage of the parent's capacity is secondary; Kubernetes adds request and limit. | Uniform units make a container on the hypervisor, a VM and a pod comparable. |
| ✅ D-34 | **Portarr runs on the hypervisor host**, next to the reverse proxy, as the place with the most height; it understands VMs and their workloads through connectors. | The central view belongs at the top of the hierarchy. |
| ✅ D-35 | **One class library per connector** (`Portarr.Providers.Docker`, `Portarr.Providers.Prometheus`…), compiled into the application. Loading external DLLs may come later at little cost, since connectors are already isolated. No plugin marketplace or third-party contributor model is designed for now. | Solo project; maximum decoupling is the goal, a plugin ecosystem is not. |
| ✅ D-36 | **Configuring tools is allowed, tool logic is not.** A connector may push configuration to its tool through the tool's API or configuration files (alert rules, scrape targets, a backup policy…): this is the preferred way to get the right information. What is forbidden is re-implementing in Portarr business logic tightly coupled to a tool (own threshold evaluation, own query engine). Any tool-specific code stays inside its connector library; the Core knows only capabilities. Refines D-30 and D-31. | Portarr steers the tools, it does not replace them. |
| ✅ D-37 | **Alerts are evaluated by the tools** (Prometheus/Alertmanager, Kubernetes, backup tools…) and received by Portarr, which links them to apps, stacks and environments. Rules can be edited from Portarr only when the connector can push them to the tool (D-36). Portarr's own signals are limited to orchestration: combining reported states (a stack degraded because a critical member is down). | No second alerting engine; one source of truth per rule. |
| ✅ D-38 | **The collector kit is not part of Portarr.** The product documents, per connector, what must be installed and how to connect it; deploying the tools is each operator's own IaC (for this project: the private config repo). The only compose files in the repo are test fixtures (E2E stack) and a minimal example. | An installation kit would mirror one particular infrastructure; Portarr stays generic. |
| ✅ D-39 | **Forge: GitHub only** for v1, organization repositories included (private ones through a read-only token). Version and vulnerability information comes from Dependabot and the GitHub APIs. | Single forge actually used; the forge connector stays behind a capability so another one can be added later. |
| ✅ D-40 | **The hypervisor is a first-class environment**: its own resources, its VMs and its containers are shown like any other environment. v1 reference: Unraid (dogfooding); Proxmox later. If the hypervisor goes down, Portarr goes down with it: accepted, the outage is obvious anyway (no external heartbeat required). | It is the top of the hierarchy and where Portarr runs (D-34). |

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

1. **Pull rather than push**: Portarr polls the APIs at regular intervals, with a cache. No mandatory agent. ❓ Q-20
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
| Backups | To be chosen: study in progress (§9.15, Q-50) | ❓ | No single tool covers every case: a study is needed |
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

**E-04 · Infrastructure → Workloads**
- Portainer-style table: name, type, environment, image:tag, status, uptime, CPU, RAM, restarts, linked app.
- Detail: inspect (ports, volumes, masked environment variables, labels), logs, metrics, actions.

**E-05 · Issues**
- Prioritized list: severity, entity, description, since when, source. Actions: open the entity, snooze, acknowledge.
- Grouping by root cause ("hypervisor-1 unreachable → 12 related issues").

**E-06 · Maintenance**
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
- **Rules**: several URLs per app (Public, LAN, Admin…); default URL chosen according to the client's network ❓ Q-07.
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
| F-SET-05 | **Users & Auth**: OIDC, local account, roles ❓ Q-03 / Q-04 | v0.1 |
| F-SET-06 | **UI**: theme, language, date format, default view mode | v0.1 |
| F-SET-07 | **First-run wizard**: create the admin, add a first provider, import a catalog | v0.2 |

### 9.13 F-API — API and integrations

| ID | Feature | Phase |
|---|---|---|
| F-API-01 | REST API `/api/v1`, API key (`X-Api-Key` header), published OpenAPI spec | v0.1 |
| F-API-02 | Documented SignalR hub for real-time clients | v0.3 |
| F-API-03 | **MCP server**: an AI assistant can query Portarr ("what is down?") and, if authorized, act ❓ Q-32 | Later |
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

### 11.2 Authentication and roles 🟡

- Modes: **OIDC** (recommended), **local account** (form), **none** (trusted network, discouraged, with a warning).
- Proposed roles ❓ Q-04:

| Role | Sees | Can act | Use |
|---|---|---|---|
| `Admin` | Everything, secrets included (audited) | Everything, including Settings | The operator |
| `Operator` | Everything except secrets | Workload actions, maintenance, acknowledgment | A possible co-administrator |
| `Observer` | Health, metrics, topology, issues, apps; **without** logs, environment variables, internal IPs or security details ❓ Q-46 | Nothing | Showing off one's infra (live demo) |
| `Guest` | Only the apps shared with them (compact launcher) | Nothing | Sharing a few links |

- **Masking for observers** 🟡: a "showcase" mode that automatically replaces sensitive values (IPs, internal host names, domains) with aliases, so you can open your real Portarr without exposing anything. Complementary to demo mode (F-DEMO), which uses fake data.
- A **role's scope** can be restricted to stacks or tags (e.g. a `Guest` only sees the `shared` tag).
- Role mapping from OIDC groups or claims.
- Scoped API keys (read-only / full).

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
| Q-04 | Roles | Which permission levels are actually useful? | 💬 Several levels wanted; Admin / Operator / Observer / Guest proposal (§11.2) pending validation |
| Q-05 | Launcher | How to navigate between stacks: tabs, sections, pages, customizable dashboard? | 💬 The launcher is secondary (D-10); the home page becomes the Overview (Q-43) |
| Q-06 | Mobile | What use on a phone? | ❓ |
| Q-07 | URLs | How to automatically pick the LAN or public URL? | ❓ |
| Q-08 | Catalog | Can the UI edit the catalog, or is it *as code* only? | ✅ Both (D-19); an app managed through config is read-only in the UI |
| Q-09 | Catalog | Priority between sources in case of conflict | ❓ |
| Q-10 | Actions | Which actions from the first version, and which ones never? | ✅ None: Portarr never acts on the infrastructure (D-31) |
| Q-12 | Status | How to distinguish "stopped on purpose" from "went down"? | 💬 F-HLT-09 approach: any shutdown with no recorded action or maintenance is "unexpected" |
| Q-14 | VM | Which view of a VM matters: inside, hypervisor, or both? | ✅ Both, correlated (D-11) |
| Q-15 | Metrics | How to express CPU (cores, %, relative to what)? | ✅ vCPU and RAM consumed, everywhere (D-33) |
| Q-16 | Logs | How much history in the live view; do we need logs persisted outside Loki? | ❓ |
| Q-17 | Notifications | Priority of notifications in the roadmap | ✅ High: email from v0.3 (D-10) |
| Q-20 | Collection | Will an agent ever be needed (unreachable hosts, NAT, VPS)? | 💬 Concrete case: a worker VPS connected through a tunnel. Approach: collect via the cluster (§4.4) rather than an agent |
| Q-21 | Extensibility | Providers compiled into the application or dynamically loaded plugins? | ✅ One class library per connector, compiled in (D-35) |
| Q-22 | systemd | Should services outside containers be monitored, and how? | ❓ |
| Q-23 | Data | Dapper + FluentMigrator (like Sonarr) or EF Core? | ✅ Dapper + FluentMigrator (D-28) |
| Q-24 | UI | Faithful *arr look or a modern design system? | ❓ |
| Q-25 | Security | Does Portarr embed Trivy, or require an external Trivy server? | ❓ |
| Q-26 | Updates | Are host OS packages in scope? | ❓ |
| Q-27 | Drift | Should we detect what runs outside IaC? | ❓ |
| Q-28 | Updates | Should Portarr open PRs on IaC repos (Renovate's role)? | ✅ Not in v1 (D-16); possible evolution |
| Q-29 | Security | Should exposure be mapped (ports, apps without authentication)? | ❓ |
| Q-30 | Metrics | Do we need business widgets per app? | ❓ |
| Q-31 | *arr | Do we need a specific integration with the *arr apps (queue widgets…)? | ❓ |
| Q-32 | AI | Is an MCP server part of the vision? | ❓ |
| Q-40 | Name | Are the existing `portarr` namesakes (Plex portal, qBittorrent utility) acceptable? | ❓ |
| Q-41 | Backups | What are backups made (or will they be made) with, and to where? | ❓ |
| Q-42 | Databases | Which engines, how they run (dedicated container per app, shared server), and how should Portarr discover them? | ❓ |
| Q-43 | Home page | Is the home page the Overview (global view) or the launcher with a status banner? | ❓ |
| Q-44 | Summary | What cadence and what content for the periodic summary? | ❓ |
| Q-45 | Backups | Are restore tests part of the need? | ❓ |
| Q-46 | Observer | What is an observer allowed to see of your real system, and what must stay hidden? | ❓ |
| Q-47 | Desktop | For a desktop app, what do we want to track: only the repository (dependencies, releases), or also the version actually installed on workstations? | ✅ Developer view only: dependencies, vulnerabilities, library evolution, framework end of life. No usage statistics (F-UPD-07) |
| Q-48 | Forges | GitHub only, or also GitLab / Gitea? Private repositories included? | ✅ GitHub only, private repositories included (D-39) |
| Q-49 | Tunnels | Which tunnel technology, and does it expose metrics? | ❓ |
| Q-50 | Backups | Which reference backup tool(s) (study §9.15)? | ❓ |
| Q-51 | Hypervisor | v1 reference hypervisor: the project's own (Unraid) or the most widespread (Proxmox)? | ✅ Unraid in v1, Proxmox later (D-40) |
| Q-52 | Backups | Does Portarr only observe backups, or does it also configure them? | ❓ |
| Q-53 | Rules | Do "rules" on app and stack state only trigger alerts, or also automatic actions (restart)? This touches on D-16 | ✅ Alerts only, never automatic actions (D-31) |
| Q-54 | Reverse proxy | v1 reference reverse proxy: SWAG/nginx (dogfooding) or Traefik (the most widespread)? | ❓ |
| Q-56 | Ecosystem | Does the project ship the collector kit (Ansible role, Helm, companion stack), or only document what needs to be installed? | ✅ Not shipped; documented per connector (D-38) |
| Q-57 | Alerts | Who owns the alerting rules: Portarr alone (a single place), or Prometheus/Alertmanager, whose alerts Portarr receives? | ✅ Evaluated by the tools, received by Portarr; editable only by pushing to the tool (D-37) |
| Q-58 | Hosting | Where does Portarr itself run, given that it must not go down along with what it monitors? | ✅ On the hypervisor host (D-34); going down with it is accepted (D-40) |
| Q-59 | Logs | v1 reference log backend: Loki or Elasticsearch / OpenSearch? | ✅ Loki + Prometheus + Grafana (D-22); Elasticsearch / OpenSearch as a later provider |
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
