# Roadmap
Last Updated: 2026-05-28

## Definition of Done
- [ ] Every concept doc is mapped to an RC or listed in Unmapped Concepts with a reason.
- [ ] Every RC has an anchor (Concept / Plan / ADR / Inline thesis).
- [ ] Every RC has a position in the prerequisite DAG (or marked parallel).
- [ ] The DAG is acyclic.
- [ ] Marketing waypoints have target RCs and rationales.

## 1.0 Thesis
Captain SDLC is a series of independently-versioned tools — like a swiss army knife — that enable creatives to smooth away the processy bits of solo Unity game development that do matter (SemVer maintenance, roadmaps, mechanical QA, contract enforcement) but don't deserve human attention. Each tool bridges idea-space to plan-space via Socratic interview. Tools eat the process; the human eats the design. — anchor: `./README.md`

## MIN PLAY Waypoint
RC: M5_RELEASE_GATES_MINIMAL. Criterion: claude-release refuses to publish a release when configured ATH smokes fail or the dependency audit reports any blocking CVE, with override recorded in commit message and trace. Proves the idea-to-plan-to-mechanical-verify-to-gated-release chain on the lightest possible payload.

## Release Candidates
| Milestone | Name | Status | Anchor | Marketing |
|---|---|---|---|---|
| M1 | CONVENTIONS_ESTABLISHED | Shipped | captain-sdlc-conventions.md | — |
| M2 | TRACE_SCHEMA_FIRST_EMITTER | Stub | trace-schema.md | — |
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
- (none configured)

## Unmapped Concepts
- `README.md` — Orientation; introduces the pipeline and positioning, not a feature.
- `vision.md` — Orientation; full picture across all milestones.
- `candidates.md` — Living backlog catalog referenced by many milestones.
- `glossary.md` — Living shared-terms reference.
- `expose.md` — Living gaps-and-ambiguities ledger.
- `open-questions.md` — Living cross-doc open-questions rollup.
- `privacy-policy-aspirational.md` — Aspirational companion to privacy-framework (M4); activates per trigger.

