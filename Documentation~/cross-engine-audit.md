# Architecture audit before cross-engine implementation

Audit baseline: committed `2c93c78dbac381a1991e2cb645599b1c922827dc`
(`feat/footage-capture`), 2026-10-28. This document records the implementation
as found before migration, rather than treating roadmap claims as current code.

## Classification

| Area | Actual implementation | Coupling and migration treatment |
| --- | --- | --- |
| `Tools~/ath-exe-client/protocol.js` | Pure envelope/sentinel parsing, state normalization and predicates | Completely engine independent. Keep this protocol and its tests; native adapters implement it. |
| `Tools~/ath-exe-client/ath-exe.js` | Node standard-library sockets, process launch, waits and local screenshot polling | No engine installation needed. Unity launch defaults, project settings lookup and engine-specific comments are the environment coupling. Extract a reusable client while retaining this entrypoint. |
| `Runtime/IAthHostAdapter.cs` | Host ownership, validity, state getters, events and mutations | Valuable host boundary, but Unity `Vector3`, mandatory player/item/ghost slices and two-dimensional teleport/velocity are unsuitable requirements for a generic host. Preserve the legacy interface; add a smaller generic host contract. |
| `Runtime/AthServices.cs` | Static registration, replacement disposal and bridge attachment | Mostly engine-independent ownership logic housed in the Unity assembly and compile gate. Preserve the public slots and methods. |
| `Runtime/AthHostEvents.cs` | Plain C# events and raisers | No Unity API; event vocabulary is gameplay specific. Add named events without removing old events. |
| `Runtime/AthBridge.cs` | Persistent `MonoBehaviour`, scene subscriptions, sticky edge flags, 16-record async ring | Unity object and scene lifecycle. Edge promotion and async completion semantics transfer through the protocol; game/world lifetime must remain adapter-owned. |
| `Runtime/AthStateDispatcher.cs` | Shared by Editor state tool and remote `harness.state`; fixed-key switch then `TryGetCustomState` | Existing arbitrary-key seam already works. Scene lookup and legacy getters are Unity/gameplay-specific. Add generic lookup without changing legacy reserved keys. |
| `Runtime/Commands/` | `IngameDebugConsole` attributes, dual overloads, `Debug.Log` sentinels | Runtime execution environment plus Unity logging. Keep command vocabulary; provide native dispatch in Unreal. |
| `Runtime/RemoteConsole/` | Loopback TCP, background accept/read, FIFO main-thread update dispatch, JSON envelope, screenshot coroutine | Protocol and option parsing are reusable concepts. Unity main thread, `JsonUtility`, console dispatch and capture are native implementation details. |
| `Runtime/Logging/AthLog.cs` | Sentinel grammar and escaping delegated to `Debug.Log` | Grammar is engine neutral; sink is Unity-specific. Unreal must emit the same escaped fields. |
| `Editor/McpSkills/` | Unity MCP registration, main-thread dispatch, PlayMode checks, state/waits, screenshot/Recorder, trace tools | Editor APIs stay in the Unity integration. Existing MCP tools must retain names and schemas. |
| `AthTraceWriter`, `AthTraceEmitter`, `AthMediaUtil` | Pure C# serialization, filesystem operations and PNG header/path helpers inside Editor assembly | Unity environment coupling, not essential API coupling. Shared Node reporting can implement the same documented Captain envelope independently. |
| `Skills/` | Unity workflows, built-player template, BTS full-loop scenario | `ath-smoke-fullloop` contains BTS classes, scenes, commands and coordinates. Retain as explicitly legacy BTS; add a separate generic smoke. |
| UPM metadata/assemblies | Root `package.json`, Runtime/Editor asmdefs, Samples and stable `.meta` GUIDs | Keep locations and installation method. Place Unreal source under `Unreal~` and tooling under `Tools~` so Unity ignores them. |

There are no runtime dependencies on LlamaBrain NPCs, MToolKit, Dirigible,
Kinecade or Super Brawl. Comments describe historical Dirigible/BTS origins;
these do not justify adding those projects as framework dependencies.

## Existing contracts and dependencies

- Unity 2022.3 minimum in `package.json`; README records Unity 6000.3.10f1 verification.
- Unity MCP `com.ivanmurzak.unity.mcp@0.76.0` is a declared dependency.
  IngameDebugConsole is a hard peer whose `IngameDebugConsole.Runtime` assembly
  is referenced by Commands, RemoteConsole and Editor. The Runtime assembly itself has no peer references.
- Recorder is optional, through a separate assembly and version define; the
  base tool returns `recorder_unavailable` without it. Existing footage code
  was explicitly pending live Editor verification at this baseline.
- Runtime files use `UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE`.
  Listener creation also requires `-ath-remote-console` or its environment flag.
  Binding is exclusively `127.0.0.1`. Preserve both production exclusion and runtime opt-in.
- `AthServices.Register` transfers ownership. Replacement detaches before disposal;
  `Unregister` is idempotent; null registration throws. Adapters use cheap,
  side-effect-free getters and main-thread disposal.
- Bridge supports registration before or after `Awake`. Sticky flags survive
  until reset or scene unload; `GameReady` combines event observation with a
  validity-guarded getter. Never reset between action and observation.
- Server appends the correlation ID. `!raw ` instead dispatches verbatim with
  no captured sentinels; its success means dispatch, not scenario completion.
- Node needs only standard libraries. Screenshot paths refer to the same
  machine/filesystem as the engine process. No remote file-transfer contract exists.

## Audit risks addressed or retained explicitly

1. Baseline `readState` treated a failed/missing-terminal envelope as successful
   empty state. Shared assertions must fail closed rather than pass `key=""`.
2. Baseline client decoded UTF-8 per TCP chunk and buffered without bounds.
   Shared transport must handle split code points, framing, request injection,
   bounded responses and total deadlines.
3. Wait flags/deadlines were inconsistent: `--timeout` in implementation versus
   `--timeout-ms` in comments, with per-poll timeouts exceeding the wait budget.
4. Node accepted any stable nonempty screenshot. Confirm PNG metadata as well as
   size; rendered capture is a separate gate from null-render protocol testing.
5. `AthBridge.TrackAsync` used `ConfigureAwait(false)` before modifying a
   `LinkedList` queried on the main thread. Generic tracking needs synchronization
   and lifecycle fencing for late completions.
6. Unity scene unload, additive scene unload, Unreal world cleanup and PIE end
   are different lifecycle events. Do not equate a surviving process with a
   continuing test session. Keep game-specific readiness explicit.
7. A timed-out queued Unity request can still execute later; captured log queues
   are truncated only after collection. These pre-existing Unity transport
   limitations must not be advertised as cancellation or bounded collection guarantees.
8. Ordinary `async_done` proves completion, not success. Expose per-ID success
   separately and assert it when failure matters.
9. Baseline built-player smoke still needed Editor MCP to publish Captain traces.
   Shared evidence emission must work without either engine installed.

## Baseline verification and documentation gaps

`node --test` passed **26/26** at the audit baseline: 22 protocol/predicate tests
and four socket/argument tests. Unity EditMode tests cover `AthMediaUtil` and
trace `.gitignore` behavior. There was no committed CI workflow, standalone C#
runner or automated runtime host lifecycle/engine acceptance coverage.

The roadmap described player transport as future work although 0.3.0 already
shipped it; adapter docs called the dispatcher Editor-side after it moved to
Runtime; some README gate descriptions omitted `ATH_REMOTE`. Historical
changelog entries describe their releases and remain historical evidence.

See [cross-engine-architecture.md](cross-engine-architecture.md), [protocol.md](protocol.md) and
[unreal.md](unreal.md) for the implemented boundary and reproducible checks.
