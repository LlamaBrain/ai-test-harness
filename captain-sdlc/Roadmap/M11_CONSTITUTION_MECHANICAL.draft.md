# M11 — CONSTITUTION_MECHANICAL
Status: Stub
Last Updated: 2026-05-29

## Definition of Done
- [ ] constitution-check <docs-dir> reads the constitution from the canonical design doc and runs mechanical-tier invariants against the code.
- [ ] file-regex-forbid catches a planted MonoBehaviour-business-logic violation with the correct code_ref and rationale.
- [ ] asmdef-forbid-reference catches a planted forbidden asmdef reference (e.g., Runtime -> Editor).
- [ ] ast and manual tier invariants appear in the report's unchecked / manual_review_required sections (parsed, not dropped).
- [ ] A violation listed in constitution-suppressions.yaml moves to the suppressed section and is not reported as a live violation.
- [ ] Output is emitted as a Markdown report + JSON sidecar in the established shape, consumable by release-gate #3.

## Theme
The mechanical-tier constitution checker: it reads a project's architectural invariants from the canonical design doc's ## Constitution block and surfaces violations (location + rationale) so the human can fix, soften, or accept. Project-authored architectural discipline made mechanically auditable, not a generic linter. On MToolKit projects the constitution's content is largely 'conform to MToolKit', so the first checks (no-MonoBehaviour-business-logic, asmdef layering, DI discipline) are well-defined out of the box — the marquee collapse from ADR-0010.

## Goals
- The checker reads the constitution from the fenced constitution block and runs mechanical-tier invariants against the code.
- file-regex-forbid and asmdef-forbid-reference pattern kinds work end-to-end.
- ast and manual invariants surface as unchecked / manual-review, never silently dropped.
- The suppression manifest is read and respected; suppressed violations are listed separately.
- Output is consumable by release-gate #3.

## Targeted
### Constitution parsing
- [ ] Find and parse the fenced constitution block inside the ## Constitution section (schema_version + invariants[]) (seam-constitution-enforcement.md, Where the constitution lives + Minimal first cut).

### Mechanical pattern kinds
- [ ] Implement file-regex-forbid (include/exclude/forbid/where_not) (seam-constitution-enforcement.md, Pattern kinds).
- [ ] Implement asmdef-forbid-reference (from must not reference to) (seam-constitution-enforcement.md, Pattern kinds + Minimal first cut).

### Tier handling
- [ ] Run mechanical-tier invariants; surface ast as unchecked and manual as manual_review_required (seam-constitution-enforcement.md, Invariant tiers).

### Violation report output
- [ ] Emit a Markdown report + .captain-sdlc/constitution-violations.json sidecar in the established shape (seam-constitution-enforcement.md, Output).

### Suppression
- [ ] Read .captain-sdlc/constitution-suppressions.yaml (manifest-only; in-code deferred); list suppressed separately; expires_after surfaces stale suppressions (seam-constitution-enforcement.md, Suppression).

### Code-reading reuse
- [ ] Build on M3's grep+asmdef code-reading layer; no duplication, choose once use twice (seam-constitution-enforcement.md, Code-reading capability; code-reading-capability.md).

### Dogfood constitution
- [ ] Author the first real constitution (MToolKit-conformance invariants: no-MonoBehaviour-business-logic, Runtime asmdef must not reference Editor, DI-registration discipline) and run M11 against Dirigible (roadmap MToolKit re-disposition).

## Blockers & Dependencies
- **Upstream RC**: M3_CODE_READING_TIER_1 - hard: the mechanical checker is the grep+asmdef layer (DAG: M3 -> M11).
- **Upstream RC**: M5_RELEASE_GATES_MINIMAL - gate #3 consumes the violation report, but M11 ships its report independently; gate wiring follows once the JSON shape stabilizes.

## References
- seam-constitution-enforcement.md
- code-reading-capability.md
- Top-level index: `../roadmap.md`

