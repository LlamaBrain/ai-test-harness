# Captain SDLC — Competitive Landscape & Redundancy Audit

**Status: Analysis (revised).** Snapshot 2026-05-28. Originally a per-tool market-moat audit; revised after discussion to the paradigm lens — the redundancy question turned out to be the wrong question. Popularity figures are from a 2026-05-28 web sweep and are approximate. This is not a marketing exercise: the one-sentence public claim is deliberately parked, not settled here.

## Summary

The starting question — *"is any of these made redundant by a popular package?"* — is answered and then dissolved.

- **Answered:** none of the three tools (claude-release, claude-interrogate, AITH) is redundant. Each is PARTIAL_OVERLAP — a commodity core wrapped in a genuinely-novel opinionated layer. No single popular package replaces any of them.
- **Dissolved:** the redundancy question only bites at the *tool* layer, and the tools are not the unit. **Captain SDLC is a paradigm, not a toolset.** You don't compete with semantic-release / Spec Kit / Unity-MCP feature-for-feature; you compete one layer up, where nobody ships the whole thing.

## The reframe — paradigm, not toolset

The Spec Kit check proved the point. Spec Kit won (~93k★) not on a Python CLI but on naming a **paradigm** — Spec-Driven Development — and shipping thin reference tools that make it tangible. Paradigm is the unit of competition. Tools get cloned in a weekend; an opinionated, battle-tested paradigm (the LAIRD lineage) does not.

So the three tools are **proof-of-paradigm, not the product.** The paradigm has two defining properties:

1. **Adversarial verification, not augmentation.** "AI-augmented SDLC" commits to nothing and describes every competitor. The actual stance is the opposite of passive augmentation: *the agent runs the loop, and deterministic gates refuse to let it lie.* claude-interrogate pressure-tests instead of gap-filling; claude-release refuses changelog prose it can't trace to a commit hash; AITH asserts against live runtime state. The verb is interrogate / verify / gate.
2. **Whole-loop, not planning-first.** Spec-driven development is planning-centric: constitution → spec → plan → tasks, then it hands off at "implement" and stops. Captain SDLC keeps the verification discipline running *past* planning — through QA (AITH) and release (claude-release), and eventually CICD, live-ops, and marketing. Design/planning is one facet, not the whole. The rigor does not end at the spec.

**The moats are not Unity-coupled.** This corrects a drift in the original agent analysis. claude-interrogate has zero Unity in it. claude-release's *primary* moat (the prose verifier) is ecosystem-agnostic. Only the API-differ's `[SerializeField]` rule and AITH itself are Unity-bound — and that flavor is a **dogfooding artifact** (the dogfood target is a Unity game), not the strategy. Unity-the-market not valuing release hygiene is therefore irrelevant to positioning.

## Method & a provenance caveat

Run as a 3-tool × 3-stage workflow (characterize from source → research landscape → adversarial verdict). The adversarial stage produced a confident but **false** accusation that claude-release ships fewer features than it advertises. Checked directly against `E:\Personal\claude-release\src` and **refuted**: `classify-api-diff.js` exists; `smell.js` has **9** check functions, not 5; `build-manifest.js:84-101` does promote `major`/`minor` from the API diff (patch is only the empty-diff fallback). Lesson logged — the verifier hallucinated a grep. The facts below are sound; the "just delete it" framing the adversary applied to claude-release was overstated.

## claude-release

**Space:** extremely crowded. The Conventional-Commits → semver → Keep-a-Changelog → tag spine is owned by incumbents at 100–1000× the adoption (semantic-release, release-please, changesets, release-it, git-cliff, commitlint). `commit-and-tag-version` even shares the never-push philosophy.

**Moat (ecosystem-agnostic, no popular equivalent):**
1. **Manifest-as-contract verification** (`verify-output.js`) — deterministically adjudicates *LLM-written* changelog prose: every bullet must trace to a real commit hash or the next-version token; hallucinated hashes, non-KAC headers, and unsupported editorializing adjectives ("world-class", "blazingly") are rejected. Mainstream tools generate the changelog themselves, so they never needed to audit prose. This is the adversarial-verification paradigm applied to release notes — and an **embryonic marketing-honesty gate**: the same verifier pointed at louder copy is the marketing arm.
2. **Roslyn C# API-diff → semver coupling**, with Unity `[SerializeField]` as serialization contract and additive-param reclassification. Closest prior art (Microsoft `ApiCompat`/`PublicApiAnalyzers`) gates breakage but doesn't drive a bump or feed a pipeline, and is Unity-blind. This is the one Unity-flavored piece — a detachable extension, not the core.

**Honest trim:** the bump math and CC parsing are solved commodity. The bump is *fed by* the API diff, so it can't be fully delegated, but semver arithmetic should not be hand-maintained as bespoke IP.

**Recommendation:** KEEP_BUT_TRIM.

## claude-interrogate

**Space:** the original "most serious threat" — GitHub Spec Kit (~93k★). **Now verified: Spec Kit does NOT eat claude-interrogate.** Its `/clarify` is a neutral spec-*completion* tool — it scans an existing spec across a 9-category taxonomy and asks ≤5 gap-filling questions. The command template explicitly **excludes** pressure-testing: it does not force rejected alternatives, does not ask you to falsify success criteria, does not challenge design choices. Remedial, not adversarial.

So interrogate's **adversarial Socratic interview** (challenge dial, forced rejected-alternatives, failure-mode disclosure) is genuinely differentiated, and the **roadmap/RC engine** (`scope.ts`: link-inferred dependency DAG with cycle-refusal, immutable shipped-lock with field-level override audit, `.draft.md` maintenance mode) plus the **corpus-consistency engine** (`sync.ts`: house-style *inference* from sibling docs, reciprocal cross-ref normalization, resolved-question migration) were never Spec Kit's territory. Zero Unity anywhere in this tool.

**Honest trim:** the doc-level `inferSemverBump` in `docs.ts` re-implements semver math already shipped as claude-release — the clearest "reinventing a commodity" instance across the set. Generic markdown hygiene → markdownlint/markdown-link-check; keep only design-doc-specific audits.

**Recommendation:** KEEP_BUT_TRIM.

## AITH (AI Test Harness)

**The original "thin veneer / ~80% commodity" verdict was wrong** — it scored a tool inventory, not a capability. Yes, AITH is a thin layer on Ivan Murzak's Unity-MCP (single dependency; three tool classes + a runtime bridge + an adapter seam), and the substrate provides MCP transport, PlayMode control, `console-get-logs`, runtime inspection, `tests-run`. But the product is not those tools — it is a **closed, automatable loop on a live running game**: drive → wait for convergence → assert on runtime state → capture failure → iterate. Used for QA *and* feature development.

**Unity-MCP cannot do this out of the box.** Its marketed "AI develop-and-test loop" is an edit-and-batch-test loop (edit scripts, run UTF tests, inspect objects at editor time) — not driving a *live* PlayMode session to convergence. The reason it can't is exactly the primitives the original analysis dismissed as glue:

- `ath-wait` — poll-until-converged predicate vocabulary with timeout-triage (no rival ships a wait-until-condition primitive).
- Correlation-id-scoped log → CMD/OK/ERR request/response channel (turns a global log stream into a per-call channel so concurrent agent commands don't cross-talk).
- Edge-sticky `*SinceLastReset` latching (resolves the agent-paced-vs-frame-paced race; an out-of-band poller would otherwise miss a one-frame event).
- `IAthHostAdapter` semantic game-state vocabulary behind a host-type-free seam.

These are what convert a manual tool pile into an automatable loop. The whole exceeds the sum. The name "AI **Test** Harness" undersells it if it's also the substrate for agent-driven feature work.

**Honest caveats:** the productized form isn't there — the shipped smoke skill is welded to one host game (BeforeTheShade); the host-coupling must be extracted. `ath-state` overlaps Unity-MCP's `object-get-data`; justify it only by the curated semantic vocabulary. Obsolescence trigger: CoplayDev/unity-mcp shipping a wait-predicate + runtime-command channel.

**Recommendation:** WRAP_OFFTHESHELF — stay a thin layer, never re-host the substrate; the value is the loop protocol added on top, not the convenience.

## Cross-cutting conclusion

Own the paradigm, delegate the commodity. The defensible thing is **whole-loop, adversarially-verified SDLC** — the agent runs the loop, deterministic gates keep it honest, end to end (design → QA → release → … → marketing). No popular package is redundant with this, because each automates one slice *for a human*; none automates the whole loop *for an agent*. The three tools are proof-of-paradigm. Release them as independent tools under one system story (consistent with the tools-not-modules ADR), not as standalone utilities — separately, each loses its individual comparison; together they demonstrate the paradigm.

The "redundancy" risk is replaced by two real ones (see Open Questions): generalization beyond the author's own loop, and whether adversarial friction reads as a feature or a turn-off.

## Cross-References

- [Captain SDLC](./README.md)
- [Captain SDLC — Vision](./vision.md)
- [Captain SDLC — Candidates](./candidates.md)
- [Captain SDLC — Conventions](./captain-sdlc-conventions.md)
- [Captain SDLC — Seam 3: Release Gates](./seam-release-gates.md)
- [Captain SDLC — Open Questions Rollup](./open-questions.md)

## Resolved Decisions

- **2026-05-28:** None of the three tools is redundant with a popular package (all PARTIAL_OVERLAP). The redundancy question is the wrong unit — competition is at the paradigm layer, not the tool layer.
- **2026-05-28:** Captain SDLC is framed as a paradigm, not a toolset; the tools are proof-of-paradigm. Defining properties: adversarial verification (not augmentation) and whole-loop coverage (not planning-first). Design is one facet.
- **2026-05-28:** The moats are not Unity-coupled. interrogate has zero Unity; claude-release's primary moat (prose verifier) is ecosystem-agnostic. The Unity flavor is a dogfooding artifact, so Unity-market apathy toward release hygiene does not constrain positioning.
- **2026-05-28:** Spec Kit does NOT eat claude-interrogate — verified against its `/clarify` template, which is neutral gap-filling and explicitly excludes adversarial pressure-testing.
- **2026-05-28:** AITH is a closed-loop QA/feature-dev automation substrate, not a thin commodity veneer; the loop (driving a live PlayMode session to convergence) is a capability Unity-MCP lacks out of the box.
- **2026-05-28:** The adversarial workflow's claim that claude-release ships phantom features was investigated and refuted; recorded so it is not re-litigated.
- **2026-05-28:** Intent is to eventually release all three publicly, as independent tools under one system story.

## Open Questions

- **Paradigm-of-one:** the paradigm has only been dogfooded on the author's own Unity loop. Does it generalize to other stacks/teams, or is it a personal methodology with three utilities attached? Public release is the test.
- **Positioning bet:** do enough people want adversarial pressure-testing over neutral gap-filling? Spec Kit bet neutral because it is safer/less annoying at scale. Is the friction a feature or a turn-off?
- Should claude-interrogate's `inferSemverBump` be removed in favor of calling claude-release, given the sibling-tool dependency that introduces?
- What is the concrete obsolescence trigger to re-run this audit (e.g., CoplayDev/unity-mcp shipping live-PlayMode tools)?

## Version History

- 0.2.0 (2026-05-28): Reframed from per-tool market-moat analysis to the paradigm lens; corrected the AITH verdict (closed-loop automation, not thin veneer); added the verified Spec Kit / claude-interrogate result; recorded that the moats are not Unity-coupled; replaced the redundancy question with the generalization and positioning risks.
- 0.1.0 (2026-05-28): Initial competitive landscape & redundancy audit across claude-release, claude-interrogate, and AITH.
