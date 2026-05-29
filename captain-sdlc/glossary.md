# Captain SDLC — Glossary
Updated: 2026-04-08
Version: 0.1.4

Created: 2026-05-28

Shared terms for the Captain SDLC docs set. Terms here recur across multiple docs and carry meaning specific to this pipeline. Generic filler omitted. Where a term has aliases or conflicting usages, both are listed.

## Pipeline concepts

**Captain SDLC** — A software development lifecycle pipeline for Unity games, designed to accelerate solo and small-team development with HITL augmentation. Composed of three tools currently, with CICD likely emerging. Value lives in the seams between tools, not the tools individually. Unity + solo-to-small-team + HITL is the design center; other engines and team sizes are out of scope. See `README.md`, `vision.md`.

**HITL** (Human-In-The-Loop) — The pipeline's stated philosophy: tools augment human judgment; the human is the safety rail. Distinct from autonomous agent loops. Justifies cuts like "AI codegen safety rails" — the H *is* the safety rail. See `README.md` § Philosophy.

**Seam** — A cross-tool boundary where Captain SDLC creates value by linking one tool's output to another tool's input. Six seams currently identified: pipeline trace, design ↔ code drift, release gates, contract testing, Live Ops ingestion, constitution enforcement. See `vision.md` § The seams.

**Phase** — A stage of the SDLC handled primarily by one tool. Current phases: Design (interrogate), Implementation + QA (human + ATH), Release (claude-release), CICD (proposed), Live Ops (likely distributed), Marketing (likely distributed).

**Nerve center** — The current home for cross-tool documentation. Presently the ATH repo (`ai-test-harness`). Not a permanent decision; moves if the cross-tool layer becomes its own tool. See `README.md` § Why this repo is the nerve center.

## Tools

**ATH** (AI Test Harness) — The Unity-driving QA tool. Drives playmode via MCP-attached smoke skills (`ath-cmd`, `ath-state`, `ath-wait`). This repo. See `README.md` § The tools.

**interrogate** — `claude-interrogate-src`. Socratic design interview, scope decomposition, docs auditing.

**claude-release** — Commit finalization, changelog generation, version bump, release publishing.

**CICD layer** — Tentative fourth tool. Build automation, artifact storage, distribution pipeline orchestration. Boundary with claude-release is an open scoping question.

**Live Ops layer** — Possibly a fifth tool, more likely distributed across existing tools. Ingests post-ship feedback into the pipeline.

**Captain SDLC layer** *(deprecated)* — Earlier placeholder name for cross-tool seams without a settled home. **Resolved:** the seams are *not* a separate tool. Schemas and conventions live in this nerve-center repo; implementations are distributed across the emitting/consuming tools. References to "Captain SDLC layer" in older notes should be read as "nerve-center docs + per-tool implementation."

## QA / runtime concepts (ATH)

**Smoke** (also: **smoke skill**, **smoke run**) — A scripted runtime assertion of expected behavior, driven by ATH against a host's running game. The `ath-smoke-fullloop` skill is the current example.

**Adapter** (full name: **host adapter**, interface: `IAthHostAdapter`) — The portability seam between ATH and the host game. The only mutation surface; host-specific knowledge belongs nowhere else in the package. Defined in the ATH package, used as a model for cross-tool contracts in `README.md`.

**Deterministic replay** — ATH capability that captures input commands (not raw input — *commands*), RNG seeds, frame counter, and host-state checkpoints so a failed smoke run can be replayed against a different build for bisection. See `vision.md` § Middle.

**Regression envelope** (alias: **envelope**, **baseline envelope**) — Per-run record of measured values (perf, alloc, asset sizes, scene-load times) that subsequent runs compare against to detect drift. Perf monitoring is its first concrete instance.

**Baseline** — The last-known-good envelope used to detect regression.

**Checkpoint** — A point in a smoke run where state is captured for artifact diff and/or replay. Shared by both features.

**Artifact diff** — Comparison of serialized state slices between runs or releases. Has two variants:
- **Runtime artifact diff** (ATH-side): scene-graph, savegame, prefab slices captured at smoke checkpoints.
- **Between-release artifact diff** (claude-release / CICD-side): what changed in the shipped binary, config, prefab files across releases.

The boundary between the two is an open scoping question — see `candidates.md` § Open scoping questions item 7.

## Design / docs concepts (interrogate)

**Canonical design doc** — The source of truth for what's being built. Every downstream phase reads it. Output of the interrogate flow.

**Constitution** — A section of the canonical design doc encoding the invariants the implementation must obey (e.g., no MonoBehaviour business logic, no synchronous main-thread I/O, no direct singletons outside composition root). Machine-readable so downstream tools can enforce. Refers interchangeably to the section and to the rules it contains; they are operationally the same thing.

**Constitution checker** — The tool that verifies the constitution holds against the code. Form TBD — Roslyn analyzer, interrogate extension, or own tool. This is the *thing*; Seam 6 below is the *activity*.

**Constitution enforcement** — Seam 6: the activity of verifying the constitution against the code. Distinct from the constitution itself (the rules) and from the constitution checker (the tool).

**Invariants** — The individual rules encoded in the constitution. When used standalone, refers to the constitution's rules unless context says otherwise.

**Design ↔ code drift** — Divergence between the canonical design doc and the implemented code. Seam 2 surfaces drift for HITL adjudication; does not auto-fix.

**Scoped task** — An implementation-sized unit produced by interrogate from a design doc. Carries a `task_id` that downstream events reference.

## Trace concepts (`trace-schema.md`)

**Pipeline trace** (also: **trace**) — Seam 1: shared cross-tool event log linking design → tasks → commit → smoke → release → post-ship issues. Backward-walkable.

**Event** — A single trace entry. One JSON object per line in the JSONL trace file.

**Envelope** — The required fields of every trace event (`schema_version`, `event_id`, `timestamp`, `tool`, `tool_version`, `kind`, `refs`).

**Refs** — The canonical references that situate an event in the pipeline (`project`, `commit`, `release`, `design_doc`, `task_id`).

**Parents** — Trace link representing direct causation. A smoke run's parent is the commit it tested. Use sparingly.

**Links** — Trace link representing soft / many-to-many references between events, with named relations (`implements`, `verifies`, `regresses`, `summarizes`, `triggers`, `replaces`, `fixes`).

**Backward walk** — Starting from a regression / bug event and following parents/links back to the design decision that caused it. The trace schema's priority-1 goal.

**Forward walk** — Starting from a design decision and finding every downstream artifact produced because of it. The trace schema's priority-2 goal.

**Kind** — A dotted-namespace event identifier (e.g., `ath.smoke.completed`, `release.gate.evaluated`). Defined per phase in `trace-schema.md` § Event kinds.

**Schema** — Used consistently in Captain SDLC for *machine-readable structural definitions* of artifacts: the trace envelope schema, gate-definition schemas, contract-test target schemas, Live Ops ingestor payload schemas, configuration-file schemas. A schema is the formal artifact (JSON Schema, YAML structure with a `schema_version` field). When the prose intent is "the loose shape" rather than the formal artifact, prefer **shape** or **format**. When the intent is the cross-tool agreement aspect, prefer **contract**. The fenced-block convention for embedding schemas in docs lives in `captain-sdlc-conventions.md`.

**Contract** — The cross-tool agreement on how an artifact is structured and used. A contract is verified by schema validation (Seam 4); the schema is the artifact, the contract is the obligation. Every shared structured artifact in Captain SDLC has both: a schema (the form) and a contract (the obligation that emitters and consumers agree on it).

## Workflow / lifecycle concepts

**Mechanical reuse** — Captain SDLC philosophy: artifacts produced by one phase are mechanically transformed and reused by other phases (QA screenshots → marketing assets; changelogs → patch notes; smoke transcripts → bug reports). Distinguished from creative authoring, which is out of scope.

**Pipeline ops** — Mechanical operations on existing artifacts (formatting, posting, templating). Used to distinguish Marketing-as-pipeline-ops (kept) from Marketing-as-content-authoring (cut).

**Marketing changelog** — Player-facing changelog derived from artifact diffs and translated via authored templates into human-readable balance/feature change narrative ("seed regen +50%", "boss HP reduced 20%"). Templated, not authored prose. Sits downstream of Artifact Diff (between-release variant), upstream of Posting Automation.

**Save migration** — The process of loading a saved game from a prior version against current code. Tested mechanically (ATH-side smoke); gated at release (claude-release-side gate).

**Ingestion** — The Live Ops pattern of piping post-ship issues (crashes, reviews, perf samples) into the design system's backlog and ATH's baselines. The pipeline's post-ship loopback.

**Balance diff** — A subset of artifact diff: changes to game-balance data files (stats, drop rates, economy values). Used by **marketing changelog** to produce player-facing change narrative. **Note:** introduced once in `candidates.md`; see `expose.md` finding #9.

## Status / process language

**Accepted / Proposed / Deferred / Cut** — Status labels in `candidates.md`.
- **Accepted**: agreed and will be roadmapped.
- **Proposed**: under consideration, not yet committed.
- **Deferred**: agreed-valuable but explicitly later, with reason.
- **Cut**: rejected with reasoning preserved.

**Considered set** — The full list of candidates including cuts. `candidates.md` is the receipt so cuts don't get re-litigated.

**Dogfooding** — Using the pipeline against a real Unity project. Currently `BeforeTheShade`. `dirigible` is the next dogfood target for save migration and localization features (see `candidates.md` § Reference projects for dogfooding).

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Code-Reading Capability](./code-reading-capability.md)
- [Captain SDLC — Conventions](./captain-sdlc-conventions.md)
- [Captain SDLC — Cross-Channel Deduplication](./cross-channel-dedup.md)
- [Captain SDLC — Cross-tool Trace Schema](./trace-schema.md)
- [Captain SDLC — Exposed Gaps and Ambiguities](./expose.md)
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

- None yet captured.

## Open Questions

- None.

## Version History

- 0.1.4 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial glossary extracted from `README.md`, `vision.md`, `candidates.md`, `trace-schema.md`.
