# Cross-engine architecture and compatibility audit

ATH shares its protocol, remote client, predicates, smoke orchestration, MCP transport,
and evidence reporting. Unity and Unreal retain native runtime integrations. Neither
engine is a dependency of the Node tooling; Unreal does not consume .NET assemblies.

```text
Agent -> shared MCP / ath CLI -> shared client and protocol
                                      |
                         newline-delimited loopback TCP
                             /                   \
                  Unity native runtime     Unreal native plugin
                  legacy / generic host    native host / subsystem
```

`Runtime/`, `Editor/`, `package.json`, and existing Unity assembly names remain the
UPM package. `Tools~/ath-exe-client/` is the shared Node package; its historical
directory and `ath-exe` entry remain valid. `Unreal~/Ath/` is an independently
installable native plugin. The `~` directories prevent Unity importing Unreal
sources and Node dependencies as assets. KinecadeSandbox is an acceptance host,
not a dependency of any generic runtime or shared module.

## Baseline audit

The migration starts from commit `2c93c78` (v0.3.0 package plus footage-capture
and raw-dispatch changes). Classification concerns actual dependencies rather
than the names of the directories.

The retained [pre-migration audit](cross-engine-audit.md) records the original
implementation in detail. This document describes the resulting shared boundary
and compatibility decisions; neither document substitutes for runtime evidence.

| Component | Classification | Compatibility / extraction decision |
|---|---|---|
| `Runtime/IAthHostAdapter.cs` | Unity API-dependent and legacy gameplay-shaped | Preserve unchanged; `Vector3`, player/item/ghost members remain optional legacy integration concerns. |
| `AthBridge.cs` | Unity lifecycle-dependent | Preserve MonoBehaviour bootstrap, scene events and legacy flags; generic observations require session fencing and safe asynchronous completion. |
| `AthServices.cs` | Engine-independent ownership concept with Unity integration | Preserve public registration and bridge slots; add a generic host seam alongside the legacy adapter. |
| `AthHostEvents.cs` | Plain C# events, legacy gameplay-shaped vocabulary | Keep existing events and raise helpers; named observation is additive. |
| `AthStateDispatcher.cs` | Unity API-dependent dispatch plus host-specific legacy keys | Keep existing keys and status conventions; expose registered generic state without imposing gameplay fields. |
| `AthAsyncOpRecord.cs` | Engine-independent value representation | Preserve names, correlation IDs and completion success/error semantics. |
| `Runtime/Commands/` | Unity console API-dependent | Preserve `[ConsoleMethod]` discovery, bare/id-bearing overloads and command names. |
| `Runtime/RemoteConsole/` | Unity lifecycle/API-dependent transport | Keep opt-in bootstrap, main-thread dispatch, remote screenshot path and wire envelope. |
| `Runtime/Logging/AthLog.cs` | Unity API-dependent emission; portable sentinel grammar | Preserve `CMD:`, `OK:`, `ERR:`, quoting and `[dispatched]`. |
| `Editor/McpSkills/Tool_*.cs` | Unity Editor/MCP-dependent | Remain the existing Unity integration. Shared MCP delegates to the remote client instead of cloning these tools for Unreal. |
| `AthTraceWriter.cs`, `AthTraceEmitter.cs` | Engine-independent serializer/IO; Unity caller supplies project root | Preserve schema and destination; Node provides the same event without requiring Editor. |
| `AthMediaUtil.cs` | Mostly engine-independent path/PNG helpers | Preserve existing behavior and tests; shared client validates runtime-produced PNG evidence. |
| Node `protocol.js` | Engine-independent | Reuse parser, aliases and predicates. No second Unreal predicate implementation. |
| Original `ath-exe.js` | Portable client with Unity launch assumptions | Extract reusable client; retain path, exported helpers, old arguments and `ath-exe` alias. |
| `Skills/ath-smoke-fullloop` | BeforeTheShade-specific | Preserve as an explicitly named host workflow outside generic skill distribution. |
| Feature-demo / EXE skill templates | Host-independent templates with Unity execution examples | Label engine requirements and document shared-client alternatives. |

The original Runtime asmdef has no assembly references but still references Unity
engine APIs. It is not a portable assembly merely because it is named Runtime.
The shared boundary is the wire contract, not an Unreal-to-C# dependency.

### Existing test coverage

The original Node suite contains 26 protocol and socket tests, including sentinel
escaping, correlation filtering, ERR precedence, predicate parsing, unknown-key
failure, fake-server round trips, connection refusal and CLI argument parsing.
These tests run without either engine. Unity EditMode tests cover pure media/path
helpers and trace `.gitignore` preservation. Existing code did not supply a
comprehensive automated scene, adapter, transport or Unity player lifecycle suite.

Historical README/changelog statements about BeforeTheShade live verification are
historical evidence, not proof that this migration passed Unity regression tests.
Current results belong in [verification.md](verification.md).

## Wire invariants

See [protocol.md](protocol.md) for the executable contract.

- One UTF-8 command line per TCP connection, terminated with LF; one LF-terminated
  JSON response. Existing envelope field names and types are retained.
- `correlationId` identifies that request. Sentinel IDs must match it; unrelated
  engine log messages cannot complete a request. ERR takes precedence over OK.
- `success` confirms a command terminal; `dispatched` confirms asynchronous
  acceptance. Completion requires a separate correlated state query or wait.
- Unknown state is `ERR:harness.state ... reason=unknown_key`. A recognized state
  that is temporarily unavailable returns `status=not_ready` and remains pollable.
- `harness.reset` clears sticky observations. Reset before the triggering command,
  never between an action and the wait observing its event.
- The Unity `!raw ` prefix remains available for bare console overloads. It
  deliberately supplies no sentinel capture; verify mutations through state.
- Unity baseline limits are 8 KiB requests, eight connections, 256 captured lines,
  64 KiB captured sentinel data and a default 2-second response deadline. Engine
  integrations must bound work without weakening the shared client's deadlines.

## Host contract and ownership

New hosts expose identity/readiness, capabilities, named state, registered commands,
named events, correlated asynchronous completion and reset. Screenshot support is
an optional capability. No new host must implement a player, item or replay model.
The preserved Unity contract is documented in [adapter-contract.md](adapter-contract.md).

Registration and teardown occur on the engine's main/Game Thread. Native UObject
references must be weak or validated at each use. Getters return stable invariant
text, stay cheap and read-only, and never emit events or mutate readiness. All
mutation occurs in explicitly registered commands. A custom handler must return
promptly; asynchronous work reports dispatch and later completion, rather than
blocking the Game Thread. A deadline can reject queued work or bound a socket
exchange; it cannot safely preempt an arbitrary blocking native handler.

ATH owns registered host lifetime until unregistration or replacement. Replacement,
world changes and shutdown clear session observations and invalidate pending work
so old completions cannot satisfy a new session. Ordinary edge reset keeps
completion history; full session teardown clears it. Completion retention is
bounded, so an evicted or never-seen ID is `not_ready`, not proof of failure.

Unreal extends through `IAthHost` / `FAthRuntime` or `UAthHostSubsystem`. It uses
native engine modules, Game Thread dispatch, socket infrastructure and screenshot
APIs. The plugin requires only Unreal facilities, never Lyra modules. The fixture
and world observations are ATH responsibilities, independent of gameplay.

## Instrumentation and evidence boundaries

Unity retains `UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE` compile gates and
the existing runtime opt-in. Unreal excludes its runtime module from normal
Shipping/Test targets and additionally requires `-ath-remote-console` before
binding. An instrumented release-shaped acceptance run is a non-Shipping build
with production-like content/settings. It does not make normal Shipping console
facilities available.

Each local endpoint binds `127.0.0.1`; no tool exposes arbitrary code evaluation or
forwards arbitrary Unreal console execution. Commands resolve through ATH's
registered surface. The shared client bounds complete exchanges, including peers
that trickle bytes, and supports cancellation.

Smoke reports, PNGs and Captain traces belong in the consuming project's
`.captain-sdlc/trace/`, never the ATH source tree. Schema v1 retains existing
`ath.smoke.completed` fields; engine/build/diagnostic context is additive. See
[trace-events.md](trace-events.md) and [shared-tooling.md](shared-tooling.md).
