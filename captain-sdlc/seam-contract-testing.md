# Captain SDLC — Seam 4: Cross-Tool Contract Testing
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

Planning doc for **Seam 4: Cross-Tool Contract Testing** — verifying that Captain SDLC's tools agree on the shared schemas they emit and consume, so silent drift between tools doesn't become discovered the hard way.

**Status:** Planning. The smallest of the five planning docs in this set, but the one with the largest blast radius when missing.

## Goal

When tool A emits a structured artifact (a trace event, a gate verdict, a smoke result, a drift report) and tool B consumes it, both tools must agree on the artifact's shape. Contract testing makes that agreement *checkable* — not "we trust everyone read the schema doc."

Mechanically: validate emitted artifacts against the shared schema; validate consumer parsers against the schema; fail loudly on mismatch.

## Why this matters

Captain SDLC's value lives in the seams — the pipeline trace links across tools, release gates read upstream outputs, Live Ops events route to downstream consumers. Every seam is a contract. Most contracts are documented but not enforced.

When the contract is documented and respected, the seam works. When the contract is documented and *one tool quietly drifts*, the seam still appears to work — events flow, files exist, no exception fires — but consumers misinterpret payloads in subtle ways. The kind of bug that takes a release cycle or two to surface.

This seam is the cheap insurance against that.

## Non-goals

- **Not behavioral testing.** Contract testing checks the shape of artifacts, not their semantic correctness. A trace event with all required fields populated correctly may still describe a wrong event; that's not Seam 4's concern.
- **Not runtime validation in every consumer.** Each consumer's parser may include schema validation as a defensive layer, but the *seam* is a periodic check across the system, not per-event enforcement.
- **Not version negotiation.** Schemas are versioned (per the trace schema pattern); negotiation between versions is the consumer's responsibility. Seam 4 verifies each tool's emissions match the version it claims to support.

## What's under contract

Every shared structured artifact in Captain SDLC. The current set:

| Contract | Emitter | Consumer(s) | Schema lives in |
|---|---|---|---|
| Trace event envelope | All tools | All tools | `trace-schema.md` |
| Per-kind trace event payload | Per-tool | Per-tool | Per-tool docs (referenced from trace schema) |
| Canonical design doc structure | interrogate | Seam 2 (drift), Seam 6 (constitution) | interrogate-side spec (TBD) |
| Constitution block format | interrogate | Constitution checker | `seam-constitution-enforcement.md` |
| ATH smoke result format | ATH | Release gates | ATH-side spec (TBD) |
| Drift report format | Seam 2 checker | Release gates | `seam-design-code-drift.md` |
| Constitution violation report format | Seam 6 checker | Release gates | `seam-constitution-enforcement.md` |
| Release gate verdict format | claude-release | Trace, humans | `seam-release-gates.md` |
| Per-gate config schema | Project | claude-release | `seam-release-gates.md` |
| Per-channel Live Ops payload schemas | Per-ingestor | Per-consumer | Per-channel ingestor docs (TBD) |
| Privacy classification annotation | Schema authors | Ingestors, trace, side-store | `privacy-framework.md` |
| Dedup config | Project | Interrogate backlog | `cross-channel-dedup.md` |

The set will grow as new seams accrete schemas. Seam 4 doesn't define schemas; it checks them.

## How contracts get checked

Two complementary mechanisms:

### Mechanism A — Static schema validation

Every shared artifact has a machine-readable schema (JSON Schema, OpenAPI, or similar). The contract test:

1. Loads the canonical schema from nerve-center docs.
2. Loads recent artifact samples from each emitter (trace events from the trace store, gate verdicts from claude-release outputs, drift reports, etc.).
3. Validates each sample against its schema.
4. Reports per-emitter, per-artifact validation pass/fail.

This catches drift where an emitter starts producing artifacts that don't match the schema — most commonly when a tool adds a field but doesn't update the schema, or removes a field that the schema still requires.

### Mechanism B — Parser introspection

For each consumer that parses a shared artifact:

1. Extract the parser's expected field set (which fields it reads, which it requires, which it tolerates).
2. Compare to the canonical schema.
3. Report parser-vs-schema mismatches.

This catches drift where a *consumer* expects fields that the schema doesn't promise, or fails to handle fields the schema does require. Subtler but real — consumer drift is often invisible until the emitter changes.

Parser introspection is harder than emission validation (it requires code analysis, not just sample validation), so the minimal first cut uses Mechanism A only.

## When contract tests run

Per-tool, on-PR or pre-release. Not per-event (that would be runtime validation, which is each consumer's call to make).

Concretely:
- A nightly job that runs all known contract tests against the current state of all tools.
- A pre-release gate (Gate #X in release-gates) that runs the subset of contract tests relevant to the tool being released.
- An on-demand `captain-contract-check` command for development.

Failures emit `contract.test.failed` trace events with the schema, the emitter, the violation, and a suggested resolution.

## Architecture

### Where the contract definitions live

In nerve-center docs, alongside the schemas they validate. Specifically: every doc that defines a shared artifact gets a `## Schema` section with a fenced ` ```jsonschema ` (or ` ```yamlschema `) block containing the machine-readable form.

This colocates the human-readable contract (the prose in the doc) with the machine-readable contract (the schema block) — same single-source-of-truth pattern the constitution uses.

### Where the contract checker lives

**Probably its own small tool**, since:

- It needs to read many tools' outputs (not just one).
- Its execution cadence (nightly + pre-release + on-demand) is different from any other tool.
- Its failure mode (schema mismatch) is a meta-concern that should be visible across tools, not buried inside any single one.

Provisional name: `captain-contract-check` (CLI command) or `captain-contracts` (more general tool). Form TBD.

Alternative considered and rejected: integrate contract checking into each tool's own test suite. Per-tool integration scales linearly with tool count and risks each tool checking only what it cares about — drift in the seams between tools wouldn't be caught.

### Configuration

Per-project config (`.captain-sdlc/contracts.yaml`):

```yaml
schema_version: 1
contracts:
  trace_envelope:
    enabled: true
    sample_source: ".captain-sdlc/trace/"
    max_samples: 1000
  ath_smoke_result:
    enabled: true
    sample_source: "Library/ath-smoke-results/"
  release_gate_verdict:
    enabled: true
    sample_source: ".captain-sdlc/gate-verdicts/"
  drift_report:
    enabled: false  # Seam 2 not yet emitting
```

Per-contract toggles let projects opt in as schemas and emitters land.

## Failure modes

Contract test failures fall into a few buckets:

| Failure | Likely cause | Resolution |
|---|---|---|
| Required field missing in emitted artifact | Emitter regressed; schema unchanged | Fix emitter; re-run |
| Unexpected field in emitted artifact | Emitter added field without schema update | Add field to schema (if intentional) or remove from emitter |
| Field type mismatch | Schema and emitter disagree on type | Resolve which is correct; update the other |
| Schema not found | Artifact references a schema that doesn't exist | Add the schema or remove the reference |
| Schema_version unsupported | Emitter declares a version no schema covers | Either implement the version's schema or stop emitting it |
| Consumer parser expects fields not in schema | Consumer drifted; schema may be older than parser | Update schema (if parser is right) or fix parser (if schema is right) |

The contract test doesn't decide who's right — it surfaces the disagreement. Resolution is HITL.

## Minimal first cut

If we ship the smallest useful version of this seam:

- **Mechanism A (static schema validation) only.** No parser introspection.
- **One contract:** the trace envelope. Because the trace is the most shared artifact in the pipeline, and because every other contract eventually flows through it.
- **Schemas as JSON Schema, embedded in the existing doc files** as fenced code blocks. No new schema format invented.
- **Tool is a simple CLI** that reads the trace event file(s) and validates each line against the envelope schema. Reports per-emitter pass/fail.
- **Run on demand only.** No nightly job, no pre-release gate integration yet.
- **No contract.test.failed trace events.** Output is stdout for now.

This minimum catches the single highest-blast-radius drift case (trace envelope) with minimal infrastructure. Every other contract follows the same pattern.

## Open scoping questions

1. **JSON Schema vs OpenAPI vs custom.** JSON Schema is lighter for individual artifacts; OpenAPI is heavier but covers more (HTTP-ish contracts, refs across files). Lean: JSON Schema. Revisit if cross-schema references become unwieldy.
2. **Where parser introspection eventually lives.** If/when Mechanism B is implemented, it needs language-aware parsing (Roslyn for .NET tools, TypeScript compiler for TS tools, etc.). Probably reuse the code-reading capability scoped in `seam-design-code-drift.md`.
3. **Schema evolution discipline.** Each schema has a `schema_version`. When breaking changes happen, multiple versions exist temporarily. Seam 4 needs to validate emitters against the version *they claim*, not always the latest.
4. **Contract test failures and the release gate.** A contract test failure during a release cycle: blocking, warning, or log? Lean: warning by default (release shouldn't be blocked by a meta-check), but per-project configurable.
5. **Visibility of contract drift over time.** A trend report ("which schemas have drifted in the last 90 days?") would be useful for catching slow erosion. Out of scope for first cut; aspirational.
6. **Per-tool vs central schema authority.** Most schemas live in nerve-center docs. A few (ATH smoke result format, per-channel Live Ops payloads) might be more natural to author in their owning tool. Worth deciding the boundary explicitly — current lean: nerve-center for cross-tool shared schemas; tool-local for tool-internal-only schemas.

## Definition of done

Seam 4 is shipped when:

- The trace envelope schema exists as a machine-readable artifact (JSON Schema or equivalent) embedded in `trace-schema.md`.
- A contract-check tool validates trace events against the envelope schema and reports per-emitter results.
- At least one additional contract (likely release gate verdict format) is also checked end-to-end.
- Failed contract tests produce structured output that a developer can act on.
- Per-project `contracts.yaml` toggles which contracts are checked.

Definition of done covers the seam's contract — heh — not the full set of validated schemas. The schema set grows with each new shared artifact.

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
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- **2026-05-28** — Contract testing checks artifact shape, not semantic correctness. Per-event runtime validation is each consumer's call.
- **2026-05-28** — Two mechanisms: static schema validation (Mechanism A, ships first) and parser introspection (Mechanism B, deferred — needs code-reading capability).
- **2026-05-28** — Contract checker is its own small tool, not integrated per-tool. Schema mismatch is a cross-tool meta-concern.
- **2026-05-28** — Schema definitions colocated with their docs via fenced code blocks. Same single-source-of-truth pattern as the constitution.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for Seam 4. Smallest surface of the planning set; largest blast radius when missing.
