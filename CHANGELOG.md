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

## [0.1.0-preview.1] — TBD

Initial scaffolding pre-release. Not yet functional; tracks the Phase 0 baseline before runtime, editor, or skills layers are implemented.
