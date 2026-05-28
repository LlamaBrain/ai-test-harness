# Minimal Host Adapter — sample

A no-op `IAthHostAdapter` implementation used to verify the AI Test Harness
runtime is independent of any specific host project. The fresh-project
dry-run (Phase 4b of the implementation plan) imports this sample and uses
it to confirm:

- `LlamaBrainLabs.Ath.Runtime.asmdef` compiles cleanly against only the
  package's runtime — no Commands, no Editor, no IngameDebugConsole,
  no Unity MCP.
- `AthServices.Register` accepts an arbitrary `IAthHostAdapter`.
- `AthBridge` instantiates and binds to the registered adapter.
- `ath-state host_name` returns `"MinimalHostAdapter"` after the bootstrap
  fires.

## What the sample does

`MinimalAthAdapter` implements every property with a safe default (`false`,
`0`, `Vector3.zero`, `""`) and every `Request*` method as a no-op. It
exposes one custom state key, `sample_marker`, returning `"minimal-ok"` —
a smoke signal you can probe via `ath-state sample_marker`.

`MinimalAthAdapterBootstrap` is a `RuntimeInitializeOnLoadMethod` that
constructs the adapter once and calls `AthServices.Register(adapter)` at
`AfterSceneLoad`.

## Importing

Open the host project's Package Manager, find **AI Test Harness**, expand
the **Samples** section, and click **Import** next to "Minimal Host Adapter."

Unity copies the sample into `Assets/Samples/AI Test Harness/<version>/Minimal Host Adapter/`.

## Compile dependency

The sample's asmdef (`LlamaBrainLabs.Ath.Samples.MinimalHostAdapter`)
references only `LlamaBrainLabs.Ath.Runtime`. **It compiles even if the
consumer has not installed IngameDebugConsole or Unity MCP** — proving
the runtime core is genuinely independent of the transport.

Note: the *package as a whole* still requires both peer deps (the
Commands and Editor asmdefs reference them). The sample's independence
demonstrates the *code* boundary, not a *distribution* boundary. See
the package README's compatibility matrix.

## Validating

After importing the sample and entering PlayMode in a fresh project:

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
