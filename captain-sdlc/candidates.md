# Captain SDLC — Candidates
Updated: 2026-04-08
Version: 0.1.4

Created: 2026-04-08

Enumerated set of proposed features and additions to the Captain SDLC pipeline. Includes items currently accepted, proposed, deferred, or cut. Acts as the considered-set receipt so cuts don't get re-litigated and so reversals are informed.

**Status legend:**
- **Accepted** — agreed and will be roadmapped.
- **Proposed** — under consideration, not yet committed.
- **Deferred** — agreed-valuable but explicitly later, with reason.
- **Cut** — considered and rejected, with reasoning preserved.

Each item names its likely home (which may be "TBD" or "new tool") and a short note.

---

## Cross-tool seams

The connective tissue between the tools. These are **not** owned by a separate "Captain SDLC layer" tool — their schemas and conventions live in this nerve-center repo, and their implementations are distributed across the emitting/consuming tools.

| Item | Likely home | Status | Notes |
|---|---|---|---|
| Pipeline trace / shared cross-tool state | Nerve-center (schema) + per-tool (emission) | Proposed | Schema first; unified consumer later. Most ambitious item on the list. See `trace-schema.md`. |
| Design ↔ code drift detection | interrogate (extended) | Proposed | Requires code-reading capability in interrogate. |
| Release gates | claude-release | Accepted | Smokes green, drift clean, changelog complete, constitution holds. |
| Cross-tool contract testing | Nerve-center (schema) + per-tool (validation) | Proposed | Verifies tools agree on shared schemas. Small surface, large blast radius. |
| Live Ops → planning ingestion | Nerve-center (schema) + per-tool (ingest) | Proposed | Pipes from post-ship issues into interrogate backlog. **Blocked on Privacy policy (below).** |
| Constitution enforcement | TBD | Proposed | Roslyn analyzer / interrogate extension / own tool. Scoping needed. See Open scoping questions. |
| Privacy framework + aspirational policy | Nerve-center (framework) | Accepted (framework); Aspirational (policy) | Framework in place at `privacy-framework.md` (classification primitives, trace-vs-side-store split, AI processing carve-out applicable today). Aspirational full policy at `privacy-policy-aspirational.md` activates per trigger (commercial release, first Live Ops channel, EU/CA player, second developer, first sensitive payload schema). |

---

## ATH (this repo)

| Item | Status | Notes |
|---|---|---|
| Deterministic replay | Accepted | Highest absolute leverage within ATH. Replay state belongs on host adapter. |
| Baseline + regression envelope | Accepted | Subsumes perf monitoring as its first concrete instance. |
| Editor-time perf instrumentation | Accepted | First concrete phase under regression envelope. Already partially scoped in `ROADMAP.md`. |
| Build-time perf monitoring (DEVELOPMENT_BUILD) | Deferred | Data collection is straightforward; transport unsolved. See existing roadmap note. |
| Artifact diff at checkpoints (runtime) | Accepted | Shares checkpoint capture with replay; design jointly. |
| Memory profiling | Proposed | Extension to perf instrumentation. |
| GPU profiling | Proposed | Extension to perf instrumentation. |
| Job / Burst scheduling validation | Proposed | Extension to perf instrumentation. Unity-specific. |
| Profile capture automation | Proposed | ATH-triggered profile captures stored as artifacts. Crosses with Artifact Diff. |
| Save migration testing | Proposed | Previous-version saves still load. Smoke skill + release gate. Reference: prototype exists in `../dirigible`. |
| Localization key audit | Proposed | Mechanical, content-adjacent. Cheap to stub early. Reference: `../dirigible` uses Unity's standard localization package — good dogfood target. |
| Impossible-state / quest-deadlock detection | Proposed | Mechanical state-graph analysis. Cheap stub. |
| `ath-wait` predicate `state_changed:<key>` | Accepted | Already in `ROADMAP.md`. Signal-edge complement to `state_equals`. |
| Extract `ath-smoke-fullloop` to host-side bridge plugin | Accepted | Portability fix. Already in `ROADMAP.md`. |
| Phase 8 — fresh-project minimal-adapter dry-run | Accepted | Already in `ROADMAP.md`. |
| `/ath-smoke-traversal` skill | Proposed | Already in `ROADMAP.md`. Cross-host smoke template. |
| ATH gets its own Claude Code setup | Proposed | Already in `ROADMAP.md`. |

---

## interrogate (`claude-interrogate-src`)

| Item | Status | Notes |
|---|---|---|
| Code as input to interrogate | Proposed | Read asmdef topology, public APIs, adapter surfaces, type-level dependencies. Prerequisite for design ↔ code drift. See `code-reading-capability.md`. |
| Interrogate backlog API | Proposed | A backlog / task-management surface inside interrogate that Live Ops ingestion (Seam 5), cross-channel dedup, and design ↔ code drift (Seam 2) all assume exists. Currently aspirational — referenced by several seam docs but not yet authored on the interrogate side. |
| Design ↔ code drift detection | Proposed | See Cross-tool seams. |
| Constitution in canonical design doc | Proposed | Machine-readable section encoding invariants the implementation must obey. Feeds Seam 6 (constitution enforcement). |
| Post-ship feedback ingestion → backlog | Proposed | Crash reports, reviews, forum issues → tasks. |
| Decision rationale tracking | Proposed | "Why this exists" annotations for design elements. Survives author memory loss. |

---

## claude-release

| Item | Status | Notes |
|---|---|---|
| Release gates | Accepted | See Cross-tool seams. |
| Dependency audit (CVE / license / staleness) | Accepted | Surface in release output, not silent. |
| Unity package upgrade safety check | Proposed | Subset of dependency audit; Unity-specific. |
| Between-release artifact diff | Proposed | What actually changed in the shipped binary, prefabs, settings. |
| Save migration gate | Proposed | Tied to ATH save migration smoke. |
| Constitution enforcement output ingestion | Proposed | Feeds release gates. |

---

## CICD (proposed new tool)

Currently TBD whether this is a new tool or a claude-release expansion. Lean: new tool.

| Item | Status | Notes |
|---|---|---|
| Headless Unity build automation | Proposed | Triggered by commits/PRs. |
| Artifact storage and versioning | Proposed | Predictable paths, retention policy. |
| Pipeline orchestration | Proposed | Commit → builds → tests → gates → deployment. |
| Status reporting / notifications | Proposed | Cross-tool, surfaces to the human operator. |
| Deployment handoff to distribution platforms | Proposed | Steam, itch.io. No marketing operations here. |
| Multi-platform build matrix | Proposed | If targeting more than one platform. |

---

## Live Ops (likely distributed across existing tools)

| Item | Status | Notes |
|---|---|---|
| Crash report ingestion → backlog | Proposed | Pipe to interrogate. Inherits privacy framework primitives; activates the aspirational policy when implementation starts. |
| Player review / forum ingestion → backlog | Proposed | Steam, Discord, forums. Inherits privacy framework; aspirational policy triggers on implementation. |
| In-the-wild perf samples → ATH baseline | Proposed | Closes loop on regression envelope. Editor-vs-player caveat applies. Inherits privacy framework; aspirational policy triggers on implementation. |
| Trend dashboards | Proposed | FPS drift, crash frequency, completion funnels, save-size growth. Inherits privacy framework; aspirational policy triggers on implementation. |
| Feature flag management | Proposed | Possibly later. |
| A/B testing infrastructure | Cut | Premature for HITL solo at current scale. Revisit if audience scales. |

---

## Marketing (pipeline ops only)

Explicitly no authoring — these are all mechanical artifact transforms.

| Item | Status | Notes |
|---|---|---|
| Changelog → patch notes templating | Proposed | Mechanical transform. No prose generation. |
| QA-smoke screenshot/clip harvesting | Proposed | Reuses existing QA output. |
| Asset formatting for distribution platforms | Proposed | Steam capsule, itch banner, social aspect ratios. |
| Posting automation to platforms | Proposed | Push templated assets to platforms. |
| Marketing changelog (player-facing artifact diff narrative) | Proposed | Surfaces artifact / balance diffs translated into player-readable form ("seed regen +50%", "boss HP reduced 20%"). Templated transform — answers "why do I feel stronger/weaker this patch." Sits downstream of Artifact Diff (between-release variant), upstream of Posting Automation. Inputs are mechanical; per-release output is mechanical; the per-field translation templates are authored once. |
| Copy authoring (patch notes prose, marketing blurbs) | Cut | Creative authoring; out per project philosophy. |
| Visual asset generation (banners, capsules, key art) | Cut | Creative; out. |

---

## Explicitly cut (with reasoning)

Preserved so cuts don't get re-litigated and so reversals are informed:

| Item | Reason |
|---|---|
| AI art / texture / model / audio generation | Creative authoring; out per project philosophy. |
| Copy / narrative / dialogue authoring | Creative; out. |
| Narrative consistency / dialogue contradiction detection | Creative-adjacent. Non-creative slice (localization, impossible states) kept separately. |
| Autonomous AI codegen safety rails | The H in HITL is the safety rail; redundant infrastructure. |
| Recovery / rollback infrastructure (general) | Git + version-bump handles for solo dev. Save migration kept separately as a real thing. |
| Economic / velocity / scope-burn modeling | Overengineering for solo HITL at this scale. |
| LiveOps A/B testing | Premature for HITL solo; revisit if audience scales. |
| Cognitive load reduction tooling (Model 2 #6) | Too vague as stated. Concrete instances (architecture maps, dependency visualizers) may re-enter individually. |
| Sentry-style crash infrastructure | Not the leverage point. Ingestion pipe is enough; don't reinvent existing tools. |
| Production-grade analytics platform | Not the leverage point. Trend dashboards on top of cheap log ingestion is enough at this scale. |

---

## Open scoping questions

These need decisions before items can be properly sequenced into a roadmap:

1. **Where does CICD live?** New tool, or claude-release expansion? Current lean: new tool.
2. **Trace schema first.** Seam 1 (pipeline trace) is upstream of several other seams. Schema definition may need to precede any per-tool trace emission.
3. **Constitution's machine-readable form.** Determines whether Seam 6 (constitution enforcement) is a Roslyn analyzer, an interrogate extension, or its own tool.
4. **Live Ops distribution.** One tool or scattered into existing? Current lean: scattered.
5. **Single Captain SDLC roadmap vs per-tool roadmaps.** Cross-tool seams definitely need a Captain SDLC home; per-tool items might stay in their per-tool ROADMAP.md files, or might be mirrored here for visibility.
6. **Constitution layer as separate item vs subsumed into design-code drift.** Currently split into two items in this list; could be folded into one if the analyzer can be shared.
7. **Where does the boundary between "captured by ATH at runtime" and "captured by CICD between builds" land for artifact diff?** Same primitive, two consumers; boundary affects ownership.

## Reference projects for dogfooding

- **`../dirigible`** — has a save migration testing prototype worth borrowing from; also uses Unity's standard localization package, making it a good first dogfood target for both Save Migration Testing and Localization Key Audit.

## Cross-References

- [Captain SDLC](./README.md)
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
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- No resolved decisions captured yet.

## Open Questions

- None.

## Version History

- 0.1.4 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
