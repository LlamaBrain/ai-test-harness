# Captain SDLC — Privacy Framework
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

**Scope:** what's concrete *today* in Captain SDLC privacy, plus the structural primitives needed so future privacy-sensitive work (Live Ops ingestion, multi-developer access, shipped products) inherits a consistent design.

Companion to `privacy-policy-aspirational.md`, which covers the full hypothetical policy that would govern when those concrete situations arrive. This doc is the today-and-primitives layer.

**Status:** Planning. The AI-processing section reflects current reality. Everything else is structural commitment without implementation.

## Why this doc exists (not the full policy yet)

Captain SDLC's current state:
- Solo dogfooding by one developer on one machine.
- No shipped product is currently piping player data into the pipeline.
- No Live Ops ingestion is wired up.
- No legal jurisdiction question is active because no data subjects exist yet besides the developer themselves.

Writing a full privacy policy now would be designing for hypothetical requirements with no way to validate the choices. Worse, the policy text would shape future decisions before the actual constraints are known. Wrong shape.

What *is* real today:
- **AI processing.** interrogate sends design docs through Claude API. Future seams (drift detection, constitution checking, smoke triage) will route more content through AI tools. This is happening *now*, in solo mode, and is a privacy-relevant data flow even with one user.
- **Trace storage primitives.** The trace schema's storage and segregation decisions affect everything privacy-related downstream. Settling the primitives now (classification levels, trace-vs-side-store split) costs little and prevents repainting the architecture later.

This doc covers those two things. Everything else is in the aspirational doc with explicit triggers.

## What's concrete today: AI processing

### The current flow

Anything that passes through a Captain SDLC tool that calls Claude API is sent to a third-party processor:

| Tool | Data sent to Claude | Frequency |
|---|---|---|
| interrogate | Design docs (Markdown content) | Per command invocation |
| Agent loops in Claude Code | Source code, design docs, conversation history, file contents | Per agent action |
| Future: drift / constitution checkers | Code structure + design doc excerpts | Per check |
| Future: smoke transcript triage | Smoke run logs, possibly containing host-state values | Per triage |
| Future: trace consumers driven by AI | Trace events for analysis | Per query |

This is fine for the solo dogfooding case because the only data subject is the developer themselves. It stops being fine the moment:
- Player data (crash reports, reviews, telemetry) enters the pipeline.
- A second developer's data ends up in shared traces.
- Customer data accidentally gets baked into design docs or test fixtures.

### Concrete guidance for today

1. **Treat design docs and smoke transcripts as outbound to Claude.** Don't bake real customer data, credentials, or personally-identifying examples into them. If a smoke needs realistic test data, use synthetic data.
2. **Be aware that file paths in stack traces leak Windows usernames.** Already true for any crash a smoke captures today. Worth scrubbing even in solo mode because the scrubbing pattern is the same one we'll need later.
3. **Don't paste live API keys, credentials, or other secrets into anything a Captain SDLC tool touches.** Use `.env` files, the OS credential store, or test stubs — not literals in code or docs.
4. **Claude API's own data retention applies.** Whatever guarantee Anthropic provides on prompt/response retention is the floor for everything Captain SDLC processes through it. If that retention story changes (Anthropic policy update, account-level controls), this doc may need to follow.

These are mechanical, achievable today, and don't require any new tool work. They're the equivalent of "don't commit `.env` to git" — discipline, not infrastructure.

### Structural commitment for tomorrow

When Live Ops ingestion lands and real player data starts flowing, there's a hard line:

**`sensitive`-class data never gets sent to Claude API directly.** If AI triage is needed for sensitive payloads, the payload gets redacted to `pseudonymous` form first; the redacted form is what Claude sees; the link back to the original lives in the side-store under stricter access.

This is a structural decision, not a policy proposal. It affects how Live Ops ingestion is designed. The aspirational doc unpacks what triage flows actually look like under that constraint.

### Canonical ownership

This section (the structural AI processing commitment) is the **canonical statement**. Other Captain SDLC docs that depend on the commitment — `privacy-policy-aspirational.md` § AI processing policy at scale, `cross-channel-dedup.md` § Detection signals → Class C, `seam-live-ops-ingestion.md` § Privacy inheritance — reference this section rather than restating it. If the commitment evolves, it evolves here first; downstream docs may add specifics but should not re-author the structural statement. Prevents wording drift across the docs set.

## Data classification primitives

Four levels. Established now so payload schemas can annotate fields when they're written.

| Level | Meaning | Trace storage | AI exposure |
|---|---|---|---|
| **public** | Information that can be in any log, screenshot, or shared file with no consequence | Direct in trace | OK |
| **pseudonymous** | Identifies a session or device but not a real-world identity | Direct in trace | OK with awareness |
| **personal** | Information tied to a real-world identity (Steam ID, email, IP, full file paths containing username) | Reference in trace; payload in side-store | Only after redaction to `pseudonymous` |
| **sensitive** | Information whose disclosure would harm the subject (real names, payment info, biometric identifiers, save-game contents) | Reference in trace; payload in side-store with stricter access | Never directly; only synthesized aggregates if at all |

Concrete examples for Unity game contexts:

- Frame time samples: `public`.
- Hardware fingerprint (CPU model, GPU model, RAM): `pseudonymous`.
- Steam ID, Discord handle: `personal`.
- Player name as configured in-game, save file contents, crash dump memory: `sensitive`.
- Stack trace file paths (which include OS username on Windows): `personal` (because of the username leak).
- Geographic IP-derived location: `personal`.

Classification is per-field, not per-payload. A crash report payload may contain `public` frame timing, `pseudonymous` hardware info, and `personal` file paths — each is handled per its own class.

## Trace vs side-store structural commitment

The pipeline trace (per `trace-schema.md`) stays grep-safe and shareable. To preserve that property when sensitive payloads start arriving:

- **`public` and `pseudonymous` payloads** can live directly in trace events.
- **`personal` and `sensitive` payloads** live in a **side-store** keyed by a reference embedded in the trace event. The trace event carries the reference (an opaque ID, a hash, or a path under `.captain-sdlc/side-store/`); the actual payload is in the side-store.

The side-store has:
- Its own access controls (stricter than the trace).
- Its own retention policy (typically shorter than the trace).
- Its own backup discipline (more deliberate than the trace).
- No requirement that consumers of the trace can access the side-store. A consumer that can only read the trace gets the structural picture (an event existed, of this kind, against this commit) without the sensitive content.

This is the structural commitment. Concrete implementation (side-store format, encryption-at-rest, retention defaults) lives in the aspirational doc and gets settled when Live Ops ingestion is actually scoped.

## Explicit deferrals

What this doc deliberately does *not* cover, and what triggers each item being written:

| Deferred topic | Trigger |
|---|---|
| Field-level classification for Live Ops payload schemas | First Live Ops ingestion channel being designed for implementation |
| Redaction conventions for crash reports, reviews, telemetry | Same trigger |
| Retention policies per class | Same trigger |
| Consent and data-subject rights mechanisms (GDPR/CCPA-shaped) | First commercial release with EU/CA players |
| Multi-developer access controls on trace and side-store | Second human gaining commit access to a Captain SDLC project |
| Cross-machine trace merging and the privacy implications | CICD landing as an additional event emitter |
| Audit logging for side-store access | First sensitive payload schema being written |
| Vendor / sub-processor disclosure (Claude API, distribution platforms, analytics) | First commercial release |
| Data breach response plan | First sensitive payload schema being written |
| Cross-border data transfer policy | First EU/CA player; first non-US developer with commit access |

Each of these gets a section in `privacy-policy-aspirational.md` as a forward-looking sketch. None get policy text written today.

## Open scoping questions

1. **Where does the side-store live, physically?** Under `.captain-sdlc/side-store/`, or somewhere outside the trace store entirely? `.gitignored` either way. Decision matters when implementation starts.
2. **How are side-store references encoded in trace events?** Opaque ID, content hash, or path? Opaque ID is most flexible; content hash gets free dedup; path is most direct. Lean opaque ID.
3. **Does the AI-processing structural commitment apply retroactively to design docs?** If a design doc has historical content that would today be classified `personal` or `sensitive`, does that flow stop, or do we accept history? Lean: stop the flow when the doc is next edited; don't rewrite history.
4. **Claude API's data retention as a moving target.** Anthropic's retention policy can change. Captain SDLC's privacy posture depends on it. Worth tracking the API guarantee explicitly somewhere — maybe in this doc as a versioned reference.

## Definition of done

The framework is established when:

- All four classification levels are referenced from at least one other Captain SDLC schema (likely the trace schema's `refs` or `payload` sections gain a `classification` field).
- The trace-vs-side-store split is referenced from `trace-schema.md` (currently flagged as a deferred privacy concern there).
- The AI-processing structural commitment ("`sensitive` data never goes to Claude directly") is acknowledged in any seam doc that handles potentially-sensitive data.
- Concrete-today guidance is being followed by the dogfooding developer.

The framework doesn't need implementation. It needs to be consulted when implementation starts.

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
- [Captain SDLC — Privacy Policy (Aspirational)](./privacy-policy-aspirational.md)
- [Captain SDLC — Seam 2: Design ↔ Code Drift](./seam-design-code-drift.md)
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- **2026-05-28** — Four-level classification (`public` / `pseudonymous` / `personal` / `sensitive`) adopted as the primitive. Per-field, not per-payload.
- **2026-05-28** — `sensitive` data does not pass through Claude API directly. Redaction to `pseudonymous` form precedes any AI processing.
- **2026-05-28** — Pipeline trace carries only `public` and `pseudonymous` payloads directly; `personal` and `sensitive` payloads live in a side-store keyed by reference.
- **2026-05-28** — Privacy is not currently blocking on Live Ops ingestion in solo mode because no player data is flowing. Live Ops gets recharacterized: it inherits these primitives when it's actually built; the framework doc unblocks the design.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial framework doc. Solo-dogfooding context. Companion to aspirational policy.
