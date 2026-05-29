# Captain SDLC — Vision
Updated: 2026-04-08
Version: 0.1.4

Created: 2026-04-08

The aspirational picture of the assembled HITL augmented SDLC pipeline. Each phase described as it would look at full vision; each seam described as the connective tissue that turns the phases into a pipeline.

This document is deliberately not prioritized and not committed. It's the "everything we're aiming at" doc. Cuts and reasoning live in `candidates.md`; sequencing will live in `roadmap.md`.

## The phases

### Front — Design (interrogate)

Idea-to-doc conversion via Socratic interrogation. The human enters with an idea; the tool exits with:

- A canonical design doc (the source of truth for what's being built).
- Scoped task decomposition handed off to implementation.
- A glossary, gap audit, and resolved-question record.
- A **constitution** — a section of the canonical design doc encoding the invariants the implementation must obey: no MonoBehaviour business logic, no synchronous main-thread I/O, no direct singletons outside composition root, all mutations go through the adapter, etc. Encoded in a form downstream tools can read mechanically. The *enforcement* of the constitution (the seam) is distinct from the constitution itself (the rules) — see Seam 6.
- An audit feedback loop: when implementation diverges from the doc (and it will), the divergence is surfaced for HITL adjudication.

The canonical design doc is the contract every downstream phase reads.

### Middle — Implementation + QA (human + AI agents + ATH)

Implementation is HITL. The human writes code, possibly with AI assistance, possibly autonomously for narrowly-scoped tasks under explicit approval. QA is mechanical: ATH drives Unity playmode against smoke skills that assert behavior and detect regression.

At full vision, ATH covers:

- **Behavior assertions** (current — `ath-cmd`, `ath-state`, `ath-wait`).
- **Performance instrumentation** — frame time, GC, GPU, memory, draw calls, scene-load time. Editor-first; player-build via `DEVELOPMENT_BUILD` is a deferred investigation.
- **Deterministic replay** — input commands (not raw input — *commands*), RNG seeds, frame counter, host-state checkpoints. Every smoke run is replayable against a different build for bisection.
- **Baseline + regression envelope** — every run produces an envelope of measured values; next run flags drift outside tolerance. Perf is the first concrete instance; alloc, asset sizes, scene-load times follow.
- **Artifact diff at checkpoints** — scene-graph slices, serialized state, savegame slices. Shares the checkpoint capture with replay. This is the *runtime* variant; the *between-release* variant (see Back below) is a separate phase, and the boundary between them is an open scoping question (`candidates.md` § Open scoping questions item 7).
- **Save migration testing** — previous-version saves still load after version bump.
- **Localization key audit** and **impossible-state / quest-deadlock detection** — mechanical content-adjacent validation. Cheap to stub early.

### Back — Release (claude-release)

Commit finalization, changelog generation, version bump, git tagging, release publishing. At full vision, also:

- **Dependency audit** — CVE check, license audit, staleness check, Unity package upgrade safety. Surfaced visibly in release output, not silent.
- **Release gates** — must have green smokes against this commit, no high-severity drift since last release, changelog mentions every user-visible scoped task, constitution invariants hold. Human still approves; gates enforce that the upstream tools' outputs are actually consistent.
- **Between-release artifact diff** — what actually changed in the shipped binary, settings, prefabs. Boundary with ATH-runtime artifact diff (Middle, above) is an open scoping question — see `candidates.md`.

### CICD — Build + Deploy (proposed new tool)

Possibly distinct from claude-release, possibly a layer underneath. Open scoping question. Handles:

- Headless build automation triggered by commits/PRs.
- Artifact storage and versioning with predictable paths and retention policy.
- Pipeline orchestration: commit → builds → tests → gates → deployments.
- Status reporting and notifications.
- Deployment handoff to distribution platforms (Steam, itch.io). No platform-side marketing operations here.

Boundary with claude-release: "is this code release-worthy" (release) vs "turn release-worthy code into running artifacts" (CICD). Different lifecycles, different cadences. Current lean is separate tool.

### Live Ops — Ingestion (likely distributed)

Post-ship feedback flows back into the pipeline. For HITL solo, the leverage is in *ingestion*, not infrastructure — don't build Sentry, build the pipes from production to planning:

- Crash reports → interrogate backlog tasks (auto-triaged where possible, HITL-adjudicated for severity).
- Steam reviews / Discord / forum bug reports → interrogate backlog tasks.
- In-the-wild performance samples → ATH baseline updates (with the editor-vs-player caveat called out).
- Player telemetry → trend dashboards: FPS drift, crash frequency, completion funnels, save-size growth.
- Feature flag management for staged rollouts.

This is where the GDD writeback gap closes. Post-ship reality informs the design system's next iteration.

**Prerequisite: Privacy framework.** Live Ops ingestion inherits its design from `privacy-framework.md` (concrete today + structural primitives) and `privacy-policy-aspirational.md` (full policy that activates per trigger). The framework is in place; the policy specifics activate when a real Live Ops channel is being implemented or when Captain SDLC ships against a commercial product. In solo dogfooding mode no real player data is flowing, so privacy isn't actively blocking; it's pre-decided so Live Ops doesn't repaint the architecture later. Crash reports carry stack traces; reviews carry user identifiers; perf samples may carry hardware fingerprints — each fits the four-level classification (`public` / `pseudonymous` / `personal` / `sensitive`) and the trace-vs-side-store split established in the framework.

### Marketing — Operations (pipeline ops only, no authoring)

Mechanical reuse of artifacts produced by other phases. Explicitly not creative authoring:

- Changelog → patch notes templating (mechanical transform, no prose generation).
- QA-smoke screenshots and clips → marketing asset library.
- Asset formatting for distribution platforms: Steam capsule, itch banner, social aspect ratios.
- Posting automation (push assets and templated copy to platforms).

Likely a small layer or skill collection rather than a standalone tool. Lives downstream of CICD.

## The seams (Captain SDLC's actual value)

The tools are useful individually. The *seams between them* are what make the assembled artifact a pipeline rather than three independent tools.

### Seam 1 — Pipeline trace

Shared cross-tool state that links: *this design doc* → *these scoped tasks* → *this commit* → *these smoke runs* → *this release* → *these post-ship issues*. Lets you ask "which design decision introduced this regression" and get an actual answer rather than re-deriving it by hand.

The most architecturally ambitious item. Likely needs to start as "every tool emits structured traces to a known location with a shared schema" and only later become "a unified consuming layer." Schema first; consumer last. Otherwise it becomes the kind of forever-roadmap item that never ships.

### Seam 2 — Design ↔ code drift

Interrogate audits docs against themselves. This seam audits *code* against the canonical doc. Doesn't auto-fix — surfaces deltas for HITL adjudication, which matches the pipeline's stated philosophy.

Requires interrogate to gain code-reading capability: asmdef topology, public APIs, adapter surfaces, type-level dependencies. Doesn't need semantic understanding — structural conformance is enough.

### Seam 3 — Release gates

claude-release becomes assertive instead of trust-based. Reads ATH smoke results, drift detection output, dependency audit, constitution-enforcement output. Asserts they pass before allowing release. Human still approves; the gate prevents inconsistent state from shipping by accident.

### Seam 4 — Cross-tool contract testing

ATH expects adapter state-key contracts. Interrogate expects doc schemas. claude-release expects commit and changelog conventions. CICD will expect build manifest conventions. Drift between tools' assumptions is a quiet failure mode in any multi-tool pipeline. A test layer verifies the tools agree on their shared contracts.

Small surface, large blast radius when missing.

### Seam 5 — Live Ops → planning ingestion

Post-ship issues route into interrogate's backlog. In-the-wild perf samples update ATH baselines. Player telemetry feeds trend dashboards consulted at design time. The post-ship loop closes back into the front of the pipeline.

### Seam 6 — Constitution enforcement

The canonical design doc's **constitution** (its invariants section) is the input. A **constitution checker** — form TBD — verifies the invariants hold against the code. Output feeds Seam 3 (release gates).

Vocabulary distinction: *constitution* = the rules (and the section that holds them); *constitution checker* = the tool that enforces them; *constitution enforcement* = the seam (this).

Open scoping question: machine-readable form for the constitution, and analyzer form for the checker. Roslyn analyzer is heaviest but most precise; YAML in the design doc + ad-hoc grep checks is lightest. Likely starts at the lighter end.

## What's deliberately out

These are not "considered useless" — they're "not where leverage is for HITL solo at this scale." Reasoning preserved so reversals are informed (`candidates.md` has full detail):

- **AI art / model / texture / audio generation.** Creative authoring.
- **Copy authoring** (marketing prose, narrative, dialogue). Creative.
- **Autonomous AI codegen safety rails.** The H in HITL is the safety rail.
- **Recovery / rollback infrastructure (general).** Git + version-bump handles. Save migration kept separately.
- **Economic / velocity / scope-burn modeling.** Overengineering for solo HITL.
- **Narrative consistency / dialogue contradiction detection.** Creative-adjacent. Non-creative slice (localization, impossible states) kept.
- **A/B testing infrastructure.** Premature for HITL solo at current scale.

Re-evaluate any of these if team size, audience, or scope grow materially.

## Open structural questions

Decisions needed before items can be properly sequenced:

1. **Does CICD become its own tool, or expand claude-release?** Current lean: separate tool. Boundary unclear.
2. **Where does Live Ops ingestion live?** Distributed across existing tools (current lean) or its own tool.
3. **Trace schema before anything else.** Seam 1 is load-bearing for Seams 3 and 5; defining the trace schema may need to happen before any per-tool trace emission begins.
4. **Constitution's machine-readable form.** Determines whether Seam 6 is a Roslyn analyzer, an interrogate extension, or its own tool.
5. **Single Captain SDLC roadmap vs per-tool roadmaps.** Cross-tool seams definitely need a Captain SDLC home; per-tool items might stay in their per-tool ROADMAP.md files.
6. **Constitution as separate item vs subsumed into design-code drift.** Currently split; could be folded.

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
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)

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
