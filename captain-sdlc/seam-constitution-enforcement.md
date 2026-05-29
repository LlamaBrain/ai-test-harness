# Captain SDLC — Seam 6: Constitution Enforcement
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

Planning doc for **Seam 6: Constitution Enforcement** — verifying that the invariants encoded in the canonical design doc's *constitution* section hold against the code. Built on the same code-reading infrastructure as Seam 2 (Design ↔ Code Drift); shares its prerequisite.

**Status: Planning.** Nothing here is implemented. Treat all claims as proposals open to argument.

## Vocabulary recap (from glossary)

- **constitution** = the section of the canonical design doc encoding invariants the implementation must obey. The rules and the section are operationally one thing.
- **constitution checker** = the tool that verifies the constitution holds (this seam's implementation).
- **constitution enforcement** = this seam (the activity).
- **invariants** = the individual rules.

## Goal

When the implementation violates a project-declared architectural invariant — "no MonoBehaviour business logic," "no synchronous main-thread I/O," "all mutations go through the adapter" — surface the violation with location and rationale so the human can fix the code, soften the rule, or accept the violation explicitly.

The goal is making project-specific architectural discipline mechanically auditable, not enforcing a one-size-fits-all linter ruleset. Each project's constitution is the project's own.

## Non-goals

- **Not a generic lint suite.** Style, formatting, code-quality concerns belong elsewhere (editorconfig, dotnet-format, etc.). Constitution invariants are *architectural*, project-authored, and live with the design.
- **Not auto-fix.** Violations are surfaced; the human (or a separate refactor) addresses them.
- **Not Seam 2.** Drift detection (Seam 2) checks structural conformance — names, surfaces, topology. Constitution enforcement (this seam) checks invariant satisfaction — pattern-based rules over code. See "Relationship with Seam 2" below.
- **Not enforcement of every rule worth having.** Some invariants are too subtle to check automatically; they're documented in the constitution but tagged for manual review only.

## Where the constitution lives

**In the canonical design doc, in a `## Constitution` section.** The section contains:

- Free-prose rationale for each invariant (Markdown).
- A fenced ` ```constitution ` block with structured rules the checker parses.

Example structure inside the design doc:

````markdown
## Constitution

Rules the implementation must obey. Each invariant has a rationale; the structured
block below is what the constitution checker reads.

### No MonoBehaviour business logic

MonoBehaviour classes adapt Unity's lifecycle to plain C# code. Business logic
that doesn't depend on Unity lifecycle should live in plain classes invoked from
MonoBehaviours, not in MonoBehaviours themselves. Keeps logic testable without a
running player.

### No synchronous main-thread I/O

...

```constitution
schema_version: 1
invariants:
  - id: no-monobehaviour-business-logic
    tier: mechanical
    severity: block
    pattern:
      kind: file-regex-forbid
      include: ["Assets/**/*.cs"]
      exclude: ["Assets/**/Editor/**", "Assets/**/Tests/**"]
      forbid: "class\\s+\\w+\\s*:\\s*MonoBehaviour"
      where_not: "Awake|Start|OnEnable|OnDisable|OnDestroy|Update|FixedUpdate|LateUpdate"
  - id: no-sync-main-thread-io
    tier: ast
    severity: block
    pattern:
      kind: api-call-forbid
      forbidden_calls: ["System.IO.File.ReadAllText", "System.IO.File.WriteAllText"]
      in_context: "main-thread"
  - id: adapter-mutation-discipline
    tier: manual
    severity: warn
    description: "All host-state mutations must go through IAthHostAdapter. Not mechanically checkable; review required."
```
````

The design doc keeps rationale and rules together. The fenced block is the machine-readable contract.

Alternative considered and rejected: a separate `constitution.yaml` referenced from the design doc. Cleaner separation but breaks the design's role as single-source-of-truth and adds a sync problem.

## Invariant tiers

Not every architectural invariant can be checked the same way. Each invariant declares a tier:

| Tier | Meaning | Checker behavior |
|---|---|---|
| **mechanical** | Expressible as a file-level pattern (grep / asmdef topology) | Checked by the grep-level checker. |
| **ast** | Requires syntax-tree or type-aware analysis | Checked when the AST-level capability is built (tree-sitter / Roslyn). Until then, surfaced as "unchecked." |
| **manual** | Too subtle for mechanical checking; documented but not enforced | Listed in the violations report as a reminder. Always produces "manual-review-required" status. |

The tier system means a constitution can include rules the checker doesn't yet enforce — the rule exists in the constitution, surfaces in reports, and gets enforced when the capability lands. Constitution authoring isn't blocked on tool maturity.

## Pattern kinds (initial set)

For tier `mechanical`:

- **`file-regex-forbid`** — match `forbid` regex against files matching `include` / not `exclude`. Optional `where_not` carves out allowed contexts.
- **`file-regex-require`** — files matching `include` must also match `require`. Catches "every adapter must implement X."
- **`asmdef-forbid-reference`** — assembly definition `from` must not reference `to`. Topological invariants ("Runtime asmdef must not reference Editor asmdef").
- **`file-must-exist`** / **`file-must-not-exist`** — for invariants about file presence.

For tier `ast` (future, scoped here for the contract):

- **`api-call-forbid`** — forbidden method calls within named contexts.
- **`inheritance-forbid`** — no class may inherit from named base in named scope.
- **`public-api-shape`** — public surface of a type must include / must not include named members.

Adding pattern kinds is additive within `schema_version`.

## Output

Violation report:

```json
{
  "schema_version": 1,
  "audited_at": "2026-05-28T17:45:12Z",
  "constitution_source": "docs/canonical/design.md#constitution",
  "constitution_hash": "sha256:...",
  "code_commit": "26e6d1a",
  "violations": [
    {
      "invariant_id": "no-monobehaviour-business-logic",
      "severity": "block",
      "tier": "mechanical",
      "code_ref": "Assets/Player/PlayerController.cs:42",
      "matched_pattern": "class PlayerController : MonoBehaviour",
      "context": "private void HandleInput() { ... }",
      "rationale": "MonoBehaviour classes adapt Unity's lifecycle...",
      "suggestion": "Move HandleInput to a plain C# class invoked from this MonoBehaviour."
    }
  ],
  "unchecked": [
    { "invariant_id": "no-sync-main-thread-io", "tier": "ast", "reason": "AST-level checking not yet available." }
  ],
  "manual_review_required": [
    { "invariant_id": "adapter-mutation-discipline", "tier": "manual" }
  ],
  "suppressed": [...]
}
```

Output is written to `constitution-violations.md` (human-readable summary) and `.captain-sdlc/constitution-violations.json` (machine-readable for the release gate). Emitted as `design.constitution.violation_detected` trace event per the trace schema.

## Suppression

Two mechanisms; both supported:

### In-code suppression

For one-off, line-local exceptions:

```csharp
// constitution-suppress: no-monobehaviour-business-logic — legacy boot path, refactor planned 2026-07
public class GameBootstrapper : MonoBehaviour
{
    void Start() { ... }
}
```

Comment must include the invariant id and a non-empty reason. Familiar shape for developers (matches ESLint disable comments). Stays close to the suppressed code.

### Manifest suppression

For whole-file, whole-folder, or many-line exceptions: `.captain-sdlc/constitution-suppressions.yaml`:

```yaml
schema_version: 1
suppressions:
  - invariant_id: no-monobehaviour-business-logic
    matches:
      file_glob: "Assets/Legacy/**/*.cs"
    reason: "Legacy folder pre-dates constitution. Tracked for refactor in v0.4."
    expires_after: 2026-09-01
  - invariant_id: no-direct-singleton
    matches:
      file: "Assets/Bootstrap/CompositionRoot.cs"
      line: 18
    reason: "Composition root is the one allowed singleton site."
```

Same `expires_after` mechanic as drift suppressions — forgotten suppressions surface as warnings when the date passes.

## Architecture

### Where the constitution checker lives

**Same home as Seam 2's drift checker.** Both consume the canonical design doc; both need the same code-reading capability. Lean: an interrogate extension exposing a new MCP tool (`design_constitution_check` or aligned with the v0.1.3 flow naming).

Alternative considered: a Roslyn analyzer ships with the project. Most precise for `ast`-tier invariants, but couples the project to .NET-only analyzer infrastructure and means non-Unity Captain SDLC adopters can't reuse. Defer until the Unity-specificity becomes a forcing function.

### Relationship with Seam 2 (Design ↔ Code Drift)

| Concern | Seam 2 | Seam 6 (this) |
|---|---|---|
| **Input from design doc** | The whole canonical doc | The constitution section only |
| **Question asked** | "Does the code provide what the design promises?" | "Does the code obey the rules the design declares?" |
| **Output unit** | Delta (missing/extra/renamed entity) | Violation (which rule, which code line) |
| **Code-reading needs** | Public surface + topology | Pattern matching + (eventually) AST analysis |
| **Severity model** | Per-delta, classified by kind | Per-invariant, declared in the constitution |
| **Suppression** | `drift-suppressions.yaml` | `constitution-suppressions.yaml` + in-code comments |
| **Release gate fed** | #5 | #3 |

The two share the code-reading capability decision (grep → tree-sitter → Roslyn) and ride the same upgrade path. The grep-level checker can satisfy `mechanical` tier today; promotion to tree-sitter / Roslyn unlocks `ast` tier.

### Code-reading capability (shared with Seam 2)

See `seam-design-code-drift.md` § Code-reading capability. The three-tier upgrade path (grep+asmdef → tree-sitter → Roslyn) is the same. **Choose once, use twice.**

For Seam 6 specifically:
- Tier `mechanical` invariants need only the grep level.
- Tier `ast` invariants need at least tree-sitter; some need Roslyn.
- Tier `manual` invariants need no code reading; they're documented and surface in reports as reminders.

## When the check runs

Same trigger model as Seam 2:

1. **On demand** — `interrogate constitution-check <docs-dir>`.
2. **Pre-release** — invoked by release-gate evaluation (gate #3 in seam-release-gates).
3. **On commit** *(later)* — git hook; deferred.

## Minimal first cut

- **Constitution section** parsed from canonical design doc via fenced ` ```constitution ` block.
- **Two pattern kinds** at launch: `file-regex-forbid` and `asmdef-forbid-reference`. Cover the highest-leverage mechanical invariants (no inheritance from X in Y; no asmdef cross-reference) without committing to AST work.
- **Tier `mechanical` only.** `ast` and `manual` invariants are parsed and surfaced in `unchecked` / `manual_review_required` sections of the report, but no checks run.
- **Manifest-based suppression only.** In-code suppression comments deferred to v2 — they require a more careful parse and a clear stance on what counts as a comment.
- **Output as Markdown report + JSON sidecar.** Release-gate integration follows once the JSON shape stabilizes.

Surfaces the most common Captain SDLC use cases (architectural layering rules, forbidden MonoBehaviour patterns) on the lightest possible infrastructure.

## Open scoping questions

1. **Constitution section discovery.** Does the checker find the constitution by `## Constitution` header, by the ` ```constitution ` fence, or both? Lean: both — find the fence, but require it to live inside a Constitution-headed section to keep rationale colocated.
2. **Invariant evolution and versioning.** When an invariant changes (tightened, loosened, renamed), how does the checker handle history? Lean: invariants are versioned only at the constitution `schema_version` level; per-invariant changes are additive (rename → new id, old id deprecated, both checked during transition).
3. **Cross-project invariant sharing.** If multiple Captain SDLC projects use similar architecture, can they share invariants (an "ATH constitution starter")? Lean: out of scope for the first cut; once two projects accumulate similar constitutions, extract a shared starter package.
4. **In-code suppression syntax.** When v2 of suppression lands, what comment form? `// constitution-suppress: <id> — <reason>` is the proposal but the exact prefix matters for grep efficiency.
5. **Pattern kind upgrade ordering.** Once the grep level proves out, which `ast` pattern kinds pay off first? Likely `inheritance-forbid` and `api-call-forbid`, since they're the natural extensions of the most common `mechanical` invariants. Decide closer to AST capability work.

## Definition of done

Seam 6 is shipped when:

- `interrogate constitution-check <docs-dir>` reads the constitution from the canonical design doc, runs mechanical-tier invariants against the code, and emits a violation report in the established shape.
- At least `file-regex-forbid` and `asmdef-forbid-reference` pattern kinds work end-to-end.
- The suppression manifest is read and respected; suppressed violations appear separately.
- `ast` and `manual` tier invariants are parsed and surfaced in the report (as `unchecked` / `manual_review_required`) even though they're not checked.
- Output is consumable by release-gate gate #3.
- Code-reading infrastructure is the same one chosen for Seam 2; no duplication.

Definition of done covers the seam's *contract*. Promotion to AST checking, in-code suppression, and on-commit hooks come later.

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
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- None yet captured for this seam.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for Seam 6. Built on shared code-reading infrastructure scoped in `seam-design-code-drift.md`.
