# Captain SDLC — Privacy Policy (Aspirational)
Updated: 2026-04-08
Version: 0.1.3

Created: 2026-05-28

**Scope:** the full privacy policy that would govern Captain SDLC when Live Ops ingestion is wired up against a shipped commercial product, possibly with multiple developers and players in regulated jurisdictions. None of this is implemented. All of it is structural sketching against hypothetical futures.

Companion to `privacy-framework.md`, which covers what's concrete *today* and the structural primitives. This doc extends those primitives into the full picture they imply.

**Status:** Aspirational. Each section names the trigger that would make it concrete. Treat the specifics as proposals open to argument; treat the primitives (classification, segregation) as inherited from the framework.

## When this matters

This policy becomes load-bearing when *any* of:

- A Captain SDLC project ships a product to real players (any platform).
- Live Ops ingestion is implemented to pull crash reports, reviews, or telemetry into the pipeline.
- A second developer gains commit access to a Captain SDLC project (multi-developer privacy concerns activate).
- A Captain SDLC project has any EU or CA player (jurisdictional regimes activate).
- AI processing routes start handling player-originated data.

None of these are true today. The aspirational doc exists so that when one becomes true, the design isn't starting from a blank page.

## Threat model

The actors the policy is protecting against:

| Actor | Concern | Mitigation primitive |
|---|---|---|
| **Regulators (GDPR / CCPA / state privacy laws)** | Insufficient consent, over-collection, missing data-subject rights | Classification + retention + DSR mechanisms |
| **Accidental leakers** (screenshots, PRs containing trace files, AI prompt logs, public dashboards) | Sensitive data ending up where it shouldn't | Trace-vs-side-store segregation + grep-safety of trace |
| **Downstream breaches** (Steam, itch, distribution platform compromise) | Aggregate data exposure | Minimize what's stored; encrypt side-store at rest |
| **AI processors** (Claude API, future LLM vendors) | Sensitive data crossing into third-party infra | AI processing carve-out; redaction before AI consumption |
| **Curious team members** (in small-team future) | Insider exposure to sensitive payloads | Side-store access controls; least-privilege defaults |
| **Players themselves** | Their data being mishandled | Consent UX; data-subject rights (delete, export); transparency |

The threat model does *not* attempt to defend against nation-state adversaries, targeted breaches with legal authority, or sophisticated re-identification attacks against well-aggregated data. Those are out of scope for a solo-to-small-team game-dev pipeline.

## Data classification — full field-level catalog (sketch)

Classification levels inherited from `privacy-framework.md`. Below is the sketch of what fields fall where, per Live Ops channel. Treat as illustrative; the real catalog gets written when each ingestion channel is designed.

### Crash reports

| Field | Classification | Redaction |
|---|---|---|
| Exception type | public | None |
| Stack trace (function names, types) | public | None |
| Stack trace (file paths) | personal | Replace user-path prefix with `<user>` |
| Hardware fingerprint (CPU, GPU, RAM) | pseudonymous | Hash if cross-correlation across reports is a concern |
| OS version, locale | pseudonymous | None |
| Game version, build hash | public | None |
| Scene name at crash | public | None |
| Recent log messages | personal | Scrub user paths; scrub any explicit identifiers |
| Memory dump | sensitive | Never collected unless explicit per-incident consent |
| Steam ID / user ID | personal | Hashed at ingest; original stays at Steam |

### Reviews / forum posts / Discord ingestion

| Field | Classification | Redaction |
|---|---|---|
| Platform identifier (Steam / Discord ID) | personal | Hashed at ingest |
| Username / display name | personal | Stored only if consent explicit; otherwise dropped |
| Post body | sensitive | Stored as-is in side-store (player's own words); never sent to AI without consent |
| Timestamp | pseudonymous | Coarsened to day-level if cross-correlation is a concern |
| Sentiment / category labels (AI-generated) | public | Derived; safe |
| Linked accounts (player linking Steam to Discord) | personal | Not cross-referenced without explicit consent |

### In-the-wild performance samples

| Field | Classification | Redaction |
|---|---|---|
| Frame time histogram | public | None |
| GC stats | public | None |
| Draw call counts | public | None |
| Hardware fingerprint | pseudonymous | Hashed if a player profile could be assembled across samples |
| Session ID | pseudonymous | Per-session, not per-player |
| Geographic region (country level) | pseudonymous | OK at country granularity; finer granularity escalates to `personal` |

### Player telemetry / completion funnels

| Field | Classification | Redaction |
|---|---|---|
| Level reached, items collected, deaths | public | Aggregated; per-player drops to `pseudonymous` |
| Session length, time-of-day | pseudonymous | None |
| Save file checksum | pseudonymous | None |
| Save file contents | sensitive | Side-store only; never exported aggregate |
| Player choices / dialogue selections | personal | Aggregate-only by default; per-player escalates to `sensitive` |
| Purchase history | sensitive | Side-store; encryption-at-rest |

These are sketches. Real schemas get built per channel when ingestion is implemented.

## Redaction conventions

Mechanical patterns that apply at ingestion time:

1. **Windows-username scrubbing.** Replace `C:\Users\([^\\]+)\` with `C:\Users\<user>\` in any string field. Same pattern for macOS `/Users/foo/` and Linux `/home/foo/`. Catches stack traces, log messages, and configuration paths.
2. **Hashing identifiers.** Steam IDs, Discord IDs, and other platform identifiers get SHA-256 hashed with a project-specific salt before storage. The salt itself lives in the side-store under stricter access.
3. **IP truncation.** If an IP is collected at all, truncate to /24 (IPv4) or /48 (IPv6) at ingestion. Full IPs never enter the trace.
4. **Timestamp coarsening.** For fields that don't need precision, coarsen to day-level before storage. Per-second timestamps create cross-correlation risk.
5. **Free-text scrubbing.** Player-authored content (reviews, forum posts) is stored as-is in the side-store but is *never* mechanically scrubbed (you'd destroy the meaning). Instead, AI processing of free-text is gated behind explicit consent.

## Storage policy

### Pipeline trace

Per `trace-schema.md` and `privacy-framework.md`:

- Carries `public` and `pseudonymous` payloads directly.
- Carries references (opaque IDs) to side-store entries for `personal` and `sensitive` payloads.
- Stays grep-safe, diff-safe, and shareable for the architectural use cases the trace was designed for.
- Default storage: `.gitignored` per machine. Cross-machine merging (when CICD lands) requires its own policy — see open questions.

### Side-store

- Location: `.captain-sdlc/side-store/` per project.
- Always `.gitignored`. Never committed.
- Encryption at rest: required for `sensitive` class. Optional but recommended for `personal`. Encryption key lives in OS credential store, not in the repo.
- Access: developer's account only by default. Multi-developer access requires explicit access grants (form TBD when triggered).
- Retention defaults per class:
  - `personal`: 90 days from last reference event.
  - `sensitive`: 30 days from last reference event, or per-incident retention defined explicitly per channel.
- Garbage collection: a scheduled sweep that drops side-store entries whose retention window has passed and whose referencing trace events have no live consumer.

### Backups

- Trace can be archived normally (sync to bucket, periodic squash).
- Side-store backups must follow the same access and encryption discipline as the live store.
- A backup is a copy. Retention applies to backups too.

## AI processing policy at scale

Inherited from `privacy-framework.md`'s structural commitment: `sensitive` data never flows to Claude API directly. The aspirational expansion:

### Allowed flows

- `public` data: unrestricted.
- `pseudonymous` data: allowed, with awareness. Periodic review to ensure no cross-correlation enables re-identification.
- `personal` data: allowed only after redaction to `pseudonymous` form. Original stays in side-store; the redacted form is what Claude sees.
- `sensitive` data: never directly. If AI processing is needed (e.g., summarizing many free-text reviews), the AI gets only aggregate or synthesized outputs derived in a privacy-preserving way, never the raw payloads.

### Specific AI integration patterns

| Pattern | Allowed? | Why |
|---|---|---|
| interrogate consuming design docs the developer wrote | Yes | Developer's own data; explicit invocation |
| ATH smoke transcripts hitting AI triage | Yes if scrubbed | Apply Windows-username and identifier scrubbing first |
| Crash reports through AI summarization | Yes if redacted | Stack traces are `public`-or-`personal`-with-scrubbing; raw stack to AI is fine after path scrubbing |
| Player free-text (reviews, forum posts) through AI sentiment classification | Yes with consent | Reviews are public on Steam already; sentiment derivation is fine. Long-form analysis requires explicit player consent if the platform doesn't already cover it |
| Save game contents through AI ("what builds are players choosing?") | No | Aggregate stats yes; raw save contents no, period |
| Trace events containing side-store references through AI | Yes for references; no for dereferenced sensitive payloads | The reference is `pseudonymous`; the dereferenced sensitive payload is not for AI |

### Vendor changes

Claude API's data retention and processing guarantees are a moving target. The aspirational policy commits to:

- Tracking the current Anthropic guarantee in this doc as a versioned reference.
- Revisiting the AI processing allowed-flows table when the guarantee changes materially.
- Disclosing the vendor relationship in user-facing privacy notices (when a user-facing product exists).

## Consent and data-subject rights

When a Captain SDLC project ships commercially:

### Consent surfaces

- **Implicit (via platform).** Steam, itch.io, and similar platforms handle baseline player-identity consent. If the pipeline only ingests what the platform already provides under its consent framework, no additional consent surface is needed for that channel.
- **Explicit (per channel).** For ingestion channels the platform doesn't cover (telemetry SDK, in-game diagnostics opt-in, AI-driven review analysis), an in-game consent surface is required. Defaults to opt-out; opt-in for sensitive channels.

### Data-subject rights mechanisms

| Right | Implementation sketch |
|---|---|
| Right to access (export) | A `captain-export <player-id>` command that walks the trace + side-store for events tagged with the (hashed) player ID and produces a portable bundle |
| Right to deletion | A `captain-delete <player-id>` command that drops side-store entries for the player and replaces trace events with redacted stubs (preserving the chain structure but removing the payload) |
| Right to rectification | Manual; supported via standard ticket flow |
| Right to portability | Subset of right-to-access |
| Right to object | Per-channel opt-out toggles |

Implementation is straightforward because the side-store keys are designed for it from day one.

### Transparency

A public privacy notice describing:
- What channels collect data.
- What fields are collected per channel.
- Retention per class.
- AI processing disclosure.
- Vendor / sub-processor list.

Lives at a URL referenced from the in-game settings.

## Cross-border data transfer

When EU or CA players exist:

- **Data location.** Default to processor regions that align with the developer's primary jurisdiction. Side-store hosted in the same region as the developer's main residence unless a specific reason exists otherwise.
- **Sub-processor disclosure.** Claude API, distribution platform analytics, and any third-party services are disclosed in the privacy notice.
- **Adequacy.** Rely on standard contractual clauses or platform adequacy frameworks rather than rolling custom transfer agreements.

Beyond this sketch, jurisdictional specifics get added when actual players in actual jurisdictions exist. Don't predict; respond.

## Multi-developer and small-team specifics

When a second developer gains commit access:

- **Trace access.** The trace remains per-machine and `.gitignored` by default. Sharing a trace across developers requires explicit copy + privacy review.
- **Side-store access.** The side-store is per-machine. A developer who needs access to another developer's side-store requires explicit access grant + audit log entry.
- **Code review of design docs.** Design docs get reviewed before merge. Reviewers may see the constitution section, which can encode `sensitive` business rules; access to design docs follows the same model as access to other source.
- **AI prompt review.** If team members are sending pipeline data through Claude via Captain SDLC tools, the AI processing policy applies the same way it does in solo mode.
- **Pipeline trace merging across machines.** If teams want a unified trace (e.g., for cross-developer pipeline walks), merging requires explicit consent and redaction conventions. Out of scope for the initial multi-developer trigger; revisited when a specific use case surfaces.

## Audit logging

When the side-store carries sensitive payloads:

- Every read of a `sensitive`-class side-store entry is logged.
- Audit log includes: reader identity, entry ID, timestamp, reason (optional but encouraged).
- Audit log itself is a side-store entry (`pseudonymous` class) — meta-trail for the trail.

For the solo case, this is one developer reading their own side-store, which is fine to log silently if at all. The audit log matters when there's more than one reader.

## Data breach response

When the policy is load-bearing, a breach response plan covers:

1. **Detection.** What signal indicates a breach? Compromised credential, unauthorized side-store access, unintended trace export.
2. **Containment.** Revoke compromised credentials; rotate encryption keys; cut affected accounts off from the side-store.
3. **Assessment.** What data classes were exposed? What players are affected?
4. **Notification.** Per regulatory regime (GDPR: 72-hour notification window). Platform-specific channels (Steam developer notification, etc).
5. **Postmortem.** Captured in the design system as a constitution invariant or new structural commitment if the breach was systemic.

A full plan gets written when the first sensitive-class payload schema is being designed. Not before.

## Open scoping questions

1. **Encryption key management for the side-store.** OS credential store is the lean, but it doesn't survive machine migrations cleanly. Worth investigating before sensitive payloads land.
2. **The free-text consent UX.** Specifically for AI processing of player-authored content (reviews, forum posts). What's the minimum-friction surface that actually informs the player? Worth borrowing from Discord / Steam's existing UX rather than inventing.
3. **Backward-compatibility for retention.** If retention defaults change (e.g., regulator tightens), what happens to data already past the new limit? Bulk purge vs grace period vs grandfather. Decision affects how aggressively defaults can evolve.
4. **AI vendor diversification.** If Captain SDLC ever supports multiple AI vendors (not just Anthropic), the AI processing policy fragments per vendor. The classification primitives remain shared, but the allowed-flows table multiplies.
5. **Synthetic / aggregate AI outputs and re-identification risk.** "AI gets only aggregates" is the safe-sounding policy, but well-known aggregates have been re-identified. Worth a sanity threshold (e.g., k-anonymity ≥ 5) when aggregate AI processing actually starts.

## Triggers (recap)

This doc starts being read seriously when:

- First commercial release with any real players.
- First Live Ops ingestion channel being designed for implementation.
- First EU/CA player.
- First second developer with commit access.
- First sensitive-class payload schema being written.

Until then it's a sketch. Once any trigger fires, the corresponding section gets promoted from aspirational to actual policy, reviewed against the actual constraints, and pinned with a real version.

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
- [Captain SDLC — Seam 2: Design ↔ Code Drift](./seam-design-code-drift.md)
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Seam 4: Cross-Tool Contract Testing](./seam-contract-testing.md)
- [Captain SDLC — Seam 5: Live Ops Ingestion](./seam-live-ops-ingestion.md)
- [Captain SDLC — Seam 6: Constitution Enforcement](./seam-constitution-enforcement.md)
- [Captain SDLC — Vision](./vision.md)

## Resolved Decisions

- **2026-05-28** — Threat model excludes nation-state adversaries and targeted re-identification attacks; in scope for solo-to-small-team commercial game dev only.
- **2026-05-28** — Steam / itch / Discord ingestion relies on platform consent for baseline; per-channel explicit consent required when the platform doesn't cover.
- **2026-05-28** — Side-store is always `.gitignored`. Cross-machine sharing requires explicit grants and audit.

## Open Questions

- None.

## Version History

- 0.1.3 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.2 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.1 (2026-04-08): Metadata, linkage, or narrow doc maintenance update.
- 0.1.0 (2026-05-28): Initial aspirational policy. Companion to privacy framework.
