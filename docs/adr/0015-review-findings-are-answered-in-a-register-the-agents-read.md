# 0015 — Review findings are answered in a register that the agents read, and a serious finding that is not fixed is the maintainer's to accept

- **Status**: proposed (accepted when this pull request is merged, which only the maintainer does: CLAUDE.md rule 13)
- **Date**: 2026-11-12

## Context

Five review agents read every pull request (and CodeQL and SonarQube read the code). They are made to find defects, so they always find something, they remember nothing from one round to the next, and they sometimes contradict each other. The answers of the author lived only in the pull request's description, which the agents do not read, so the same findings came back in every round, and the author was tempted either to follow every finding blindly or to set aside a serious one without anyone noticing. The same protocol has been running in Orchestrarr since 2026-11-10 (its ADR 0022).

## Decision

1. **Three answers, one table per pull request.**

   | Answer | When | Who decides |
   |---|---|---|
   | **fixed** | The finding is right | The author |
   | **declined** or **deferred**, rated medium, minor or low | A preference, a hardening beyond the threat model, a defect that does not occur, a duplicate: the reason is written | The author |
   | **declined** or **deferred**, rated **blocker, critical, high or major** (or a CodeQL security alert) | The author does not want to fix a serious finding | **The maintainer**, in a comment on the pull request, before it is final |

2. **A register in the repository**: `docs/review-decisions/<pr number>.md`, one entry per finding in a fixed shape ([the README of the folder](../review-decisions/README.md)). The agents read it ("Decisions already taken" in their instructions) and do not raise again a finding that an entry answers, unless the code changed so that the reason no longer holds. A serious entry without a valid approval is not trusted.
3. **A check, not only a promise.** The `review-decisions` job fails a pull request that adds or changes a serious entry that is not fixed and does not cite a comment of an approver (`.github/review-approvers`). It runs the checker and the list of approvers **of the base branch**: a pull request cannot approve itself.
4. **The rules that judge are the maintainer's tier**: the README of the folder, the checker, the approvers file, the job, the agents' instructions and `CLAUDE.md` are review contract files (CLAUDE.md rule 13). The entries themselves are written by the author of each pull request; the job checks them. An approval is a comment of an approver **on that pull request that names the finding** ("OK to decline R-36-2").
5. **A fifth agent, `spec-reviewer`**, checks that the documentation (README, spec, ADRs, verticals, connector pages) says what the code does, in both directions.

## Consequences

- The agents' noise falls, and the author keeps the last word on everything that is not serious, so a round no longer chases a cosmetic point.
- A serious finding cannot be set aside silently: the maintainer sees each one, and the job proves it.
- An entry answers the same finding at the same place and nothing wider: it is not a standing waiver for a component.
- The job cannot know the level the agent gave: it is the author's word in the heading, so the maintainer reads the table of the pull request. A serious finding rated lower there is a breach of the protocol that no job catches.
- The agents still judge differently from one run to the next, since they are models: the register reduces the noise, it does not remove it.
- Until this pull request is merged, the base branch has no checker and the job passes with a notice.
