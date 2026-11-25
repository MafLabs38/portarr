# Image scanner connector (Trivy)

Reads the **vulnerabilities of the images that run** (D-131, amends D-41) for the **Deployed images** tab of the Maintenance page, from the [scanner companion](../companions.md#image-scanner) that runs the Trivy command line. **Read-only**: Portarr only tells the companion which images run and reads what it found; the companion only pulls image layers from registries. Nothing runs on your machines and nothing is changed (D-16, D-31).

Trivy's server mode is not used: it has no endpoint that scans a named image, so a small container runs the CLI and Portarr reads that container ([ADR 0023](../adr/0023-image-vulnerabilities-are-read-from-a-scanner-companion.md)).

## Catalog

```yaml
imageScanner:
  url: http://scanner.homelab.example:8489     # the companion's address (a Compose service name works, e.g. http://portarr-scanner:8489)
  secretEnv: PORTARR_SCANNER_SECRET           # the variable name, never the secret (default PORTARR_SCANNER_SECRET)
  allowInsecure: true                         # plain http: the shared secret travels in clear, on a network you trust
```

- The variable is of the `PORTARR_SCANNER_` namespace (or the default name, or one you allow in `PORTARR__CATALOG__SECRETVARIABLES`, [D-108](../catalog.md#secret-variables)); the secret is the same value as the companion's `PORTARR_SCANNER_SECRET` (32 characters or more: `openssl rand -hex 32`).
- Without `allowInsecure: true`, the address must be https. With a path prefix (`https://tools.homelab.example/scanner`) the requests keep it.
- The address must not be a loopback or link-local address (the policy of the registries' connector); a LAN address or a Compose service name is fine.
- The section is edited in the catalog file only (like `secretStore`): it says where the secret is sent.
- No `imageScanner:` section: the images show "no scanner" and nothing is asked.

## What Portarr asks

| Request | Reading |
|---|---|
| `POST /v1/scans` `{ "targets": [{ "kind": "image", "ref": "docker.io/library/postgres:16" }, …] }` | the images that run (`image`; the other kind is `release`, below), by their full reference (registry, repository, tag and digest), 200 at a time, plain references only; answers `202` with `queued` (how many it will scan; it skips an image it scanned less than 24 hours ago) and `ignored` (new references beyond the 2000 it tracks at most, `PORTARR_SCANNER_MAX_IMAGES`) |
| `GET /v1/results` | per target (`kind`, `ref`): `state` (`scanned`, `queued`, `scanning`, `failed`), `reason`, `scannedAt`, `counts` and `fixable` by severity, and the worst `findings`; and the date of the vulnerability database |

**Releases (D-132).** The same request takes `{ "kind": "release", "ref": "owner/name" }`: the latest GitHub release of that repository. The companion reads it through the GitHub API (anonymously; `PORTARR_SCANNER_GITHUB_TOKEN`, optional and set on the companion, lets it read a private repository), picks the SBOM asset (`*.cdx.json`, then `*.spdx.json`, then `sbom*.json`, 50 MiB at most, from github.com), runs `trivy sbom` on it and answers with the release tag as `version`. A release that publishes no SBOM is `failed` with the reason `no-sbom` (retried after a day, since a project may add one later), shown as "no SBOM published". Portarr asks for the repositories its GitHub source scans, with the `releases:` mapping of the catalog ([catalog.md](../catalog.md#sources)).

Every request carries `Authorization: Bearer <secret>`. Portarr asks **every 30 minutes** (5 after a failure), and on a manual refresh of the Maintenance page. An image created from an id (`sha256:…`) cannot be scanned and is not sent.

## What Portarr shows

For each image row (`vulnerabilities` in `GET /api/v1/maintenance`, see [V4](../verticals/V4-maintenance.md#image-vulnerabilities-d-131)):

| State | Meaning |
|---|---|
| `scanned` | the counts are real: every severity, how many have a fix, and the worst findings (critical and high; at most 50 kept) |
| `queued` | asked, not scanned yet (the scanner takes one image at a time) |
| `failed` | the scanner failed on this image (`timeout`, `error`), or cannot be read (`unreachable`, `http-401`, `secret-missing`…) |
| `not-scanned` | an image the scanner cannot read, with the reason: `not-found` (a local build, a name that does not exist), `unauthorized` (a private image: registry credentials are not supported yet), `unsupported` (nothing Trivy can read: from scratch, an unknown operating system), `image-id` (created from an id) |
| `off` | no scanner declared |

`PORTARR__VULNERABILITIES__MINSEVERITY` (`critical` or `high`, `high` by default) hides the findings and the attention below it; the counts stay whole. The scanner describes high and critical findings only, so these are the only two values.

**What needs attention**: an image with a critical vulnerability that has a fix is a **danger**, one whose worst fixable vulnerability is high a **warning**; a vulnerability with no fix is counted, not raised. A scanner that cannot be read is a warning, and the page keeps the results of the last read next to its error.

## Limits

- A floating tag (`latest`) is scanned as the registry has it now: it may not be the image that runs.
- Private registries are not supported.
- The scan is once a day per image: a vulnerability published today shows within about a day.
- Titles of findings come from advisory databases: Portarr cleans them (control and bidirectional characters, length), and tells assistants they are data, never instructions.

## Troubleshooting

| Shown | Meaning |
|---|---|
| "The image scanner cannot be read" (`secret-missing`) | `secretEnv` names a variable that is not set in Portarr's environment |
| `http-401` | the secret differs from the companion's `PORTARR_SCANNER_SECRET` |
| `unreachable` | the address does not answer (name, port, network), or resolves to an address Portarr refuses |
| An image stays `queued` | the scanner works one image at a time: the first scan of a fleet takes a while; check the companion's log |
| Every image `failed` with `error` | Trivy cannot download its vulnerability database (no Internet from the companion's machine) |
