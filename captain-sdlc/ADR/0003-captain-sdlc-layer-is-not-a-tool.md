# ADR-0003: Captain SDLC layer is not a separate tool

**Date:** 2026-05-28

## Problem

Several seam-level items in `candidates.md` (pipeline trace, cross-tool contract testing) were originally tagged with home = "Captain SDLC layer." The label was a placeholder for "the cross-tool plumbing" but never defined an architectural form — was it a new tool? A shared library? An emergent property of conventions? Anything tagged that way had no actual owner, only a label. This was the first finding in `expose.md`.

## Solution

The cross-tool seams are *not* a separate tool. Schemas, conventions, and shared specifications live in this nerve-center docs repo (currently the ATH repo per ADR-not-yet-filed on nerve-center location). Implementations are distributed across the emitting/consuming tools — each tool that needs to participate in a seam implements its end of the contract.

Affected items in `candidates.md` were re-homed to "Nerve-center (schema) + per-tool (emission/validation/ingest)." Reinforces ADR-0001 (tools-not-modules).

## Alternatives

- **Build a Captain SDLC layer tool** — Would centralize cross-tool plumbing in one repo but couple every tool to it; adds a runtime dependency and a release-coordination concern.
- **Shared library imported by each tool** — Closer to monorepo coupling; rejected per ADR-0001 (tools share conventions, not code).
