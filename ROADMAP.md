# AI Test Harness roadmap

Historical roadmap snapshot from ATH dogfooding against BeforeTheShade. Current workflow authoring and acceptance follow `Documentation~/runtime-smoke-usage.md` and `Documentation~/runtime-smokes.md`. The skill, template, and command/wait smoke proposals below are historical; new acceptance belongs in host-owned RuntimeSmoke workflows.

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

### Phase 7 — Legacy procedural smoke guidance (historical)
An earlier package skill described the BTS death-rewind-and-finish loop and emitted
trace context. That procedure was host-specific and its command, state, wait, and
marker sequence is not current smoke acceptance guidance. RuntimeSmoke workflows
and their structured results now own acceptance. The historical dogfooding findings
remain useful context, but do not establish a current workflow or gate.

### Trace emitter + nerve-center extraction (`v0.2.0`)
The optional `ath-trace-emit` editor MCP tool can append a legacy `ath.smoke.completed` trace-context event to the consuming project's `.captain-sdlc/trace/YYYY-MM-DD.jsonl` (Captain SDLC Seam 1; envelope in the captain-sdlc repo's `trace-schema.md`). The field named `result` is caller-supplied context and is not RuntimeSmoke output or acceptance evidence. The historical procedural skill used this event; current workflows do not require it. Separately, the Captain SDLC nerve-center docs were extracted from this package to `LlamaBrain/captain-sdlc` (TD-001) — the package no longer ships them.

## Later (historical backlog; RuntimeSmoke guidance supersedes smoke-skill proposals)

### Superseded: host-owned RuntimeSmoke workflows
The former plan to move `ath-smoke-fullloop` into a BTS plugin and keep procedural smoke skills/templates in this package is historical. Current hosts own checked-in RuntimeSmoke workflows, while AITH owns the generic coordinator, transports, Editor controls, and suite runner. Use `Documentation~/runtime-smoke-usage.md` for authoring and execution. The retired skill notices remain historical references; do not create another command/wait acceptance skill or use this roadmap section as current operating guidance.

### Phase 8 — Fresh-project minimal-adapter boundary check
Install the package into a clean Unity project, wire only `Samples~/MinimalHostAdapter`, and confirm the adapter transport and typed state contract work without host-specific code. This boundary check validates that Runtime does not depend on host Commands/Editor code and that `autoReferenced` works in a project with no host `Assets/` code. It does not establish RuntimeSmoke acceptance; use the checked-in host workflow and shared RuntimeSmoke runner for that.

### ATH gets its own Claude Code setup
The package has historically been dogfooded from BTS's working directory — its `.claude/` settings, MCP server config, skills, hooks, and CLAUDE.md memory live there. This roadmap proposal is historical and does not describe current package or BTS validation status. If revisited, package-development guidance should be scoped to AITH implementation work and must not add procedural smoke-acceptance skills or templates. AITH and BTS retain their own setup; they meet at the package protocol boundary.

### Player-build perf monitoring (investigation)
Companion question to editor-first perf instrumentation: should ATH also surface perf samples from `DEVELOPMENT_BUILD` players, not just Editor playmode? Data collection is the easy half — `UnityEngine.Profiling.Profiler`, `ProfilerRecorder` (Unity 2020.2+), and frame timing all work in dev builds, and the existing `#if UNITY_EDITOR || DEVELOPMENT_BUILD` file gate is exactly the right shape. The open question is transport: the AI agent → Editor MCP → Runtime chain is Editor-only (`LlamaBrainLabs.Ath.Editor.asmdef` has `includePlatforms: ["Editor"]`), so a running player build has no path to surface samples to a smoke. Options worth weighing when this comes up:

- **Offline dump.** Sampler writes to a file during the run; smoke reads it after the player exits. Cheap, but loses `ath-wait` semantics — assertions only fire at end-of-run.
- **Side-channel transport.** IngameDebugConsole already runs in builds; pair it with a local HTTP / named-pipe sink on the player and a thin MCP shim on the host that reads from it. Real-time, but a new transport surface to design, version, and maintain.
- **Editor-attached play.** Unity's Profiler attaches to a running player over its own network protocol; a smoke could drive `ProfilerDriver` from the Editor against a connected build. Reuses Unity's machinery, but the Editor still has to be present — which partially defeats the point of player-build coverage.

The decision worth making before any of this is *what player-build perf is for*: CI regression catches (offline is enough) or interactive perf debugging from a live smoke (which is what justifies new transport). Editor-first coverage probably absorbs most of the regression-catching value, so this likely stays in "later" for a while.
