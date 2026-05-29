# Changelog

All notable changes to the AI Test Harness package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-05-29

### Added
- Captain SDLC trace emitter (Seam 1, milestone M2). New editor MCP tool
  `ath-trace-emit` appends one `ath.smoke.completed` event to the consuming
  project's `.captain-sdlc/trace/YYYY-MM-DD.jsonl` (append-only JSON Lines,
  LF-terminated, UTF-8 no-BOM), matching the envelope in
  the captain-sdlc repo's `trace-schema.md`. The tool owns record correctness — it mints a
  UUIDv4 `event_id`, stamps an ISO-8601 UTC `timestamp`, and pins
  `schema_version: 1` / `tool: "ath"` / `tool_version` = `AthRuntimeFlag.PackageVersion`;
  callers pass only the verdict + context. The trace directory resolves to the
  project root (parent of `Application.dataPath`), not the package, and the
  emitter lazily writes `.captain-sdlc/.gitignore` (`trace/`, `side-store/`) so
  traces are never committed. Serialization (`AthTraceWriter`) and IO
  (`AthTraceEmitter`) are split into pure, dependency-free units (no Newtonsoft —
  the Editor asmdef pins a closed reference set) that are unit-testable without
  an MCP attachment, mirroring the `AthStateDispatcher` split. The
  `ath.smoke.completed` payload schema is documented in
  `Documentation~/trace-events.md`. `ath-smoke-fullloop` SKILL gains a Step 8
  that emits the trace on both pass and fail before PlayMode exit. Perf
  "envelope summary", `parents`/`links`, and additional event kinds are
  deferred (see the M2 milestone doc). Verified in the BeforeTheShade editor
  (compile + a live pass/fail emit).

### Changed
- MCP tool invocation is now documented as `unity-mcp-cli run-tool <tool> --input '<json>'`
  across the smoke SKILL, `using-ath.md`, and `trace-events.md`. The `/ath-*`
  slash form is flagged as conditional on `unity-mcp-cli setup-skills`, with the
  "Unknown skill" error and its fix called out.

### Removed
- The Captain SDLC nerve-center docs (`captain-sdlc/`) no longer ship in the
  package — extracted to their own repo (`LlamaBrain/captain-sdlc`) along with
  the 76 Unity `.meta` files they had generated and the interrogate config. The
  package now contains only the ATH runtime, editor tools, skills, and
  `Documentation~/` (resolves TD-001).

## [0.1.0] - 2026-05-29

**First stable release.** Graduated from the `0.1.0-preview` series. The editor MCP tools (`ath-cmd`, `ath-state`, `ath-wait`), the in-game IngameDebugConsole command surface, the host adapter contract (`IAthHostAdapter` + `AthBridge` + `AthServices`), and the Phase 7 smoke pipeline are committed as the stable v0.1.0 API surface. Dogfooded against BeforeTheShade; ready for additional host projects.

### Changed
- `Tool_AthWait` and `Tool_AthCmd` `[Description]` text extended with a triage clause directing callers to fetch `/console-get-logs` before retrying when ath-wait returns `Status=timeout` or ath-cmd returns `Status=failed` with `FailReason=no_response`/`command_error` (and whenever the bridge appears stuck). Editor-side exceptions — NullRef in a predicate path, missing scene actor, mid-playmode compile error, dispatched-command throws — surface in the Unity console but not in the tool result, so silent retries burn turns. The `exception:` FailReason path already carries the root cause and is called out as not needing the log fetch. No code-path change; description-only update propagates into generated SKILL.md on next `unity-skill-generate` regen.
- Phase 7 smoke `ath-smoke-fullloop` Step 6 reworked from `player.tp 12.50 3.50` + `goal_reached` wait to `world.restart` (clears the Step-5 ghost which would otherwise crush the live player at spawn) followed by `world.goal` direct event fire. Real-traversal goal coverage moves to a future `/ath-smoke-traversal`. Frontmatter + `AthRuntimeFlag.PackageVersion` + `package.json#version` bumped to `0.1.0-preview.2` to keep the fail-fast version pre-flight aligned. Discovered through dogfooding the smoke against BTS — Unity's `OnTriggerEnter2D` does not fire when a body teleports already overlapping a trigger, and `GoalObject.OnTriggerEnter2D` additionally gates on `IsHoldingSeed` (gameplay correctness, not harness coverage).

### Added
- Scaffolded the empty package: `package.json`, `README.md`, `LICENSE.md`, `.gitignore`, `CHANGELOG.md`.
- Phase 1 runtime core: `IAthHostAdapter`, `AthHostEvents`, `AthServices`, `AthBridge` (DontDestroyOnLoad singleton with edge-sticky flags + 16-slot async ring buffer + validity-guarded `GameReady`), `AthBootstrap` (RuntimeInitializeOnLoadMethod), `AthAsyncOpRecord`, `AthRuntimeFlag`, `Logging/AthLog` (CMD/OK/ERR sentinel helpers). All file-gated by `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Phase 2 adapter contract doc (`Documentation~/adapter-contract.md`) and `Samples~/MinimalHostAdapter/` (no-op adapter + one-line bootstrap, asmdef references only Runtime). Sample declared in `package.json` so it appears in Package Manager Samples.
- Phase 3 console-transport asmdef (`LlamaBrainLabs.Ath.Commands`, references `IngameDebugConsole.Runtime` by name) and project-agnostic commands: `test.echo`, `test.scene`, `test.bridge_ready`, `test.game_ready`, `test.adapter_name`, `test.last_async`, `harness.reset`, `harness.ping`, `harness.set_log_level`, `lifecycle.in_gameplay`, `lifecycle.scene`. All dual-overload (user-typed form generates correlation id and forwards to id-bearing form); all emit `CMD:` on entry and `OK:`/`ERR:` on exit. Confirmed `[ConsoleMethod]` auto-discovery picks up commands from a `Packages/`-located asmdef — no fallback `DebugLogConsole.AddCommand` registration needed.
- Phase 4 Editor MCP tools: `LlamaBrainLabs.Ath.Editor` asmdef (Editor-only, `UNITY_MCP_READY` versionDefine on `com.ivanmurzak.unity.mcp`, precompiled refs `ReflectorNet.dll`/`McpPlugin.dll`/`McpPlugin.Common.dll`) + three `[McpPluginToolType]` partial classes: `Tool_AthCmd` (correlation-id log-capture + sentinel parser, identifies `[dispatched]` async marker), `Tool_AthState` (delegates to `AthStateDispatcher`, returns rich shape with `AdapterPresent`/`BridgePresent`/`CustomStateAttempted` diagnostics), `Tool_AthWait` (synchronous 100ms poll for 16 predicates including `state_equals:<key>=<value>` and `spawn_attempts_at_least:<int>`, returns `LastEvaluatedValue` + on-exit playmode/bridge/adapter flags). All `MainThread.Run`-marshaled; `Tool_AthWait` deliberately sync (the MCP serializer marks async returns as "Pending"). Tools registered as `/ath-cmd`, `/ath-state`, `/ath-wait`.
- Phase 7 smoke skill + operator docs: `Skills/ath-smoke-fullloop/SKILL.md` (8-step BTS full-loop smoke with fail-fast version pre-flight, real scene coordinates baked in for spawn/seed-spawn/goal, "do not harness.reset between action and wait" footgun warning at the restart step), `Skills/README.md` (copy/symlink instructions + version-verification convention), `Documentation~/using-ath.md` (operator's guide covering the three tools, basic loop, the #1 footgun, state keys, predicates, correlation-id sentinel pattern, diagnostic richness, common-ops cheat sheet). New `AthRuntimeFlag.PackageVersion` const + `package_version` state key so skills can fail-fast on stale `.claude/skills/` copies.

## [0.1.0-preview.1] — TBD

Initial scaffolding pre-release. Not yet functional; tracks the Phase 0 baseline before runtime, editor, or skills layers are implemented.
