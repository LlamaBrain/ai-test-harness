# ADR-0012: The human owns taste; the tools own correctness

**Date:** 2026-05-28

## Problem

Captain SDLC needs one sharp statement of its division of labor — what the tools do, what the human does, and *why* that split is the point rather than a limitation. "Tools eat the process; the human eats the design" (README mission) and "SemVer is process, milestones are design" (ADR-0002) gesture at it, but "process / design" is fuzzy: plenty of design work is mechanical, and plenty of process needs judgment. The paradigm lacked its one-sentence claim.

## Solution

**The tools own correctness; the human owns taste.**

- **Correctness** is everything with a checkable right answer: SemVer bumps, Conventional-Commit compliance, smoke pass/fail, regression drift, dependency CVEs, constitution-invariant satisfaction, schema/contract conformance. Because correctness is *verifiable*, it is mechanizable — and therefore the tools' job. A machine holds that line tirelessly and without ego.
- **Taste** is the residue: the calls with no right answer, only judgment — what is worth building, whether it *feels* right, when something "correct" still isn't "good," whether this should ship *now*. Taste does not scale, cannot be checked, and is the one thing the human must spend attention on. It is the human's job, by design.

The split is defined negatively and exhaustively: the tools take everything checkable; whatever remains is taste. Taste is not a leftover the tools couldn't reach — it is the deliberately protected core.

**The inversion is the whole point.** Most tooling *adds* process, and process quietly taxes the one resource that doesn't scale: the human's taste-attention. Captain SDLC *removes* process — by mechanizing every checkable concern — specifically to buy that attention back. "More creation, more often, without quality loss" reduces to: spend the reclaimed attention on taste. HITL is not safety theater; the human-in-the-loop is where taste enters, and taste is the value.

This is the paradigm's one-sentence claim and positioning line.

## Consequences

- Every seam holds a correctness floor and then **stops at the taste boundary.** Release gates (M5) verify smokes/CVEs and refuse inconsistent ships — then the human still presses release, because "should this ship now" is taste. The constitution checker (M11) flags invariant violations — then the human fixes, softens, or accepts. Drift detection surfaces deltas for adjudication; it never auto-resolves. A seam that tries to automate a taste call is mis-scoped.
- Anything proposed for automation must be checkable. If evaluating a proposed gate/check requires judgment, the verdict belongs to the human, not the tool — the tool may *surface* it, but it does not *decide* it.

## Alternatives

- **"Tools eat process; the human eats design" (status quo).** Kept as mission framing, but too fuzzy for the load-bearing line — design has mechanical parts, process has judgment parts. "Correctness / taste" cuts cleaner because it keys on *checkability*, which is also the automation criterion.
- **Leave it implicit.** Rejected — the paradigm competes at the positioning layer and needs its claim stated, not inferred.

## References

- ADR-0002: Milestone-version separation — this ADR generalizes that split's underlying principle (checkable → tool; judgment → human).
- README mission: "Tools eat the boring repetitive work; the developer's attention stays on the parts that need taste."
