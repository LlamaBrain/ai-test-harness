# Changelog

All notable changes to the AI Test Harness package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Footage capture for HITL-validation evidence (ADR-0014), two tiers that feed
  the trace `artifacts` field:
  - **Tier 1 — `ath-snap`** (editor MCP tool, no new dependency): captures a
    Game-view PNG under `<project>/.captain-sdlc/trace/media/` and returns a
    trace-relative `media/<file>` path for `ath-trace-emit`. Non-blocking —
    `action:"capture"` returns `pending` + a `CaptureId` (Unity writes the file
    at end-of-frame); `action:"query"` reports `ok` with width/height/size once
    the PNG lands (by `CaptureId`, or by trace-relative `path` after a domain
    reload). Never spins the main thread.
  - **Tier 2 — `ath-record`** (opt-in soft dependency on `com.unity.recorder`
    >= 4.0.0): records the Game view to mp4 via Unity Recorder; `start`/`stop`/
    `query` with session state persisted across calls. The real implementation
    lives in a separate `defineConstraints`-gated assembly
    (`LlamaBrainLabs.Ath.Editor.Recorder`, define `ATH_RECORDER`); a base-assembly
    stub returns `recorder_unavailable` when Recorder isn't installed, so the
    tool always exists and degrades cleanly. Recordings flush on PlayMode exit
    and before domain reload; `query` reports `Finalized` only once the mp4 size
    is stable across >=2 polls.
- `AthMediaUtil` — pure, unit-tested helpers (media dir, label sanitization,
  trace-relative path safety + resolution, PNG-header dimension read), mirroring
  the `AthTraceWriter`/`AthTraceEmitter` split.
- First EditMode test assembly (`LlamaBrainLabs.Ath.Editor.Tests`) with unit
  tests for the pure helpers.
- `ath-feature-demo` — a host-agnostic SKILL **template** that brackets a feature
  demonstration with `ath-record`/`ath-snap` and emits the trace with the footage
  attached. Host-specific smokes stay in the consuming project.

### Changed
- `ath-trace-emit` now validates each `artifacts` entry: only safe trace-relative
  paths are accepted (normally `media/<file>`); absolute/rooted, `..`, and
  backslash paths are rejected with `Status=bad_artifact` and no event is written.
- `AthTraceEmitter.EnsureGitignore` is now idempotent-append — it preserves an
  existing `.captain-sdlc/.gitignore` and adds only the missing `trace/` /
  `side-store/` lines (fixing a pre-existing-file gap), so captured media under
  `trace/media/` is reliably ignored.

### Note
- The Recorder integration (`ath-record`) and `ath-snap`'s in-editor capture
  behavior are **pending live-verify** in a real Unity Editor; the pure helpers
  are verified by unit tests. Footage capture is not yet released — it ships in a
  later version once the Recorder/OBS pieces are proven.

## [0.3.0] - 2026-06-16

### Added
- EXE remote-console harness (extends dirigible ADR-0031): an optional,
  off-by-default, developer-only tier that drives a built player over a
  `127.0.0.1` loopback socket using the existing IngameDebugConsole command
  vocabulary + `CMD:`/`OK:`/`ERR:` sentinels — to catch the build-only bug
  class (managed stripping, Obfuz, `#if UNITY_EDITOR` fallbacks, addressable
  trim) that is structurally invisible to the editor harness. Live-verified
  against a non-dev IL2CPP release build of BeforeTheShade.
  - In-player (`Runtime/RemoteConsole/`, gated `ATH_REMOTE`): `AthRemoteConsoleServer`
    (loopback listener, FIFO main-thread pump, one JSON response per connection),
    `AfterSceneLoad` bootstrap (opt-in via `-ath-remote-console`), pure `AthRemoteOptions`.
  - New runtime commands `harness.state` (shared `AthStateDispatcher`, relocated
    Editor→Runtime, + structured `async:<id>`) and `harness.snap` (in-player
    `ScreenCapture` PNG to the media dir).
  - Internal Node client `Tools~/ath-exe-client` (`ath-exe`): `cmd`/`state`/`wait`/
    `snap`/`launch`/`attach`; 26 `node --test` passing (incl. a fake-server round-trip).
  - Editor: Project Settings ▸ ATH Remote toggle for the `ATH_REMOTE` define; a
    package `link.xml` preserves the harness from IL2CPP managed stripping.
  - `Skills/ath-exe-smoke` host-agnostic built-player smoke template.

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
