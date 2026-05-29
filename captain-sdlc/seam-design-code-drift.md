# Captain SDLC — Seam 2: Design ↔ Code Drift
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

Planning doc for **Seam 2: Design ↔ Code Drift** — auditing the implementation against the canonical design doc and surfacing deltas for HITL adjudication. The seam interrogate currently can't perform because it only reads docs, not code.

**Status: Planning.** Nothing here is implemented. Treat all claims as proposals open to argument.

## Goal

When the implementation diverges from what the canonical design doc says was being built (and it will — refactors, deferred scope, silent renames, abandoned approaches), surface the divergence so the human can decide what to do about it. Don't auto-fix. Don't enforce the design as a strict spec. Make drift visible while the cause is still recoverable.

The pipeline's existing audit/redress flows check docs against themselves. This seam adds the second half: code against the doc.

## Non-goals

- **Not semantic understanding of code intent.** Drift detection is structural conformance — names, surfaces, topology. If the implementation is correct but the design doc no longer reflects it, the seam surfaces the delta; what to do about it is the human's call.
- **Not auto-fixing.** Drift reports are read by humans (and the release gate). No tool edits either the design or the code to "close" drift.
- **Not enforcing the design as a strict spec.** Some drift is intentional. The seam surfaces all deltas; an authored suppression manifest lets the human mark known-and-accepted drift.
- **Not the constitution checker.** Constitution invariants are Seam 6's concern. Seam 2 and Seam 6 share their code-reading infrastructure but their checks are distinct — see "Relationship with Seam 6" below.

## Inputs

What the drift checker reads:

1. **The canonical design doc** — interrogate's authoritative output for the project. Markdown.
2. **The codebase structure** — asmdef topology, public APIs, type signatures, file layout. Does *not* need function bodies or implementation logic.
3. **The drift suppression manifest** — a project-level file (`.captain-sdlc/drift-suppressions.yaml`) listing known-and-accepted drift entries with reasons.

What it does **not** need:

- Function bodies or runtime behavior (that's ATH's territory).
- Performance characteristics (ATH).
- Constitution invariants (Seam 6).
- Code style or quality concerns (linters).

## Output

A drift report listing deltas between what the design doc names and what the code provides:

```json
{
  "schema_version": 1,
  "audited_at": "2026-05-28T17:45:12Z",
  "design_doc_path": "docs/canonical/death-rewind.md",
  "design_doc_hash": "sha256:...",
  "code_commit": "26e6d1a",
  "deltas": [
    {
      "kind": "missing_in_code",
      "severity": "major",
      "design_ref": "death-rewind.md:42",
      "named_entity": "GhostController.OnRestart",
      "expected": "public method on GhostController",
      "observed": "GhostController exists; no OnRestart method found",
      "suggestion": "Either implement OnRestart or update the design doc."
    }
  ],
  "suppressed": [
    {
      "named_entity": "IPlayerInput",
      "design_ref": "input-system.md:18",
      "reason": "Renamed to IPlayerInputSource during refactor; design doc will be updated in v0.3."
    }
  ]
}
```

Output is written to a known file (`drift.md` or `drift.json`) in the canonical docs directory, and emitted as a `design.code_drift.detected` trace event per the trace schema.

## Delta classification

### Kinds

- **`missing_in_code`** — Design names something; no matching code structure found.
- **`missing_in_design`** — Code has a public surface; the design doc doesn't mention it.
- **`renamed`** — Probable match by shape, name differs. Heuristic.
- **`refactored`** — Named entity exists in code but in a different module/location than the design indicates.
- **`stale_reference`** — Design references a file/path that no longer exists or has changed.

### Severity

- **`major`** — Public API contract broken. Design says `IFoo.Bar()` exists; code's `IFoo` has no `Bar`.
- **`moderate`** — Named module/class exists in design but not code (or vice versa); needs adjudication.
- **`minor`** — Naming mismatch, file-location mismatch, member visibility differs.
- **`informational`** — Code has private internals not reflected in design. Usually fine; surfaces for completeness.

Release-gate severity defaults: major → block, moderate → warn, minor/informational → log. Per-project overridable via the gate config.

## Suppression manifest

A delta can be acknowledged and suppressed without changing either the design or the code:

```yaml
# .captain-sdlc/drift-suppressions.yaml
schema_version: 1
suppressions:
  - named_entity: IPlayerInput
    design_ref: input-system.md:18
    reason: "Renamed to IPlayerInputSource during refactor; design update planned v0.3."
    expires_after: 2026-08-01  # optional; warns when past this date
  - named_entity: GhostController.lastRunRecording
    reason: "Private implementation detail; deliberately not in design."
    kind: missing_in_design
```

Rules:
- A suppression matches a delta by `named_entity` + (optional) `design_ref` + (optional) `kind`.
- Suppressed deltas appear in the report under `suppressed`, not as active drift.
- `reason` is required.
- `expires_after` is optional but encouraged for "we'll fix this later" suppressions — the tool warns when the date passes, surfacing forgotten cleanup.

## Architecture

### Where the drift checker lives

**Interrogate, extended.** Interrogate already owns the canonical design doc. Adding code-reading capability to it keeps the drift check colocated with the source of truth. Likely a new MCP tool: `design_code_drift_audit` (or named to match the v0.1.3 `convert`/`refresh`/`reveal`/`trace` flow nomenclature once that's understood).

Alternative considered and rejected: a standalone analyzer tool that consumes interrogate's design doc + a code-structure dump. Cleaner separation of concerns, but means interrogate has to emit a structured docs export — which it doesn't yet do. Until that export exists, interrogate is the practical home.

### Code-reading capability (the prerequisite)

Interrogate currently reads only Markdown. To check drift, it needs to read code structure. Three options, ordered by weight:

| Option | What it gives | Cost |
|---|---|---|
| **Roslyn / language-server integration** | Full AST, signatures, type-level deps, precise rename detection | Heavy. Locks interrogate to .NET-aware tooling. |
| **Tree-sitter parse** | Syntax trees across many languages; surface-level signatures | Moderate. Language-agnostic. Renames are heuristic. |
| **Grep + asmdef parse** | Named-entity existence, asmdef topology, file presence | Light. Misses renames; lots of false positives. Ships fast. |

Lean: ship the **grep + asmdef** version as minimal first cut; promote to **tree-sitter** when false-positive rate hurts; only reach for **Roslyn** if Unity-specific .NET checks become load-bearing.

**Important:** the code-reading capability is shared with Seam 6 (constitution enforcement). Both seams need the same infrastructure. Choose once, use twice.

### Relationship with Seam 6 (Constitution enforcement)

| Concern | Seam 2 (this) | Seam 6 |
|---|---|---|
| **Input** | Whole canonical design doc | Constitution section only |
| **Check type** | Structural conformance (names, surfaces, topology) | Invariant satisfaction (no MonoBehaviour business logic, no main-thread I/O, etc.) |
| **Output** | Deltas (what's missing/extra/renamed) | Violations (which rule, which code line) |
| **Code-reading needs** | Public surface + topology | Same |
| **Auto-fix?** | No | No |

They share infrastructure (code-reading) and audience (release gates + human review) but answer different questions. Both should ship under the same code-reading capability work.

## When the check runs

Three trigger points, none mutually exclusive:

1. **On demand** — `interrogate drift-audit <docs-dir>`. Always available.
2. **Pre-release** — invoked by release-gate evaluation (gate #5 in seam-release-gates).
3. **On commit** *(later)* — git hook. Surfaces drift while the cause is still in head; out of scope for minimal first cut.

## Minimal first cut

If we ship the smallest useful version of this seam:

- **Grep-based code reading only.** No AST, no tree-sitter, no Roslyn.
- **Check named-entity existence only.** Every `` `Foo` `` in the design doc with a code-context tag should grep-match somewhere in code. Anything missing → `missing_in_code` delta.
- **No rename detection, no refactor detection.** Those are heuristic and noisy; defer.
- **Suppression manifest with simple name+reason matching.**
- **Output as Markdown report** (`drift.md`) committed alongside the design doc. Trace event emission deferred until interrogate emits other event kinds.
- **No release-gate integration yet.** Release gate #5 stays "blocked on Seam 2" until the report format stabilizes.

This minimum surfaces "the design mentions X but I can't find X in code" — the most common and highest-leverage drift signal — without committing to AST infrastructure that may not pay off.

## Open scoping questions

1. **How does the design doc mark "load-bearing" vs "illustrative" entities?** A design doc names many things; not all of them are contracts. Without marking, every code example in the design generates false positives. Lean: a backtick-style convention (`` `IAthHostAdapter` `` is load-bearing; *italicized* is illustrative) — but worth confirming with the interrogate flow's actual output conventions.
2. **Code-reading capability choice.** Grep / tree-sitter / Roslyn — pick once, use for both Seams 2 and 6. Tied to the v0.1.3 `convert`/`reveal`/`trace` flows whose semantics aren't fully scoped yet; one of them may already imply a code-reading model.
3. **Suppression manifest staleness.** `expires_after` warns when overdue, but who acts on the warning? Probably a release-gate concern (gate emits warning), not Seam 2's. Worth confirming.
4. **Cross-project suppression sharing.** If multiple Unity projects use the same canonical design (shared library), do they each maintain their own suppressions or share? Lean: each maintains its own — projects diverge on what they consider accepted drift.
5. **What about the inverse — `missing_in_design`?** Code has surfaces the design doesn't mention. Often fine (private internals), sometimes a real signal (someone added a public API without updating the design). Default severity `informational`; per-project escalation if drift discipline matters.

## Definition of done

Seam 2 is shipped when:

- `interrogate drift-audit <docs-dir>` runs against a project, reads the canonical design doc, scans the codebase, and emits a drift report.
- The report classifies deltas by kind and severity.
- A suppression manifest is read and respected; suppressed deltas appear separately from active drift.
- The output format is stable enough that release-gate gate #5 can consume it.
- Code-reading uses at least the grep + asmdef level; choice of upgrade path (tree-sitter / Roslyn) is decided, even if not built.

Definition of done covers the seam's *contract*, not the full feature set. Rename detection, refactor detection, and on-commit hooks come later.

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
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- None yet captured for this seam.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for Seam 2.
