# Exploit intelligence (CISA KEV and FIRST EPSS)

Two public files tell Portarr which vulnerabilities are **known to be exploited in the wild** and how **likely** the others are to be (D-133, [ADR 0025](../adr/0025-known-exploited-and-likely-exploited-vulnerabilities.md)). They enrich the findings of the [image scanner](trivy.md) for the images that run (D-131) and the latest releases of the repositories (D-132); they scan nothing themselves. They need no catalog entry and no credential, and are read only when the catalog declares an image scanner.

| File | Capability | Read |
|---|---|---|
| `https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json` | `IExploitIntelligence` (known exploited) | CISA's catalog of Known Exploited Vulnerabilities, whole (about 1,500 entries): per CVE, the day it was added (`dateAdded`), the day US federal agencies must have mended it (`dueDate`), whether ransomware campaigns are known to use it (`knownRansomwareCampaignUse`), and the vendor, product and name. `dateReleased` is the catalog's date |
| `https://epss.empiricalsecurity.com/epss_scores-current.csv.gz` | `IExploitIntelligence` (predictions) | FIRST's EPSS scores of the day, a gzip-compressed CSV of every scored CVE (about 300,000 rows): `#model_version:…,score_date:…`, then `cve,epss,percentile`. `epss` is the probability of exploitation in the next 30 days, `percentile` its rank among all; both between 0 and 1 |

## Privacy: the files are read whole

EPSS also has an API that answers for a list of ids; Portarr does not use it. Every request would tell a third party which vulnerabilities the fleet has. The whole file is downloaded instead (a few MiB), **streamed** through a gzip reader line by line, and only the rows of the CVE ids the scanner reported are kept; the reading stops once they are all found. The file is never held in memory, and no request names a CVE.

## Rules

- Read by the Maintenance monitor after the scanner, **once a day** (an hour after a failure), and kept in memory; a manual refresh of the Maintenance page does not read them again. A part that cannot be read keeps the previous data, with its error (a reason code) next to it (`threatIntel.error`, and the attention line `threat-intel-error`).
- The KEV catalog is kept whole: an id that appears between two reads is known to be exploited at once. Its **EPSS score arrives at the next daily read** (only the ids present at a read are kept). A read made before the scanner had any finding is made again as soon as there are some.
- Ids are CVE ids only (`CVE-YYYY-NNNN…`, any case, upper-cased); a finding the scanner names otherwise (a GHSA id) is not matched. Texts are cleaned of control and bidirectional characters and cut at 200 characters; scores are clamped to [0, 1], a score that is not a number is dropped.
- GET only. Each read has a 60 second time limit, body included. A redirect is followed only to an https address without credentials (the EPSS file is served through a CDN redirect), three at most. Every connection goes through the outbound address policy (no loopback, link-local or multicast address). Caps: 16 MiB for the KEV JSON, 16 MiB for the EPSS file as sent and 256 MiB once decompressed, 1,024 characters per line (a longer line is skipped).

## What it changes

- Each finding (`images[].vulnerabilities.findings[]`, `repositories[].releaseVulnerabilities.findings[]`) gets `exploited` (`{ dateAdded, dueDate, knownRansomwareUse }`, or null) and `epss` (`{ score, percentile }`, or null), see [V4](../verticals/V4-maintenance.md#known-exploited-vulnerabilities-d-133).
- The findings are ordered **known exploited first, then by severity, then by EPSS** (then those with a fix); a known exploited finding is shown whatever `PORTARR__VULNERABILITIES__MINSEVERITY`. The counts are the scanner's, unchanged.
- **Every id is matched, not only the findings kept.** The scanner describes the 50 worst findings of an image or a release, but sends the `ids` of every finding, any severity ([trivy.md](trivy.md)); KEV and EPSS are matched on all of them (on the findings' ids alone for a companion that sends no list). A known exploited id whose finding is not described is listed in `exploitedBeyondFindings` (`[{ id, dateAdded, dueDate, knownRansomwareUse, epss }]`, ransomware first, then the most likely to be exploited) on the image's `vulnerabilities` or the release's `releaseVulnerabilities`; the page shows it after the findings, the MCP digest carries ten of them with their total. `idsTruncated` says the scanner had more than 5000 ids and sent the most severe: the match is then partial, and the page says so.
- **What needs attention**: an image or a latest release with at least one id in KEV, among the findings described or beyond them, is a **danger** (`known-exploited-vulnerabilities`, each subject counting its distinct ids), **with a fix or not**: an exploited flaw must be mitigated even before anyone mends it. EPSS raises nothing; it orders.

## Settings

| Variable | Default | Purpose |
|---|---|---|
| `PORTARR__THREATINTEL__ENABLED` | `true` | `false`: nothing is fetched, the findings carry no exploitation, `threatIntel.configured` is false |
| `PORTARR__THREATINTEL__KEVURL` | CISA's address above | A mirror of the KEV JSON (an air-gapped installation), https only, without credentials |
| `PORTARR__THREATINTEL__EPSSURL` | FIRST's address above | A mirror of the EPSS gzip file, https only, without credentials |

An address that is not https, or that carries credentials, stops Portarr at startup with the variable's name.

## Limits

- A target with more than 5000 distinct ids is matched on the 5000 most severe (`idsTruncated`). A known exploited flaw beyond the 50 described findings shows its id, KEV dates and EPSS, not its package (the detail stays with the 50 worst).
- KEV lists exploitation its publisher has evidence of, anywhere; it does not say that this fleet is attacked.
- The EPSS score of a new finding arrives within a day.

## Troubleshooting

| Shown | Meaning |
|---|---|
| "The exploit intelligence cannot be read" with `unreachable` or `timeout` | no Internet from Portarr's machine, or a firewall: set `PORTARR__THREATINTEL__ENABLED=false`, or point the addresses at a mirror |
| `insecure-redirect` | a source (or a proxy) redirected to http, or to an address with credentials: refused |
| `too-many-redirects` | more than three redirects |
| `invalid-response` | a file that is not what was expected (not JSON, not gzip, no header), or larger than its cap |
| `http-<status>` | the source answered with that status |

## Contract

The capability has a contract suite in `Portarr.Test.Common` (`ExploitIntelligenceContract`) that every connector passes; the connector's own rules are tested in `KevEpssExploitIntelligenceFixture`, the monitor's cadence in `MaintenanceThreatIntelFixture` and the report in `KnownExploitedReportFixture`.
