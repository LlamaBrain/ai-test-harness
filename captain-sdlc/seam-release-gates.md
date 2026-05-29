# Captain SDLC — Seam 3: Release Gates
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

Planning doc for **Seam 3: Release Gates** — turning claude-release from a trust-based ceremony into an assertive check layer that reads outputs from other tools and refuses inconsistent releases.

**Status: Planning.** Nothing here is implemented. Treat all claims as proposals open to argument.

## Goal

Make claude-release refuse to ship a release when the upstream tools' outputs are inconsistent with what's about to be released. Specifically: smokes must be green against the commit, declared drift must be within tolerance, dependency audit must pass, the constitution must hold, and the changelog must cover the user-visible work.

The human still presses release. The gate prevents an inconsistent state from shipping *by accident* — not by design. Intentional overrides exist and are recorded.

## Non-goals

- **Not removing human judgment from release decisions.** HITL applies. Gates surface inconsistencies; humans decide whether they matter for this release.
- **Not autonomous release.** Even with all gates green, a human still confirms.
- **Not replacing claude-release's existing tooling.** Release gates are an additive check layer; commit/changelog/version-bump mechanics stay.
- **Not arbitrary CI checks.** Gates read structured outputs from the other Captain SDLC tools. Generic "did the build succeed" is CICD's concern, not Seam 3's.

## Gate inputs

What claude-release reads, in priority order:

| # | Gate | Source | Status |
|---|---|---|---|
| 1 | Smoke results pass against this commit | ATH smoke results (file + `ath.smoke.completed` trace events) | Foundational. First gate to ship. |
| 2 | No high-severity drift in regression envelope | ATH regression envelope output (`ath.regression.detected` events) | Foundational. Pairs with #1. |
| 3 | Constitution holds against current code | Constitution checker output (Seam 6, form TBD) | Blocked on Seam 6. |
| 4 | Dependency audit passes (CVE / license / staleness) | claude-release-side dependency audit (already candidates-accepted) | Independent; can ship anytime. |
| 5 | Design ↔ code drift within tolerance | Seam 2 output | Blocked on Seam 2. |

Gates 1, 2, and 4 can ship without other seams in place. The rest cascade as their dependencies land.

### Deferred to v2

- **Pipeline trace events expected for this release exist.** Originally proposed as gate #6 but cut on review: it's a contract-testing concern (Seam 4), not a release-gate concern, and it has a self-referential sequencing problem (the release event itself hasn't been emitted yet at gate-evaluation time). Let Seam 4 handle "did the tool run."
- **Changelog mentions every user-visible scoped task since last release.** Originally proposed as gate #7 but deferred: bakes in too many assumptions (interrogate emits scoped tasks with a "user-visible" flag, claude-release can match changelog entries to tasks, "since last release" is a stable concept). Revisit once those upstream contracts are real — possibly split into smaller mechanical checks then.

## Gate verdict

Each gate produces:

```json
{
  "schema_version": 1,
  "gate": "smoke_results_pass",
  "verdict": "pass | fail | warn | not_applicable | error",
  "severity": "block | warn | log",
  "required": true,
  "reason": "string — human-readable explanation",
  "inputs": ["event_id ...", "file paths ..."],
  "evaluated_at": "ISO 8601"
}
```

- `pass` / `fail` are the substantive outcomes.
- `warn` is "didn't fail but reader should look." Gate-configurable.
- `not_applicable` is "this gate doesn't apply to this release" (e.g., constitution gate when no constitution exists yet).
- `error` is "I couldn't evaluate this gate at all" (input missing, tool errored). Treated as fail unless configured otherwise.

**Interpretation of `not_applicable` depends on the gate's `required` flag:**
- `required: true` (default for enabled gates) — `not_applicable` is treated as `error`. The gate is in your config; if it can't apply, surface it. Catches "broken NA" (input missing, gate misconfigured) instead of letting it silently pass.
- `required: false` — `not_applicable` is treated as `pass`. Use only for gates with legitimate NA states (e.g., constitution gate when the project genuinely has no constitution yet, but you want the gate in config so it activates the moment one appears).

If a gate is genuinely irrelevant to a project, set `enabled: false` instead of relying on NA semantics.

Overall verdict: aggregate of all gate verdicts. `fail` if any blocking gate failed; `warn` if any non-blocking gate failed; otherwise `pass`.

## Override

A release can be cut despite a failing gate, but only with explicit override + reason recorded:

```
claude-release --force-release --override "smoke_results_pass:known flake in scene-load smoke, manually verified"
```

Override semantics:
- **Per-gate, not global.** `--override gate_a,gate_b` skips both; everything else still evaluates.
- **Only applies to currently-blocking gates.** Override is rejected if the named gate is configured `severity: warn` or `severity: log` — those aren't blocking, so overriding them is meaningless. Surfaces "you didn't actually need this override" and keeps the override list honest.
- **Reason is required.** Empty reason rejected.
- **Audit trail:** override is recorded in both the release commit message (human-readable convenience) and a `release.gate.override` trace event (the authoritative source for automated walk-backs).
- **No "force everything" flag.** If every gate fails, that's a signal the release shouldn't happen.

## Architecture

### Where gate definitions live

The **gate schema** (what gates exist, their inputs, their verdict format) lives in this nerve-center repo, alongside the trace schema. Specifically: a `gates/` subdirectory with one YAML per gate. This lets all consuming projects share the gate vocabulary.

The schema itself is versioned. Each gate definition declares `schema_version: 1` (or whatever); the per-project config pins which schema version it consumes. Mirrors the trace-schema versioning pattern — additive evolution allowed within a version, breaking changes bump it, projects don't get surprised by a new mandatory gate appearing mid-release-cycle.

The **per-project gate config** (which gates are enabled, their severities, their tolerance values, their required-ness) lives in `.captain-sdlc/release-gates.yaml` in the consuming project. Format:

```yaml
schema_version: 1
gates:
  smoke_results_pass:
    enabled: true
    severity: block
    required: true
    smoke_set: ath-smoke-fullloop
  drift_within_tolerance:
    enabled: true
    severity: block
    required: true
    perf_p95_frame_time_ms_max: 20
  dependency_audit:
    enabled: true
    severity: warn  # soft until we trust the audit output
    required: true
  constitution_holds:
    enabled: true
    required: false  # NA is OK until a constitution exists in the design doc
```

Defaults: `enabled: true`, `required: true`. Omitting either field from a gate's config gets the default.

### Where gate evaluation runs

In **claude-release** itself, as a check phase before commit-finalization. Each gate is a separately-invokable function that returns the verdict shape above; claude-release composes their results and emits the aggregate.

Why not in ATH or interrogate: the gate evaluation is a release-time concern, not a runtime or design-time concern. Reading gate outputs from the same place that decides whether to release keeps the decision boundary clear.

### How gates get their inputs

Three input mechanisms, ordered by preference:

1. **Trace events** (canonical). Each upstream tool emits its results to the pipeline trace; claude-release queries the trace for events tagged against the current commit.
2. **Direct file reads** (fallback). For tools that don't yet emit traces, claude-release reads their output files directly (e.g., ATH's smoke results JSON).
3. **Tool-specific CLI** (last resort). Invoke the upstream tool's CLI to get a fresh evaluation. Slow and brittle; only for gates that genuinely need a current evaluation.

Mechanism #1 is the goal once Seam 1 (pipeline trace) is broadly emitted. #2 and #3 are bridges until then.

### What gets emitted

Per gate evaluation:
- `release.gate.evaluated` trace event with the verdict shape above.

Per release decision:
- `release.gate.summary` trace event with the aggregate verdict.
- If overridden: `release.gate.override` trace event with the override list and reasons.

These integrate with the trace schema's event-kind taxonomy.

## Composition rules

When multiple gates produce verdicts, the aggregate is computed as:

- Any `fail` at severity `block` → overall `fail`.
- Any `error` at severity `block` (and not configured to tolerate) → overall `fail`.
- Any `fail` or `error` at severity `warn` → overall `warn`.
- All others → overall `pass`.

`not_applicable` and `pass` at any severity contribute nothing to the aggregate.

No weighted scoring, no "3 warns equal a fail," no AND/OR composition operators. Keep it simple — humans read the gate list and apply judgment.

## Minimal first cut

If we ship the smallest useful version of this seam:

- **Two gates only:** `smoke_results_pass` and `dependency_audit`.
- **No trace event emission yet** — claude-release reads ATH's smoke results JSON directly and runs the dependency audit inline.
- **No per-project YAML config yet** — gates hardcoded as "must pass." Override via `--force-release "<reason>"` (no per-gate selection).
- **No constitution, no drift, no design ↔ code, no trace integration.**

This minimum unblocks claude-release shipping its first assertive release and establishes the verdict shape. Everything else follows the same pattern.

## Open scoping questions

1. **Gate schema location and format.** Tentative: YAML-per-gate in `gates/`. Revisit if the gate set grows to where a single `gates.yaml` reads better — not load-bearing now.
2. **Releases evaluated against a commit or a range?** A release usually represents a range of commits since last release. Gates that depend on "the commit" (smoke results, drift) probably mean "the HEAD of the range." Gates that depend on a range (eventual changelog coverage gate, if revived) mean the range. Worth documenting explicitly when it matters.
3. **What about pre-release / RC builds?** Same gates, different severity defaults? Or a separate gate config profile? Lean: same gates, RC profile with softer severities.

## Definition of done

Seam 3 is shipped when:
- claude-release refuses to publish a release if smoke results report failure against the target commit.
- claude-release refuses to publish if dependency audit reports any blocking CVE.
- An override flag exists that records reason in both commit message and trace.
- Per-project gate config is read and respected (even if only `severity` is configurable initially).
- A `release.gate.summary` event is emitted to the pipeline trace.

That's intentionally short of "all seven gates working." Definition of done covers the seam's *contract*; the gate set grows with the upstream tools.

## Cross-References

- [Captain SDLC](./README.md)
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
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- **2026-05-28 (review pass):** Cut gate "Pipeline trace events expected for this release" — double-counts Seam 4 (contract testing) and has a self-referential sequencing problem.
- **2026-05-28 (review pass):** Deferred gate "Changelog mentions every user-visible scoped task" to v2 — bakes in too many upstream-contract assumptions.
- **2026-05-28 (review pass):** Gate schema is versioned (`schema_version` on gate definitions and per-project config). Mirrors trace-schema versioning.
- **2026-05-28 (review pass):** Override flag rejected when the named gate isn't currently configured `severity: block`. Overriding a warn-severity gate is meaningless and should surface as a usage error.
- **2026-05-28 (review pass):** Added `required: true|false` per-gate config. `required: true` (default) treats `not_applicable` as `error` to catch broken NA; `required: false` treats it as `pass` for legitimately-optional gates (e.g., constitution checker when no constitution exists yet).
- **2026-05-28 (review pass):** Override audit trail uses both commit message and `release.gate.override` trace event. Two audiences; trace event is authoritative.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for Seam 3.
