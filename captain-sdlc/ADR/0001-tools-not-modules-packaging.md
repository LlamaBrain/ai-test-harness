# ADR-0001: Tools-not-modules packaging

**Date:** 2026-05-28

## Problem

Captain SDLC consists of multiple distinct capabilities — Socratic design interview, Unity QA, release bookkeeping, eventually CICD and Live Ops. A traditional choice would be to ship these as a single coherent package or monorepo with shared library code, ensuring consistency but coupling adoption to the whole.

## Solution

Captain SDLC is a series of independently-versioned tools — like a swiss army knife. Each tool (interrogate, ATH, claude-release, future CICD) is independent in versioning, distribution, and adoption. Tools share *conventions* (trace schemas, classification primitives, fenced-block format, `.captain-sdlc/` directory layout, suppression file convention) but not *code*. A consumer can adopt one tool without committing to the whole pipeline. The pipeline emerges from shared conventions, not from internal coupling.

## Alternatives

- **Monorepo / single package** — Would force adoption of the whole pipeline; couple versioning; make it harder to evolve tools at their own pace.
- **Shared SDK / library imported by each tool** — Would create internal coupling and breaking-change risk across tools; rejected in favor of contract-based interop where each tool emits/consumes structured artifacts that conform to nerve-center schemas.
