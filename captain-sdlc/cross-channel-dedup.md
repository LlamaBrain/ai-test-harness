# Captain SDLC — Cross-Channel Deduplication
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

Planning doc for cross-channel dedup — the problem of recognizing when a crash report, a Steam review describing that crash, and a perf-sample anomaly are all signals of the same underlying issue. Belongs to Seam 5 (Live Ops Ingestion) but is concerning enough to deserve its own doc.

**Status:** Planning. Aspirational in the same way Live Ops is aspirational (no real data flowing yet); the structural commitments here shape how Live Ops eventually behaves.

## The problem

A single underlying bug can surface as:

- A **crash report** with a particular stack signature (auto-collected, structured).
- One or more **player reviews** describing the symptom in prose ("game freezes when I open the inventory in the throne room").
- A **perf-sample anomaly** showing frame time spikes around the same interaction (continuous telemetry).
- A **forum bug report** referencing the same symptom.
- A **Discord support ticket** from a player asking for help.

Each ingestion channel sees its own slice. Without dedup, the backlog gains five entries for one bug. With naive intra-channel dedup, the backlog gains five clusters — one per channel — that the developer still has to manually merge.

Cross-channel dedup recognizes that *different signals about the same underlying issue should converge into one trackable thing* — without forcing each channel's ingestor to know about every other channel.

## Goal

When multiple ingestion events plausibly describe the same underlying issue, recognize the convergence, route all the signals to one backlog task, and preserve the chain so the developer can later inspect every contributing event.

The chain matters: when a player asks "did you fix this?" the answer should be traceable back through every ingestion event that fed the task.

## Non-goals

- **Not perfect dedup.** Cross-channel signal correlation is fuzzy. Some convergences will be missed; some non-convergences will be over-merged. The system should support easy un-merging.
- **Not autonomous merging at high confidence.** Even "obvious" dedup happens with audit trail and a reversible action — never silent.
- **Not a clustering algorithm research project.** Pragmatic dedup, not novel ML.
- **Not cross-project dedup.** Each project's backlog is its own. A crash in Project A doesn't dedup against a crash in Project B even if the stacks look similar.

## Detection signals

Three classes of signal, ordered by reliability:

### Class A: Mechanical / structural

These can dedup with high confidence and minimal HITL:

- **Identical stack signature hash.** Two `liveops.crash.received` events whose normalized stack hashes match are the same crash. Definition of "normalized": strip file paths, line numbers, and dynamic addresses; keep function names and exception type. Standard practice.
- **Identical error code or exception type within a short window.** Weaker than stack signature but still strong for some kinds of error.
- **Same client-side build hash + same scene + same error message.** Composite signature for cases without stack traces.

### Class B: Heuristic / contextual

These suggest dedup but warrant HITL confirmation before merging:

- **Temporal proximity.** Multiple distinct signals within N minutes of each other, especially if they all reference the same scene or feature. Window size is per-project (a release-day flood vs steady-state day-to-day differs).
- **Build-hash correlation.** All signals are against the same build version, in the same time window, against the same hardware class. Pattern-suggestive.
- **Player-id correlation.** Same hashed player ID generates multiple signals (a crash and a forum post). Strong suggestion they're describing the same incident.
- **Telemetry-to-crash correlation.** Perf anomaly at time T, crash at time T + small delta, same player. Suggests cascade.

### Class C: Semantic / AI-assisted

Useful but fuzziest; needs the privacy carve-out applied:

- **Free-text similarity.** Review text says "game freezes when opening inventory in the throne room"; an existing task is titled "Inventory crash, throne room scene." AI embedding similarity ranks them as related.
- **Sentiment + topic clustering.** A burst of negative reviews after a release with similar topic vectors suggests a regression.
- **Cross-language symptom matching.** Reviews in different languages describing the same symptom.

Per the AI processing carve-out (`privacy-framework.md`): semantic matching of `personal`-class free-text uses redacted forms; `sensitive`-class content (private support tickets) doesn't get AI-similarity-checked unless the player consented.

## Convergence — what the structure looks like

A backlog task is the convergence point. The task has:

- An **identity** (a stable task ID like `bug-2026-05-001`).
- A **first-seen** and **last-seen** timestamp.
- An **occurrence count** (incremented per dedup-matched ingestion event).
- A **chain of contributing events** via the trace's `triggers` relation.
- A **confidence record** showing how each contributing event was matched (signature hash, AI similarity, manual link, etc.).

Trace shape:

```
liveops.crash.received (A)  ─┐
liveops.crash.received (B)  ─┼─→  liveops.task.created (task T)
liveops.feedback.received(C)─┤    └─ links: [triggers: A, triggers: B, triggers: C]
liveops.perf_sample.received(D)┘
```

Each ingestion event keeps its own identity; the task aggregates. Nothing is destroyed by dedup — the convergence is additive metadata.

Subsequent ingestion events that match an existing task emit a `liveops.task.occurrence` event linking to the task. Maintains append-only discipline (no rewriting of past events) while keeping occurrence counts current.

## Confidence and HITL

Each detected convergence has a confidence score and an action:

| Confidence | Source signals | Default action |
|---|---|---|
| **High** | Class A (mechanical match) | Auto-link to existing task; increment occurrence; log to trace |
| **Medium** | Class B (heuristic) or AI similarity above threshold | Propose convergence for HITL review; surface in triage queue |
| **Low** | Class C below threshold, or single weak signal | No automatic action; available as a "potentially related" link in the task view |

Auto-link on high confidence still emits a `liveops.task.occurrence` event so the action is auditable. A misfire is reversible by unlinking — see Re-dedup below.

HITL review of medium-confidence proposals happens in the consumer (interrogate's backlog UI). Each proposal carries:
- The candidate task it would converge with.
- The new ingestion event(s) that triggered the proposal.
- The signals that led to the proposal (signature distance, temporal proximity, AI similarity score).
- Options: confirm convergence, reject (create a new task), defer (keep as a related-but-distinct link).

## Re-dedup and unmerge

Convergence is a guess and guesses can be wrong. The system supports:

### Splitting

A merged task discovered to actually be two separate bugs:
- New task is created from the misattributed events.
- The split is recorded as a `liveops.task.split` event with `parents: [original_task_id]` and an explicit reason.
- The original task retains the events that genuinely belong; the split task takes the rest.

### Merging discovered later

Two independently-triaged tasks discovered to be the same bug:
- Both tasks remain in the trace history.
- A `liveops.task.merge` event records the merge, with the surviving task as `parent` and the absorbed task as a `links[merged_into]` reference.
- All future ingestion events match against the surviving task.

### Reopening on recurrence

A closed task receiving a new ingestion event:
- Default behavior: emit `liveops.task.recurrence` event linked to the closed task; surface for HITL.
- HITL options: reopen the task, create a new task with a `recurrence_of` link to the closed one, or dismiss as unrelated.

Audit trail is preserved end-to-end. No event is rewritten. All actions are additive.

## Architecture

### Where dedup logic lives

**In the consumer (interrogate's backlog), not in the ingestors.** Each ingestor emits its own classified event; the consumer is the dedup engine.

Reasons:
- Ingestors are per-channel and channel-specific. Dedup is cross-channel by definition.
- The consumer already aggregates events into tasks; it's the natural integration point.
- Keeps ingestor code small and focused.

### Dedup config

Per-project config (`.captain-sdlc/dedup-config.yaml`):

```yaml
schema_version: 1
class_a:
  stack_signature:
    enabled: true
    normalization: standard  # or custom regex set
  exception_type_window_minutes: 10
class_b:
  temporal_proximity_minutes: 30
  build_hash_correlation: true
  player_id_correlation: true
class_c:
  ai_similarity:
    enabled: true
    embedding_model: "claude-haiku-4-5"
    similarity_threshold: 0.85
  sentiment_clustering: false  # not yet useful for solo
auto_link_above_confidence: high
hitl_review_above_confidence: medium
```

Defaults are conservative — auto-link on Class A only.

### Embedding storage

For Class C semantic matching:

- Embeddings of free-text payloads live in the side-store (since the source text is often `personal` or `sensitive`).
- The embedding itself is `pseudonymous` (a vector that doesn't trivially decode back to the original text), so the embedding can live in the trace-adjacent space with looser controls than the source.
- Embeddings have their own retention (typically tied to the source payload's retention).

This means semantic dedup requires the side-store + embedding compute. The cost is real but bounded; defer until at least one channel needs it.

## Minimal first cut

If we ship the smallest useful version of cross-channel dedup:

- **Class A only.** Stack-signature hash matching for crashes. Nothing else.
- **Hardcoded normalization.** Standard "strip path / line / address" only; no per-project customization yet.
- **Auto-link on exact signature match.** No HITL prompt; events with matching hashes auto-aggregate to the existing task.
- **No re-dedup operations.** Split / merge / reopen handled manually if needed.
- **No AI similarity.** Class C deferred entirely.
- **No cross-channel.** Despite the name, the minimal cut only deduplicates within the crash channel — because that's the channel that ships first and the one where dedup has the highest payoff.

This is honest: most of the value of cross-channel dedup is in real multi-channel ingestion, which doesn't exist today. The minimal cut establishes the convergence pattern (task aggregates ingestion events) so the broader dedup story has a target.

## Open scoping questions

1. **AI embedding model choice.** Claude Haiku for cost; alternatives. Possibly a smaller dedicated embedding model. Decide closer to Class C work.
2. **Embedding retention.** Tie to source payload retention or live longer? Privacy-relevant question.
3. **Cross-build dedup.** When a release ships and the same crash recurs against the new build, is it the "same crash" or a "regression of the fix"? Lean: same task, with a `recurrence_of_fixed` flag if the task was closed in a prior version.
4. **Player frequency thresholds.** "If N distinct players hit the same crash, escalate severity" — useful, but the implementation depends on hashed-player-id discipline being in place. Defer to when player ID hashing is real.
5. **Misfire cost.** Auto-merging is reversible but not free; each misfire confuses the trace history. Worth a sanity threshold on "how often is auto-merge wrong" before promoting medium-confidence to auto-link.
6. **Triage workload modeling.** If a flood-day produces 10,000 ingestion events, the dedup system reduces backlog noise but might still overwhelm HITL review of medium-confidence proposals. Rate-limit the proposal queue per developer per day.

## Definition of done

Cross-channel dedup is shipped when:

- Stack-signature dedup auto-links matching events to existing tasks within the crash channel.
- The trace records the convergence via `liveops.task.occurrence` events.
- Split, merge, and reopen operations exist as explicit commands with auditable trace events.
- Dedup confidence is recorded per match, not collapsed into a single label.
- At least one cross-channel signal (probably player-ID correlation, since it's mechanical) is in place beyond the crash-only case.

Definition of done excludes AI semantic similarity, cross-build dedup, and frequency-threshold escalation. Those are accretions on a working baseline.

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Code-Reading Capability](./code-reading-capability.md)
- [Captain SDLC — Conventions](./captain-sdlc-conventions.md)
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

- **2026-05-28** — Dedup logic lives in the consumer (interrogate's backlog), not in per-channel ingestors. Cross-channel dedup is cross-channel by definition; ingestors stay per-channel.
- **2026-05-28** — Auto-link only on high-confidence (Class A) signals by default. Medium-confidence proposes for HITL; low is informational only.
- **2026-05-28** — Convergence is additive, not destructive. No ingestion event is rewritten; tasks aggregate via `triggers` / `occurrence` events.
- **2026-05-28** — Split / merge / reopen all emit explicit trace events with audit trail. No silent restructuring.
- **2026-05-28** — Embeddings for semantic matching live in side-store-adjacent space; original payloads stay in side-store under the privacy framework.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial planning doc for cross-channel dedup. Sub-concern of Seam 5.
