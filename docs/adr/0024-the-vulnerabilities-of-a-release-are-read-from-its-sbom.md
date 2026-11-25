# 0024 — The vulnerabilities of a release are read from its SBOM

- **Status**: proposed
- **Date**: 2026-11-25
- **Spec**: D-132; extends D-131 (the scanner companion gets a second kind of target); consistent with D-16 and D-31 (the companion reads, it never acts)

## Context

D-131 reports the vulnerabilities of **the images that run**. The operator also makes software that is not an image: a desktop application, a command line tool, a library. Nothing runs in the fleet for Portarr to read, and the code host's alerts (Dependabot, code scanning, D-64) only help when a lockfile exists and the feature is on. A self-contained desktop build also bundles things no lockfile names: the .NET runtime it ships, a native library, a media codec.

What does ship is the **release**: the build the users install. A CycloneDX (or SPDX) **SBOM** published with it lists exactly what is inside, and the scanner already knows how to turn such a list into vulnerabilities (`trivy sbom`). Many projects publish their builds in a repository other than the source (a private source, a public releases repository), so the link between the two has to be declarable.

## Decision

1. **A second target kind, `release`.** The scanner companion accepts `{ "kind": "release", "ref": "owner/name" }`: the latest GitHub release of that repository. It reads the release through the GitHub API (anonymously, github.com only; an optional token set on the companion only lifts the rate limit, so release scans are public repositories only), picks the SBOM asset (`*.cdx.json`, then `*.spdx.json`, then `sbom*.json`, then `bom.json`, 50 MiB at most), downloads it into its own data folder, runs `trivy sbom` and removes the file. A release without a usable SBOM is `failed` with the reason **`no-sbom`**, shown as "no SBOM published", never as "no vulnerability". The result carries the release tag (`version`), so the page says which build it describes. The same once-a-day rescan, one-at-a-time worker and results file as the images; the pair kind and reference names a result.
2. **Which releases are asked.** After the repository scan, Portarr asks for the latest release of every scanned public repository of github.com that has one (not archived, not a fork, a real release, not a bare tag; a private repository is asked only through a mapping, and a GitHub Enterprise Server's repositories never, since a target is only `owner/name` and could name another project on github.com). A repository whose builds live elsewhere is declared in the GitHub source: `releases: { owner/source: owner/releases }`; the result is shown on the source repository, and the releases repository is not asked for itself. Nothing is asked without a declared scanner.
3. **What the page and the attention say.** Each repository row of the Maintenance report gets `releaseVulnerabilities` (the same states, counts, fixable counts and worst findings as an image, plus the release tag and the repository the release was read from), filtered by the same `PORTARR__VULNERABILITIES__MINSEVERITY`. A critical vulnerability **with a fix** in the latest release is a danger (`release-critical-vulnerabilities`), a high one with a fix a warning (`release-vulnerabilities`), naming the repository; an unfixed one is counted only. The MCP `get_maintenance` carries them like the images'.
4. **Producing the SBOM is the project's job**, in its release workflow (a few lines; documented in [companions.md](../companions.md#image-scanner)). Components no advisory database knows (a bundled FFmpeg) can be listed for the record but are not matched: they stay a manual watch.

## Alternatives considered

- **Download and unpack the release archive**: works without any change to the project, but it moves hundreds of megabytes a day per release and needs an unzip step in the companion. A released SBOM is small, exact (it names what was bundled), and the standard supply-chain artifact.
- **`trivy repo` on the source**: needs a lockfile, misses the bundled runtime and native parts, and needs a private repository's credentials on the scanner.
- **GitHub's dependency graph**: only what a manifest declares; says nothing about what a self-contained build bundles.

## Consequences

- A desktop or tool release shows its vulnerabilities in Portarr like a running image does, as soon as its pipeline publishes an SBOM; a repository that publishes none says so.
- Portarr still never acts: the companion reads GitHub and one small file.
- The scan describes the **latest published** release, not what the users have installed.
- Not built here: SBOMs of old releases, the vulnerabilities of an app's dependency tree from source, private releases, a host other than github.com (the target names no host: a host-qualified reference is the way when GitHub Enterprise Server or another code host is wanted), a rename of the `image`-named types to `scan` ("image scan" reads as "scan target" from now on), and the watch of components with no advisory database (the security watch, a later vertical).
