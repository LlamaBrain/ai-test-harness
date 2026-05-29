# M2 — TRACE_SCHEMA_FIRST_EMITTER
Status: In Progress
Last Updated: 2026-05-29

## Theme
The smallest viable instance of the whole Captain SDLC pipeline shape: one real
tool emitting one real event into the shared cross-tool trace. ATH writes
`ath.smoke.completed` to `.captain-sdlc/trace/`, matching the envelope in
`trace-schema.md`. Proving the format end-to-end with a live emitter is what
turns the schema from a planning doc into a substrate the downstream seams
(M5 release gates, M7 regression envelope) can read.

## Goals
- Prove the trace **envelope** (`trace-schema.md`) by emitting a real event from
  a real tool, not a fixture.
- Set the **emitter precedent**: a tool owns the correctness of its records
  (mints `event_id`, stamps the timestamp, pins `schema_version`/`tool`/
  `tool_version`); callers pass only verdict + context. Honors ADR-0012.
- Lay the **storage substrate** (`<project>/.captain-sdlc/trace/YYYY-MM-DD.jsonl`)
  that later consumers read, with the local-state `.gitignore` guaranteed by the
  tool.

## Definition of Done
- [x] `ath.smoke.completed` payload schema documented in ATH's own docs
  (`Documentation~/trace-events.md`), linking back to the canonical envelope.
- [x] Editor MCP tool `ath-trace-emit` appends a `schema_version: 1` envelope to
  the day's JSONL file, append-only, LF-terminated, UTF-8 no-BOM.
- [x] The tool mints a UUIDv4 `event_id`, stamps an ISO-8601 UTC `timestamp`,
  and pins `tool: "ath"` + `tool_version` = `AthRuntimeFlag.PackageVersion`.
- [x] Trace directory resolves to the **consuming project root** (parent of
  `Application.dataPath`), not the ATH package directory.
- [x] Emitter lazily creates `.captain-sdlc/.gitignore` excluding `trace/` and
  `side-store/` (per `captain-sdlc-conventions.md`).
- [x] Serialization (`AthTraceWriter`) and IO (`AthTraceEmitter`) are split into
  pure, dependency-free units that can be unit-tested without an MCP attachment
  (mirrors the `AthStateDispatcher` split). No Newtonsoft dependency.
- [x] `ath-smoke-fullloop` SKILL calls the emitter as its final step, on **both**
  pass and fail, before PlayMode exit.
- [ ] **Live-verified in a Unity editor:** running the smoke against a host
  project (BeforeTheShade) writes a well-formed line to the day's file for both
  a pass and a forced fail; the JSON parses and validates against the envelope.
  *(Close-out gate — requires the editor; cannot be verified headless.)*
- [ ] Unity-generated `.meta` files for the three new `.cs` files and the new doc
  committed alongside source.

## Targeted
**In scope (this milestone):** the `ath.smoke.completed` envelope + payload, a
single emitter (ATH), the trace storage location, and the protective
`.gitignore`. This is the schema's own "minimal first cut."

**Deferred (named, not silently dropped):**
- Perf **envelope summary** in the payload → M7 (baseline regression envelope).
- `parents` / `links` → needs an upstream emitter (`code.commit.created` from
  claude-release) to link to.
- Additional event kinds (`ath.regression.detected`, `ath.replay.*`, …).
- A **trace consumer / walk-back CLI** — a prototype now exists and is verified:
  `captain-sdlc/tools/captain-trace.mjs` (`walk` / `by-commit` / `ls`),
  satisfying the schema's "one emitter + one consumer" minimal first cut. It is
  *not* part of the ATH package (it's nerve-center cross-tool tooling). Formal
  milestone placement for the cross-tool layer is pending (trace-schema open
  questions #1 and #3).
- Cross-machine trace merging → revisit when CICD (M21) lands.

## Blockers & Dependencies
- **M1_CONVENTIONS_ESTABLISHED** — the `.captain-sdlc/` layout and
  `schema_version` policy. Satisfied.
- **Live verification** depends on a Unity editor with a registered host adapter
  (BTS or dirigible). This is the one open DoD item.
- **Emitter-placement decision is ADR-worthy** — "tools own trace-record
  correctness via an editor MCP tool" sets precedent for every future tool's
  emitter. File as an ADR before a second tool emits.

## References
- `../trace-schema.md` — canonical event envelope (Seam 1).
- `../captain-sdlc-conventions.md` — `.captain-sdlc/` layout, `schema_version`,
  git policy.
- `../../Documentation~/trace-events.md` — ATH-owned `ath.smoke.completed`
  payload schema.
- Source: `Editor/McpSkills/AthTraceWriter.cs` (envelope serializer),
  `Editor/McpSkills/AthTraceEmitter.cs` (IO + paths),
  `Editor/McpSkills/Tool_AthTraceEmit.cs` (`ath-trace-emit` MCP tool).
- Skill wiring: `Skills/ath-smoke-fullloop/SKILL.md` Step 8.
- Top-level index: `../roadmap.md`
