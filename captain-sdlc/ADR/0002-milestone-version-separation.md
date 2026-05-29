# ADR-0002: Milestone-version separation in the roadmap data model

**Date:** 2026-05-28

## Problem

interrogate's roadmap flow originally identified Release Candidates with SemVer-shaped `version: string` fields (e.g., `0.95.0_SHOWCASE`). This conflated *milestone identity* (a design concept — "what gets built when") with *version assignment* (a process concept — "what compatibility this release breaks"). The conflation imposed an indie-game-style version-as-content-milestone planning model on every project; even users designing tooling, libraries, or infra had to fight defaults that didn't fit. Stated principle: **SemVer maintenance is PROCESS. Milestones are DESIGN.**

## Solution

Decouple them in the data model:
- `RCMetadata.milestone: number` — ordinal milestone identifier. Used for ordering and ID generation. Replaces `version: string`.
- Version assignment happens at *release* time via claude-release (Conventional Commits → deterministic SemVer bump), driven by what actually changed, not what was planned.
- Default `rcNamingScheme` is `{prefix}{milestone}_{NAME}.md`; prefix is `M` for build milestones, `MRC` for release-candidate milestones.
- Reserved slots, naming schemes, and validation all switched to milestone-based (integer).

Shipped: interrogate v0.1.6 (data-model change). Planned: v0.1.7 (MRC prefix notation, in-progress).

## Alternatives

- **Keep `version: string`, just change defaults** — Would leave the conceptual conflation in the data model; future contributors would re-encounter the bias.
- **Make version optional** — Half-fix; doesn't force the design clarity.
- **Pre-assign SemVer versions to milestones with "they're just labels" disclaimer** — User pushed back: "Version numbers is wrong. Versions are orthogonal to milestones."
