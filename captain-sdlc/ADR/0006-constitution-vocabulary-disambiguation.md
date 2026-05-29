# ADR-0006: Constitution vocabulary disambiguation

**Date:** 2026-05-28

## Problem

The word "constitution" was used three ways across `vision.md`, `candidates.md`, and `seam-constitution-enforcement.md` — referring to (a) the section of the canonical design doc holding invariants, (b) the tool that verifies them, and (c) the rules themselves. A reader had to infer from context. Future contributors would re-introduce the overload. Second-pass `expose.md` finding #2.

## Solution

Distinct terms across the docs set:
- **constitution** — the rules (and the section of the canonical design doc that holds them; operationally one thing).
- **constitution checker** — the tool that enforces them (form TBD; mechanical-tier ships in interrogate via the code-reading capability — see ADR-0004).
- **constitution enforcement** — the seam (Seam 6; the *activity* of enforcing).
- **invariants** — the individual rules.

`glossary.md` carries the four definitions. `seam-constitution-enforcement.md` carries the vocabulary distinction inline so first-time readers of that doc don't have to context-switch to the glossary.

## Alternatives

- **Keep "constitution" overloaded; rely on context** — Rejected; vocabulary drift across docs is inevitable without disambiguation.
- **Coin distinct terms per concept (e.g., "constitution-section / constitution-checker / constitution-rules")** — More precise but verbose. Rejected in favor of operationally-conflating constitution-as-section and constitution-as-rules (they are the same thing in practice — the section IS the rules).
