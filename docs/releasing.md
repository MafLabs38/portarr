# Releasing

Portarr's versions are decided by [semantic-release](https://semantic-release.org) from the Conventional Commits of the history ([ADR 0014](adr/0014-releases-with-semantic-release.md), D-109). Nobody types a version.

## How a version is decided

On every push to `main`, after the whole pipeline is green, the `release` job reads the commits since the last `v*` tag:

| Commit | Version |
|---|---|
| `feat(…): …` | minor: `0.1.0` → `0.2.0` |
| `fix(…)`, `perf(…)`, `revert(…)` | patch: `0.1.0` → `0.1.1` |
| a breaking change (`feat(…)!:` or a `BREAKING CHANGE:` footer) | minor while below 1.0, major from 1.0 |
| `chore`, `docs`, `test`, `refactor`, `style`, `ci` | no release |

It then creates the tag `vX.Y.Z` and a GitHub release whose notes are the changelog. Because the title of a squash-merged pull request becomes the commit on `develop`, **the title of the pull request decides the version**: write `fix:` for a fix and `feat:` for something new.

## The build number

The images are built with the version `X.Y.Z.<n>`, where `<n>` is the number of the CI run (`0.1.0.123`). That is what the application shows in **System > Status**. It answers "which build is this"; the tags `X.Y.Z` and `latest` are what a deployment follows.

## Cutting a release

1. `develop` is green and holds what should ship.
2. Open a pull request **`develop` → `main`**. The title does not matter for the version (the commits do).
3. A maintainer reviews it and merges it with a **merge commit**, never a squash (a squash would hide every `feat` and `fix` from the analyzer: the
   first release came out empty that way). The repository allows both only because `merge.allow_merge_commit: true` is set for it in `repos/portarr.yaml`
   of the Terraform repository; every other pull request is squash-merged. If `develop` and `main` have diverged (a release squash-merged by mistake),
   a plain `develop` → `main` pull request can conflict: merge `develop` into a branch cut from `main`, resolve, and open the release pull request from
   that branch (`release/X.Y.Z`).
4. The pipeline runs on `main`: tests, scans, end-to-end smoke test, then `release` (tag and notes) and `image` (the three images under the new version).
5. A deployment that follows the version tags picks the new version up on its own ([deployment.md](deployment.md#following-the-releases)).

A run on `main` that finds nothing worth releasing (only `chore`, `docs`…) publishes no version: the `main` and commit images are built, and nothing is deployed.

## What is published

| Image | Content |
|---|---|
| `ghcr.io/<owner>/portarr` | the application |
| `ghcr.io/<owner>/portarr-reader` | the optional read-only host reader |
| `ghcr.io/<owner>/portarr-agent` | the chat's agent runner |

Tags: `X.Y.Z`, `latest`, the branch (`develop`, `main`) and `sha-<commit>`.

## The seed tag

semantic-release counts from the last tag. The repository starts from a tag `v0.0.0` on the initial commit of `main`, so that the first release is `0.1.0` and not `1.0.0`. Do not delete it.

## Checking a release without publishing

```bash
GITHUB_TOKEN=<a token> npx semantic-release --dry-run --no-ci --branches develop
```

prints the version it would choose and the notes, and publishes nothing.
