# M5 — RELEASE_GATES_MINIMAL
Status: Stub
Last Updated: 2026-05-29

## Definition of Done
- [ ] A release attempt with a failing configured ATH smoke against HEAD is refused (blocked, non-zero exit).
- [ ] A release attempt with a blocking CVE reported by the dependency audit is refused.
- [ ] With all gates passing, the release proceeds (human still confirms).
- [ ] --force-release --override smoke_results_pass:"<reason>" cuts the release despite that gate failing; an empty reason is rejected; overriding a non-blocking gate is rejected.
- [ ] The override is recorded in the release commit message.
- [ ] The release emits an aggregate gate summary in the standard verdict shape.

## Theme
claude-release stops being a trust-based ceremony and becomes assertive: it refuses to publish a release when the lightest upstream signals (ATH smokes, dependency audit) are inconsistent with what is shipping. This is the pipeline's MVP / MIN PLAY — idea -> plan -> mechanical-verify -> gated-release proven end-to-end on the smallest payload. The human still presses release; the gate only stops accidental inconsistent ships, with explicit recorded overrides.

## Goals
- A failing ATH smoke against the target commit blocks the release.
- A blocking CVE from the dependency audit blocks the release.
- A release can still be cut via an explicit per-gate override carrying a required, recorded reason.
- Every gate produces the standard verdict shape and the release emits an aggregate gate summary.

## Targeted
### Gate verdict contract
- [ ] Implement the per-gate verdict shape {schema_version, gate, verdict, severity, required, reason, inputs, evaluated_at} (seam-release-gates.md, Gate verdict).

### Gates (minimal cut)
- [ ] smoke_results_pass: read ATH smoke results against the target commit via direct file read (seam-release-gates.md, Gate inputs #1 + Minimal first cut).
- [ ] dependency_audit: thin inline CVE check, fail on any blocking CVE; M6 expands it to license/staleness later (seam-release-gates.md, Gate inputs #4).

### Aggregate + composition
- [ ] Compose gate verdicts via the block/warn/log rules; no weighted scoring (seam-release-gates.md, Composition rules).

### Override
- [ ] --force-release --override gate:"reason": per-gate, non-empty reason required, reject override of a non-blocking gate, record override in the release commit message (seam-release-gates.md, Override).

## Blockers & Dependencies
- **Upstream RC**: M2_TRACE_SCHEMA_FIRST_EMITTER - soft: the minimal cut bridges via direct file reads; gate trace emission (release.gate.summary/override) rides M2 when it lands.
- **Upstream RC**: M6_DEPENDENCY_AUDIT - soft: M5 ships a thin inline CVE check; M6 expands to license/staleness.

## References
- seam-release-gates.md
- Top-level index: `../roadmap.md`

