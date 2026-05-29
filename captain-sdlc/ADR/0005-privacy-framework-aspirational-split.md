# ADR-0005: Privacy framework + aspirational policy split

**Date:** 2026-05-28

## Problem

Captain SDLC's privacy story spans two timescales. Structural commitments matter *today*: AI processing routes data through Claude API (a third-party processor) even in solo dogfooding mode; classification primitives shape every future schema; the trace-vs-side-store decision is load-bearing for everything downstream. Full policy specifics — consent UX, retention defaults, jurisdictional compliance, data-subject rights — only matter *when shipped against real users*. Writing the full policy now would design for unvalidated hypothetical requirements.

## Solution

Split the privacy story across two docs:
- **`privacy-framework.md`** — concrete today + structural primitives. AI processing carve-out (canonical statement: `sensitive`-class data never goes to Claude API directly), four-level classification (`public` / `pseudonymous` / `personal` / `sensitive`), trace-vs-side-store split, explicit deferral list with triggers.
- **`privacy-policy-aspirational.md`** — full hypothetical policy with explicit triggers for when each section activates: first commercial release, first EU/CA player, second developer with commit access, first sensitive payload schema. Sketches, not active policy.

## Alternatives

- **Write the full policy now** — Designs for unvalidated hypothetical requirements; biases future decisions before constraints are known.
- **Skip the framework entirely until Live Ops ships** — Leaves the AI-processing concrete-today commitment uncaptured; the next person to consider AI processing would re-derive it.
