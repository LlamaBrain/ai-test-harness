# ADR-0007: Generic defaults for interrogate

**Date:** 2026-05-28

## Problem

interrogate originally shipped with dirigible-style default roadmap configuration biased toward indie-game development: `marketingWaypoints: ["Wishlist", "Early Access", "Launch"]` (Steam-specific), `reservedSlots: [{0.95.0:"Showcase content"}, {0.98.0:"Stretch"}, {0.99.0:"Late-stage polish"}, {1.0.0:"Release readiness"}]` (game-content milestones), and `{major}_{minor}_{patch}_{NAME}.md` naming (SemVer-as-milestone). These imposed an indie-game mental model on every project. Even a game developer designing tooling, libraries, or infra had to fight defaults that didn't fit. User: "Interrogate is a design tool, not specifically for games."

## Solution

Defaults are deliberately generic. Domain-specific conventions live in per-project `claude-interrogate.json`, never as imposed defaults:
- `marketingWaypoints: []` — empty (v0.1.5).
- `reservedSlots: []` — empty (was `[{1.0.0: "First stable release"}]` in v0.1.5; reduced further when MRC notation lands).
- `rcNamingScheme: "{prefix}{milestone}_{NAME}.md"` — milestone-prefixed (v0.1.6).

Drove three releases — v0.1.5 (waypoints + reserved-slots fix), v0.1.6 (milestone-vs-version refactor), v0.1.7 (MRC notation, in-progress) — each correcting a distinct framing bias.

## Alternatives

- **Keep dirigible-style defaults** — Imposed bias on every project. Rejected after user pushback.
- **Opt-in preset system (`preset: "indie-game"`)** — Considered for v0.1.7+; valid but adds a config surface. Deferred; not blocking the immediate fix.
