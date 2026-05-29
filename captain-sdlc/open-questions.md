# Captain SDLC — Open Questions Rollup
Updated: 2026-04-08
Version: 0.1.1

Created: 2026-05-28

A single rollup of unresolved open questions across the Captain SDLC docs set. Each doc maintains its own per-doc `Open Questions` section; this rollup surfaces the questions that span multiple docs and provides an index for the rest.

**Status:** Navigation aid. Not the authoritative location for any individual question — each question lives in its owning doc. This rollup makes cross-doc questions visible without forcing readers to scan every doc.

## Why this exists

The docs set is now 16 files. Each doc carries its own Open Questions section. Several questions span multiple docs (the answer affects more than one doc's design). Without a rollup, a reader has to scan every doc's open-questions section to find the active cross-doc ones. This doc consolidates.

## Cross-cutting open questions

Questions that genuinely span multiple docs. Each entry: the question, which docs raise it, current lean if any.

### CC-1 — Code-reading capability tier promotion triggers

**Where raised:** `code-reading-capability.md` § Open scoping questions; `seam-design-code-drift.md`; `seam-constitution-enforcement.md`; `seam-contract-testing.md`.

**The question:** All three dependent seams agree on the grep → tree-sitter → Roslyn upgrade path. What concretely triggers each promotion? "False-positive rate hurts" is the current lean but isn't measurable.

**Lean:** Tier 1 ships first; defer the trigger threshold until real usage produces a rate to measure.

### CC-2 — Trace storage cross-machine merging

**Where raised:** `trace-schema.md` § Open scoping questions; `privacy-policy-aspirational.md` § Cross-border data transfer; `seam-live-ops-ingestion.md` § Small-team aspiration; `captain-sdlc-conventions.md` § Git policy.

**The question:** Trace is `.gitignored` per machine. When CICD lands as an additional emitter, or when small-team adoption activates, traces become cross-machine and need a merge story. What's the merge model?

**Lean:** Defer until CICD or second-developer trigger fires. Current single-machine assumption explicit.

### CC-3 — `.captain-sdlc/` config files and git

**Where raised:** `captain-sdlc-conventions.md` § Git policy; `privacy-framework.md` § Trace vs side-store; `trace-schema.md` § Storage and location.

**The question:** Config files committed; trace and side-store `.gitignored`. What about per-developer overrides (a developer who wants stricter local severity than team default)? Currently unaddressed.

**Lean:** Convention is enough until a real use case surfaces; revisit at second-developer trigger.

### CC-4 — Schema versioning across config evolution

**Where raised:** `captain-sdlc-conventions.md` § The `schema_version` convention; every doc that defines a `schema_version`-bearing config.

**The question:** "Additive evolution within a version; consumers refuse unknown versions" is established. What's the migration story when a project's config is at version N and the tool requires N+1?

**Lean:** No automatic migration; HITL updates. Revisit when migration cost becomes visible.

### CC-5 — Interrogate backlog API existence

**Where raised:** `candidates.md` § interrogate; `seam-live-ops-ingestion.md`; `cross-channel-dedup.md`; `seam-design-code-drift.md`.

**The question:** Four docs lean on "interrogate's backlog." The backlog API doesn't exist on the interrogate side yet. When does it get built? What's its surface (commands, files, MCP tools)?

**Lean:** Aspirational interrogate-side feature. Authored once a Captain SDLC project actually needs it; until then, the docs that reference it inherit aspirational status.

### CC-6 — Constitution checker form

**Where raised:** `vision.md` § Seam 6; `seam-constitution-enforcement.md`; `code-reading-capability.md`; `candidates.md` § Constitution enforcement.

**The question:** The constitution checker can be a Roslyn analyzer, an interrogate extension, or its own tool. Decision blocks Seam 6 minimal first cut from going beyond `mechanical` tier.

**Lean:** Mechanical tier ships as interrogate extension. AST and Roslyn tiers decided when promoted.

### CC-7 — Trace event recovery from broken chains

**Where raised:** `trace-schema.md` § Open scoping questions; `expose.md` finding #7.

**The question:** Trace is append-only. Real-world failure modes (disk corruption, accidental deletes, gitignore-induced loss) break chains. What's the recovery / surfacing story?

**Lean:** Tolerate broken chains; surface visibly. Recovery is HITL.

### CC-8 — Side-store implementation details

**Where raised:** `privacy-framework.md` § Open scoping questions; `privacy-policy-aspirational.md` § Storage policy; `cross-channel-dedup.md` § Embedding storage.

**The question:** Side-store needs encryption-at-rest (sensitive class), reference encoding (opaque ID vs hash vs path), and retention policy. None are specified.

**Lean:** Settle when first sensitive payload schema is being designed; trigger-conditioned.

### CC-9 — Pattern language portability across code-reading tiers

**Where raised:** `code-reading-capability.md` § Open scoping questions; `seam-constitution-enforcement.md` § Pattern kinds.

**The question:** A constitution invariant written for Tier 1 (regex) doesn't translate to Tier 2 (syntax-tree query). Invariants opt into a tier — but does that mean a project's invariant set is fragmented across tiers?

**Lean:** Tier-specific patterns; invariants declare their tier. Tier promotion may invalidate some Tier 1 patterns; explicit migration.

### CC-10 — Fenced-block format standardization

**Where raised:** `captain-sdlc-conventions.md` § The fenced-block convention; `seam-constitution-enforcement.md` (` ```constitution `); `seam-contract-testing.md` (proposed ` ```contract-schema `); `seam-release-gates.md` (proposed ` ```gate-schema `).

**The question:** Tags name role (`constitution`, `contract-schema`). What about renderer compatibility (GitHub, etc.)? What about migration when an existing tag's content evolves?

**Lean:** Renderer doesn't matter for tool discovery; ignore highlighting. Content evolution follows the schema_version convention.

## Per-doc open-question index

For doc-specific questions that don't span multiple docs, the doc's own Open Questions section is authoritative. This index lists where to look.

| Doc | Approx. count | Highlights |
|---|---|---|
| `captain-sdlc-conventions.md` | 3 | Migration support, validation tooling, schema discoverability |
| `code-reading-capability.md` | 5 | See CC-1, CC-9; also caching strategy, multi-language adopters |
| `cross-channel-dedup.md` | 6 | AI embedding model, retention, cross-build dedup, frequency thresholds, misfire cost, triage workload |
| `expose.md` | ~15 unresolved | Numbered findings #3, #5–#9, #11, #12, #14, #15, plus second-pass #18, #21, #24, #25 |
| `privacy-framework.md` | 4 | See CC-3, CC-8; also side-store reference encoding, AI retention as moving target |
| `privacy-policy-aspirational.md` | 5 | Encryption key management, free-text consent UX, retention backward-compat, AI vendor diversification, aggregate re-identification |
| `seam-constitution-enforcement.md` | 5 | See CC-6, CC-9, CC-10; also constitution discovery (header vs fence), cross-project sharing, in-code suppression syntax, pattern kind upgrade ordering |
| `seam-contract-testing.md` | 6 | See CC-10; also JSON Schema vs OpenAPI, parser introspection home, schema evolution, contract-test-failures-and-the-gate, contract drift trend reports, per-tool vs central schema authority |
| `seam-design-code-drift.md` | 5 | Load-bearing vs illustrative entity marking, code-reading choice (CC-1), suppression manifest staleness, cross-project suppression sharing, missing_in_design escalation |
| `seam-live-ops-ingestion.md` | 6 | Tool-per-channel boundary, cross-channel dedup discipline (see `cross-channel-dedup.md`), AI classification accuracy, backlog state machine, rate limiting, ingestion failure surfacing |
| `seam-release-gates.md` | 3 | Gate schema location, releases evaluated against commit or range, RC builds |
| `trace-schema.md` | 6 | See CC-2, CC-4, CC-7; also schema evolution governance, minimal consumer milestone, privacy model for live ops, single file vs per-tool subfiles, `refs.project` namespacing |
| `vision.md` | 6 | See CC-6; also CICD-vs-claude-release boundary, Live Ops distribution, trace schema first, single roadmap vs per-tool, constitution as separate vs subsumed |
| `candidates.md` | 7 | CICD location, trace schema first, constitution form, Live Ops distribution, single roadmap, constitution subsumption, ATH vs CICD artifact diff boundary |

## Resolved (recently)

Questions marked resolved in `expose.md` § Resolved Decisions are no longer open and don't appear in this rollup. As of 2026-05-28, that includes:

- First-pass: #1 (Captain SDLC layer), #2 (constitution overload), #4 (cross-tool seam homes), #10 (artifact diff boundary tone), #13 (Live Ops privacy).
- Second-pass: #16 (schema_version), #17 (`.captain-sdlc/` layout), #19 (event-kind backfill), #20 (interrogate backlog), #22 (code-reading), #23 (fenced-block format).

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Code-Reading Capability](./code-reading-capability.md)
- [Captain SDLC — Conventions](./captain-sdlc-conventions.md)
- [Captain SDLC — Cross-Channel Deduplication](./cross-channel-dedup.md)
- [Captain SDLC — Cross-tool Trace Schema](./trace-schema.md)
- [Captain SDLC — Exposed Gaps and Ambiguities](./expose.md)
- [Captain SDLC — Glossary](./glossary.md)
- [Captain SDLC — Privacy Framework](./privacy-framework.md)
- [Captain SDLC — Privacy Policy (Aspirational)](./privacy-policy-aspirational.md)
- [Captain SDLC — Seam 2: Design ↔ Code Drift](./seam-design-code-drift.md)
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- None — this doc is a rollup, not a decision surface. Resolutions live in `expose.md`.

## Open Questions

- None.

## Version History

- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial rollup. Surfaces ten cross-cutting questions and indexes per-doc questions.
