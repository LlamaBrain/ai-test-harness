# Captain SDLC — Conventions
Updated: 2026-04-08
Version: 0.1.2

Created: 2026-05-28

Cross-cutting conventions that multiple Captain SDLC docs depend on. Currently covers three: the `.captain-sdlc/` directory layout, the `schema_version` policy for config files, and the fenced-block convention for embedded structured data.

**Status:** Reference. Pulled together because the same conventions were getting re-asserted across multiple planning docs with risk of drift. This doc is the single source of truth.

## Why these live together

Each convention is small. None of them deserves its own doc. But each is referenced from at least three other docs, so without a central home the wording drifts and the conventions silently fork. This doc owns them all.

## The `.captain-sdlc/` directory

A per-project state directory at the repo root, holding Captain SDLC's persistent project-local state. Always `.gitignored` (with the possible exception called out below). Created lazily by tools that need it.

### Layout

```
.captain-sdlc/
├── trace/                            # Pipeline trace events (see trace-schema.md)
│   └── YYYY-MM-DD.jsonl              # One file per day, append-only
├── side-store/                       # personal/sensitive payloads (see privacy-framework.md)
│   └── <opaque-id>.json              # Keyed by reference embedded in trace events
├── release-gates.yaml                # Per-project gate config (see seam-release-gates.md)
├── drift-suppressions.yaml           # Drift suppression manifest (see seam-design-code-drift.md)
├── constitution-suppressions.yaml    # Constitution suppression manifest (see seam-constitution-enforcement.md)
├── liveops-routing.yaml              # Per-channel Live Ops routing (see seam-live-ops-ingestion.md)
├── dedup-config.yaml                 # Cross-channel dedup config (see cross-channel-dedup.md)
├── contracts.yaml                    # Contract testing config (see seam-contract-testing.md)
└── privacy-config.yaml               # Privacy framework toggles (see privacy-framework.md)
```

Subdirectories and files appear only when the corresponding tool/feature is wired up. A minimal Captain SDLC project may have only `trace/` and one or two config files.

### Git policy

- **Trace and side-store: always `.gitignored`.** Local state. Cross-machine merging is a deferred concern (see `trace-schema.md`).
- **Config files (`*.yaml`): committed by default.** These define project policy; sharing them across machines is the point. The `.captain-sdlc/.gitignore` should explicitly exclude `trace/` and `side-store/` while leaving config files tracked.

Recommended `.gitignore` snippet for the directory itself:

```
.captain-sdlc/trace/
.captain-sdlc/side-store/
```

### Ownership

The directory and its layout are owned by this nerve-center docs set. Tools that introduce new files under `.captain-sdlc/` should add them to this doc.

## The `schema_version` convention

Every machine-readable config file and structured artifact in Captain SDLC carries a top-level `schema_version` integer. This includes:

- `.captain-sdlc/release-gates.yaml`
- `.captain-sdlc/drift-suppressions.yaml`
- `.captain-sdlc/constitution-suppressions.yaml`
- `.captain-sdlc/liveops-routing.yaml`
- `.captain-sdlc/dedup-config.yaml`
- `.captain-sdlc/contracts.yaml`
- `.captain-sdlc/privacy-config.yaml`
- Trace event envelope (each event carries it inline)
- Gate verdict shape
- Drift report shape
- Constitution violation report shape
- All Live Ops ingestor payload schemas

### Rules

- **Integer, not semver.** `schema_version: 1`, `schema_version: 2`. Simpler than semver and removes the temptation to use minor versions for breaking changes.
- **Additive evolution within a version.** New optional fields and new enum values are fine. Renaming, removing, or changing types of existing required fields requires a `schema_version` bump.
- **Consumers refuse unknown versions.** Tools that read a config with an unrecognized `schema_version` emit an error and refuse to proceed, rather than guessing. Catches drift visibly.
- **Per-config evolution.** Each schema versions independently. `release-gates.yaml` schema_version 1 doesn't pin anything else's version.
- **No automatic migration.** If a project's config is at schema_version N and the tool requires N+1, the human (or a separately-invoked migration step) updates the config. Not silent.

### Why integer-per-schema instead of one global Captain SDLC version

A global version would couple all schemas — bumping one would either force a global bump or create a confusing version-mismatch surface. Per-schema versioning keeps the impact local.

## The fenced-block convention for embedded structured data

When a doc embeds machine-readable structured data inside Markdown prose, use a Markdown code fence with a **content-kind language tag**. Tools that consume the structured data find it by fence tag.

### Conventions

- **Tag names describe the content's role, not its format.** Use ` ```constitution `, not ` ```yaml `. The format (YAML, JSON, JSON Schema) is implicit from context; the role (constitution rules, gate schema, contract schema) is what tooling looks for.
- **One block per role per doc.** A constitution-bearing doc has exactly one ` ```constitution ` block. If the role needs multiple blocks, the tag should indicate that (` ```constitution-mechanical ` vs ` ```constitution-ast `, etc.).
- **Surrounding prose carries the rationale; the block carries the rules.** Same single-source-of-truth pattern across all uses.

### Current tag registry

| Tag | Role | Defined in |
|---|---|---|
| ` ```constitution ` | Constitution invariants | `seam-constitution-enforcement.md` |
| ` ```gate-schema ` *(proposed)* | Release-gate schema definitions | `seam-release-gates.md` |
| ` ```contract-schema ` *(proposed)* | Contract testing schema definitions | `seam-contract-testing.md` |
| ` ```ingestor-payload ` *(proposed)* | Live Ops payload schemas | `seam-live-ops-ingestion.md` |

New tags get added to this table when new embedded-block roles appear.

### Renderer compatibility

GitHub and most Markdown renderers don't know these custom tags. They'll render as plain code blocks with no syntax highlighting, which is fine — readability is human-prose-driven, not syntax-highlighting-driven. If a project wants highlighting, it can configure renderer aliases (e.g., GitHub mapping `constitution` → `yaml` for display purposes) without affecting tool discovery.

## The suppression file convention

Several Captain SDLC seams have an "expected violations" problem — drift detection (Seam 2), constitution enforcement (Seam 6), and likely future seams (contract test failures, Live Ops triage decisions). Each needs a way for humans to mark known-and-accepted violations without changing either the code or the canonical doc.

All such suppression files follow this shape:

```yaml
schema_version: 1
suppressions:
  - <identifier-fields>      # which violation this suppression addresses
    reason: <required-text>  # why it's accepted
    expires_after: <date>    # optional; warn when overdue
```

### Common rules

- **`schema_version`** at top per the convention above.
- **`reason` is required.** Empty reason rejected at parse time.
- **`expires_after` is optional but encouraged** for "we'll fix this later" suppressions. When the date passes, the tool warns rather than silently rejecting — surfaces forgotten cleanup.
- **Identifier fields are seam-specific.** Drift uses `named_entity` / `design_ref` / `kind`; constitution uses `invariant_id` + `matches` (file_glob, file, line). Each seam defines its own; the wrapper is shared.
- **Suppressions appear in violation reports under a `suppressed` section**, not as active violations. They're acknowledged, not hidden.

### Current suppression files

| File | Seam | Identifier fields |
|---|---|---|
| `.captain-sdlc/drift-suppressions.yaml` | Seam 2 | `named_entity`, `design_ref`, `kind` |
| `.captain-sdlc/constitution-suppressions.yaml` | Seam 6 | `invariant_id` + `matches` block |

Future seams that need suppression files inherit this convention. The wrapper is shared; the identifier fields are seam-specific.

## Open scoping questions

1. **Migration support.** "No automatic migration" is the current policy; if config files start including more state, automated migration may be worth supporting. Decide when the cost becomes visible.
2. **Validation tooling.** A small `captain-config-check` tool that validates every `.captain-sdlc/*.yaml` against its schema would catch drift earlier than per-tool failures. Probably belongs as a wrapper around Seam 4's contract-testing infrastructure.
3. **Per-config schema discoverability.** Currently each tool knows where its own schema lives. A registry (`.captain-sdlc/schemas/` or similar) could centralize. Defer until contract testing is operational.

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Code-Reading Capability](./code-reading-capability.md)
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

- **2026-05-28** — `.captain-sdlc/` is the canonical state directory. Layout owned by this doc.
- **2026-05-28** — `trace/` and `side-store/` always `.gitignored`. Config files committed.
- **2026-05-28** — Integer `schema_version` per config / artifact. Additive evolution within version. Consumers refuse unknown versions.
- **2026-05-28** — Fenced-block tags name the content's role (`constitution`, `gate-schema`, etc.), not its format. Registry maintained here.

## Open Questions

- None.

## Version History

- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial conventions doc. Consolidates `.captain-sdlc/` layout, `schema_version` policy, and fenced-block convention.
