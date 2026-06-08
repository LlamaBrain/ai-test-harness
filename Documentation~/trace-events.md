# AI Test Harness — Trace Events

ATH emits Captain SDLC **cross-tool trace events** (Seam 1) so the pipeline can
answer questions like *"which design decision introduced this regression?"* The
shared event **envelope** — `schema_version`, `event_id`, `timestamp`, `tool`,
`tool_version`, `kind`, `refs`, and the optional `parents`/`links` — is defined
once, canonically, in the nerve-center doc:

- Envelope spec: [trace-schema.md](https://github.com/LlamaBrain/captain-sdlc/blob/main/trace-schema.md) (the `captain-sdlc` repo)
- Storage + conventions: [captain-sdlc-conventions.md](https://github.com/LlamaBrain/captain-sdlc/blob/main/captain-sdlc-conventions.md)

**This doc owns the *payloads*** for the event kinds ATH emits — per the
schema's "tool docs own each kind's payload schema" rule. Right now ATH emits
exactly one kind.

## Where events go

`<consuming-project-root>/.captain-sdlc/trace/YYYY-MM-DD.jsonl` — append-only
JSON Lines, one event per line, one file per UTC day. The "consuming project" is
the Unity project under test (e.g. BeforeTheShade), resolved as the parent of
`Application.dataPath` — **not** the ATH package directory. The emitter lazily
writes `.captain-sdlc/.gitignore` (`trace/`, `side-store/`) the first time it
runs, so traces are never committed by accident.

Capture artifacts (screenshots; later, video) land under `.../trace/media/` and
are referenced from `artifacts` as trace-relative paths like `media/<file>`.
Because they live under `trace/`, the same `.gitignore` keeps them out of
version control.

## Emitter

The `ath-trace-emit` editor MCP tool — invoked through the Unity-MCP bridge,
**not** as a slash-command:

```bash
unity-mcp-cli run-tool ath-trace-emit --input '{"result":"pass","summary":"loop green"}'
```

Because an ATH smoke is
agent-orchestrated — a SKILL drives the editor and the agent computes the
verdict — there is no single host process to hook. The skill passes the verdict
and context; the **tool** owns record correctness: it mints the `event_id`,
stamps the UTC `timestamp`, and pins `schema_version` / `tool` / `tool_version`
(`tool_version` = `AthRuntimeFlag.PackageVersion`). Callers never hand-assemble
the envelope.

Serialization lives in the pure `AthTraceWriter`; IO in `AthTraceEmitter`. Both
are dependency-free (no Newtonsoft — the Editor asmdef pins a closed reference
set) and unit-testable without an MCP attachment, mirroring the
`AthStateDispatcher` split.

## Capture artifacts

`ath-snap` (editor MCP tool) captures a **Game-view PNG** as HITL-validation
evidence, written under `.../trace/media/` and returned as a trace-relative path
(`media/snap_<ts>.png`) ready to drop into `artifacts`. Capture is non-blocking:
`action:"capture"` returns `pending` + a `CaptureId` immediately (Unity writes
the file at end-of-frame), and `action:"query"` (by `CaptureId`, or by a
trace-relative `path` after a domain reload) reports `ok` with width/height/size
once the file has landed. Requires PlayMode.

Artifact paths are validated at the emit boundary: only safe **trace-relative**
paths are accepted (normally `media/<file>`; safe bare filenames too); an
absolute/rooted, `..`, or backslash path is rejected with `Status=bad_artifact`
and no event is written.

> Tier-2 motion capture (`ath-record`, Unity Recorder mp4) is an opt-in soft
> dependency; its payload notes will be added here when it ships.

## Kind: `ath.smoke.completed`

Emitted once at the end of an ATH smoke skill, on **both pass and fail** — the
fail records are what make the backward-walk useful. `tool_version` is bumped
whenever this payload shape changes (additive changes — new optional fields —
do not require a `tool_version` bump but are good practice to note).

### Payload fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `skill` | string | yes | Skill that produced the verdict, e.g. `ath-smoke-fullloop`. |
| `skill_version` | string | yes | Skill frontmatter version. Defaults to the package version (the smoke skills track the package in lockstep). |
| `result` | `"pass"` \| `"fail"` | yes | The smoke verdict. |
| `failed_step` | string \| null | yes | The step that failed, e.g. `"Step 5"`. `null` on pass. |
| `summary` | string \| null | yes | One-line human summary of the run. `null` when not supplied. |
| `artifacts` | string[] | yes | Trace-relative artifact paths — normally `media/<file>` produced by `ath-snap` (safe bare filenames also accepted). Absolute/rooted, `..`, and backslash paths are rejected by the emitter. `[]` when none. |

### refs used by this kind

| Ref | Source | Notes |
|---|---|---|
| `project` | explicit arg → adapter `HostName` slug → project folder name | Recommended to pass explicitly (see trace-schema open question on project namespacing). |
| `commit` | caller (`git rev-parse --short HEAD`) | `null` when not supplied. |
| `release` | — | Always `null` for a smoke (no release context). |
| `design_doc` | caller | The design doc this smoke verifies. Optional. |
| `task_id` | caller | The task this smoke verifies. Optional. |

`parents` / `links` are **not** emitted yet. Once `claude-release` emits
`code.commit.created`, a smoke's `parents` should point at the commit-completion
event it tested (per the schema's linking conventions). Tracked as a follow-up.

### Example

```json
{"schema_version":1,"event_id":"f1c2a3b4-5d6e-4a7b-8c9d-0e1f2a3b4c5d","timestamp":"2026-05-29T15:42:08.123Z","tool":"ath","tool_version":"0.3.0","kind":"ath.smoke.completed","refs":{"project":"before-the-shade","commit":"26e6d1a","release":null,"design_doc":null,"task_id":null},"payload":{"skill":"ath-smoke-fullloop","skill_version":"0.3.0","result":"pass","failed_step":null,"summary":"full death→ghost→finish→restart loop green","artifacts":[]}}
```

### Failure example

```json
{"schema_version":1,"event_id":"a9b8c7d6-...","timestamp":"2026-05-29T15:55:01.004Z","tool":"ath","tool_version":"0.3.0","kind":"ath.smoke.completed","refs":{"project":"before-the-shade","commit":"deadbee","release":null,"design_doc":null,"task_id":null},"payload":{"skill":"ath-smoke-fullloop","skill_version":"0.3.0","result":"fail","failed_step":"Step 5","summary":"ghost_active stayed false after respawn","artifacts":["media/fullloop_FAIL_gameview.png"]}}
```

## Deferred (not in the first cut)

- **Perf "envelope summary"** in the payload — the schema mentions it, but the
  baseline regression envelope is M7. Added to this payload when M7 lands.
- **`parents`/`links`** — wired when an upstream emitter (`code.commit.created`)
  exists to link to.
- **Additional kinds** — `ath.regression.detected`, `ath.replay.captured`, etc.
  are in the schema's taxonomy but unimplemented; each gets a payload section
  here when ATH starts emitting it.
