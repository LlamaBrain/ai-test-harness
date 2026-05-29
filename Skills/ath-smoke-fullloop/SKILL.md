---
name: ath-smoke-fullloop
description: End-to-end PlayMode smoke for the BeforeTheShade full death-rewind-and-finish loop. Asserts that the harness can spawn a player, kill it, observe ghost-replay materialization, fire the goal event, restart cleanly, and that all the relevant edge-flag state machinery converges within bounded timeouts. Closes the regression gap for ghost replay + restart cleanup in BTS v0.1+.
version: 0.1.0
---

# Full-loop smoke (death → ghost → finish → clean restart)

End-to-end smoke for BTS's death-rewind core loop. Exercises spawn, kill,
ghost spawn, goal touch, and post-restart cleanliness. Driven entirely
through `/ath-cmd`, `/ath-state`, `/ath-wait`.

## What this skill proves

1. The harness package is at the expected version on disk *and* the copied
   skill version matches (fail-fast version check before any other step).
2. PlayMode entry runs the bootstrap correctly: `[AthBridge]` appears in
   `DontDestroyOnLoad`, `BtsAthBootstrap` registers `BtsAthAdapter`,
   `WorldController` is alive in the `Game` scene.
3. `OnPlayerSpawn` fires once and the bridge promotes it to
   `PlayerSpawnedSinceLastReset` (so a wait can converge race-free).
4. `player.interact` walks the `PlayerController.FindNearbyInteractable`
   path: a seed in `interactRadius` is selected via the `IPlayerInteractable`
   contract (non-empty `GetUseLabel`, closest-by-anchor) and picked up via
   `TryPickupSeed`. Closes the regression gap left by `world.goal`
   direct-fire, which bypasses both pickup and the goal collider.
5. `player.kill` invokes `OnPlayerDeath`, a `GhostController` (and
   `GhostSeedController`) materialize, and `LastRunRecording.Count > 0`.
6. `world.restart` clears all ghosts, zeroes `spawn_attempts` then
   re-increments to 1 from the new spawn, nullifies the recordings, and
   the live player ends up at the spawn point with velocity zero.

## When to run

- After modifying `WorldController` event invocation order or
  `RestartLevel()`.
- After modifying `GhostController.Initialize` / `GhostSeedController.Initialize`.
- After modifying any `*SinceLastReset` flag in `AthBridge`.
- Before stamping a BTS v0.1.x release.

## Preconditions

- Unity Editor running with BeforeTheShade project loaded.
- Unity MCP plugin enabled and reachable (`unity-mcp-cli ping` returns "pong").
- The Game scene is at `Assets/Game/Data/Scenes/Game.unity` and is the
  active scene.
- No compile errors.

## Step 0 — Version pre-flight (fail-fast)

```jsonc
// Frontmatter declares: version: 0.1.0
// Compare against the live package's runtime constant.
let live = ath-state { "key": "package_version" }
// expected: live.Value == "0.1.0"

if (live.Value != "0.1.0") {
  ABORT: stale skill copy.
  "Frontmatter declares version 0.1.0 but the live package
   reports " + live.Value + ". Re-copy
   E:/Personal/ai-test-harness/Skills/ath-smoke-fullloop/SKILL.md
   into <project>/.claude/skills/ath-smoke-fullloop/SKILL.md and
   re-invoke."
}
```

Do not proceed past Step 0 if the versions disagree.

## Step 1 — Enter PlayMode + readiness gates

```jsonc
editor-application-get-state {}
// if not playing:
editor-application-set-state { "isPlaying": true }

ath-wait { "predicate": "playmode",            "timeout_ms": 30000 }
ath-wait { "predicate": "scene_loaded:Game",   "timeout_ms": 30000 }
ath-wait { "predicate": "adapter_ready",       "timeout_ms":  5000 }
ath-wait { "predicate": "game_ready",          "timeout_ms": 30000 }
```

Each must return `Satisfied=true`. A `Status=timeout` here means either
PlayMode never started, the Game scene isn't the active scene, or
`BtsAthBootstrap` never ran (check `RuntimeInitializeOnLoadMethod`
attribute + ensure `Assets/Game/` compiles into Assembly-CSharp via the
package's `autoReferenced: true`).

## Step 2 — Baseline state + identity checks

```jsonc
ath-cmd   { "command": "harness.reset" }          // clear edge flags
let HOST  = ath-state { "key": "host_name" }       // expect "BeforeTheShade"
let SCENE = ath-state { "key": "scene_name" }      // expect "Game"
ath-state { "key": "adapter_ready" }               // expect "true"
ath-state { "key": "bridge_ready" }                // expect "true"

let SPAWN0 = ath-state { "key": "spawn_attempts" } // baseline (typically 1 after fresh PlayMode entry)
ath-state { "key": "player_alive" }                // expect "true"
ath-state { "key": "player_holding_seed" }         // expect "false"
ath-state { "key": "ghost_active" }                // expect "false"
ath-state { "key": "ghost_seed_active" }           // expect "false"
ath-state { "key": "seed_count" }                  // expect ">=1"
ath-state { "key": "last_run_recording_frames" }   // expect "0"
ath-state { "key": "last_seed_recording_frames" }  // expect "0"
```

If `HOST.Value != "BeforeTheShade"` or `SCENE.Value != "Game"`, abort —
the wrong project is loaded.

## Step 3 — Sanity round-trip

```jsonc
ath-cmd { "command": "test.echo pre-kill" }
// expect Status=success, OutputLines contains 'OK:test.echo' with echo="pre-kill"
ath-cmd { "command": "harness.ping" }
// expect OK:harness.ping pong=true
```

## Step 3.5 — Seed pickup round-trip (IPlayerInteractable path)

This step exists because `world.goal` direct-fire bypasses both
`PlayerController.FindNearbyInteractable` and `IsHoldingSeed` — without
this step the smoke is blind to regressions in label-eligibility
filtering, `GetUseLabel` returning null, `TryPickupSeed` rejection, or
anything else inside the `IPlayerInteractable` selection path.

```jsonc
ath-cmd  { "command": "harness.reset" }

// Teleport the seed onto the live player so the selection path has a
// candidate inside interactRadius. The exact coords here mirror the
// scene's spawn point (-4.50, 0.50). If you change the spawn, change
// this too.
ath-cmd  { "command": "seed.tp -4.5 0.5" }

// Fire one E-press via the public QueueInteract surface. Mirrors a real
// keyboard tap through the same UpdateInteract -> nearbyInteractable.Interact
// pathway, so the IPlayerInteractable contract is exercised end-to-end.
ath-cmd  { "command": "player.interact" }
// expect Status=success, OutputLines contains
// 'OK:player.interact queued=true label_before="E to Grab" holding_before=false'

ath-wait { "predicate": "state_equals:player_holding_seed=true", "timeout_ms": 5000 }
// expect Satisfied=true — pickup wired end-to-end

ath-state { "key": "player_holding_seed" }     // expect "true"
ath-cmd   { "command": "player.holding" }      // expect OK:player.holding holding=true
```

The kill cycle in Step 4 drops the held seed via `HandleDeath`, so the
remaining steps don't need an explicit drop here.

## Step 4 — Kill cycle

```jsonc
ath-cmd  { "command": "harness.reset" }
ath-cmd  { "command": "player.kill" }
// expect Status=success, OutputLines contains 'OK:player.kill fired=OnPlayerDeath'

ath-wait { "predicate": "player_died", "timeout_ms": 5000 }
// expect Satisfied=true, ElapsedMs<200

ath-state { "key": "last_run_recording_frames" }
// expect Value to parse as int > 0

ath-state { "key": "last_seed_recording_frames" }
// expect Value to parse as int > 0  (seed records frames whether held or not)
```

Optional screenshot for the failure-handling block:
`screenshot-game-view {}` → save as `fullloop_01_after_death.png`.

## Step 5 — Respawn + ghost materialization

```jsonc
ath-cmd  { "command": "harness.reset" }
ath-cmd  { "command": "player.respawn" }
// expect Status=success, OutputLines contains 'OK:player.respawn fired=OnPlayerRespawn'

ath-wait { "predicate": "player_spawned", "timeout_ms": 5000 }
// expect Satisfied=true

ath-wait { "predicate": "state_equals:ghost_active=true", "timeout_ms": 5000 }
// expect Satisfied=true — GhostController materialized from LastRunRecording

ath-wait { "predicate": "state_equals:ghost_seed_active=true", "timeout_ms": 5000 }
// expect Satisfied=true — GhostSeedController materialized from LastSeedRecording

ath-cmd { "command": "ghost.dump_recording" }
// expect OK:ghost.dump_recording frames>=1 summary="count=N first=(x,y) last=(x,y)"
```

Optional screenshot: `fullloop_02_ghost_replaying.png`.

## Step 6 — Goal event via direct fire

`world.goal` is the BTS test affordance that invokes `OnPlayerGoal`
directly, mirroring `player.kill`'s pattern. This bypasses the gameplay
gates — the goal trigger only fires for a `PlayerController` holding the
seed, AND Unity's `OnTriggerEnter2D` does not fire when an object
teleports already overlapping a trigger. Both constraints are gameplay
correctness, not harness coverage, so the smoke tests the event
mechanism directly.

```jsonc
// First restart so we test the goal fire from a clean live-player state
// (no ghost from Step 5 overlapping the spawn point and crushing the new
// player before we can act).
ath-cmd  { "command": "world.restart" }
ath-wait { "predicate": "player_spawned", "timeout_ms": 5000 }

ath-cmd  { "command": "harness.reset" }
ath-cmd  { "command": "world.goal" }
// expect Status=success, OutputLines contains 'OK:world.goal fired=OnPlayerGoal'

ath-wait { "predicate": "goal_reached", "timeout_ms": 5000 }
// expect Satisfied=true
```

Optional screenshot: `fullloop_03_goal_reached.png`.

**Note:** Real traversal-and-trigger goal coverage belongs in a future
`/ath-smoke-traversal` that drives the live run via input simulation.

## Step 7 — Restart + clean state

```jsonc
ath-cmd  { "command": "world.restart" }
// expect Status=success, OutputLines contains 'OK:world.restart restarted=true'

// IMPORTANT: do NOT issue harness.reset between world.restart and the
// wait below — RestartLevel fires OnPlayerRestart synchronously, which
// triggers the SpawnPlayer.OnRestart handler, which spawns a new player
// and flips PlayerSpawnedSinceLastReset in the same frame. A spurious
// harness.reset would clear the just-set flag and the wait would time
// out. (This is the #1 footgun the adapter contract warns about.)
ath-wait { "predicate": "player_spawned", "timeout_ms": 5000 }
// expect Satisfied=true

ath-state { "key": "ghost_active" }              // expect "false"
ath-state { "key": "ghost_seed_active" }         // expect "false"
ath-state { "key": "last_run_recording_frames" } // expect "0"
ath-state { "key": "last_seed_recording_frames" }// expect "0"
ath-state { "key": "is_paused" }                 // expect "false"
ath-state { "key": "spawn_attempts" }            // expect "1" (RestartLevel zeros it, then the new spawn increments to 1)
ath-state { "key": "player_alive" }              // expect "true"
ath-cmd   { "command": "player.pos" }            // expect player at spawn point (-4.50, 0.50) approximately
```

## Step 8 — Cleanup

```jsonc
editor-application-set-state { "isPlaying": false }
```

## PASS criteria

- Step 0 version check passes (live package_version == frontmatter version).
- Steps 1, 3.5, 4, 5, 6, 7 every `ath-wait` returns `Satisfied=true` inside its timeout.
- Step 3.5: `player_holding_seed=true` after `player.interact`.
- Step 4: `last_run_recording_frames` AND `last_seed_recording_frames` both > 0.
- Step 5: `ghost_active=true` AND `ghost_seed_active=true` after respawn.
- Step 6: `goal_reached` converges.
- Step 7: `ghost_active=false`, recordings cleared, `spawn_attempts=1`,
  player back at spawn coordinates.

## Failure handling

If any step fails, capture context before the user's PlayMode session
gets disturbed:

1. **Screenshots:**
   - `screenshot-game-view {}` → `fullloop_FAIL_gameview.png`
   - `screenshot-scene-view {}` → `fullloop_FAIL_sceneview.png`
2. **State dumps:**
   - `ath-cmd { "command": "ghost.dump_recording" }`
   - `ath-cmd { "command": "ghost.seed_dump_recording" }`
   - `ath-cmd { "command": "ghost.active" }`
   - `ath-cmd { "command": "player.pos" }`
   - `ath-cmd { "command": "world.spawn_attempts" }`
3. **Common diagnoses:**
   - `adapter_ready` times out → `BtsAthBootstrap.Register` didn't run.
     Check that `Game.Testing.BtsAthBootstrap` exists at
     `Assets/Game/Source/Testing/BtsAthBootstrap.cs`, the
     `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` attribute is intact,
     and that the package's Runtime asmdef has `autoReferenced: true`.
   - `player_died` times out → `RequestPlayerKill` adapter implementation
     didn't invoke `OnPlayerDeath`. Inspect `BtsAthAdapter.RequestPlayerKill`.
   - `player_holding_seed` stays false after `player.interact` → either
     `seed.tp` didn't land within `interactRadius` of the player, the
     seed's `IPlayerInteractable.GetUseLabel` returned null/empty (check
     `IsHeld` and `player.IsHoldingSeed` short-circuits), or
     `PlayerController.TryPickupSeed` rejected the call. Inspect the
     `OK:player.interact` line for `label_before` and `holding_before`,
     and grep recent console logs for `[seedfix]`-style diagnostic output.
   - `ghost_active` stays false after respawn → `GhostController` not
     spawned by the respawn path. Check `WorldController.OnPlayerRespawn`
     handler chain and `SpawnPlayer.OnRespawn`.
   - `last_run_recording_frames == 0` after death → recording not
     committed by `SetLastRunRecording`. Check `PlayerController.Recording`
     was non-empty at the moment of death and that `WorldController`
     called `SetLastRunRecording(...)` from its death handler.
   - `goal_reached` times out → teleport didn't land inside the goal
     collider, or the goal listener doesn't fire `OnPlayerGoal`. Verify
     `PlatformerGoal` trigger size, that the `Player` filter checks
     `PlayerController` (not just tag — known footgun), and consider
     whether `Physics2D.SyncTransforms()` is needed after the position
     write.
   - `world.restart` doesn't clear ghosts → `WorldController.RestartLevel`
     didn't tear down via the right path. That's the bug the smoke just
     caught.
4. **Do NOT** automatically exit PlayMode on failure — leave it open so
   the user can poke around.

## Notes

- Skill version: `0.1.0`. If you modify the package, bump
  both `package.json#version` AND the frontmatter above in the same
  commit.
- The teleport-onto-goal step is a shortcut around real platforming
  traversal. A future skill `/ath-smoke-traversal` should drive the live
  run via input simulation when traversal coverage is needed.
- The hard-coded goal `(12.50, 3.50)` and spawn `(-4.50, 0.50)`
  coordinates are scene-content-coupled to the current Game.unity. If
  the scene layout changes, update both the SKILL.md and the docs.
