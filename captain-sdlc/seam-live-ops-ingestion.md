# Captain SDLC — Seam 5: Live Ops Ingestion
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

Planning doc for **Seam 5: Live Ops → Planning Ingestion** — the post-ship loop that pipes real-world play (crash reports, reviews, in-the-wild perf samples, telemetry) back into the planning system so each release doesn't re-discover problems from scratch.

**Status:** Planning, with substantial aspirational caveat. Captain SDLC is currently solo dogfooding with no shipped commercial product. This seam becomes load-bearing when a project ships and real player data starts flowing. Until then, the doc captures structural intent so the design isn't started from a blank page when the trigger fires.

## Goal

Close the post-ship loop. When a real player crashes, complains, or generates telemetry that exposes a regression the smokes didn't catch, that signal lands somewhere useful — interrogate's backlog, ATH's baseline, a trend dashboard — rather than dying in a third-party service's UI.

Mechanically, this is "pipes from existing third-party services into the planning queue," not a new crash-reporting product.

## Non-goals

- **Not building Sentry.** Captain SDLC doesn't host crash reporting infrastructure. It ingests *from* existing services (Steam's reporting, BugSplat, Sentry-the-product, Discord, etc.) into its own planning surfaces.
- **Not autonomous backlog grooming.** HITL still adjudicates which items become tasks, which get rejected as noise, which combine into one work item.
- **Not real-time alerting / monitoring.** Captain SDLC is dev-time tooling. If you need on-call paging, that's a separate concern.
- **Not handling player-facing consent UX.** Consent surfaces live in the game's settings/notice UI; this seam consumes data the consent layer has already approved. The privacy responsibility for the consent surface lives in `privacy-policy-aspirational.md` § Consent surfaces.

## Channels

Each ingestion channel is its own small ingestor. Channels are independent — a project can wire up one, three, or none.

| Channel | Source examples | Downstream consumer |
|---|---|---|
| **Crash reports** | Steam crash reporting, BugSplat, Sentry, custom in-game crash logger | interrogate backlog (as bug task) |
| **Reviews / written feedback** | Steam reviews, itch feedback, Discord channels, forum bug reports | interrogate backlog (as feedback task) |
| **In-the-wild performance samples** | Game-embedded telemetry, profiler captures | ATH baseline (as regression-envelope data point) |
| **Player telemetry** | Game-embedded analytics (completion funnels, feature usage) | Trend dashboard (Seam 5 downstream; aspirational) |
| **Feature flag exposure data** | Staged-rollout signals | Trend dashboard; future analysis |

Each downstream consumer (interrogate's backlog, ATH's baseline, the dashboard) needs its own intake API before its corresponding channel can land — see "Prerequisites" below.

## Ingestor pattern

Every channel has a thin ingestor that does the same four things:

1. **Read** from the source (poll an API, consume a webhook, scrape an export).
2. **Classify and redact** the payload per the privacy framework (`privacy-framework.md`).
3. **Emit** a `liveops.<channel>.received` event to the pipeline trace, with sensitive payload (if any) in the side-store, referenced by ID.
4. **Route** the structural substance to the downstream consumer — interrogate, ATH, the dashboard.

The ingestor doesn't decide what to do with the item beyond routing. Triage, deduplication, and adjudication happen in the consumer.

### Trace events emitted

Per `trace-schema.md` § Event kinds (Live Ops):

- `liveops.crash.received`
- `liveops.feedback.received`
- `liveops.perf_sample.received`
- `liveops.telemetry.received`
- `liveops.task.created` — emitted by the *consumer* when an ingested item becomes a backlog task. Links back to the ingestion event via `triggers` relation.

Multiple ingestion events can converge on one task (the same crash from 50 players = 50 `liveops.crash.received` events linked to one `liveops.task.created`).

### Routing rules

| Ingested kind | Triage hint | Default consumer | When HITL is required |
|---|---|---|---|
| Crash with novel stack signature | "new bug" | interrogate backlog | Always for first occurrence; auto-dedup for repeats |
| Crash matching a known signature | "duplicate" | Increment counter on existing task | HITL only if frequency crosses threshold |
| Review with bug-like content (AI-classified) | "feedback / possible bug" | interrogate backlog | Always — review classification is fuzzy |
| Review with positive sentiment | "feedback / non-actionable" | Trend dashboard only | Never |
| Perf sample within baseline tolerance | "noise" | Aggregate into baseline stats | Never |
| Perf sample outside tolerance | "regression candidate" | ATH baseline alert | When pattern persists across N samples |
| Telemetry event | "stats" | Trend dashboard | Per-channel configuration |

These rules live per-channel in the ingestor's config (`.captain-sdlc/liveops-routing.yaml`); not in this doc as binding policy.

## Architecture

### Where ingestors live

**Each channel ingestor is a separate small tool**, not a unified Live Ops layer. Reasons:

- Different channels have wildly different read mechanics (Steam Web API ≠ Sentry webhook ≠ Discord bot ≠ game-embedded telemetry HTTP endpoint).
- Different channels have different update cadences (polling daily for reviews ≠ webhook for crashes ≠ continuous telemetry stream).
- Different channels touch different credentials and need different sandboxing.

A "Live Ops layer" tool that tried to do all of this would either over-generalize or bloat. Lean: tiny per-channel tools that share the trace + classification primitives via convention, not via shared code.

Naming convention: `liveops-<source>-ingest` (e.g., `liveops-steam-reviews-ingest`, `liveops-bugsplat-ingest`). Each is independently versionable.

### Where the routing config lives

`.captain-sdlc/liveops-routing.yaml` in the consuming project. Per-channel:

```yaml
schema_version: 1
channels:
  steam_crash:
    enabled: true
    poll_interval_minutes: 60
    consumer: interrogate
    dedup_strategy: stack_signature_hash
    classification_overrides: {}
  steam_reviews:
    enabled: false  # not authorized to read yet
  bugsplat:
    enabled: true
    webhook_url: ...  # secret reference, not literal
    consumer: interrogate
```

Per-project, per-channel toggles. Secrets reference OS credential store, never literal in the config.

### How consumers ingest from the trace

Downstream consumers (interrogate, ATH) watch for new `liveops.*.received` events tagged for them and apply their own logic. They don't talk to ingestors directly — the trace is the boundary.

This decouples ingestors from consumers. An ingestor changing its internal logic doesn't affect interrogate or ATH; they just see new event payloads.

## Privacy inheritance

This seam inherits its privacy story from `privacy-framework.md`:

- All four classification levels apply per-field. Each ingestor's payload schema (when written) annotates field-level classification.
- `personal` and `sensitive` payloads go to the side-store; the trace carries references.
- AI-assisted classification (e.g., "is this review describing a bug?") follows the AI processing carve-out — redacted forms only for `personal`-class inputs, never for `sensitive`-class inputs.

The aspirational policy (`privacy-policy-aspirational.md`) covers the full per-channel field catalog, retention defaults, and consent surfaces that activate when actual implementation starts.

**No ingestion channel ships in production without its field-level classification catalog being authored.** This is the load-bearing privacy gate, captured here for the seam and in the aspirational policy for the catalog itself.

## Prerequisites

| Prerequisite | Status | Blocks |
|---|---|---|
| Trace schema (Seam 1) — for emitting events | Planning doc in place; first emitter not yet built | All ingestion channels |
| Privacy framework primitives | In place (`privacy-framework.md`) | Crash, reviews, telemetry (all sensitive-adjacent) |
| Privacy policy specifics per channel | Aspirational; activates per ingestion design | Per-channel ingest implementation |
| interrogate backlog intake API | Doesn't exist yet | Crash + reviews routing |
| ATH baseline intake API | Doesn't exist yet | Perf sample routing |
| Trend dashboard | Aspirational; no design | Telemetry + feature flag routing |
| A shipped product surfacing real data | Doesn't exist | Everything in this seam |

The last row is the meta-prerequisite. Until a Captain SDLC project actually ships and real data flows, every other prerequisite can be planned but not validated.

## Minimal first cut

If we ship the smallest useful version of this seam:

- **One channel only.** Crash reports, because they're the highest signal-to-noise per ingestion event.
- **Manual trigger.** A developer runs `liveops-crash-ingest --since <date>` against their crash source; no scheduler / webhook yet.
- **No AI classification.** Every crash becomes a candidate task; HITL adjudicates.
- **No dedup beyond exact-signature matching** (hash of normalized stack trace).
- **Privacy framework applied** — classification per the framework's structural commitment; redaction patterns from the aspirational doc's redaction conventions section (Windows-username scrubbing, etc.).
- **Routes to interrogate** as a "needs triage" backlog entry. Interrogate's backlog intake is itself minimal — a simple file the developer reviews.

This minimum proves the ingest → trace → consumer chain with the privacy primitives applied. Every other channel follows the same shape.

Even this minimum requires:
- The trace's first emitter being functional (otherwise ingestion events go nowhere).
- A live crash source (which requires a shipped product).

So even minimal-first-cut is gated on shipping. The planning value of this doc is making sure the shape is ready when the trigger fires.

## Small-team aspiration

When more than one developer is involved (aspirational — Captain SDLC's stated stretch goal):

- **Backlog adjudication becomes shared work.** Interrogate's backlog API needs assignment / claim semantics so two developers don't triage the same item simultaneously.
- **Trace becomes a shared resource.** Cross-machine trace merging (currently deferred per `trace-schema.md`) becomes load-bearing.
- **Ingestor authentication and audit.** Which developer's credential ingested which item? Audit log lives alongside the trace event. Touches the audit logging deferral in `privacy-policy-aspirational.md`.
- **Triage policy alignment.** Per-channel routing rules need team consensus, not just per-developer config.

None of these are designed today. They're flagged so the small-team trigger doesn't surprise the seam.

## Open scoping questions

1. **One-tool-per-channel vs unified Live Ops layer.** Current lean is per-channel tiny tools. Worth confirming when more than one channel is being designed; a small shared library may be useful but a unified tool is overkill.
2. **Dedup discipline across channels.** A crash, a review describing the crash, and a perf-sample anomaly may all be the same underlying bug. The dedup story for *cross-channel* convergence is hard and probably out of scope for the first cut — but worth flagging.
3. **AI classification for free-text reviews.** Inherent privacy + accuracy tradeoff. Lean: use AI for sentiment labeling and rough categorization (low-stakes outputs); HITL for any item that becomes a backlog task. Aligns with the AI processing carve-out.
4. **Backlog state machine.** Once a `liveops.task.created` event exists, what states does the task move through (open → triaged → in-progress → fixed → closed)? Probably interrogate's concern, not this seam's, but worth referencing.
5. **Rate limiting / flooding defense.** If a release ships with a high-frequency crash, the ingestor might fire thousands of events. Need a back-pressure story before this hits production.
6. **Ingestion failure surfacing.** If an ingestor's source is unreachable, how is that surfaced? Probably a `liveops.ingest.failed` trace event with a retry policy.

## Definition of done

Seam 5 is shipped when:

- At least one ingestion channel works end-to-end: source → ingestor → trace event → downstream consumer.
- The channel's payload schema has its field-level classification catalog authored and reviewed.
- Privacy framework primitives apply correctly (sensitive payloads in side-store, not trace).
- `liveops.<channel>.received` and `liveops.task.created` events flow as designed.
- HITL has visibility into ingested items via the downstream consumer (interrogate's backlog file, ATH's baseline alerts, whatever).
- A second channel can be added without redesigning the first.

Definition of done covers the *seam's contract*. The full channel catalog (Steam crashes, BugSplat, Steam reviews, Discord, telemetry, feature flags, etc.) accretes as each is built.

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
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- **2026-05-28** — Per-channel small ingestor tools, not a unified Live Ops layer. Shared primitives via trace + privacy framework, not shared code.
- **2026-05-28** — Trace is the boundary between ingestors and consumers. Consumers watch trace events; they don't talk to ingestors directly.
- **2026-05-28** — Privacy framework primitives apply at ingestion time. No channel ships in production without a field-level classification catalog.
- **2026-05-28** — Live Ops ingestion is doubly aspirational: blocked on a shipped product and on downstream consumer intake APIs (interrogate backlog, ATH baseline intake). Doc captures structural intent; implementation triggers on a shipped product.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for Seam 5. Doubly aspirational; documents structural intent in advance of a shipped product.
