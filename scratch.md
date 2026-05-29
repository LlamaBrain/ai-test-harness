# Scratch

Register of work in flight. Triage on each session: remove completed entries, defer non-active items to `captain-sdlc/Roadmap/` or `captain-sdlc/candidates.md`, keep and trim active ones. New session prepends a dated section above prior ones.

## 2026-05-28 — handoff from long Captain SDLC session

### Active — interrogate

- **Re-run `/claude-interrogate:roadmap` on `captain-sdlc/`** (maintenance mode) with `kind: "release-candidate"` on `M27_DEFINITION_OF_DONE_END_TO_END`. Renames the placeholder to `MRC1_DEFINITION_OF_DONE_END_TO_END` in a `.draft.md` sibling; review, replace original. Requires `/plugin update claude-interrogate` first to pull v0.1.7. ~5 min.

### Active — claude-release

- **v0.4.0 — version-surface drift check.** Mechanically diff every JSON/YAML/text file in the repo for version-shaped strings; fail if they disagree. Would have caught the marketplace.json oversight from v0.3.1. Memory `project_publish_discipline_pattern.md` is the design brief — convert it into a check script. Modest scope (~30 min once the discipline is committed in code).
- **Hardcoded version path in `commands/release.md`.** Refers to `cache/llamabrain-release/claude-release/0.2.0/lib/build-manifest.js` literally. Should be relative or env-resolved so command markdown survives version bumps. Caught in the v0.1.0 release flow when the cached 0.2.0 command was being read despite v0.3.2 being live.

### Active — interrogate (smaller)

- **Migration tool for old roadmaps** (`/migrate-roadmap` flow). Pre-existing roadmaps using SemVer-shaped IDs (`0_8_0_QUESTS`) or M-only prefix won't round-trip cleanly against v0.1.6+'s milestone format or v0.1.7+'s MRC notation. No external users yet so non-urgent. Flagged in v0.1.6 CHANGELOG.
- **Preset system** — `roadmap.preset: "indie-game"` opt-in that restores Wishlist/EA/Launch waypoints + game-dev reserved slots. Flagged in ADR-0007. Optional.

### Active — Captain SDLC project work

- **M2 — TRACE_SCHEMA_FIRST_EMITTER.** First real Captain SDLC milestone. Scope: ATH emits one `ath.smoke.completed` event to `.captain-sdlc/trace/YYYY-MM-DD.jsonl` during a smoke run, matching `trace-schema.md`'s envelope. Smallest viable instance of the whole pipeline shape. Worth a fresh session — full context window matters for the first emitter design.
- **M5 — RELEASE_GATES_MINIMAL.** The MIN PLAY waypoint. Depends on M2. Two gates: `smoke_results_pass` + `dependency_audit`. First end-to-end demonstration of idea→plan→verify→release loop. Per `seam-release-gates.md`.

### Maintenance / cleanup

- **`doc:` → `docs:` for future commits.** Two ATH commits in the v0.1.0 range used non-canonical `doc:`. Not blocking, but the canonical Conventional Commits type going forward is `docs:` (plural).
- **Verify the `scratch.md` + scratch SKILL files** that landed in interrogate v0.1.7 via `git add -A` are intentional. They were untracked when the session started; if they were experimental and shouldn't have shipped, that's a v0.1.8 fix (the v0.1.7 tag is now immutable).
- **interrogate v0.1.7 partial work that committed cleanly** — the MRC notation refactor landed end-to-end (data model, ID generation, filename rendering, parser, table output, RC stub headers, one focused test). 85/85 tests pass against the new code paths.

### Done this session (drops out next triage)

- ✓ ATH v0.1.0 (graduated from preview series; tagged + pushed)
- ✓ Captain SDLC docs committed (17 docs + 9 ADRs + roadmap.md + 27 RC stubs)
- ✓ claude-release v0.3.1 (bundle fix) + v0.3.2 (marketplace.json sync)
- ✓ claude-interrogate v0.1.4 / v0.1.5 / v0.1.6 / v0.1.7
- ✓ ADR-0001 through ADR-0009 filed in `captain-sdlc/ADR/`
- ✓ Memory entries for goal/mission/principle/pattern/outcome/packaging/origin-story/role-assignments/publish-discipline/ADR-trigger
- ✓ `v0.1.0-preview.2` backfilled as a tag on ATH commit `b420db8`
- ✓ `.claude/` added to `.gitignore`
