# Roadmap
Last Updated: 2026-05-28

## Planning Model
Two orthogonal axes (ADR-0002 — *SemVer is process, milestones are design*):
- **Versions get tags.** SemVer, assigned per commit by claude-release from Conventional Commits. Process, mechanical, continuous — not tracked in this roadmap.
- **Milestones get releases.** Design, effort-gated (ship when the work list hits zero, not on a date). Two kinds:
  - **M** — a numbered *build* milestone (a feature / capability).
  - **MRC** — a *release* milestone: a named release push the project defines. The ladder is a design choice, not a fixed scheme (a tool might run MVP → Alpha → Beta → Release; a game, Early Access → Launch → DLC).
- A milestone ships on top of whatever version the commit stream is at — it's a release push, not a tag, and can span many version bumps. Files follow `{M|MRC}{n}_{NAME}.md`.

## Definition of Done
- [ ] Every concept doc is mapped to an M build milestone or listed in Unmapped Concepts with a reason.
- [ ] Every M has an anchor (Concept / Plan / ADR / Inline thesis).
- [ ] Every M has a position in the prerequisite DAG (or marked parallel).
- [ ] The DAG is acyclic.
- [ ] Every MRC release milestone has a gate (M-cluster) and a criterion.

## 1.0 Thesis
Captain SDLC is a series of independently-versioned tools — like a swiss army knife — that enable creatives to smooth away the processy bits of solo Unity game development that do matter (SemVer maintenance, roadmaps, mechanical QA, contract enforcement) but don't deserve human attention. Each tool bridges idea-space to plan-space via Socratic interview. Tools eat the process; the human eats the design. — anchor: `./README.md`

## MIN PLAY Waypoint
RC: M5_RELEASE_GATES_MINIMAL. Criterion: claude-release refuses to publish a release when configured ATH smokes fail or the dependency audit reports any blocking CVE, with override recorded in commit message and trace. Proves the idea-to-plan-to-mechanical-verify-to-gated-release chain on the lightest possible payload.

In the orthogonal model this is the first release milestone — the MVP-equivalent push. Further release milestones (the MRC ladder, likely MVP → Alpha → Beta → Release) are added as MRC-prefixed rows in the milestone table below once their scope firms up; per ADR-0002, build (M) and release (MRC) milestones share that one table, distinguished by prefix.

## MToolKit Re-Disposition (2026-05-28)
MToolKit was ratified as a *runtime blade* of the knife (ADR-0010). Crediting the developer's existing back-tech (MToolKit + the `TemplateGameBuildScript` build template) and deferring what's premature for solo dogfooding, the 26 open milestones re-disposition four ways. Principle: **process blades detect MToolKit and lean on it when present, degrade gracefully when absent** — so these dispositions hold for MToolKit projects (Dirigible) and fall back toward Build / not-applicable for small ones (BeforeTheShade).

**Have** — capability is existing back-tech; the milestone reduces to a gate / wrapper / templatize:
- M8 EDITOR_PERF_INSTRUMENTATION — **verified present**: Dirigible `StartupProfiler` + `StartupPerformanceBudgetAsset` (configured budget) + `StartupGrade`, installer-wired and Serilog-emitted, on MToolKit's `IStartupProfiler` hook. Scope nuance: startup-phase + static-budget (not gameplay-frame, not auto-captured baseline).
- M13 CONTRACT_TESTING_MECHANISM_A — pattern proven in Dirigible (`WorkstationCapabilityRoundTripTests`) + MToolKit `SchemaHashWalker`; templatize + gate.
- M14 SAVE_MIGRATION_TESTING — **verified richer than flagged**: a mature golden-corpus harness across 12 domains (golden_*_v*.es3, drift-hash pinning, reproducible `[Explicit]` generators). Posture nuance: Dirigible *refuses* pre-current saves (`RefusedFatal`, no migration bodies) rather than migrating them — so "old saves still load" needs migration bodies (deliberately unwritten pre-1.0), while "old saves refused loudly" is already done. Migrate-vs-refuse is a per-project policy call.
- M21 CICD_HEADLESS_BUILD — `TemplateGameBuildScript` (dev/stage/prod, IL2CPP, Addressables, headless `-executeMethod`) is back-tech; add commit trigger + test-gate.

**Thin** — build a thin layer on an existing MToolKit primitive, not the generic thing:
- M2 TRACE_SCHEMA_FIRST_EMITTER — Slog JSONL emitter exists; add `schema_version` + a typed event envelope.
- M4 PRIVACY_FRAMEWORK_ACTIVATED — consent plumbing exists; classification is doc/policy (defer-leaning; no real player data in dogfooding).
- M7 BASELINE_REGRESSION_ENVELOPE — **verified**: the budget→grade comparison exists (`StartupPerformanceBudgetAsset.ComputeGrade`). The gap is the *envelope* proper — an auto-captured baseline + per-run drift tolerance (vs a static hand-set budget), and coverage beyond startup.
- M10 ARTIFACT_DIFF_RUNTIME — snapshot capture exists; build the diff.
- M11 CONSTITUTION_MECHANICAL — content = "conform to MToolKit"; enforcement collapses to asmdef / DI / no-MonoBehaviour-business-logic checks (ADR-0006).
- M15 LOCALIZATION_KEY_AUDIT — Unity Localization infra exists (7+ locales, TextSync round-trip); the missing/unused-key audit is the actual deliverable and is not yet built.
- M19 MARKETING_SCREENSHOT_HARVESTING — save-screenshot capture exists; wire to QA-smoke trace.
- M23 LIVE_OPS_CRASH_INGESTION — MToolKit error→analytics is pluggable; build the ingest-to-backlog pipe. (Defer-leaning.)

**Build** — genuine greenfield Captain SDLC seam work (the differentiated value): M3 CODE_READING_TIER_1 · M5 RELEASE_GATES_MINIMAL (the seam; MIN PLAY) · M6 DEPENDENCY_AUDIT · M9 DETERMINISTIC_REPLAY (MToolKit's command-driven arch aids it) · M12 DESIGN_CODE_DRIFT_MINIMAL · M16 BETWEEN_RELEASE_ARTIFACT_DIFF · M18 MARKETING_PATCH_NOTES_TEMPLATING · M20 MARKETING_CHANGELOG_PLAYER_FACING · M22 CICD_DISTRIBUTION_DEPLOYMENT · M27 DEFINITION_OF_DONE_END_TO_END.

**Defer** — premature for solo HITL at current scale: M17 UNITY_PACKAGE_UPGRADE_SAFETY · M24 LIVE_OPS_REVIEW_INGESTION · M25 LIVE_OPS_PERF_SAMPLES_TO_BASELINE · M26 CROSS_CHANNEL_DEDUP_CLASS_A.

Net: ~8–9 active novel milestones once back-tech is credited and the live-ops tail is deferred — concentrated in the cross-tool seams.

## Release Candidates
| Milestone | Name | Status | Anchor | Marketing |
|---|---|---|---|---|
| M1 | CONVENTIONS_ESTABLISHED | Shipped | captain-sdlc-conventions.md | — |
| M2 | TRACE_SCHEMA_FIRST_EMITTER | In Progress | trace-schema.md | — |
| M3 | CODE_READING_TIER_1 | Stub | code-reading-capability.md | — |
| M4 | PRIVACY_FRAMEWORK_ACTIVATED | Stub | privacy-framework.md | — |
| M5 | RELEASE_GATES_MINIMAL | Stub | seam-release-gates.md | — |
| M6 | DEPENDENCY_AUDIT | Stub | candidates.md | — |
| M7 | BASELINE_REGRESSION_ENVELOPE | Stub | candidates.md | — |
| M8 | EDITOR_PERF_INSTRUMENTATION | Stub | candidates.md | — |
| M9 | DETERMINISTIC_REPLAY | Stub | candidates.md | — |
| M10 | ARTIFACT_DIFF_RUNTIME | Stub | candidates.md | — |
| M11 | CONSTITUTION_MECHANICAL | Stub | seam-constitution-enforcement.md | — |
| M12 | DESIGN_CODE_DRIFT_MINIMAL | Stub | seam-design-code-drift.md | — |
| M13 | CONTRACT_TESTING_MECHANISM_A | Stub | seam-contract-testing.md | — |
| M14 | SAVE_MIGRATION_TESTING | Stub | candidates.md | — |
| M15 | LOCALIZATION_KEY_AUDIT | Stub | candidates.md | — |
| M16 | BETWEEN_RELEASE_ARTIFACT_DIFF | Stub | candidates.md | — |
| M17 | UNITY_PACKAGE_UPGRADE_SAFETY | Stub | candidates.md | — |
| M18 | MARKETING_PATCH_NOTES_TEMPLATING | Stub | candidates.md | — |
| M19 | MARKETING_SCREENSHOT_HARVESTING | Stub | candidates.md | — |
| M20 | MARKETING_CHANGELOG_PLAYER_FACING | Stub | candidates.md | — |
| M21 | CICD_HEADLESS_BUILD | Stub | candidates.md | — |
| M22 | CICD_DISTRIBUTION_DEPLOYMENT | Stub | candidates.md | — |
| M23 | LIVE_OPS_CRASH_INGESTION | Stub | seam-live-ops-ingestion.md | — |
| M24 | LIVE_OPS_REVIEW_INGESTION | Stub | seam-live-ops-ingestion.md | — |
| M25 | LIVE_OPS_PERF_SAMPLES_TO_BASELINE | Stub | seam-live-ops-ingestion.md | — |
| M26 | CROSS_CHANNEL_DEDUP_CLASS_A | Stub | cross-channel-dedup.md | — |
| M27 | DEFINITION_OF_DONE_END_TO_END | Stub | Inline | — |

## Prerequisite Chain
- M1_CONVENTIONS_ESTABLISHED → M2_TRACE_SCHEMA_FIRST_EMITTER (Trace storage needs the .captain-sdlc/ layout and schema_version policy.)
- M1_CONVENTIONS_ESTABLISHED → M3_CODE_READING_TIER_1 (Code-reading capability follows the conventions doc's tool-extension pattern.)
- M1_CONVENTIONS_ESTABLISHED → M4_PRIVACY_FRAMEWORK_ACTIVATED (Side-store and config files inherit .captain-sdlc/ conventions.)
- M1_CONVENTIONS_ESTABLISHED → M6_DEPENDENCY_AUDIT (Audit output format follows shared conventions.)
- M1_CONVENTIONS_ESTABLISHED → M15_LOCALIZATION_KEY_AUDIT (Audit follows convention pattern.)
- M2_TRACE_SCHEMA_FIRST_EMITTER → M5_RELEASE_GATES_MINIMAL (Release gates read trace events to determine inputs.)
- M2_TRACE_SCHEMA_FIRST_EMITTER → M7_BASELINE_REGRESSION_ENVELOPE (Envelope uses trace events for run identity and baseline lookup.)
- M2_TRACE_SCHEMA_FIRST_EMITTER → M9_DETERMINISTIC_REPLAY (Replay captures emit trace events for chain-walking.)
- M2_TRACE_SCHEMA_FIRST_EMITTER → M13_CONTRACT_TESTING_MECHANISM_A (First contract to validate is the trace envelope itself.)
- M2_TRACE_SCHEMA_FIRST_EMITTER → M16_BETWEEN_RELEASE_ARTIFACT_DIFF (Between-release diff is anchored by trace events at release time.)
- M2_TRACE_SCHEMA_FIRST_EMITTER → M19_MARKETING_SCREENSHOT_HARVESTING (Harvester selects screenshots by walking smoke trace events.)
- M3_CODE_READING_TIER_1 → M11_CONSTITUTION_MECHANICAL (Mechanical-tier invariants use the grep+asmdef code-reading layer.)
- M3_CODE_READING_TIER_1 → M12_DESIGN_CODE_DRIFT_MINIMAL (Drift checker uses the same code-reading layer.)
- M4_PRIVACY_FRAMEWORK_ACTIVATED → M23_LIVE_OPS_CRASH_INGESTION (Crash payloads inherit classification + side-store split.)
- M4_PRIVACY_FRAMEWORK_ACTIVATED → M24_LIVE_OPS_REVIEW_INGESTION (Review payloads carry personal/sensitive classes.)
- M4_PRIVACY_FRAMEWORK_ACTIVATED → M25_LIVE_OPS_PERF_SAMPLES_TO_BASELINE (Perf samples include pseudonymous hardware fingerprints.)
- M5_RELEASE_GATES_MINIMAL → M14_SAVE_MIGRATION_TESTING (Save migration gate is a release gate extension.)
- M5_RELEASE_GATES_MINIMAL → M21_CICD_HEADLESS_BUILD (CICD only builds release-gated commits.)
- M6_DEPENDENCY_AUDIT → M17_UNITY_PACKAGE_UPGRADE_SAFETY (Unity package upgrade is a Unity-specific subset of dependency audit.)
- M7_BASELINE_REGRESSION_ENVELOPE → M8_EDITOR_PERF_INSTRUMENTATION (Editor perf is the first concrete instance of regression envelope.)
- M7_BASELINE_REGRESSION_ENVELOPE → M25_LIVE_OPS_PERF_SAMPLES_TO_BASELINE (In-the-wild samples feed the regression envelope established in M7.)
- M9_DETERMINISTIC_REPLAY → M10_ARTIFACT_DIFF_RUNTIME (Runtime artifact diff shares the checkpoint capture mechanism with replay.)
- M16_BETWEEN_RELEASE_ARTIFACT_DIFF → M18_MARKETING_PATCH_NOTES_TEMPLATING (Patch notes are templated from between-release diffs (composited per-release changelogs).)
- M16_BETWEEN_RELEASE_ARTIFACT_DIFF → M20_MARKETING_CHANGELOG_PLAYER_FACING (Player-facing changelog turns artifact diffs into narrative.)
- M18_MARKETING_PATCH_NOTES_TEMPLATING → M20_MARKETING_CHANGELOG_PLAYER_FACING (Player-facing changelog is a templated transform of patch notes.)
- M21_CICD_HEADLESS_BUILD → M22_CICD_DISTRIBUTION_DEPLOYMENT (Distribution deploys built artifacts.)
- M23_LIVE_OPS_CRASH_INGESTION → M26_CROSS_CHANNEL_DEDUP_CLASS_A (Stack-signature dedup needs at least the crash channel emitting events.)
- M5_RELEASE_GATES_MINIMAL → M27_DEFINITION_OF_DONE_END_TO_END (DoD aggregates the minimum viable end-to-end pipeline.)
- M11_CONSTITUTION_MECHANICAL → M27_DEFINITION_OF_DONE_END_TO_END (DoD requires constitution enforcement shipped.)
- M12_DESIGN_CODE_DRIFT_MINIMAL → M27_DEFINITION_OF_DONE_END_TO_END (DoD requires design-code drift shipped.)
- M13_CONTRACT_TESTING_MECHANISM_A → M27_DEFINITION_OF_DONE_END_TO_END (DoD requires contract testing shipped.)
- M22_CICD_DISTRIBUTION_DEPLOYMENT → M27_DEFINITION_OF_DONE_END_TO_END (DoD requires CICD deployment shipped.)
- M26_CROSS_CHANNEL_DEDUP_CLASS_A → M27_DEFINITION_OF_DONE_END_TO_END (DoD includes baseline Live Ops dedup.)

## Marketing Waypoints
Parallel track, keyed to MRC release milestones (not their own versions). None configured yet.

## Unmapped Concepts
- `README.md` — Orientation; introduces the pipeline and positioning, not a feature.
- `vision.md` — Orientation; full picture across all milestones.
- `candidates.md` — Living backlog catalog referenced by many milestones.
- `glossary.md` — Living shared-terms reference.
- `expose.md` — Living gaps-and-ambiguities ledger.
- `open-questions.md` — Living cross-doc open-questions rollup.
- `privacy-policy-aspirational.md` — Aspirational companion to privacy-framework (M4); activates per trigger.

