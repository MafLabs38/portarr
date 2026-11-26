# V4 — Maintenance

> **Naming:** this page is called V4 because it was the fourth vertical built in the sandbox order; the implementation plan calls the same work **V12 Maintenance**. The page keeps its name so existing links hold.

> Status: **in progress** (sandbox, phase 1). Pulls forward plan entry V12 Maintenance ("Maintainer priorities (2026-10-30)"); the plan entry V4 Kubernetes keeps its row (its connector already shipped with V2). Features: F-SEC-04 (Dependabot and code scanning alerts), F-UPD-01 (outdated images, partial), F-UPD-04 (version inventory, deployed and available only), F-UPD-07 (first step: Dependabot pull requests waiting). Decisions: D-64 to D-67, D-75, D-130 to D-132. F-SEC-01 (first slice: image vulnerabilities). Screen: E-06, first slice.

## Need

> "A Maintenance tab to see my whole application fleet: which Git repo has which Dependabot vulnerability, which container image has a newer version, what is outdated or late. There are two notions: apps I **make** (my GitHub repos, including apps that are not deployed in my homelab: a Windows app, small utilities) and apps I **use** (containers whose images change version)."

Two halves of one question, "what is vulnerable, outdated or late?":

- **My code**: every repository the operator makes, deployed here or not. Is it vulnerable (Dependabot, code scanning, secret scanning)? Is its security scanning even turned on? Is its CI green? Are Dependabot pull requests piling up? What is its latest release?
- **Deployed images**: every image the fleet runs. Is a newer version published? How far (patch, minor, major) and since when? When Portarr cannot tell, it says why.

## Scope

| In | Out (later) |
|---|---|
| A `sources: github:` catalog section: owners to scan (users, organizations), activity filter, include and exclude lists, forks and archived switches | GitLab, Gitea and Forgejo sources; a GitHub App instead of a token |
| GitHub connector (GET only, conditional requests): visibility, language, last push, default branch, open Dependabot alerts by severity, open code scanning alerts by severity (quality rules apart), open secret scanning alerts, latest release (or tag), open Dependabot pull requests, last CI run of the default branch | Security → Vulnerabilities (E-07) as its own screen (alert details are in, D-130); closing or dismissing alerts (D-16); the repository's dependency list and library versions (F-UPD-07 proper: a dependency list read from source; the SBOM of a release is read by the scanner, D-132) |
| Links between repositories and catalog apps: `repository:` on an app, an image's OCI source label, a GHCR or Docker Hub image named like a scanned repository | Declared versions read from the IaC and drift (F-UPD-04 "declared", Q-27); the IaC file to change (F-UPD-02) |
| Registry connector (anonymous, GET and HEAD only): Docker Hub, GHCR, lscr.io, Quay, any OCI registry; tags, their dates and digests when cheap | Private registries (a pull token per registry), Helm charts, host OS packages (Q-26) |
| Image verdicts for running Docker containers and Kubernetes pods: up to date, behind (patch / minor / major / build), rebuilt (floating tag moved), floating (cannot tell), unknown (with the reason) | Per-app tag filters and ignored versions (F-UPD-01 rules); release notes links; end of life of the runtimes of apps (base images, files of repositories; F-UPD-06: the machines' platforms are done, D-119); deployment detection (F-UPD-05) |
| Vulnerabilities of the running images (D-131): an optional scanner companion runs the Trivy command line, Portarr reads it; counts by severity, those that have a fix, the worst findings, per image | Private registries; pinning the scan to the running digest of a floating tag; Security → Vulnerabilities (E-07); per-app findings; a history |
| **Maintenance** page after Data: a summary, "what needs attention" across the halves, sub-tabs *My code*, *Deployed images* and *Platforms* (D-119) | Issues and notifications (V5, V6); the "update" badge on app tiles; a timeline of releases |

## UI proposal (D-67)

One page, `/maintenance`, sidebar entry **Maintenance** after Data, three blocks:

1. **Summary tiles**, two groups. *My code*: repositories (and how many are deployed), critical and high alerts, medium and low alerts, Dependabot off (n/N), CI failing. *Deployed images*: running images, up to date, behind, major updates, cannot tell.
2. **Needs attention**: one line per kind of finding, worst first, each naming what it concerns as links (the repository's GitHub page, the failing CI run): critical/high vulnerabilities and leaked secrets (danger); CI failing, images with a new major version, Dependabot alerts off (warning); minor/patch updates, rebuilt floating tags, medium/low/quality alerts, code scanning off, Dependabot pull requests waiting, images that cannot be checked (info). Mixing both halves here is the point: the operator reads one list, then drills down.
3. **Two sub-tabs** (WAI-ARIA tabs; `?tab=images` keeps the choice in the URL):
   - **My code**: repositories in two groups, *Running in the fleet* (linked to an app or a running image) and *Not deployed here* (desktop apps, libraries, tools). One compact row each: name (link), private lock, language, the apps or images it runs as; Dependabot / Code scanning / Secrets chips (severity chips, "off" dashed and struck through, "?" when the token cannot read it); CI state (link to the run), latest release, Dependabot PRs waiting, last push. A *Details* button opens one line per alert under the row (D-130).
   - **Deployed images**: one row per running image (identical containers merged: three socket proxies are one row), worst first: verdict (icon + text), reference, current → newest of the same kind, how old that release is, or why it cannot tell; then the apps (or workloads) running it and their environments.

Why not two separate sidebar entries: the maintainer asked one question ("what needs attention?"); the summary and the attention list must mix both halves, and *arr pages use tabs for facets of one subject.

## Behavior

### Code sources (D-64)

```yaml
sources:
  github:
    owners: [example, example-org]   # users or organizations
    tokenEnv: GITHUB_TOKEN           # the variable name, never the token
    pushedWithinMonths: 6            # 0: every repository
    include: [example/old-but-important]   # always scanned (bypasses activity and ownership)
    exclude: ["example/test-*"]      # never scanned, * wildcard
    forks: false
    archived: false
```

- An organization lists `orgs/<org>/repos?type=all`; a user is listed with `user/repos` when the token belongs to them (private repositories included), else `users/<user>/repos` (public only).
- Per repository, six GET requests: Dependabot alerts, code scanning alerts, secret scanning alerts (open, 100 per page, `Link` pages followed on the API host only), latest release (else latest tag), open pull requests (counted when opened by `dependabot[bot]`), last workflow run of the default branch started by a **push** (Dependabot and pull request runs say nothing about the branch).
- **Off vs unknown**: GitHub says a feature is off in words ("Dependabot alerts are disabled", "no analysis found", "Code Security must be enabled", "Secret scanning is disabled"): the feature is **Off**, which is itself a finding. Any other refusal is **Unknown** with its reason (`http-403`, `rate-limited`): the token may not read it. Secret scanning is not shown for a private repository where it is off (it needs a paid plan there).
- Code scanning alerts are counted by **security severity** (critical, high, medium, low); a rule without one (a quality rule: error, warning, note) counts as "quality", never as a vulnerability.
- **Rate limits**: scanned every 30 minutes, 4 repositories in parallel; every answer is kept with its ETag and asked again with `If-None-Match` (a 304 costs no rate limit); calls stop when fewer than 250 requests remain in the window. A homelab scan (25 repositories) costs about 160 requests when everything changed, almost nothing otherwise, out of 5,000 per hour.
- A failed scan keeps the last repositories and shows the reason above them.
- **Token**: the sandbox uses the maintainer's `gh` session token. **Production must use a fine-grained, read-only token** limited to the scanned owners, with *Metadata*, *Contents*, *Dependabot alerts*, *Code scanning alerts*, *Secret scanning alerts*, *Actions* and *Pull requests*, all **Read-only**. Portarr only sends GET requests ([connectors/github.md](../connectors/github.md)).

### Alert detail (D-130)
- **What is kept.** For every open alert: its source (`dependabot`, `code-scanning`, `secret-scanning`), severity (none for a secret or a code quality rule), title, package and ecosystem, vulnerable range, the version that mends it (none when nobody fixed it yet), GHSA and CVE identifiers, the file (and line) it points at, its link and date. These are fields of the pages Portarr already reads: no request is added.
- **How much.** The counts cover every open alert; the resource describes the **worst 50 per feature and repository**, worst first (`alerts` next to `total` in each `dependabot`, `codeScanning` and `secretScanning`). When `total` is larger, the page says how many are shown and the rest is on GitHub.
- **The page.** A *Details* button on each repository row (WAI-ARIA disclosure) opens one line per alert under the row: severity, title (a link to the alert), package, range, fixed version or "no fix yet", identifiers, file.
- **The assistant.** `get_maintenance` gives each repository `severeAlerts`: its critical and high alerts and open secrets, at most ten, worst first, with the package, the version that mends it, the advisory (the CVE identifier, else the GHSA one) and the link. An assistant (Orchestrarr, a chat) reads *which* flaw to mend without reading GitHub itself.
- **Safety.** A secret's value is never read (`hide_secret=true`, and no field of it is mapped). A text from a third party (an advisory's summary) is cut at 200 characters; a link is kept only when it is an absolute https address.

### Linking code and apps (D-65)
A repository is **deployed** when one of these links it to the fleet:
1. `repository: owner/name` on a catalog app (explicit, wins);
2. a running image whose OCI label `org.opencontainers.image.source` is `https://github.com/owner/name`;
3. a running image `ghcr.io/owner/name` or Docker Hub `owner/name` with the same owner and name as a scanned repository.

Everything else is code the operator makes but does not run here (a Windows app, a library, a template): first-class rows, grouped apart.

### Deployed images (D-66)
- Images of **running** Docker containers and Kubernetes pods (the first container of a pod), parsed like the Docker CLI (`postgres:16` is `docker.io/library/postgres:16`).
- **Registries**, anonymously: Docker Hub through its web API (tags sorted by last update, with date and digest, outside the pull rate limit; 3 pages of 100); other registries through the OCI distribution API and the anonymous token flow of their `WWW-Authenticate` challenge (HTTPS realms only; 10 pages of 1000 tags). For the newest tag of an OCI registry, its manifest and configuration are read for its date. Each image repository is looked up every **6 hours** (1 hour after a failure), at most 25 per minute, in memory only.
- **Comparison**: a tag is read as prefix + numbers + suffix and only compared with tags of the **same shape**: `16-alpine` with `18-alpine`, never with `18`, `18.1-alpine`, `19-rc1-alpine` or `amd64-19-alpine`; `4.0.20.3014-ls1` with `4.0.20.3014-ls2` (a **build** change). The change is major, minor or patch from the first number that differs.
- **Floating tags** (`latest`, `stable`, a name without numbers): compared by **digest** when the runtime reports the running one (Kubernetes `imageID`): same digest is up to date, a different one is **rebuilt**. Otherwise through the image's OCI version label, **only when that label is itself a tag of the repository** (a base image's label, such as an OS version, would be compared with unrelated tags). Otherwise **Floating: cannot tell**.
- **Unknown**, always with a reason: started from an image id (Docker shows `sha256:…`: the repository is lost), not in the registry (built locally), registry refused anonymous access (private image), rate limited, unreachable.

### Image vulnerabilities (D-131)
- **Source**: the [scanner companion](../companions.md#image-scanner) (`scanner/`), declared by `imageScanner:` in the catalog ([connectors/trivy.md](../connectors/trivy.md), [ADR 0023](../adr/0023-image-vulnerabilities-are-read-from-a-scanner-companion.md)). It runs the Trivy command line, one image at a time, each image once a day; Portarr tells it the running images (not the ones created from an id) every 30 minutes (5 after a failure) and keeps its last answer in memory. No `imageScanner:` section: every image says `off` and nothing is asked.
- **A scanner that is down** keeps the previous results; the error (a reason code) is shown next to them (`imageScanner.error`, and the attention line `image-scanner-error`).
- **The threshold** `PORTARR__VULNERABILITIES__MINSEVERITY` (default `high`) hides the findings and the attention below it; the counts stay whole.
- **What needs attention**: `image-critical-vulnerabilities` (danger: an image with a critical vulnerability that has a fix) and `image-vulnerabilities` (warning: the worst fixable vulnerability is high), each naming the image and how many; `image-scanner-error` (warning). A vulnerability with no fix is counted, not raised.
- **The summary** gets `imagesScanned`, `imagesVulnerable` (images with a critical or high vulnerability that has a fix, at the threshold), `imageCritical` and `imageHigh` (every critical and high vulnerability of the scanned images, fixed or not).

### Release vulnerabilities (D-132)
- **Source**: the same [scanner companion](../companions.md#image-scanner), told the repositories whose latest release to read: every scanned public github.com repository that publishes a release (not archived, not a fork, not a bare tag), plus the repositories named by the catalog's `sources.github.releases` mapping (a private source repository to the public one that publishes its builds; the mapped target is not shown for itself).
- **On the page**: the repository row says which build was scanned (and where it was read when mapped), then the same severity chips and Details panel as an image; a release without an SBOM says "no SBOM published", never "no vulnerability".
- **What needs attention**: `release-critical-vulnerabilities` (danger: a critical vulnerability with a fix in the latest release) and `release-vulnerabilities` (warning: a high one with a fix); an unfixed one is counted only. Same `PORTARR__VULNERABILITIES__MINSEVERITY` threshold. Not built: a Code-tab filter for these two (they open the Code tab).

**The API shape** (`GET /api/v1/maintenance`). Each element of `images[]` has a `vulnerabilities` object; the response has an `imageScanner` object:

```json
{
  "images": [
    {
      "reference": "idp:1",
      "status": "up-to-date",
      "vulnerabilities": {
        "state": "scanned",
        "reason": null,
        "scannedAt": "2026-11-25T09:00:00+00:00",
        "counts":  { "critical": 1, "high": 14, "medium": 30, "low": 2, "unknown": 0, "total": 47 },
        "fixable": { "critical": 1, "high": 10, "medium": 0,  "low": 0, "unknown": 0, "total": 11 },
        "findings": [
          { "id": "CVE-2026-0001", "package": "openssl", "installedVersion": "3.0.1", "fixedVersion": "3.0.2",
            "severity": "critical", "title": "…", "url": "https://avd.example/cve-2026-0001" }
        ]
      }
    },
    { "reference": "local:1", "vulnerabilities": { "state": "not-scanned", "reason": "not-found", "scannedAt": null,
        "counts": { "critical": 0, "high": 0, "medium": 0, "low": 0, "unknown": 0, "total": 0 }, "fixable": { "…": 0 }, "findings": [] } }
  ],
  "imageScanner": { "configured": true, "readAt": "2026-11-25T10:00:00+00:00", "error": null, "databaseUpdatedAt": "2026-11-25T03:00:00+00:00" },
  "summary": { "imagesScanned": 1, "imagesVulnerable": 1, "imageCritical": 1, "imageHigh": 14 }
}
```

| Field | Values |
|---|---|
| `state` | `off` (no scanner declared), `scanned`, `queued` (asked, not scanned yet), `failed`, `not-scanned` |
| `reason` | `null` when scanned, queued or off. `not-scanned`: `image-id`, `not-found` (a local build, a name that does not exist), `unauthorized` (private), `unsupported`. `failed`: `timeout`, `error` (the scan failed), or the code of a scanner that cannot be read (`unreachable`, `timeout`, `http-<status>`, `secret-missing`, `invalid-response`) |
| `counts`, `fixable` | every severity, even below the threshold; `fixable` counts those that have a fix; `total` is the sum. All zero unless `scanned` |
| `findings` | at most 50, worst first (critical before high, those that have a fix first), none below the threshold; empty unless `scanned`. `severity`: `critical`, `high`, `medium`, `low` or `null`; `fixedVersion` null when no fix exists; `title` and `url` may be null; `url` is https only. `title` is text from an advisory database: show it as text, never as markup or instructions |
| `imageScanner.readAt` | when Portarr last asked the scanner (`null` before the first read, or when none is declared); `error`: a reason code when the last read failed; `databaseUpdatedAt`: the age of Trivy's database |

Each repository of the report also has `releaseVulnerabilities` (D-132): the same `state`, `reason`, `scannedAt`, `counts`, `fixable` and `findings` for the latest release read from its SBOM, plus `version` (the tag scanned) and `releaseRepository` (where it was read, the catalog's `releases:` mapping), or `null` when the repository has no release to scan; the attention kinds `release-critical-vulnerabilities` (danger) and `release-vulnerabilities` (warning) follow the images' rule (a fix exists). The attention kinds are `image-critical-vulnerabilities` and `image-vulnerabilities` (subjects: the image, with the count of its fixable critical and high vulnerabilities) and `image-scanner-error` (subject: the reason code). The MCP tool `get_maintenance` carries `images[].vulnerabilities` (the counts, `fixableCritical`, `fixableHigh` and at most ten `worst` findings), lists an up-to-date image that has a critical or high vulnerability with a fix, and says the titles are third-party text.

### Maintenance page (D-67)
As proposed above. The page is built from memory (last scan, last lookups): opening it reaches neither GitHub nor a registry; it refreshes every minute.

### Filters, freshness and refresh (D-75)
- **Filters.** Every summary figure and every line of "Needs attention" is a link (keyboard accessible: a real link, focus ring, `aria-current` on the active figure) that filters the page to the items it counts. The filter lives in the URL (`/maintenance?tab=images&filter=behind`): shareable, and each choice is a history entry, so the back button undoes it. An active-filter pill ("Showing only: Dependabot off", "22 of 25 shown") has a clear (x) button. A filter that is unknown, or belongs to the other tab, is ignored.

  | Tab | Filters (summary figure or attention line) |
  |---|---|
  | My code | `severe` (critical / high), `moderate` (medium / low), `other-alerts` (medium, low or quality), `secrets`, `ci-failing`, `dependabot-off`, `code-scanning-off`, `dependabot-prs` |
  | Deployed images | `up-to-date`, `behind` (any newer version or rebuild), `major`, `behind-minor`, `rebuilt`, `cannot-tell` (floating or unknown), `unknown` |

- **Freshness.** Above the summary: "GitHub updated 12 min ago · next in 18 min" and "Registries updated 3 h ago". GitHub is the time of the last repository scan; the registries line is the most recent image repository lookup (each one has its own 6 h cadence).
- **Refresh.** The Refresh button calls `POST /api/v1/maintenance/refresh`. Portarr answers `202` at once and rescans in the background; the button spins ("Refreshing…") while `refresh.refreshing` is true and the page asks for the report every 2 s, then shows the new data. At most one refresh is accepted per 60 s (`429` with `Retry-After` otherwise; the button counts down). A refresh ignores the cadences below, never the guards: GitHub's stop below 250 remaining requests is applied by the connector to every request, conditional requests (ETags) still make an unchanged answer free, and registry lookups stay at 25 per minute (the repositories older than the request are looked up over the following minutes, oldest first). It only reads external APIs, so it is allowed by D-31.

### Refresh cadence
| What | When | Protection |
|---|---|---|
| GitHub (repositories, alerts, releases, CI) | every 30 min, 4 repositories in parallel | ETags (`If-None-Match`: a 304 costs no rate limit); calls stop below 250 remaining requests of the window |
| Registries | each image repository every 6 h (1 h after a failure) | at most 25 repositories per minute, 3 in parallel; anonymous GET and HEAD only |
| Image scanner (D-131) | Portarr tells it the running images and reads it every 30 min (5 min after a failure); it scans each image once a day, one at a time | niced, ten minutes at most, run outside the production cluster |
| All | on request (Refresh), at most once per 60 s | the guards above still apply |

Everything is in memory: a restart rescans (the first scan takes about a minute; the scanner companion keeps its own results across a restart). The API (`GET /api/v1/maintenance`) returns `refresh.codeAt`, `codeNextAt`, `registriesAt`, `refreshing` and `availableAt`.

## Statuses
Maintenance findings are not app statuses: an app is not Down because a newer version exists. Severe vulnerabilities and failing CI become issues in V5 (F-SEC-04 rules: a critical vulnerability with a fix older than N days).

## Acceptance criteria

- [x] `sources: github:` lists the active, non-archived, non-fork repositories of the owners, minus exclusions, plus inclusions; invalid entries (a token instead of a variable name, a URL instead of `owner/name`) are catalog errors naming the key
- [x] Each repository shows its open Dependabot and code scanning alerts by severity, or **off** when GitHub says the feature is disabled, or **?** with the reason when the token cannot read it
- [x] Quality rules of code scanning are never counted as vulnerabilities
- [x] CI failing on the default branch (push runs only), latest release, Dependabot pull requests waiting, last push are shown with links to GitHub
- [x] Repositories linked to an app or a running image are "Running in the fleet"; the others "Not deployed here"
- [x] Only GET requests reach GitHub; unchanged answers are revalidated with ETags; no page link leaves the API host
- [x] Running images show current → newest of the same shape, the kind of change and the release age when the registry gives it
- [x] A floating tag is compared by digest (Kubernetes) or by a version label that is a tag of the repository; otherwise "cannot tell"
- [x] An image started from an id, built locally or private says why it cannot be checked
- [x] Each image repository is looked up at most every 6 hours; repositories at most every 30 minutes
- [x] The attention list mixes both halves, worst first, and names what each finding concerns
- [x] The *Platforms* tab lists what each machine runs (K3s, its operating system) with the newer versions that exist (the patch of the same line, then the next line), when support stops and what that deserves; a date nobody announced is shown as unknown (D-119)
- [x] Summary figures and attention lines filter the page (URL query string, pill with a clear button); the page says when GitHub and the registries were read; a rate-limited manual refresh (D-75)
- [x] Integration tests of the tools over HTTP with a fake code source and a fake registry (`MaintenanceToolsFixture`), and an E2E test of the page (`maintenance.spec.ts`)

## First run on the maintainer's fleet (sandbox, 2026-10-30)

| | |
|---|---|
| Repositories scanned (2 owners, pushed within 6 months) | 25 (9 running in the fleet, 16 not deployed: a desktop app, tools, templates) |
| Dependabot alerts | 4 medium, in 1 repository; **Dependabot off on 22 of 25** |
| Code scanning | 25 quality alerts (no security severity) in 1 repository; **off on 24 of 25** |
| CI failing on the default branch | 7 repositories |
| Dependabot pull requests waiting | 39 in 7 repositories |
| Running images | 69 rows: 4 up to date, 13 behind (6 major), 17 floating, 35 unknown (24 started from an image id, 8 private images refused anonymously, 3 built locally) |
