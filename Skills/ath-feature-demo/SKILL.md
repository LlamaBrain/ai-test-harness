---
name: ath-feature-demo
description: Host-agnostic AUTHORING TEMPLATE for capturing HITL-validation footage of a feature working. Brackets a feature demonstration with ath-record (Unity Recorder mp4) and/or ath-snap (PNG stills), then emits an ath.smoke.completed trace with the footage attached. Copy this into your host project and replace the placeholder steps (<scene-name>, <feature commands>) with your feature's actual commands — keep the record/snap scaffolding.
version: 0.3.0
---

# Feature-demo capture (template)

This is a **template**, not a runnable smoke. It produces watchable evidence that
a feature works — a clip (and/or stills) attached to the run's trace record so a
human reviewer can validate the gate by *looking* instead of re-running the test.

> **Host-agnostic.** This file ships in the `ai-test-harness` package and must
> stay free of any one game's assumptions. Everything game-specific is a
> `<placeholder>`. Concrete, game-specific smokes belong in the **consuming
> project** (or a host-side plugin), not here — copy this skeleton there and fill
> it in.

Driven through the ATH editor MCP tools `ath-cmd`, `ath-state`, `ath-wait`,
`ath-snap`, `ath-record`, and `ath-trace-emit`.

## How to invoke these tools (read first)

Every `tool { ... }` call below is an **MCP tool** run through the Unity-MCP
bridge — **not** a Claude Code slash-command. Universal form:

```bash
unity-mcp-cli run-tool <tool-name> --input '<json-args>'
# examples:
unity-mcp-cli run-tool ath-record --input '{"action":"start","label":"my_feature"}'
unity-mcp-cli run-tool ath-snap   --input '{"action":"capture","label":"key_moment"}'
```

The `tool { args }` shorthand below means: call `run-tool <tool>` with `args` as
the `--input` JSON.

## Two capture tiers

- **Tier 2 — video (`ath-record`):** an mp4 of the bracketed window. The primary
  HITL artifact (it shows motion). Requires the **opt-in** `com.unity.recorder`
  package; without it, `ath-record` returns `Status=recorder_unavailable` — that
  is **not** a test failure, fall back to stills.
- **Tier 1 — stills (`ath-snap`):** PNG keyframes at decisive moments. No extra
  dependency; the CI-safe floor and the fallback when Recorder is absent.

Both write under `<project>/.captain-sdlc/trace/media/` and hand back a
trace-relative path (`media/<file>`) ready for `ath-trace-emit`'s `artifacts`.

## Step 0 — Version pre-flight (fail-fast)

```jsonc
// Frontmatter declares: version: 0.3.0
let live = ath-state { "key": "package_version" }
// expected: live.Value == "0.3.0"

if (live.Value != "0.3.0") {
  ABORT: stale skill copy. Re-copy this SKILL.md into
  <project>/.claude/skills/ath-feature-demo/SKILL.md and re-invoke.
}
```

## Step 1 — Enter PlayMode + readiness gates

```jsonc
editor-application-get-state {}
// if not playing:
editor-application-set-state { "isPlaying": true }

ath-wait { "predicate": "playmode",                 "timeout_ms": 30000 }
// OPTIONAL — only if your feature needs a specific scene loaded; omit if
// adapter_ready / game_ready are sufficient:
ath-wait { "predicate": "scene_loaded:<scene-name>", "timeout_ms": 30000 }
ath-wait { "predicate": "adapter_ready",            "timeout_ms":  5000 }
ath-wait { "predicate": "game_ready",               "timeout_ms": 30000 }
```

## Step 2 — Start capture

```jsonc
let rec = ath-record { "action": "start", "label": "<feature>" }
// expect rec.Status == "ok" (recording) OR "recorder_unavailable" (Recorder not installed).
//
// DEGRADATION: if rec.Status == "recorder_unavailable", continue WITHOUT video —
// rely on the per-step ath-snap stills in Step 3. Do NOT treat this as a failure.
```

## Step 3 — Demonstrate the feature  (REPLACE THIS BLOCK)

> Replace the placeholders below with your feature's actual commands. Drive the
> feature with `ath-cmd` and gate progress with `ath-wait`, exactly as a smoke
> would. Snap a still at each decisive moment so there is keyframe evidence even
> when video is unavailable.

```jsonc
ath-cmd  { "command": "<feature commands>" }
ath-wait { "predicate": "<predicate that proves the feature worked>", "timeout_ms": 5000 }

// Still backstop at the decisive moment:
let shot = ath-snap { "action": "capture", "label": "feature_proven" }
// shot.Status == "pending"; confirm it landed before relying on it:
ath-snap { "action": "query", "captureId": shot.CaptureId }
// expect Status=ok, Width/Height > 0  (else pending — query again)
```

## Step 4 — Stop capture + confirm the clip

```jsonc
// Skip if Step 2 reported recorder_unavailable.
let stop = ath-record { "action": "stop" }
// expect stop.Status == "ok", stop.Path == "media/<clip>.mp4"

// The mp4 may need a moment to finalize. Poll until Finalized:
ath-record { "action": "query", "path": stop.Path }
// expect Status=ok, Finalized=true, SizeBytes>0  (else query again)
```

## Step 5 — Emit completion trace (always: pass or fail)

Compute the verdict from your PASS criteria, then call `ath-trace-emit` once,
**before** exiting PlayMode, with the captured artifacts:

```bash
# Pass — attach the clip and/or stills (all trace-relative media/ paths):
unity-mcp-cli run-tool ath-trace-emit --input '{"result":"pass","skill":"ath-feature-demo","summary":"<feature> demonstrated","artifacts":"media/<clip>.mp4,media/<still>.png"}'

# Fail — name the failing step; attach whatever stills you captured:
unity-mcp-cli run-tool ath-trace-emit --input '{"result":"fail","skill":"ath-feature-demo","failedStep":"Step 3","summary":"<what went wrong>","artifacts":"media/<still>.png"}'
```

`artifacts` must be trace-relative (`media/<file>`); absolute/`..`/backslash
paths are rejected with `Status=bad_artifact` and no event is written.

## Step 6 — Cleanup

```jsonc
// On PASS only — leave PlayMode open on failure so the user can inspect.
editor-application-set-state { "isPlaying": false }
```

## PASS criteria

- Step 0 version check passes.
- Step 1 readiness gates all `Satisfied=true`.
- Step 3: the feature's proof predicate(s) converge (define these for your feature).
- Evidence captured: a finalized `media/<clip>.mp4` (Tier 2) **or**, when Recorder
  is unavailable, at least one confirmed `media/<still>.png` (Tier 1).
- Step 5 trace emitted with the artifacts attached.

## Notes

- `recorder_unavailable` and an unresolved `ath-snap` `pending` are **capture**
  problems, not test failures — unless evidence capture is itself the gate.
- Capture artifacts live under `.captain-sdlc/trace/media/` and are covered by
  the trace `.gitignore`, so they are never committed.
- Skill version `0.3.0` tracks the package in lockstep; bump both together.
