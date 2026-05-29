# ADR-0013: Tools emit their own trace records; correctness lives in the tool, not the agent

**Date:** 2026-05-29

## Problem

Seam 1 (the cross-tool trace, `trace-schema.md`) needs a first emitter. The
obvious candidate — ATH's `ath.smoke.completed` event — has an awkward host:
an ATH smoke is *agent-orchestrated*. A SKILL drives a live Unity editor
through `ath-cmd`/`ath-state`/`ath-wait`, and the **agent** computes the
pass/fail verdict from the SKILL's PASS criteria. There is no single smoke
*process* whose exit could append a line. So "who writes the event, and who
guarantees it conforms to the envelope?" is undecided — and the answer sets the
precedent for every future emitter (claude-release's `code.commit.created`,
interrogate's `design.*`, the release-gate consumer that reads them).

Three options: (A) a tool the agent calls, which writes the record; (B) the
agent assembles the JSON and writes the file itself at the end of the SKILL;
(C) a standalone cross-tool CLI all tools shell out to, built now.

## Solution

**Each tool emits its own trace records through a tool surface that owns the
record's correctness. The orchestrating agent supplies only the verdict and
context; it never hand-assembles the envelope.**

Concretely for ATH (option A): a new editor MCP tool, `ath-trace-emit`, mints
the `event_id`, stamps the UTC `timestamp`, pins `schema_version`/`tool`/
`tool_version`, resolves the consuming-project trace directory, and serializes
via a pure, dependency-free writer. The SKILL calls it as its final step, on
both pass and fail.

This is the direct corollary of ADR-0012 (*tools own correctness; the human
owns taste*) applied to the trace: envelope conformance is a checkable concern,
so a tool owns it — not the agent's per-run discipline. It also resolves a
practical constraint: only the editor process reliably knows the consuming
project root (parent of `Application.dataPath`), where `.captain-sdlc/trace/`
must live — not the ATH package repo, and not a CLI invoked from elsewhere.

Per-tool emitters are accepted as the model: the trace envelope is small and
fixed, so each language ecosystem re-implementing the ~20 lines of envelope
assembly is cheaper than coupling every tool to a shared runtime. The schema
already anticipates this — *"per-tool docs own each kind's payload schema."*

## Consequences

- Trace-record correctness is mechanically guaranteed at the emit boundary, not
  dependent on an agent getting JSON right every run.
- A small amount of envelope-assembly logic is duplicated across tools/languages
  as more emitters come online. Accepted; revisit only if the envelope grows
  complex enough that duplication becomes a real maintenance cost.
- The cross-tool *consumer* (walk/by-commit) is the shared piece, not the
  emitters. When that consumer graduates into "the cross-tool layer becomes its
  own tool" (trace-schema open question #1), schema *ownership* moves there; the
  per-tool emitters stay where they are.
- Emit is decoupled from the verdict: an emit failure is a trace-write problem,
  surfaced but never able to flip a smoke's PASS/FAIL.

## Alternatives

- **(B) Agent writes the JSON directly.** Lightest — no tool code. Rejected:
  trace conformance becomes a function of agent discipline per run, which is
  exactly the class of checkable concern ADR-0012 says a tool should own.
- **(C) Standalone cross-tool CLI now.** The eventual shape, but premature: the
  schema's own "minimal first cut" is *one ATH emitter*, a CLI can't easily
  resolve the Unity project root, and building the shared layer before there's a
  second emitter to share it inverts the incremental plan.

## References

- `../trace-schema.md` — cross-tool trace envelope (Seam 1).
- `0012-human-owns-taste-tools-own-correctness.md` — the principle this applies.
- `../Roadmap/M2_TRACE_SCHEMA_FIRST_EMITTER.md` — the milestone.
- `../../Documentation~/trace-events.md` — ATH-owned `ath.smoke.completed` payload.
