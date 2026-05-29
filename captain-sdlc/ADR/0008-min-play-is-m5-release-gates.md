# ADR-0008: MIN PLAY waypoint is M5_RELEASE_GATES_MINIMAL

**Date:** 2026-05-28

## Problem

The Captain SDLC roadmap needed a designated MIN PLAY milestone — the smallest end-to-end demonstration of the pipeline's idea→plan→verify→release loop. Candidates: M2 (trace schema first emitter — foundational), M5 (release gates minimal — integrative), M12 (design-code drift minimal — philosophical/idea-bridging). The choice anchors the rest of the roadmap: everything past MIN PLAY accretes on the shape MIN PLAY established.

## Solution

`M5_RELEASE_GATES_MINIMAL` is the MIN PLAY waypoint. Criterion: claude-release refuses to publish a release when configured ATH smokes fail or the dependency audit reports any blocking CVE, with override recorded in both commit message and trace. This is the lightest payload that demonstrates the full chain — design captured as a milestone, milestone realized as code, code mechanically verified by ATH, verification gated by claude-release.

Everything past M5 accretes on this shape: each subsequent milestone produces more gate inputs (drift, contracts, constitution) or more verified artifacts (perf envelope, replay, artifact diff) that flow through the established loop.

## Alternatives

- **M2 (trace schema first emitter)** — Foundational but no end-to-end visible value; a single tool emitting events isn't a pipeline yet.
- **M12 (design-code drift minimal)** — Philosophically richer (most directly demonstrates the idea↔plan bridge) but more abstract; harder to articulate "we know it works."
