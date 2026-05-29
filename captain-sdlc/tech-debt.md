# Captain SDLC — Technical Debt
Updated: 2026-05-29
Created: 2026-05-29

Durable ledger of known structural debt — issues we've consciously deferred,
with enough context to act later. Not transient (that's `scratch.md`); not a
feature backlog (that's `candidates.md` / the roadmap); not an open design
question (that's `open-questions.md`). Debt here is "the current structure is
wrong-ish and we know it; here's the cost and the fix when we get to it."

Each entry: ID, status, the problem, why it's debt, fix options, blast radius,
and links. Mark `Resolved` (with the commit/ADR) rather than deleting, so the
history of why-we-changed-it survives.

---

## TD-001 — `captain-sdlc/` ships inside the ATH Unity package
**Status:** Open · **Discovered:** 2026-05-29 (M2 emitter dogfood — first Unity import of the package after the nerve-center docs landed)

**Problem.** The `captain-sdlc/` nerve-center tree (ADRs, roadmap, seam docs,
`tools/captain-trace.mjs`) lives at the root of the ATH UPM package and is *not*
tilde-hidden, so Unity imports it like any asset folder. On first import into
BeforeTheShade, Unity stamped ~65 `.meta` files across the tree; per the repo's
meta-tracking policy (`.gitignore` line 41) those are now committed
(`0f7c98f`). Because it's regular package content, the whole tree also ships in
the published package tarball — any project installing
`com.llamabrainlabs.ai-test-harness` gets the entire Captain SDLC nerve center
(internal ADRs, roadmap, planning) under its `Packages/`.

**Why it's debt.** Nerve-center docs are internal planning material, not
consumer-facing package content. Shipping them leaks internal docs to
consumers, bloats the package, and produces recurring `.meta` churn (every new
nerve-center doc mints a tracked meta on the next import — e.g. this very file
will). It also entangles two things ADR-0001 and ADR-0003 deliberately keep
separate: the ATH *tool* vs the Captain SDLC *layer*. Tied to the still-open
question of whether `captain-sdlc/` should share the ATH package's version
surface / tag at all.

**Fix options (decide later):**
1. **Tilde-hide** — rename `captain-sdlc/` → `captain-sdlc~/`. Unity ignores
   `~`-suffixed folders (as with `Documentation~` / `Samples~`): no import, no
   metas. Cheapest; stops the meta churn. *Does not* stop the docs shipping in
   the tarball.
2. **Relocate out of the package** — move the nerve center to a sibling dir or a
   separate repo. Stops both the meta churn and the shipping; cleanest
   separation; most work.
3. **Exclude from the published artifact only** — keep it imported in-repo but
   drop it from the package payload. Stops shipping; *does not* stop meta churn.

Leaning 1 or 2; 1 is the quick win, 2 is the principled end-state.

**Blast radius of a rename/move:** `.claude-interrogate.json` docs-dir path;
intra-doc relative links across the set; `captain-sdlc/tools/captain-trace.mjs`
path + its README; `Documentation~/trace-events.md` and source-comment
references to `captain-sdlc/...`; the 76 committed `.meta` files (removed if
tilde-hidden/moved).

**Related:** ADR-0001 (tools-not-modules packaging), ADR-0003 (Captain SDLC
layer is not a tool), ADR-0013 (cross-tool layer ownership), and the open
question on `captain-sdlc/` sharing ATH's version/tag.

---

## Cross-References
- [Captain SDLC](./README.md)
- [Captain SDLC — Roadmap](./roadmap.md)
- [Captain SDLC — Open Questions Rollup](./open-questions.md)
- [Captain SDLC — Candidates](./candidates.md)
- [ADR Index](./ADR/index.md)
