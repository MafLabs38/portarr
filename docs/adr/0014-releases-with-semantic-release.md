# 0014 — Releases are decided by semantic-release from main

- **Status**: accepted
- **Date**: 2026-11-01
- **Spec**: D-109 (refines the release line of IMPLEMENTATION_PLAN §1.2)

## Context

Until now the pipeline published an image per branch (`develop`, `main`) and per commit, versioned `0.0.0-<tag>`. There was no version, no tag and no release notes. The project is about to ship its first usable build, and the person who runs it wants every release to be versioned by the pipeline, not by hand, and to land on the cluster without anyone typing a version.

Constraints:

- `main` is protected: no direct push, even for the pipeline. Whatever releases cannot commit a changelog or a version file back to `main`.
- Feature branches squash-merge into `develop`, so each commit on `develop` carries one Conventional Commit message (`feat(…)`, `fix(…)`, `chore(…)`).
- Portarr is below 1.0: the first release is **0.1.0**, and an incompatible change must not jump to 1.0.0 by accident.

## Decision

1. **semantic-release runs on `main` only**, as a job of the CI workflow after every test, scan and the end-to-end smoke test have passed. It reads the Conventional Commits since the last `v*` tag, picks the next version, creates the tag and the **GitHub release**, whose notes are the changelog. Nothing is committed back to `main`: the version lives in the tag.
2. **Release rules** (`.releaserc.json`): `feat` is a minor, `fix`, `perf` and `revert` a patch, a breaking change is a **minor** while the project is below 1.0 (the rule goes away at 1.0). Everything else (`chore`, `docs`, `test`, `refactor`, `style`, `ci`) does not release.
3. **The first release is 0.1.0.** A seed tag `v0.0.0` sits on the initial commit of `main`; semantic-release counts from it.
4. **A release pull request `develop → main` is merged with a merge commit, not squashed.** A squash would turn 70 commits into one message and hide every `feat` and `fix` from the analyzer. With a merge commit, `main` contains the commits themselves, and the tag stays reachable from `main` for the next release; `develop` does not need `main` merged back.
5. **The version is `X.Y.Z` plus a build number**: the images are built with `VERSION=X.Y.Z.<run number>` (the application shows `0.1.0.123`) and tagged `X.Y.Z` and `latest`. semantic-release only knows three numbers; the fourth is the CI run that built the image, and answers "which build is this".
6. **Three images are published together under that version**: `portarr`, `portarr-reader` and `portarr-agent`. A run that finds nothing worth releasing publishes the `main` and commit tags only, and the version output is empty.
7. A run on `main` is **never cancelled** by the next push (the other branches still are): a tag created and an image not built is the one inconsistent state to avoid.

## Consequences

- A release is a pull request `develop → main` that a person reviews and merges; everything after the merge is automatic. Release notes are readable on the releases page, not in a `CHANGELOG.md`.
- The Conventional Commit title of every pull request now decides the version: a `feat` that is really a `fix` over-bumps. The PR template says so.
- A merge commit makes `main` a little noisier than a squash would. It is the price of letting the analyzer see each change.
- If the release succeeds and the image build fails, the tag exists without an image. Re-running the failed `image` job in the same run fixes it (the version output is kept).

## Alternatives considered

- **Pre-releases `-beta.N` on `develop`** (the plan's first wording). They need `main` merged back into `develop` after every release, a second pull request per release, for tags nobody deploys. Dropped; `develop` keeps publishing its `develop` and commit images.
- **A `CHANGELOG.md` and a version bump committed by the pipeline** (`@semantic-release/git`). Impossible with the no-direct-push ruleset, and a bypass for a bot is a hole worth not digging.
- **Squash merge of the release pull request.** Hides the commits from the analyzer.
- **Release-please** (a release PR maintained by a bot). A fine tool, but a second pull request flow on top of ours; semantic-release does exactly what the pipeline already does elsewhere in the organization.
