# Changelog

All notable changes to the AI Test Harness package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Scaffolded the empty package: `package.json`, `README.md`, `LICENSE.md`, `.gitignore`, `CHANGELOG.md`.
- Phase 1 runtime core: `IAthHostAdapter`, `AthHostEvents`, `AthServices`, `AthBridge` (DontDestroyOnLoad singleton with edge-sticky flags + 16-slot async ring buffer + validity-guarded `GameReady`), `AthBootstrap` (RuntimeInitializeOnLoadMethod), `AthAsyncOpRecord`, `AthRuntimeFlag`, `Logging/AthLog` (CMD/OK/ERR sentinel helpers). All file-gated by `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Phase 2 adapter contract doc (`Documentation~/adapter-contract.md`) and `Samples~/MinimalHostAdapter/` (no-op adapter + one-line bootstrap, asmdef references only Runtime). Sample declared in `package.json` so it appears in Package Manager Samples.
- Phase 3 console-transport asmdef (`LlamaBrainLabs.Ath.Commands`, references `IngameDebugConsole.Runtime` by name) and project-agnostic commands: `test.echo`, `test.scene`, `test.bridge_ready`, `test.game_ready`, `test.adapter_name`, `test.last_async`, `harness.reset`, `harness.ping`, `harness.set_log_level`, `lifecycle.in_gameplay`, `lifecycle.scene`. All dual-overload (user-typed form generates correlation id and forwards to id-bearing form); all emit `CMD:` on entry and `OK:`/`ERR:` on exit. Confirmed `[ConsoleMethod]` auto-discovery picks up commands from a `Packages/`-located asmdef — no fallback `DebugLogConsole.AddCommand` registration needed.
- Phase 4 Editor MCP tools: `LlamaBrainLabs.Ath.Editor` asmdef (Editor-only, `UNITY_MCP_READY` versionDefine on `com.ivanmurzak.unity.mcp`, precompiled refs `ReflectorNet.dll`/`McpPlugin.dll`/`McpPlugin.Common.dll`) + three `[McpPluginToolType]` partial classes: `Tool_AthCmd` (correlation-id log-capture + sentinel parser, identifies `[dispatched]` async marker), `Tool_AthState` (delegates to `AthStateDispatcher`, returns rich shape with `AdapterPresent`/`BridgePresent`/`CustomStateAttempted` diagnostics), `Tool_AthWait` (synchronous 100ms poll for 16 predicates including `state_equals:<key>=<value>` and `spawn_attempts_at_least:<int>`, returns `LastEvaluatedValue` + on-exit playmode/bridge/adapter flags). All `MainThread.Run`-marshaled; `Tool_AthWait` deliberately sync (the MCP serializer marks async returns as "Pending"). Tools registered as `/ath-cmd`, `/ath-state`, `/ath-wait`.

## [0.1.0-preview.1] — TBD

Initial scaffolding pre-release. Not yet functional; tracks the Phase 0 baseline before runtime, editor, or skills layers are implemented.
