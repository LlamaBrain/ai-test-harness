# ADR-0004: Shared code-reading capability owned by interrogate

**Date:** 2026-05-28

## Problem

Three Captain SDLC seams need to read code structure: design-code drift (Seam 2), constitution enforcement (Seam 6), and contract testing's parser-introspection mechanism (Seam 4 Mechanism B). Without a shared owner, each seam was deferring the decision to "the other docs," and three independent code-reading implementations would have accumulated incompatible upgrade paths and divergent false-positive profiles.

## Solution

The code-reading capability is owned by interrogate, extended with three tiers chosen by upgrade-path discipline:
- **Tier 1**: grep + asmdef parse. Cheap; covers named-entity existence and assembly topology.
- **Tier 2**: tree-sitter. Multi-language syntax-tree analysis; covers inheritance, signatures, comment/code discrimination.
- **Tier 3**: Roslyn (or language-server equivalent). Full semantic analysis; .NET-only.

All three consumer seams start at Tier 1. Promote when false-positive rates hurt. Tier downgrades unsupported. Full design in `code-reading-capability.md`.

## Alternatives

- **Standalone analyzer tool** — Adds a process boundary and version-pinning concern; rejected because no concrete pain forces the separation yet.
- **Per-seam independent implementations** — Three upgrade paths diverging; rejected as antithetical to the shared-conventions model.
- **Ship at Tier 3 first** — Heaviest, most precise. Rejected: pays for capability that the minimal first cuts of Seams 2/6 don't need.
