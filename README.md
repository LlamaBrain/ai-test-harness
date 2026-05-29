# AI Test Harness

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com)

By **Michael Tiller** (<contact@michaeltiller.com>) · a [LlamaBrain](https://github.com/LlamaBrain) project, part of the [Captain SDLC](https://github.com/LlamaBrain/captain-sdlc) pipeline.

A reusable Unity package providing a three-layer MCP-driven dev test harness, mirroring the [dirigible2D pattern](https://metagrue.com/2026/05/25/ai-test-harness/).

> **Status:** v0.2.0 — stable. The editor MCP tools (`ath-cmd`, `ath-state`, `ath-wait`, `ath-trace-emit`), the in-game command surface, the host adapter contract, and the smoke pipeline are functional and dogfooded against BeforeTheShade.

## What it is

Three layers:

1. **In-game runtime** — `IngameDebugConsole` commands with `CMD:`/`OK:`/`ERR:` sentinels tagged with correlation IDs. Strips from release builds via `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.
2. **Editor MCP tools** — `ath-cmd`, `ath-state`, `ath-wait`, `ath-trace-emit`. Synchronous; correlation-id-filtered log capture, state queries, predicate waits, and Captain SDLC trace emission.
3. **Claude `SKILL.md` workflows** — composed smoke tests that orchestrate the three tools.

Hosts implement `IAthHostAdapter` to expose their game state and lifecycle events to the harness. The package never references host types.

## Compatibility

| Component             | Status       | Verified                                                                                            |
|-----------------------|--------------|-----------------------------------------------------------------------------------------------------|
| Unity                 | required     | 6000.3.10f1                                                                                         |
| Unity MCP             | hard dep     | `com.ivanmurzak.unity.mcp@0.76.0` (declared in `package.json`)                                      |
| IngameDebugConsole    | hard peer    | yasirkula's `IngameDebugConsole.Runtime` asmdef. NOT in `package.json`. **Package will not compile without it.** |
| Host project          | required     | works with default `Assembly-CSharp` via `autoReferenced` on the package's Runtime asmdef           |

## Installing (host project)

### 1. Configure the OpenUPM scoped registry

Edit your host project's `Packages/manifest.json` to include:

```json
"scopedRegistries": [
  { "name": "OpenUPM (ivanmurzak)", "url": "https://package.openupm.com",
    "scopes": ["com.ivanmurzak"] },
  { "name": "OpenUPM (yasirkula)", "url": "https://package.openupm.com",
    "scopes": ["com.yasirkula"] }
]
```

Packages cannot bring their own scoped registries; this is your responsibility as the host.

### 2. Install peer dependencies

- **Unity MCP** — resolves automatically once the registry is configured.
- **IngameDebugConsole** — install `com.yasirkula.ingamedebugconsole` from OpenUPM. Verify the imported package contains the `IngameDebugConsole.Runtime` asmdef. (If you already have IngameDebugConsole imported as an asset under `Assets/Plugins/IngameDebugConsole/`, that's fine too — the asmdef name is the same.)

### 3. Install AI Test Harness

Add to `Packages/manifest.json`:

```json
"com.llamabrainlabs.ai-test-harness": "file:../../ai-test-harness"
```

(Or pin a tagged release via git: `"https://github.com/LlamaBrain/ai-test-harness.git#v0.2.0"`.)

### 4. Implement the adapter

Create a class that implements `IAthHostAdapter` in your project (e.g., under `Assets/Game/Source/Testing/`). See `Documentation~/adapter-contract.md` for the full contract. A no-op example is available via Package Manager > Samples > "Minimal Host Adapter".

### 5. Register the adapter at startup

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void RegisterAth()
{
    LlamaBrainLabs.Ath.AthServices.Register(new MyAthAdapter());
}
```

### 6. Install the smoke skills

The package ships its Claude smoke-test skills under `Skills/<name>/SKILL.md`. Manually copy or symlink the relevant skill folders into your host project's `.claude/skills/` directory. (Future improvement: an editor menu item to sync these automatically.)

## What's in here

- `Runtime/` — core (`IAthHostAdapter`, `AthServices`, `AthBridge`) + `Commands/` (package-supplied `[ConsoleMethod]` commands).
- `Editor/McpSkills/` — `Tool_AthCmd`, `Tool_AthState`, `Tool_AthWait`, `Tool_AthTraceEmit` (+ pure `AthTraceWriter`/`AthTraceEmitter`).
- `Skills/` — Claude `SKILL.md` workflows.
- `Samples~/MinimalHostAdapter/` — no-op adapter for fresh-project smoke testing.
- `Documentation~/` — adapter contract (`adapter-contract.md`), operator guide (`using-ath.md`), trace events (`trace-events.md`).

## Architecture notes

The package never references host types. All host data flows through `IAthHostAdapter`. The runtime/commands asmdef split is an architectural seam for future transport swap-out — v0.2 still requires IngameDebugConsole as a peer dep.

ATH is the QA tool in the broader Captain SDLC pipeline. See the nerve center ([`LlamaBrain/captain-sdlc`](https://github.com/LlamaBrain/captain-sdlc)) for the cross-tool design, the trace schema, and the roadmap.
