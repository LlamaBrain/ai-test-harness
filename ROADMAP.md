# AI Test Harness roadmap

Planned work, in rough priority order. None of these block the package's current use; they're improvements that came out of dogfooding against BeforeTheShade.

## Shipped

### Phase 1 — Runtime core (`v0.1.0-preview.1`)
`IAthHostAdapter` portability seam, `AthHostEvents`, `AthServices` (Register/Unregister with idempotent semantics), `AthBridge` (DontDestroyOnLoad singleton, edge-sticky `*SinceLastReset` flags, 16-slot async ring buffer, validity-guarded `GameReady`), `AthBootstrap` (`RuntimeInitializeOnLoadMethod`), `AthAsyncOpRecord`, `AthRuntimeFlag`, `Logging/AthLog` (CMD/OK/ERR sentinel helpers). All file-gated by `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

### Phase 2 — Adapter contract + minimal sample (`v0.1.0-preview.1`)
`Documentation~/adapter-contract.md` plus `Samples~/MinimalHostAdapter/` (no-op `IAthHostAdapter` + one-line bootstrap, asmdef references only Runtime). Sample declared in `package.json` so it appears in Package Manager Samples.

### Phase 3 — Console-transport commands (`v0.1.0-preview.1`)
`LlamaBrainLabs.Ath.Commands` asmdef referencing `IngameDebugConsole.Runtime` by name. Project-agnostic commands: `test.echo`, `test.scene`, `test.bridge_ready`, `test.game_ready`, `test.adapter_name`, `test.last_async`, `harness.reset`, `harness.ping`, `harness.set_log_level`, `lifecycle.in_gameplay`, `lifecycle.scene`. Dual-overload pattern (user-typed form generates correlation id and forwards to id-bearing form). Confirmed `[ConsoleMethod]` auto-discovery picks up commands from a `Packages/`-located asmdef — no fallback `DebugLogConsole.AddCommand` registration needed.

### Phase 4 — Editor MCP tools (`v0.1.0-preview.1`)
`LlamaBrainLabs.Ath.Editor` asmdef (Editor-only, `UNITY_MCP_READY` versionDefine on `com.ivanmurzak.unity.mcp`). Three `[McpPluginToolType]` partial classes:
- `Tool_AthCmd` — correlation-id log capture + sentinel parser, identifies `[dispatched]` async marker.
- `Tool_AthState` — delegates to `AthStateDispatcher`, returns rich shape with `AdapterPresent` / `BridgePresent` / `CustomStateAttempted` diagnostics.
- `Tool_AthWait` — synchronous 100ms-poll for 16 predicates including `state_equals:<key>=<value>` and `spawn_attempts_at_least:<int>`, returns `LastEvaluatedValue` + on-exit playmode/bridge/adapter flags.

All `MainThread.Run`-marshaled. `Tool_AthWait` deliberately sync (the MCP serializer marks async returns as "Pending"). Tools registered as `/ath-cmd`, `/ath-state`, `/ath-wait`.

### Phase 7 — Smoke skill + operator docs (`v0.1.0-preview.2`)
`Skills/ath-smoke-fullloop/SKILL.md` walks the BTS death-rewind-and-finish loop end-to-end (spawn, kill, ghost replay materialization, goal event fire, restart cleanliness). `AthRuntimeFlag.PackageVersion` + `package_version` state key let skills fail-fast on stale `.claude/skills/` copies. Step 6 fires `world.goal` directly (the host-side test affordance, mirroring `player.kill`'s pattern); real-traversal goal coverage moves to `/ath-smoke-traversal` below. `Skills/README.md` covers distribution; `Documentation~/using-ath.md` is the operator's guide.

Dogfooding caught three real BTS bugs (respawn race, teleport read-back stale, ghost cleanup edge case) — the smoke working as designed.

### Trace emitter + nerve-center extraction (`v0.2.0`)
`ath-trace-emit` editor MCP tool emits one `ath.smoke.completed` event per smoke run to the consuming project's `.captain-sdlc/trace/YYYY-MM-DD.jsonl` (Captain SDLC Seam 1; envelope in the captain-sdlc repo's `trace-schema.md`). Pure `AthTraceWriter` serializer + `AthTraceEmitter` IO, no JSON dependency; the smoke SKILL's Step 8 emits on pass and fail. Verified against BTS (compile + live emit). Separately, the Captain SDLC nerve-center docs were extracted from this package to `LlamaBrain/captain-sdlc` (TD-001) — the package no longer ships them.

## Later

### Extract `ath-smoke-fullloop` out of the package (portability fix)
The skill currently lives at `Skills/ath-smoke-fullloop/SKILL.md` and references `world.restart`, `player.kill`, `world.goal`, scene coordinates `(−4.50, 0.50)` / `(12.50, 3.50)`, `BtsAthBootstrap`, `Game.Testing.BtsAthBootstrap` — all BTS-specific. It is BTS knowledge masquerading as a package-shipped artifact, the same kind of leak that the Runtime asmdef carefully avoids. The fix is to move it (and any future host-specific smoke) to a BTS-side bridge plugin (proposed name `bts-ath-bridge`, living at `BeforeTheShade/.claude/plugins/bts-ath-bridge/`), leaving the ATH package with only:

- A host-agnostic authoring template / skeleton SKILL.md.
- A minimal-adapter dry-run smoke that runs entirely against `Samples~/MinimalHostAdapter` and asserts nothing about gameplay (overlaps with Phase 8 below — they're the same artifact).

Bump the version and CHANGELOG the move. Hosts that already copied the BTS-coupled SKILL into their `.claude/skills/` will fail-fast on the version pre-flight, which is the right behavior.

### Phase 8 — Fresh-project minimal-adapter dry-run
Install the package into a clean Unity project, wire only `Samples~/MinimalHostAdapter`, and confirm `/ath-cmd { harness.ping }` round-trips and `/ath-state { adapter_ready }` returns `true` without any host-specific code. Tests the package boundary — that Runtime really doesn't leak into Commands/Editor and that `autoReferenced` works on a project with no `Assets/` code yet. Independent of BTS gameplay findings.

### `/ath-smoke-traversal` skill
The fullloop smoke fires `world.goal` directly because Unity's `OnTriggerEnter2D` doesn't fire on teleport-in and (in BTS) the goal additionally gates on `IsHoldingSeed`. That's correct gameplay but loses coverage of the *traversal* path. A traversal smoke would simulate input (`PlayerInput` or `InputSystem.QueueStateEvent`) to drive the live player from spawn → seed pickup → goal trigger, asserting the full chain rather than the event-fire shortcut. Hosts that don't expose a `world.goal`-equivalent affordance would lean on this skill.

### Ghost-cleanup race follow-up (BTS-side; surfaced by ATH smoke)
The Step 7 cleanliness assertions intermittently observe an orphan `GhostController` lingering after `world.restart` even though `SpawnPlayer.OnRestart` schedules `Destroy(currentGhost.gameObject)`. A subsequent `world.restart` resolves it. Smells like either Unity deferred-Destroy crossing an MCP-call boundary, or an OnPlayerRespawn subscriber order issue in BTS that spawns a ghost despite a null `lastRunRecording`. Worth a focused investigation — either harden the smoke (`ath-wait state_equals:ghost_active=false` rather than a bare state read) or fix the underlying BTS cleanup. The ATH-side fix is to make Step 7 robust either way.

### `ath-wait` predicate `state_changed:<key>` (signal-edge complement to `state_equals`)
`state_equals` polls until the value matches a target. The complement — "wait until this key's value changes from whatever it currently is" — is useful for "I just fired X, wait until Y reacts" cases where you don't know the destination value, only that it should move. Cheap to add; the polling skeleton in `Tool_AthWait` already supports per-predicate state.

### ATH gets its own Claude Code setup
Today the package is dogfooded from BTS's working directory — its `.claude/` settings, MCP server config, skills, hooks, and CLAUDE.md memory all live in BTS. That's fine while ATH is small, but as it matures the package will outgrow BTS-resident tooling: skills authored against ATH (not BTS gameplay), permissions scoped to the package's own asmdefs, a CLAUDE.md tuned to package-authoring concerns instead of platformer-feel feedback, and project memory that doesn't mix host findings with package design notes. Stand up `E:/Personal/ai-test-harness/.claude/` with its own settings, skills folder, and CLAUDE.md. BTS keeps its own setup; the two only meet at the file-protocol package reference.

### Cross-host smoke template
Once Phase 8 ships, codify what a host needs to author its own smoke: a SKILL.md frontmatter convention (`requires-host: <name>`, `requires-version: <semver>`), a checklist of which `ath-state` keys + commands the smoke depends on, and a copy-from-template flow. Lets a second host adopt ATH without re-reading BTS's `ath-smoke-fullloop` line by line.

### Player-build perf monitoring (investigation)
Companion question to editor-first perf instrumentation: should ATH also surface perf samples from `DEVELOPMENT_BUILD` players, not just Editor playmode? Data collection is the easy half — `UnityEngine.Profiling.Profiler`, `ProfilerRecorder` (Unity 2020.2+), and frame timing all work in dev builds, and the existing `#if UNITY_EDITOR || DEVELOPMENT_BUILD` file gate is exactly the right shape. The open question is transport: the AI agent → Editor MCP → Runtime chain is Editor-only (`LlamaBrainLabs.Ath.Editor.asmdef` has `includePlatforms: ["Editor"]`), so a running player build has no path to surface samples to a smoke. Options worth weighing when this comes up:

- **Offline dump.** Sampler writes to a file during the run; smoke reads it after the player exits. Cheap, but loses `ath-wait` semantics — assertions only fire at end-of-run.
- **Side-channel transport.** IngameDebugConsole already runs in builds; pair it with a local HTTP / named-pipe sink on the player and a thin MCP shim on the host that reads from it. Real-time, but a new transport surface to design, version, and maintain.
- **Editor-attached play.** Unity's Profiler attaches to a running player over its own network protocol; a smoke could drive `ProfilerDriver` from the Editor against a connected build. Reuses Unity's machinery, but the Editor still has to be present — which partially defeats the point of player-build coverage.

The decision worth making before any of this is *what player-build perf is for*: CI regression catches (offline is enough) or interactive perf debugging from a live smoke (which is what justifies new transport). Editor-first coverage probably absorbs most of the regression-catching value, so this likely stays in "later" for a while.

## Cross-cutting design notes

- **Skill frontmatter must pin `package_version`.** Every smoke skill embeds the live `AthRuntimeFlag.PackageVersion` it was authored against and fail-fasts in Step 0 if the live package reports something different. Prevents the silent skew that's inevitable when skills are copied into `.claude/skills/` and the package upgrades behind them.
- **Don't `harness.reset` between an action and the wait it gates.** RestartLevel and friends fire `OnPlayerRestart`→`OnRespawn` synchronously, flipping `PlayerSpawnedSinceLastReset` in the same frame; a spurious `harness.reset` clears the just-set flag and the wait times out. Documented in `using-ath.md` and in the SKILL footgun block, but it's load-bearing enough that every new skill author trips on it once.
- **The host adapter is the only mutation surface, and host knowledge belongs nowhere else in the package.** All state writes go through `IAthHostAdapter.Request*` methods. The package never reaches into host types. Host-specific affordances (`BtsAthAdapter.RequestPlayerGoal` is the current example) live on the concrete adapter and are invoked by host-side commands via `as <ConcreteAdapter>` casts — they don't pollute the interface. **Same rule for skills:** a SKILL.md that names host commands, host scene coordinates, or host-specific bootstrap classes belongs in a host-side bridge plugin, not in `Skills/` shipped from the package. The package ships *authoring templates* and *minimal-adapter dry-runs*; hosts ship their own smokes against their own scenes.
- **Synchronous MCP tools, async escape hatch.** `Tool_AthCmd` and `Tool_AthWait` block the MCP call; long-running operations fire-and-forget via the `[dispatched]` marker and the caller polls `async_done:<id>`. Don't make the tools themselves async — the MCP serializer marks `Task<T>` returns as "Pending" and the caller can't see the result.
- **Edge-sticky flags, not levels.** `*SinceLastReset` flags latch on event-fire and only clear via `harness.reset`. Level-state queries (`player_alive`, `ghost_active`) read live; edge queries read latched. This is the resolution to the "OnPlayerSpawn fired but I missed the moment" race.
