# Captain SDLC — Exposed Gaps and Ambiguities
Updated: 2026-04-08
Version: 0.1.4

Created: 2026-05-28

Gaps, undefined seams, and risky ambiguities identified in the Captain SDLC docs set. Findings are separated by confidence: **grounded** findings are directly observable in the docs; **inferred** findings are extrapolated from what's not addressed but probably should be.

## Grounded — concrete gaps in current docs

### 1. "Captain SDLC layer" is an unscoped placeholder

**RESOLVED 2026-05-28:** Captain SDLC is positioned as a Unity-game SDLC pipeline for solo/small-team HITL development; the "Captain SDLC layer" is *not* a separate tool. Cross-tool seams have schemas + conventions that live in this nerve-center repo, with implementations distributed across the emitting/consuming tools. `candidates.md` homes updated to "Nerve-center (schema) + per-tool (emission/validation/ingest)." `README.md` and `glossary.md` reflect the resolution.

`candidates.md` and `vision.md` both reference "Captain SDLC layer" as the home for several seam-level items (pipeline trace, cross-tool contract testing). No doc defines what the Captain SDLC layer *is* — a future tool? A library shared by the three existing tools? An emergent property of trace conventions? It's used as a placeholder for "the cross-tool plumbing" but never resolved.

**Affected:** `candidates.md` § Cross-tool seams (multiple rows); `vision.md` § The seams.

**Question to resolve:** What architectural form does the Captain SDLC layer take? Until decided, anything tagged "home: Captain SDLC layer" has no actual owner, only a label.

### 2. "Constitution" overloaded across three meanings

**RESOLVED 2026-05-28:** Disambiguated vocabulary —
- **constitution** = the rules (and the section of the canonical design doc that holds them; operationally one thing).
- **constitution checker** = the tool that enforces them (form TBD).
- **constitution enforcement** = the seam (the activity, Seam 6).
- **invariants** = the individual rules.

`vision.md` § Seam 6 carries the vocabulary distinction inline; `glossary.md` updated.

`vision.md` and `candidates.md` use the word "constitution" three ways:
- **Constitution layer**: a section in the canonical design doc encoding invariants.
- **Constitution enforcement**: the check tool that verifies the constitution holds (Seam 6).
- **Constitution invariants**: the rules themselves.

All three are valid concepts but they share one word. A reader has to infer from context. `glossary.md` documents all three but doesn't resolve the overload.

**Affected:** `vision.md` § Front — Design; `vision.md` § Seam 6; `candidates.md` § interrogate-side; `candidates.md` § Cross-tool seams.

**Question to resolve:** Either coin distinct terms (e.g., constitution-section / constitution-checker / constitution-rules) or pick one canonical use and rename the other two.

### 3. Five of six seams have no planning doc

`vision.md` describes six seams in a few paragraphs each. `trace-schema.md` exists as the planning doc for Seam 1. The other five (design ↔ code drift, release gates, contract testing, Live Ops ingestion, constitution enforcement) have nothing beyond their vision-level description.

If Captain SDLC's value is in the seams (per its own philosophy), then five-of-six seams sitting at paragraph depth is the largest single doc gap in the set.

**Affected:** `vision.md` § The seams; everything depending on those seams.

**Question to resolve:** Do the other seams get their own planning docs (matching `trace-schema.md`'s shape)? In what order? If not, where does their design live?

### 4. Several cross-tool items have no settled home

**RESOLVED 2026-05-28** (as a consequence of #1): Cross-tool seam homes updated in `candidates.md`. Pipeline trace, contract testing, and Live Ops ingestion all carry "Nerve-center (schema) + per-tool (emission/validation/ingest)" homes. Constitution enforcement stays TBD pending the checker-form decision — that's a different scoping question, not a missing home.

Tightly related to #1. `candidates.md` lists these with home "Captain SDLC layer":
- Pipeline trace / shared cross-tool state.
- Cross-tool contract testing.

These with home "Cross-tool":
- Live Ops → planning ingestion.

This as "TBD":
- Constitution enforcement.

That's four of six cross-tool seams without a settled home. Schema-first is the right discipline for trace; for the others, scoping is genuinely not started. Tracking these as a single "needs home" set may be more honest than three different placeholder strings.

### 5. `.captain-sdlc/` directory purpose under-specified

`trace-schema.md` says traces live at `.captain-sdlc/trace/` in the project root. No doc says whether `.captain-sdlc/` is *only* for traces or whether it will host other Captain SDLC state (configs, caches, generated artifacts, glossary indices, etc.). If other state ends up there, the naming `.captain-sdlc/trace/` is fine — but the directory's overall scope isn't documented.

**Affected:** `trace-schema.md` § Storage and location.

**Question to resolve:** Reserve `.captain-sdlc/` for Captain SDLC state generally, and document its expected layout — or rename to something narrower like `.captain-trace/` if traces are the only intended inhabitant.

### 6. `refs` vs `parents` vs `links` boundaries explained but not exemplified

`trace-schema.md` § Linking conventions states:
- `refs` situates the event in the pipeline (commit, release, doc).
- `parents` for direct causation.
- `links` for soft / many-to-many references.

The rules are stated but no worked example shows where the line falls. When does a commit event reference its design doc via `refs.design_doc` versus via `links[implements]` to a `design.task.scoped` event? Both are valid reads of the schema.

**Affected:** `trace-schema.md` § Linking conventions.

**Question to resolve:** Add a worked example for each ambiguous case, or tighten the rule so the choice is mechanical.

### 7. Post-deletion / corruption recovery not addressed

`trace-schema.md` treats traces as append-only and implicitly immutable. Real-world failure modes — disk corruption, accidental deletes, gitignore-induced loss across machines, OS-level append-on-Windows quirks — aren't addressed. No doc says what happens when an event chain is broken because its parent was lost, or what a consumer should do when it finds an orphan event.

**Affected:** `trace-schema.md` § Storage and location; § Identity and provenance.

**Question to resolve:** Either say "broken chains are surfaced and tolerated" (define the surface) or specify a more robust storage strategy.

### 8. `refs.project` provenance unspecified

`trace-schema.md` requires `refs.project` but doesn't say where the project slug comes from. Options: `package.json`, a `.captain-sdlc/project.json`, implicit from git remote, a config in the canonical design doc. Different choices have different drift modes (renamed repos, monorepo subprojects, etc.).

**Affected:** `trace-schema.md` § Open scoping questions item 6.

**Question to resolve:** Pick a single source of truth for the project slug. Documented per-project, not per-tool.

### 9. "Balance diff" introduced without definition

`candidates.md` § Marketing introduces "marketing changelog (player-facing artifact diff narrative)" with the description "artifact / balance diffs translated into player-readable form." "Balance diff" appears here without a prior definition; implied meaning is "diff against game-balance data files." Reader has to infer.

`glossary.md` defines it as a subset of artifact diff, but that resolution lives in glossary alone — `candidates.md` and `vision.md` don't clarify.

**Affected:** `candidates.md` § Marketing.

**Question to resolve:** Either define balance diff inline where it's first used, or fold it under artifact diff with a clarifying note.

### 10. Boundary between runtime and between-release artifact diff is open in one doc and presented as decided in another

**RESOLVED 2026-05-28** (tone harmonized, not resolved): `vision.md` § Middle and § Back now explicitly note the boundary is open and link to `candidates.md` § Open scoping questions item 7. The underlying scoping question is still open; only the doc-set tone inconsistency is fixed.

`candidates.md` § Open scoping questions item 7 explicitly flags "where does the boundary between ATH-runtime artifact diff and CICD between-release diff land" as unresolved. `vision.md` describes both as items in separate phases without acknowledging the boundary question — the tone reads as decided.

**Affected:** `vision.md` § Middle and § Back; `candidates.md` § Open scoping questions.

**Question to resolve:** Either resolve the boundary, or harmonize the tone so both docs flag it as open in matching terms.

## Inferred — gaps extrapolated from what's missing

### 11. Existing tools' upgrade lifecycle not addressed

The trace envelope has a `schema_version` policy. The canonical design doc has no equivalent versioning policy. Constitution invariants likewise have no version. When a tool ships breaking changes — to event payload schemas, design doc structure, or constitution invariants — there's no documented compatibility story.

**Likely affected:** `trace-schema.md` § Versioning policy (envelope only); nothing else.

**Question to resolve:** Define a versioning expectation for the canonical design doc and constitution layer, mirroring the trace envelope's.

### 12. No documented onboarding path for a new project

The README describes Captain SDLC's tools and philosophy. It doesn't say what files a new Unity project needs to start using the pipeline. Implied path: install plugins, add `.captain-sdlc/`, write a canonical doc, configure interrogate's `docsDir`. Worth making explicit — especially since dirigible is named as the next dogfood target.

**Likely affected:** `README.md` § How to use this.

**Question to resolve:** Add an "Adopting Captain SDLC in a new project" section, or a separate `onboarding.md`.

### 13. Live Ops privacy policy is deferred but blocking

**RESOLVED 2026-05-28:** `vision.md` § Live Ops now carries an explicit "Prerequisite: Privacy and sensitive-data policy" block declaring no Live Ops item ships until privacy is settled. `candidates.md` § Cross-tool seams gains a new "Privacy and sensitive-data policy" row, and all four Live Ops items in `candidates.md` are tagged "Blocked on Privacy policy."

`trace-schema.md` § Privacy and sensitive payloads explicitly defers the privacy policy until live ops ingestion is scoped — but also says privacy needs to be settled *before* ingestion lands. `vision.md` describes Live Ops ingestion at full vision without surfacing the privacy dependency. If Live Ops is on the critical path, privacy is too, and it's currently parked behind a "TBD" that nothing else references.

**Affected:** `trace-schema.md` § Privacy; `vision.md` § Live Ops.

**Question to resolve:** Treat privacy policy as a hard prerequisite to Live Ops ingestion, named explicitly in `vision.md` and tracked in `candidates.md`.

### 14. No "definition of done" for any seam

Seams are described as features the pipeline gains. None of the seam descriptions in `vision.md` says when the seam is considered shipped. `trace-schema.md` proposes a "minimal first cut" milestone for Seam 1 — but that's a milestone, not a definition of done. Useful for scoping; insufficient for deciding when to stop.

**Likely affected:** `vision.md` § The seams.

**Question to resolve:** For each seam, name (a) what minimum delivers the seam's stated value and (b) what would be considered "done" vs "first cut." Captures the difference between "trace can backward-walk a regression" (done) and "every tool emits all event kinds" (aspirational).

### 15. ATH gaining its own Claude Code setup interacts with `.captain-sdlc/` placement

The existing ATH `ROADMAP.md` notes "ATH gets its own Claude Code setup" as a Later item — currently it's dogfooded from BTS's working directory. If `.captain-sdlc/` is a per-project directory and ATH eventually gets its own `.claude/` setup, where does ATH's own pipeline trace live? In the ATH repo? In a separate location? Tightly coupled to #5 and the nerve-center placement decision.

**Likely affected:** `README.md` § Why this repo is the nerve center; future ATH `.claude/` setup.

**Question to resolve:** Decide whether the nerve-center repo also hosts its own `.captain-sdlc/` trace, or whether nerve-center docs are repo-of-record while traces live in the consuming project.

## What was NOT found

- **No contradictions between docs.** The four originally-authored docs are internally consistent. `audit-docs` found only metadata issues (now fixed by `sync-docs`).
- **No stale open questions.** Every open question currently in the docs is active.
- **No fuzzy ownership of explicitly Accepted items.** Items marked Accepted in `candidates.md` all have homes (ATH or claude-release). The fuzziness is in Proposed and Cross-tool items.

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Code-Reading Capability](./code-reading-capability.md)
- [Captain SDLC — Conventions](./captain-sdlc-conventions.md)
- [Captain SDLC — Cross-Channel Deduplication](./cross-channel-dedup.md)
- [Captain SDLC — Cross-tool Trace Schema](./trace-schema.md)
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

## Second pass — findings against the expanded docs set (2026-05-28)

After the first pass, six planning docs landed (four seam docs, two privacy docs, one cross-channel dedup doc). This pass surfaces gaps and ambiguities introduced or made worse by the expansion. Numbering continues from the first pass.

### 16. The `schema_version` pattern appears in six configs without a central convention

Trace envelope schema, release-gates.yaml, drift-suppressions.yaml, constitution-suppressions.yaml, liveops-routing.yaml, dedup-config.yaml, contracts.yaml — all carry a `schema_version: 1` field with the same intent (additive-evolution-within-version, breaking-changes-bump). No single doc owns the convention. Worth a short reference (likely a section in `trace-schema.md` or its own `config-conventions.md`) so consuming tools can implement consistent version-handling once.

**Affected:** every config-bearing doc.

### 17. `.captain-sdlc/` directory layout is now load-bearing and undocumented

The expanded docs reference `.captain-sdlc/trace/`, `.captain-sdlc/side-store/`, `.captain-sdlc/release-gates.yaml`, `.captain-sdlc/drift-suppressions.yaml`, `.captain-sdlc/constitution-suppressions.yaml`, `.captain-sdlc/liveops-routing.yaml`, `.captain-sdlc/dedup-config.yaml`, `.captain-sdlc/contracts.yaml`, and an implied `.captain-sdlc/privacy-config.yaml`. First-pass finding #5 (".captain-sdlc/ purpose under-specified") was open; it's now actively blocking — too many tools assume the directory's structure.

**Question to resolve:** Promote first-pass #5 from "open" to "needs a layout spec." Likely a single doc (`captain-sdlc-directory.md`) that owns the layout.

### 18. Two distinct "suppression" mechanisms, similar shape, no shared vocabulary

`seam-design-code-drift.md` defines drift suppressions; `seam-constitution-enforcement.md` defines constitution suppressions. They have nearly identical YAML shapes (matcher, reason, optional `expires_after`) but no shared definition. Future seams will likely want similar mechanics (suppression for contract test failures? for Live Ops noise?). Worth either factoring a common "suppression file" spec or explicitly noting that each is its own convention.

**Affected:** `seam-design-code-drift.md`, `seam-constitution-enforcement.md`, anywhere a future seam wants the same pattern.

### 19. Trace event kind registry has drifted out of sync with the docs that emit events

`trace-schema.md` § Event kinds listed an initial taxonomy. New kinds have accumulated since: `liveops.task.occurrence`, `liveops.task.split`, `liveops.task.merge`, `liveops.task.recurrence` (in dedup), `release.gate.summary`, `release.gate.override` (in release-gates), `contract.test.failed` (in contract testing), `design.constitution.violation_detected` (in constitution enforcement). The trace schema doc doesn't reflect them.

**Question to resolve:** Either backfill the trace event taxonomy or move kind registration to a separate appendix that's easier to keep current.

### 20. "Interrogate's backlog" is referenced as if it exists; it doesn't

Live Ops ingestion routes items to "interrogate's backlog." Cross-channel dedup uses "interrogate's backlog UI." Seam 2 references "interrogate backlog" as a downstream sink. Interrogate has no documented backlog feature today — this is an aspirational interrogate capability that several Captain SDLC docs already lean on. Worth a single tagged note: "interrogate backlog API is itself a deferred interrogate-side capability."

**Affected:** `seam-live-ops-ingestion.md`, `cross-channel-dedup.md`, `seam-design-code-drift.md` (via release-gate gate #5 downstream).

### 21. "Schema" is overloaded across docs

Used to mean: the trace envelope schema, gate definition schemas, contract-test target schemas, ingestor payload schemas, shared-artifact schemas, configuration file schemas. Six distinct senses sharing one word. Glossary documents none of them. Echoes first-pass #2 (constitution overloading); should resolve similarly with disambiguation.

**Affected:** every doc that uses "schema" without a qualifier.

### 22. Code-reading capability is a shared prerequisite across three seams with no owning doc

`seam-design-code-drift.md` proposes the grep → tree-sitter → Roslyn upgrade path. `seam-constitution-enforcement.md` inherits it. `seam-contract-testing.md` references it for Mechanism B (parser introspection). Three seams depend on this decision; no doc owns it. Worth extracting to its own short planning doc (`code-reading-capability.md`) that the three seams reference.

**Affected:** `seam-design-code-drift.md`, `seam-constitution-enforcement.md`, `seam-contract-testing.md`.

### 23. Fenced-block format for embedded structured data has no convention

Constitution uses ` ```constitution `. Contract testing proposes ` ```jsonschema ` or ` ```yamlschema `. These are both "embed structured rules in a Markdown doc via fenced block" — same discoverability pattern, no shared convention. As more seams add embedded blocks, the absence of a rule will fragment the parser surface.

**Question to resolve:** Pick a convention. Lean: language tags name the content kind (` ```constitution `, ` ```gate-schema `, ` ```contract-schema `) so block-finding is by tag. Document once.

### 24. AI processing carve-out is restated in three docs with slight drift

The "`sensitive` data never goes to Claude API directly" commitment is in `privacy-framework.md`, restated in `privacy-policy-aspirational.md`, and referenced (with mildly different phrasing) in `cross-channel-dedup.md`. Currently consistent enough; the wording drift will widen as docs evolve unless one doc owns the canonical statement and the others link.

**Question to resolve:** `privacy-framework.md` is the canonical owner; everywhere else should link rather than restate.

### 25. Cross-doc open questions have no rollup

First-pass finding #14 (no definition of done per seam) gestured at this. With six planning docs each carrying their own "Open Questions" section, several questions span docs (code-reading capability, `.captain-sdlc/` layout, schema_version convention, fenced-block format). A reader has to scan every doc's open-questions section to find the active cross-doc ones.

**Question to resolve:** Either consolidate to a single `open-questions.md` rollup that tags questions by affected doc, or accept the per-doc duplication.

## Resolved Decisions

- **2026-05-28** — Finding #1: "Captain SDLC layer" is not a separate tool; cross-tool seams = nerve-center schema + distributed implementation.
- **2026-05-28** — Finding #2: Constitution vocabulary disambiguated (constitution / constitution checker / constitution enforcement / invariants).
- **2026-05-28** — Finding #4: Cross-tool seam homes refactored as a consequence of #1.
- **2026-05-28** — Finding #10: Artifact diff boundary tone harmonized across `vision.md` and `candidates.md` (underlying scoping question still open).
- **2026-05-28** — Finding #13: Privacy policy explicitly marked as blocking prerequisite for Live Ops ingestion.
- **2026-05-28** — Second-pass finding #16 (`schema_version` convention): Resolved by `captain-sdlc-conventions.md` § The `schema_version` convention.
- **2026-05-28** — Second-pass finding #17 (`.captain-sdlc/` layout): Resolved by `captain-sdlc-conventions.md` § The `.captain-sdlc/` directory.
- **2026-05-28** — Second-pass finding #19 (trace event kind registry drift): Resolved by backfilling `trace-schema.md` § Event kinds with the accumulated kinds (`design.constitution.violation_detected`, `release.gate.summary`, `release.gate.override`, `liveops.perf_sample.received`, `liveops.telemetry.received`, four `liveops.task.*` kinds, `liveops.ingest.failed`, and `contract.test.failed`).
- **2026-05-28** — Second-pass finding #20 (interrogate backlog as undocumented dependency): Resolved by adding "Interrogate backlog API" row to `candidates.md` § interrogate as a Proposed item explicitly tagged as aspirational dependency for several seams.
- **2026-05-28** — Second-pass finding #22 (code-reading capability shared without owning doc): Resolved by `code-reading-capability.md`. Three-tier upgrade path (grep / tree-sitter / Roslyn) and shared dependency model owned in one place.
- **2026-05-28** — Second-pass finding #23 (fenced-block format convention): Resolved by `captain-sdlc-conventions.md` § The fenced-block convention for embedded structured data. Tag registry maintained there.
- **2026-05-28** — Second-pass finding #18 (suppression vocabulary): Resolved by `captain-sdlc-conventions.md` § The suppression file convention. Shared wrapper (schema_version, suppressions list, required reason, optional expires_after) with seam-specific identifier fields.
- **2026-05-28** — Second-pass finding #21 ("schema" overload): Resolved by `glossary.md` entries for **Schema** and **Contract**. Schema = the machine-readable artifact; Contract = the cross-tool agreement; *shape* / *format* for prose-level intent.
- **2026-05-28** — Second-pass finding #24 (AI carve-out drift): Resolved by adding § Canonical ownership to `privacy-framework.md` § What's concrete today. Other docs reference rather than restate; verified current docs already follow that pattern.
- **2026-05-28** — Second-pass finding #25 (cross-doc open-questions rollup): Resolved by `open-questions.md`. Ten cross-cutting questions consolidated; per-doc index for the rest.

## Open Questions

- None.

## Version History

- 0.1.4 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial expose pass against `README.md`, `vision.md`, `candidates.md`, `trace-schema.md`.
