# Captain SDLC
Updated: 2026-05-28
Version: 0.1.5

Created: 2026-04-08

The nerve-center documentation for **Captain SDLC**.

> **The tools own correctness; the human owns taste.** (ADR-0012)

## What it is

A series of tools — like a swiss army knife — that enable creatives to smooth away the processy bits of software development that *do matter* (SemVer maintenance, roadmaps, mechanical QA validation, contract enforcement) but don't deserve human attention. The work still happens; the human doesn't have to do it. Each tool is independent in versioning, distribution, and adoption; they share *conventions* (trace schemas, classification primitives, fenced-block format) but not *code*. You can pick up one tool without committing to the whole pipeline.

## Mission

> *I create. The tools do process to accelerate my ability to create.*

The output of Captain SDLC isn't "safer software." It's **more creation, more often, without quality loss**. Tools eat the boring repetitive work; the developer's attention stays on the parts that need taste.

## Core principle

> *SemVer maintenance is PROCESS. Milestones are DESIGN.*

Process lives in tools (automatic SemVer bumps, conventional-commit auditing, mechanical drift detection, smoke evaluation, contract validation). Design lives in the human (which milestone gets built next, which invariants matter, what the canonical design says). The pipeline routes work to the right side.

## The repeating pattern

Every Captain SDLC tool is a **bridge from idea-space to plan-space**, mediated by Socratic interview:

- **interrogate** turns an idea into a canonical design doc.
- The roadmap flow turns a design doc set into a sequenced milestone list.
- **ATH** turns milestones into mechanically-verified behavior.
- **claude-release** turns finished work into a tracked, conventional release.

Tools live in plan-space (executing process). Humans live in idea-space (deciding what to build). The interview is the seam between them. When a proposed feature *bridges* that seam — turns more idea-work into automated plan-work — it's aligned. When it *adds ceremony within one side* — more sign-offs, more queues — it's misaligned.

## The experiential outcome

When this pipeline is doing its job:

- **QA becomes about feel.** Mechanical correctness is handled by ATH; the human's attention shifts to "does this feel right."
- **The roadmap is always ready.** Interrogate maintains the next-up state; the developer consults rather than re-deriving "what's next" from scratch each time.
- **Everything still came from the human.** No tool decides what to build. Every artifact in the pipeline is rooted in a Socratic interview the developer drove.

## Design center

- **Unity games**, solo and small-team scale, HITL throughout.
- Generalizing to other engines, autonomous agent loops, or large teams is a non-goal until those constraints stop fitting.

## Out of scope

- **Creative authoring.** No AI art, no generated copy, no narrative authoring. AI's mandate is the boring repetitive stuff, not the taste-bearing stuff. Art is creative; SemVer is process. The pipeline knows the difference.

## Status

Aspirational. This directory is the staging ground for the cross-tool vision before it gets interrogated and refined. Treat any specific claim here as provisional until it's been audited. Items here include things that are committed, things that are proposed, and things that are explicitly cut with reasoning preserved.

## Why this repo is the nerve center

Captain SDLC is currently assembled from three repos with at least one more emerging. The ATH repo (this one) is the central documentation home because:

- **Runtime is load-bearing.** Every other tool eventually defers to "does the game still work" — and that question lives here.
- **The adapter pattern is the model.** ATH's portability seam (`IAthHostAdapter`) is the closest existing analogue to the cross-tool contracts the rest of the pipeline will need.
- **It's where the dogfooding pressure is.** Real bugs surface here first; the pipeline's gaps surface alongside them.

This is not a permanent home decision. If Captain SDLC outgrows this arrangement — for instance, if the cross-tool layer becomes a tool of its own — the docs move.

## The tools

| Tool | Repo | Role |
|---|---|---|
| **interrogate** | `claude-interrogate-src` | Front end: Socratic design interview, scope decomposition, docs auditing |
| **ATH** (AI Test Harness) | `ai-test-harness` *(this repo)* | Middle: drives Unity playmode, asserts behavior via MCP-attached smokes |
| **claude-release** | `claude-release` | Back end: commit finalization, changelog, version bump, release |
| **CICD layer** *(tentative)* | TBD | Build automation, artifact storage, distribution pipeline orchestration |
| **MToolKit** | `MToolKit` | Runtime blade *(distinct class)*: canonical "sane C# Unity" foundation — DI, forward save migration, Unity Localization, structured logging, analytics. Opt-in for substantial projects; process blades detect it and lean on it when present (ADR-0010). |

Other items in `candidates.md` may also condense into new tools — Live Ops ingestion and marketing pipeline ops are the current candidates. The cross-tool seams themselves (pipeline trace, contract testing, etc.) are **not** a separate tool; their schemas live in this nerve-center repo and their implementations are distributed across the emitting tools.

**Two classes of blade.** The first four are *process* blades — they automate SDLC process around any project. MToolKit is a *runtime* blade — the canonical foundation substantial projects are built on. The classes compose: the more of the runtime blade a project adopts, the cheaper each process blade becomes, because MToolKit canonizes the very structures the process blades operate on (save migration, localization, the architectural constitution). Process blades never *require* MToolKit — they detect it and degrade gracefully on projects that don't use it (ADR-0010). MToolKit is opt-in by project scale: it's the foundation for substantial projects (Dirigible), and deliberately skipped on small ones (BeforeTheShade).

## Operating philosophy

Rules the tools and docs follow internally — extensions of the mission above:

- **HITL throughout.** The human stays in the loop on every decision that requires taste. Tools handle the parts that don't.
- **Trace, don't recompute.** Captain SDLC's cross-tool value lives in the *seams* between tools — being able to ask "which design decision introduced this regression" and get an answer rather than re-deriving the chain. Persistent traces are load-bearing.
- **Mechanical reuse over generation.** QA screenshots become marketing assets. Smoke transcripts become bug reports. Changelogs become patch notes. The pipeline reshapes existing artifacts; it doesn't generate new creative ones.
- **Cuts preserve reasoning.** `candidates.md` and `expose.md` record what was rejected and why, so cuts don't get re-litigated and so reversals are informed.
- **Tools, not modules.** Each tool ships independently. Conventions are shared, code is not.

## What's in this directory

**Orientation:**
- `README.md` — this file.
- `vision.md` — the full picture of the assembled pipeline, phase by phase, with the cross-tool seams.
- `candidates.md` — enumerated set of proposed features with status (accepted, proposed, deferred, cut) and likely home.
- `glossary.md` — shared terms across the docs set.

**Cross-cutting conventions:**
- `captain-sdlc-conventions.md` — `.captain-sdlc/` directory layout, `schema_version` policy, fenced-block convention, suppression file convention.
- `code-reading-capability.md` — three-tier capability (grep → tree-sitter → Roslyn) shared by Seams 2, 4, 6.
- `trace-schema.md` — pipeline trace event schema (Seam 1's planning doc).

**The seam planning docs:**
- `seam-design-code-drift.md` (Seam 2)
- `seam-release-gates.md` (Seam 3)
- `seam-contract-testing.md` (Seam 4)
- `seam-live-ops-ingestion.md` (Seam 5) + `cross-channel-dedup.md` (Seam 5 sub-concern)
- `seam-constitution-enforcement.md` (Seam 6)

**Privacy:**
- `privacy-framework.md` — concrete today + structural primitives.
- `privacy-policy-aspirational.md` — full policy that activates per trigger.

**Living / maintained artifacts:**
- `expose.md` — gaps and ambiguities, with resolutions tracked as they land.
- `open-questions.md` — rollup of cross-doc open questions, indexed back to per-doc sections.
- `roadmap.md` — the sequenced milestone plan (generated by interrogate's roadmap flow).
- `Roadmap/` — per-RC stubs, one per active milestone.

## What this is not

This directory is **not** the ATH Unity package's documentation. The package's user-facing docs live in `Documentation~/` and ship with the package via UPM. This directory is meta-documentation about the larger pipeline ATH participates in, and is not part of the published package.

## How to use this

If you're picking this up cold and you want to:

- **Understand the pipeline at a glance** → read `vision.md`.
- **See the full backlog including cuts** → read `candidates.md`.
- **Find out what's actually committed for ATH** → read `../ROADMAP.md`.
- **Find out what's committed cross-tool** → read `roadmap.md` once it exists.
- **Polish all of the above** → run interrogate's `audit-docs` / `redress` against this directory.

## Cross-References

- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Code-Reading Capability](./code-reading-capability.md)
- [Captain SDLC — Conventions](./captain-sdlc-conventions.md)
- [Captain SDLC — Cross-Channel Deduplication](./cross-channel-dedup.md)
- [Captain SDLC — Cross-tool Trace Schema](./trace-schema.md)
- [Captain SDLC — Exposed Gaps and Ambiguities](./expose.md)
- [Captain SDLC — Glossary](./glossary.md)
- [Captain SDLC — Open Questions Rollup](./open-questions.md)
- [Captain SDLC — Privacy Framework](./privacy-framework.md)
- [Captain SDLC — Privacy Policy (Aspirational)](./privacy-policy-aspirational.md)
- [Captain SDLC — Seam 2: Design ↔ Code Drift](./seam-design-code-drift.md)
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- No resolved decisions captured yet.

## Open Questions

- None.

## Version History

- 0.1.5 (2026-05-28): Added MToolKit as a runtime blade of the knife (ADR-0010); documented the two-class runtime/process blade model.
- 0.1.4 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
