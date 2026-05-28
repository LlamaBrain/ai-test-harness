# Using the AI Test Harness

This document is the operator's guide for driving the harness from a
Claude session against a Unity host project. It assumes the host has
already implemented `IAthHostAdapter` (see `adapter-contract.md`) and
that the package is installed and resolved.

## The three MCP tools

| Tool | Purpose | Sync? |
|---|---|---|
| `/ath-cmd`   | Fire an IngameDebugConsole command, capture its CMD/OK/ERR sentinel lines, return a structured result. | Sync. |
| `/ath-state` | Query a named slice of harness state (host identity, player slice, ghost slice, custom host-specific keys). | Sync, idempotent, read-only. |
| `/ath-wait`  | Poll a named predicate at 100 ms cadence until satisfied or timeout. | Sync. |

All three live in the Editor-only `LlamaBrainLabs.Ath.Editor` asmdef and
register at PlayMode entry via `[McpPluginTool]`. They are invisible in
release builds.

## The basic loop

A canonical sequence in a skill looks like:

```jsonc
// 1. Make sure PlayMode is entered and the adapter is wired.
editor-application-set-state { "isPlaying": true }
ath-wait { "predicate": "playmode", "timeout_ms": 30000 }
ath-wait { "predicate": "adapter_ready", "timeout_ms": 5000 }
ath-wait { "predicate": "game_ready", "timeout_ms": 30000 }

// 2. Clear any sticky flags from a prior sequence.
ath-cmd { "command": "harness.reset" }

// 3. Fire the action you care about.
ath-cmd { "command": "player.kill" }

// 4. Wait for the convergent state. The bridge promoted OnPlayerDeath
//    to PlayerDiedSinceLastReset, which the predicate checks.
ath-wait { "predicate": "player_died", "timeout_ms": 5000 }

// 5. Query final state.
ath-state { "key": "last_run_recording_frames" }
```

## The #1 footgun: spurious `harness.reset`

The bridge's `*SinceLastReset` flags are **sticky** — once an event fires,
the flag stays true until you call `harness.reset` or the scene unloads.

This is by design: it makes waits race-resistant. An `ath-wait
player_died` after `player.kill` will converge even if `OnPlayerDeath`
fired before the wait subscribed to anything.

But it cuts both ways. If you call `harness.reset` *between* the action
and the wait, you'll wipe the just-set flag and the wait will time out
mistakenly.

```jsonc
// WRONG — the reset between restart and wait clears the just-set flag.
ath-cmd  { "command": "world.restart" }   // fires OnPlayerRestart →
                                          // SpawnPlayer respawns → 
                                          // OnPlayerSpawn fires → 
                                          // PlayerSpawnedSinceLastReset = true
ath-cmd  { "command": "harness.reset" }   // ← wipes the flag we want to wait on
ath-wait { "predicate": "player_spawned" }// times out
```

```jsonc
// RIGHT — reset BEFORE the action, not between action and wait.
ath-cmd  { "command": "harness.reset" }
ath-cmd  { "command": "world.restart" }
ath-wait { "predicate": "player_spawned" } // converges
```

Rule of thumb: `harness.reset` is a **prelude** to a sequence, not a
divider within one.

## Built-in state keys

The package's `AthStateDispatcher` handles these without consulting the
adapter's `TryGetCustomState`:

```
in_gameplay, scene_name, host_name, package_version,
bridge_ready, adapter_ready, game_ready,
player_alive, player_holding_seed, player_position, player_velocity,
spawn_attempts, is_paused,
ghost_active, ghost_seed_active,
last_run_recording_frames, last_seed_recording_frames,
player_died_since_reset, player_spawned_since_reset,
goal_reached_since_reset, seed_consumed_since_reset,
last_async
```

Anything else falls through to the adapter — `Status="unknown_key"`
with `CustomStateAttempted=false` distinguishes "no adapter registered"
from "adapter doesn't recognize this key."

## Built-in wait predicates

```
playmode, scene_loaded:<name>, in_gameplay, game_ready, adapter_ready,
player_died, player_spawned, player_alive,
goal_reached, seed_consumed, paused_equals:<bool>, ghost_active,
async_done:<id>, state_equals:<key>=<value>,
log_match:<regex>, spawn_attempts_at_least:<int>
```

`state_equals:<key>=<value>` lets you wait on *any* state key reaching a
specific value — including host-custom keys via `TryGetCustomState`. This
is the predicate to reach for when no dedicated predicate fits.

## Correlation IDs and the `CMD:`/`OK:`/`ERR:` sentinels

Every `ath-cmd`-driven command flows through this pattern:

1. `Tool_AthCmd` appends an 8-char N-format guid to the command line.
2. It subscribes `Application.logMessageReceived` and filters lines by
   `id={correlation-id}` substring.
3. It synchronously calls `DebugLogConsole.ExecuteCommand("<cmd> <id>")`.
4. The console-side command (the dual-overload pattern's id-bearing form)
   emits:
   - `CMD:<name> id=<id> args=...` on entry
   - `OK:<name> id=<id> ...` on success
   - `ERR:<name> id=<id> reason=...` on failure
   - `OK:<name> id=<id> [dispatched]` for fire-and-forget async ops
     (caller follows up with `/ath-wait async_done:<id>`)
5. `Tool_AthCmd` spin-waits up to 250 ms for `OK:` or `ERR:` delivery,
   then classifies and returns a `Result` with status, captured lines,
   and timing.

The correlation id is what makes overlapping `ath-cmd` calls non-interfering.
Each call only captures *its* lines.

For commands that don't define an id-bearing overload (anything from
outside the harness's dual-overload commands), pass `tagId: false` so
the id isn't appended and no log capture is attempted. The result is
"the command was dispatched without throwing."

## Diagnostic richness on timeout

`ath-wait`'s `Result` returns enough state to triage a timeout without
re-running the smoke:

```jsonc
{
  "Predicate":            "state_equals:ghost_active=true",
  "Arg":                  "ghost_active=true",
  "Satisfied":            false,
  "Status":               "timeout",
  "ElapsedMs":            30000,
  "LastEvaluatedValue":   "key=ghost_active;want=\"true\";got=\"false\";status=ok",
  "PlaymodeOnExit":       true,
  "BridgeReadyOnExit":    true,
  "AdapterReadyOnExit":   true
}
```

`LastEvaluatedValue` tells you what the predicate was actually seeing.
`PlaymodeOnExit` / `BridgeReadyOnExit` / `AdapterReadyOnExit` tell you
whether the *substrate* held up across the wait — if the bridge or
adapter dropped out mid-wait, that's likely the proximate cause and you
should look at scene reloads, host singleton lifecycle, or the bootstrap.

## Writing a new smoke skill

See `Skills/README.md` for the authoring conventions. The shortest version:

1. Create `Skills/<skill-name>/SKILL.md` in the package repo.
2. Add YAML frontmatter with `name`, `description`, and `version`.
3. Step 0 is always a fail-fast version check.
4. Step 1 enters PlayMode and waits for readiness.
5. Subsequent steps clear with `harness.reset` *first*, then act, then
   wait, then assert.
6. A "PASS criteria" block enumerates every assertion.
7. A "Failure handling" block describes screenshots + state dumps.

When you bump the package version, bump the frontmatter `version` field
of every smoke skill in the same commit.

## Common operations cheat sheet

```jsonc
// Enter PlayMode + reach gameplay
editor-application-set-state { "isPlaying": true }
ath-wait { "predicate": "playmode",      "timeout_ms": 30000 }
ath-wait { "predicate": "scene_loaded:<scene-name>", "timeout_ms": 30000 }
ath-wait { "predicate": "game_ready",    "timeout_ms": 30000 }

// Exit PlayMode
editor-application-set-state { "isPlaying": false }

// Fire any harness-aware command
ath-cmd { "command": "<cmd> <args>" }

// Fire a non-harness console command (no correlation-id overload)
ath-cmd { "command": "<legacy-cmd>", "tagId": false }

// Capture state with adapter introspection
let s = ath-state { "key": "<key>" }
// s.Status, s.Value, s.AdapterPresent, s.BridgePresent, s.CustomStateAttempted

// Wait for an arbitrary key to reach a specific value
ath-wait { "predicate": "state_equals:<key>=<value>", "timeout_ms": 5000 }

// Wait for a regex match in any log line
ath-wait { "predicate": "log_match:^Player .* died", "timeout_ms": 5000 }

// Track an async-dispatched op (when a command emits OK:... [dispatched])
ath-wait { "predicate": "async_done:<correlation-id>", "timeout_ms": 30000 }
```
