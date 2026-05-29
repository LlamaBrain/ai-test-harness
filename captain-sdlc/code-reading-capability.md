# Captain SDLC — Code-Reading Capability
Updated: 2026-04-08
Version: 0.1.2

Created: 2026-05-28

Planning doc for the **code-reading capability** — the shared prerequisite for Seams 2 (Design ↔ Code Drift), 4 (Cross-Tool Contract Testing, Mechanism B), and 6 (Constitution Enforcement). Each seam needed it; none of them owned the decision. This doc does.

**Status:** Planning. The capability doesn't exist yet. Lives here as a single source of truth so the three dependent seams don't each defer the decision to "the other docs."

## Goal

Give the Captain SDLC tools that need to reason about source code (drift checker, constitution checker, parser introspection in contract testing) a shared way to read code structure. Choose once; use everywhere those tools need it.

## Non-goals

- **Not semantic understanding of code intent.** All consumers are doing structural checks (names, surfaces, topology, pattern matches). Behavioral reasoning is not in scope.
- **Not a language-server.** No IDE features, no incremental analysis, no on-the-fly completions.
- **Not real-time.** All consumers run on demand or at gate time. Sub-second latency is not a requirement.
- **Not a replacement for runtime testing.** ATH owns runtime behavior; this capability owns static structure.

## The three tiers

Each consumer has a minimum tier requirement. Higher tiers unlock additional consumer features but cost more to maintain.

### Tier 1 — Grep + asmdef parse

Lightest. Treats code as text files plus assembly-definition topology.

**What it gives:**
- Named-entity existence checks (does `IAthHostAdapter` appear anywhere in the code?).
- File presence (does `Assets/Bootstrap/CompositionRoot.cs` exist?).
- File-content pattern matches (does any file under `Assets/**/*.cs` match the regex for a MonoBehaviour with custom methods?).
- Assembly topology (does `Runtime.asmdef` reference `Editor.asmdef`?).

**What it doesn't give:**
- Knowing whether a matched pattern is in a comment, a string literal, or actual code.
- Distinguishing classes from interfaces from structs from method names that happen to collide.
- Rename detection (a class renamed from `Foo` to `FooImpl` looks like "Foo missing, FooImpl extra").
- Type-level dependency analysis.

**Cost:** Trivial. Bash + asmdef JSON parsing. Ships in days.

### Tier 2 — Tree-sitter parse

Syntax-tree-aware. Multi-language by design.

**What it gives, in addition to Tier 1:**
- Distinguishing classes / interfaces / structs / methods by syntactic role.
- Inheritance graph (which class inherits from which).
- Method signature extraction (parameters, return types — at syntax level, not semantic).
- Comment-vs-code discrimination.
- Rough rename detection via shape similarity.

**What it doesn't give:**
- Type resolution. A reference to `Foo` may be ambiguous between three `Foo` classes in different namespaces; tree-sitter can't disambiguate.
- Member-resolution (`obj.Bar` — what's `Bar`'s type?).
- Cross-file semantic dependencies.

**Cost:** Moderate. Tree-sitter grammars exist for C#, JSON, YAML, and most languages Captain SDLC would care about. Integration is a few weeks.

### Tier 3 — Roslyn (or equivalent language-server)

Full semantic analysis for .NET. Equivalent stacks exist for other languages but Captain SDLC's current focus is Unity / C#.

**What it gives, in addition to Tier 2:**
- Type resolution. `Foo` is the right `Foo` from the right namespace.
- Member resolution. `obj.Bar` is a known method with a known signature.
- Type-level dependency analysis.
- Cross-assembly reference tracking.
- AST patterns rich enough for the most invasive invariants (e.g., "no MonoBehaviour with private methods other than Unity lifecycle hooks" — Tier 1 fakes this with regex; Tier 3 actually checks it).

**What it doesn't give:**
- Multi-language support (Roslyn is C# / .NET only). Captain SDLC tools targeting non-.NET projects would need parallel infrastructure.
- Anything not-yet-compilable. Roslyn needs a buildable code state; checks against in-progress / broken code are degraded.

**Cost:** Heavy. Roslyn integration is real engineering. Months, not weeks. Pays off when Tier 1/2 false-positive rates start hurting.

## What each consumer needs

| Consumer | Minimum tier | Why |
|---|---|---|
| **Seam 2 drift checker — minimal first cut** | Tier 1 | Named-entity existence is the priority-1 signal |
| **Seam 2 drift checker — full vision** | Tier 2 (Tier 3 for rename detection) | Distinguishing kinds + rename heuristics |
| **Seam 6 constitution checker — `mechanical` tier** | Tier 1 | Pattern-based forbids work at file-grep level |
| **Seam 6 constitution checker — `ast` tier** | Tier 2 minimum, Tier 3 for type-aware invariants | API-call-forbid, inheritance-forbid, public-API-shape |
| **Seam 4 contract testing — Mechanism A** | None | Sample-against-schema validation needs no code reading |
| **Seam 4 contract testing — Mechanism B (parser introspection)** | Tier 2 or 3 | Reading consumers' parser code to extract expected field sets |

## Upgrade path

Default plan, agreed across all three dependent seams:

1. **Ship at Tier 1.** All three seams' minimal first cuts are Tier 1-compatible. Get the discipline in place; measure false-positive rate; surface what's actually hurting.
2. **Promote to Tier 2 when the false-positive rate justifies it.** Concretely: when developers start suppressing drift / violation reports faster than triaging them, the tier needs to be higher. Tree-sitter is the right answer at that point.
3. **Promote to Tier 3 only when Tier 2's missing semantic resolution causes real misses on load-bearing invariants.** Probably starts with Seam 6's `ast`-tier invariants and stays scoped to those.

No seam ships Tier 3 first. The cost is too high relative to the value of Tier 1's coverage of the most common cases.

## Architecture

### Where the capability lives

**Interrogate, extended.** All three dependent seams already point at interrogate as their natural home (drift checker reads the canonical design doc; constitution checker reads the constitution; contract testing's parser introspection would build on interrogate's existing doc-reading). Adding code-reading to interrogate keeps the dependency tree shallow.

Alternative considered and rejected: a standalone analyzer tool that all three seams invoke. Cleaner separation, but adds a process boundary and version-pinning concern that doesn't pay off until contract testing's Mechanism B exists (and probably not even then).

### How tier upgrades are surfaced

When the capability is upgraded from Tier N to Tier N+1:

- The interrogate version bumps to reflect the new capability.
- The three consumer docs (drift, constitution, contract testing) update their "what's checked" sections to reflect new pattern kinds that became available.
- Per-project config can opt in to new tier features (some projects may not want the cost / configuration burden).

Tier downgrades are not supported. Once a project's invariants depend on Tier 2, they don't go back to Tier 1.

### Failure modes

| Failure | Cause | Resolution |
|---|---|---|
| Pattern match in comment treated as code (Tier 1) | Grep can't distinguish comments | Either promote to Tier 2 or add inline `// captain: ignore-grep` markers |
| False-positive missing entity (Tier 1) | Entity exists but in a form grep doesn't see (generic parameter, partial class) | Promote tier, or add to suppression manifest |
| Roslyn requires buildable code (Tier 3) | Check requested mid-refactor when code doesn't compile | Skip the check; report "not evaluable" rather than fail |
| Tree-sitter grammar gap (Tier 2) | Language not yet covered | Fall back to Tier 1 for that language |

## Minimal first cut

If we ship the smallest useful version of the capability:

- **Tier 1 only.** Grep over `*.cs` files + JSON parse of `*.asmdef`.
- **One consumer:** Seam 6's constitution checker's `mechanical` tier (most concrete value).
- **No tier promotion path implemented yet.** A future Tier 2 promotion is a separate work item, not gated by this minimum.
- **Read-only.** No code modification, no rewriting.
- **No incremental cache.** Each run scans fresh. Fast enough for the scale we care about.

This minimum proves the integration shape (interrogate gains a code-reading entry point, consumer seams call it) without committing to AST infrastructure prematurely.

## Open scoping questions

1. **Tier boundary detection.** How does interrogate know it's at Tier 1 vs Tier 2? Likely a version-declared capability table; consumer seams check at runtime what's available.
2. **Per-language tier independence.** Tier 2 (tree-sitter) lights up on a per-language basis. If a project has only C# files, do we ship the C# grammar only, or pull all grammars? Lean: only what's needed.
3. **Caching strategy.** First cut is no cache. As corpus grows, an incremental cache becomes worth it — but cache invalidation across tiers is its own headache. Defer.
4. **Pattern language portability across tiers.** A constitution invariant written for Tier 1 (regex) doesn't translate cleanly to Tier 2 (syntax-tree query). Worth deciding whether invariant patterns are tier-specific or pattern-portable. Lean: tier-specific; invariants explicitly opt into a tier.
5. **Multi-language Captain SDLC adopters.** If a non-Unity adopter shows up with a Rust project, Tier 1 still works (grep is language-agnostic). Tier 2 works if a grammar exists. Tier 3 doesn't (no Roslyn equivalent for Rust). Trigger for considering parallel non-.NET infrastructure: first such adopter.

## Definition of done

The capability is shipped at a given tier when:

- The tier's primitives are exposed as a callable entry point in interrogate (CLI command and / or MCP tool).
- At least one consumer seam uses the tier's new primitives end-to-end.
- The failure modes the tier introduces (comment-vs-code, build-state requirement, etc.) have documented mitigations.
- A project can opt in or out of the tier via interrogate's version pinning.

Tier promotion isn't a single "done" — it's a per-tier ship event. Each tier is its own definition-of-done.

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Candidates](./candidates.md)
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
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- **2026-05-28** — Three-tier capability (Tier 1: grep + asmdef; Tier 2: tree-sitter; Tier 3: Roslyn). Adopted across all three dependent seams.
- **2026-05-28** — Capability lives in interrogate, extended. Not a standalone analyzer tool.
- **2026-05-28** — Default upgrade path: ship at Tier 1; promote to Tier 2 when false-positive rate hurts; Tier 3 only for invariants Tier 2 can't express.
- **2026-05-28** — Tier downgrades not supported. Tier upgrades are explicit per-tier ship events.

## Open Questions

- None.

## Version History

- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for the shared code-reading capability across Seams 2, 4, 6.
