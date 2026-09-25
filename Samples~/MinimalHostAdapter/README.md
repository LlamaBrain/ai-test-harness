# Minimal Host Adapter — sample

A no-op `IAthHostAdapter` implementation intended for the planned Phase 8
fresh-project package-boundary check. That clean-project Unity check has not
been recorded as passed. The sample is designed to check that:

- `LlamaBrainLabs.Ath.Runtime.asmdef` compiles cleanly against only the
  package's runtime — no Commands, no Editor, no IngameDebugConsole,
  no Unity MCP.
- `AthServices.Register` accepts an arbitrary `IAthHostAdapter`.
- `AthBridge` instantiates and binds to the registered adapter.
- the low-level diagnostic state query `host_name` returns
  `"MinimalHostAdapter"` after the bootstrap fires.

## What the sample does

`MinimalAthAdapter` implements every property with a safe default (`false`,
`0`, `Vector3.zero`, `""`) and every `Request*` method as a no-op. It
exposes one custom state key, `sample_marker`, returning `"minimal-ok"` —
an adapter-wiring diagnostic you can probe through the low-level state API.
It is not a RuntimeSmoke predicate or acceptance signal.

`MinimalAthAdapterBootstrap` is a `RuntimeInitializeOnLoadMethod` that
constructs the adapter once and calls `AthServices.Register(adapter)` at
`AfterSceneLoad`.

## Importing

Open the host project's Package Manager, find **AI Test Harness**, expand
the **Samples** section, and click **Import** next to "Minimal Host Adapter."

Unity copies the sample into `Assets/Samples/AI Test Harness/<version>/Minimal Host Adapter/`.

## Compile dependency and Phase 8 setup

The sample's asmdef (`LlamaBrainLabs.Ath.Samples.MinimalHostAdapter`)
references only `LlamaBrainLabs.Ath.Runtime`. This source-level boundary does
not mean the whole package can be imported without its peer dependencies.
For the Phase 8 clean-project check, install the dependencies listed by the
package plus the IngameDebugConsole peer dependency before compiling; import
only this sample as host-specific source. The sample asmdef itself must still
compile against Runtime alone.

The package manifest declares Unity MCP. The package README also identifies
`IngameDebugConsole.Runtime` as a required peer dependency because package
assemblies reference it. The sample's independence demonstrates the *code*
boundary, not a *distribution* boundary. See the package README's compatibility
matrix.

## Diagnostic boundary check (not smoke acceptance)

As the unverified Phase 8 check, import the sample into a fresh project with
the dependencies above installed, confirm clean compilation, enter PlayMode,
and use low-level diagnostics to inspect adapter registration. These calls
check package wiring only; their output does not establish RuntimeSmoke
acceptance. Record the Unity version, dependency revisions, and fresh compile
log before treating the boundary check as passed.

```jsonc
ath-cmd { "command": "test.echo hi" }
// expect Status=success

ath-state { "key": "host_name" }
// expect Value="MinimalHostAdapter"

ath-state { "key": "sample_marker" }
// expect Value="minimal-ok"

ath-state { "key": "game_ready" }
// expect Value="true"
```

## When to consult

- You're implementing a new host adapter and want a structural template.
- You're debugging a "the harness doesn't see my adapter" issue — try
  importing this sample alongside yours and see if the minimal one binds
  cleanly. If it does, the bug is in your adapter; if it doesn't, the
  bug is in the host-side bootstrap or asmdef references.
