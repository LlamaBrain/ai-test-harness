# AI Test Harness

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com)

By **Michael Tiller** (<contact@michaeltiller.com>) · a [LlamaBrain](https://github.com/LlamaBrain) project, part of the [Captain SDLC](https://github.com/LlamaBrain/captain-sdlc) pipeline.

A reusable Unity test harness with a host-neutral RuntimeSmoke runner and lower-level diagnostic transports, mirroring the [dirigible2D pattern](https://metagrue.com/2026/05/25/ai-test-harness/).

> **Status:** v0.6.0. This package implements the host-neutral RuntimeSmoke compiler, typed assertions, coordinator, Editor window, MCP operations, and EXE runner. Consumer-specific registration and end-to-end validation remain separate: Editor/player runs, human review, and exact-artifact build-gate acceptance must be reported from that consumer's own evidence.

Start with the [RuntimeSmoke usage and authoring guide](Documentation~/runtime-smoke-usage.md), then see the [RuntimeSmoke API contract](Documentation~/runtime-smokes.md). The older `ath-cmd`, `ath-state`, `ath-wait`, `ath-trace-emit`, direct EXE console tools, and procedural smoke skills remain low-level diagnostics; they do not provide an alternate smoke acceptance path. See the [AITH documentation migration audit](Documentation~/runtime-smoke-migration-audit.md).

## What it is

The package has a typed smoke runner plus lower-level diagnostic transports:

1. **RuntimeSmoke** — checked-in host workflows executed through the shared coordinator, structured results, and JSONL transcript. Host bridges provide declared game capabilities; AITH owns protocol and assertion semantics.
2. **Editor controls** — the RuntimeSmoke window and typed MCP operations expose the host registry's workflows and run state.
3. **Built-player runner** — `ath-exe runtime-smoke <spec.json>` watches one declared FSD workflow; `ath-exe runtime-smoke-suite <spec.json>` attests a fresh artifact copy, runs an ordered suite, and emits aggregate gate JSON/JUnit for development or `smoke_harness` builds.
4. **Diagnostics** — `ath-cmd`, `ath-state`, `ath-wait`, `ath-trace-emit`, and direct EXE remote-console commands help inspect a host. They are not smoke acceptance tools. Legacy procedural skills are retired as acceptance guidance.

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

(Or pin a tagged release via git after publication: `"https://github.com/LlamaBrain/ai-test-harness.git#v0.6.0"`.)

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

### 6. Add host RuntimeSmoke workflows

Author host-owned workflow JSON in the project's RuntimeSmokes tree, then use the shared guide and the host's quickstart to register and run those documents. The package's old procedural skills are retained only as retired references and must not be used for smoke acceptance. See [RuntimeSmoke usage and authoring](Documentation~/runtime-smoke-usage.md).

## What's in here

- `Runtime/` — host contract, diagnostic commands, and host-neutral `RuntimeSmokes/` protocol.
- `Editor/` — RuntimeSmoke window, typed MCP tools, and low-level diagnostic tools.
- `Tools~/ath-exe-client/` — direct diagnostics plus the structured `runtime-smoke` artifact runner.
- `Skills/` — legacy procedural guides retained as explicitly retired references; host workflows belong in the host's checked-in RuntimeSmoke documents.
- `Samples~/MinimalHostAdapter/` — no-op adapter for a fresh-project package-boundary diagnostic.
- `Documentation~/` — [RuntimeSmoke operator/authoring guide](Documentation~/runtime-smoke-usage.md), [RuntimeSmoke contract](Documentation~/runtime-smokes.md), migration audit, adapter contract, diagnostic transport guides, and trace events.

## Optional capture and raw diagnostic commands

`ath-snap` captures Game-view PNGs; `ath-record` uses the optional Unity Recorder package for video and reports `recorder_unavailable` when it is absent. Captures are review artifacts; RuntimeSmoke remains the acceptance authority. Live capture/Recorder proof is retained as pending. The EXE client supports `cmd --raw` for commands with bare overloads, alongside the existing `runtime-smoke` and `runtime-smoke-suite` commands.

## Architecture notes

The package never references host types. All host data flows through `IAthHostAdapter`. The runtime/commands asmdef split is an architectural seam for future transport swap-out — v0.2 still requires IngameDebugConsole as a peer dep.

ATH is the QA tool in the broader Captain SDLC pipeline. See the nerve center ([`LlamaBrain/captain-sdlc`](https://github.com/LlamaBrain/captain-sdlc)) for the cross-tool design, the trace schema, and the roadmap.
