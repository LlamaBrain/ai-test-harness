# `IAthHostAdapter` — the adapter contract

This document codifies the rules every `IAthHostAdapter` implementation must
honor. The package itself enforces none of these mechanically; they exist
because the `AthBridge` and the MCP tools rely on them. Violations are
not compile errors — they're behavior bugs that surface as silent races,
stale state, or test-harness lockups.

If you only read one section, read **Property cost & purity**.

---

## 1. Property cost & purity

> **All `IAthHostAdapter` property getters must be cheap and side-effect-free.**

- Acceptable inside a getter: returning a cached value, reading a field on
  the host singleton, calling `Object.FindFirstObjectByType<T>()` once (still
  O(active-scene-roots) — acceptable for harness paths but not for inner
  loops).
- **Not** acceptable inside a getter: raising events, mutating flags,
  calling `Debug.Log`, starting coroutines, allocating large collections,
  performing scene mutations, calling `Request*` methods.

The bridge polls these properties from `ath-state` and `ath-wait` predicates
at 100 ms cadence. Any side effect in a getter compounds on every poll —
the symptom is usually a hard-to-attribute heisenbug.

If you find yourself wanting to "fire `GameReady` lazily on first read,"
restructure: raise the event from a real lifecycle hook (constructor,
subscribed game-event handler) and have the getter return a pure read
of the cached flag (see §3).

---

## 2. Mutation surface

> **Only `Request*` methods mutate gameplay state. `TryGetCustomState` is read-only.**

`Request*` methods are the legitimate mutation surface — they map onto the
project-specific console commands (`world.restart`, `player.kill`, etc.).
Inside a `Request*` method you may:

- Call host singletons, invoke host events, write to `Rigidbody2D.position`,
  destroy GameObjects, etc.
- Throw on invalid input (e.g., `RequestPlayerTeleport(NaN, NaN)`) — the
  caller (`Tool_AthCmd`) wraps exceptions and reports them as `ERR:` lines.

`TryGetCustomState` is invoked from `ath-state` for keys the package's
built-in dispatcher doesn't know. It must return `bool` indicating whether
the key is known to this adapter, and write a stable text representation
into the `out string value`. Mutation, allocation beyond the string itself,
and `Debug.Log` are all out of bounds here too.

---

## 3. Event semantics

The bridge subscribes to the adapter's `AthHostEvents` and promotes most
events to edge-sticky `*SinceLastReset` flags. The adapter's job is to
raise the events at the right moments.

| Event           | When to raise                                              | Bridge behavior              |
|-----------------|------------------------------------------------------------|------------------------------|
| `GameReady`     | At most **once** per adapter lifetime, when the host has reached its first stable "in gameplay" state. Guard with an `_gameReadyRaised` bool. | Sets `_gameReadyEventFired = true`. Combined with adapter's pure `GameReady` getter via OR. |
| `PlayerSpawned` | Every spawn occurrence.                                    | Sets `PlayerSpawnedSinceLastReset = true`. |
| `PlayerRespawned`| Every respawn occurrence.                                 | Sets `PlayerRespawnedSinceLastReset = true`. |
| `PlayerRestarted`| Every level-restart occurrence.                           | Sets `PlayerRestartedSinceLastReset = true`. |
| `PlayerDied`    | Every death.                                               | Sets `PlayerDiedSinceLastReset = true`. |
| `GoalReached`   | Every goal touch.                                          | Sets `GoalReachedSinceLastReset = true`. |
| `ItemConsumed`  | Every consume event.                                       | Sets `ItemConsumedSinceLastReset = true`. |
| `PauseChanged`  | Every transition (pause → unpause OR unpause → pause).     | Updates `LastPauseValue` + increments `PauseChangeCount`. Not promoted to a `*SinceLastReset` flag — pauses come and go too freely. |

**Edge promotion gives skill authors race-resistance:** an `ath-wait
player_died` works whether the death happened before or after the wait
was issued, because the flag survives until `harness.reset` or scene
unload clears it.

---

## 4. The `GameReady` property — pure read

The single most subtle rule:

```csharp
// Acceptable:
public bool GameReady => _gameReadyRaised || (_world?.PlayerController != null);

// NOT acceptable:
public bool GameReady
{
    get
    {
        if (!_gameReadyRaised && _world?.PlayerController != null)
        {
            _gameReadyRaised = true;
            _events.RaiseGameReady();   // side effect — contract violation
        }
        return _gameReadyRaised;
    }
}
```

The bridge OR-combines `_gameReadyEventFired` (its own flag) with the
adapter's getter, *guarded by `IsValid`*. So the adapter only needs to
return the truthful state; the bridge handles the "event was missed"
case. The adapter's lifecycle hooks (e.g., the `OnPlayerSpawn` handler)
fire the event; the getter just reports.

---

## 5. `IsValid` — scene/singleton validity

`bool IsValid { get; }` — adapters expose this so the host bootstrap can
detect when to recreate. The package itself uses it to gate the
validity-guarded `GameReady` read on the bridge.

- BTS implementation: `_world != null && _world == WorldController.Instance`
  (captures the host singleton at construction time, returns false if a
  scene reload swapped the singleton).
- A non-scene-based host can return `true` unconditionally.
- A "single-shot" host adapter (destroyed once unloaded) can return `false`
  permanently after teardown — the bootstrap will dispose and replace.

Property must be cheap (no `Find*` calls) — the bridge reads it on every
state query and wait poll.

---

## 6. No-gameplay-scene behavior

All properties returning host data must return **safe defaults** when no
gameplay scene is loaded, not throw:

- `bool`: `false`
- `int`: `0`
- `Vector3`: `Vector3.zero`
- `string`: `""`

The harness uses `InGameplayScene` to gate. Throwing from a getter would
cascade into `ath-state` failures during scene transitions — the harness
expects best-effort reads, not exceptions.

`TryGetCustomState` returns `false` for keys it doesn't recognize, never
throws.

---

## 7. Custom state stringification

`TryGetCustomState(string key, out string value)` returns text. Use stable,
parseable formats:

- Bools: `"true"` / `"false"` (lowercase).
- Vectors: `"x,y,z"` with `F4` precision (`$"{v.x:F4},{v.y:F4},{v.z:F4}"`).
- Counts: plain integer strings.
- Structured slices: `;`-delimited `k=v` pairs, e.g. `"count=4;first=(0.5,1.0);last=(3.2,1.0)"`.

Document each custom key in your project's adapter documentation.
Reserved keys (those the built-in dispatcher already handles) are listed
in §10 — do not redefine them in `TryGetCustomState`.

---

## 8. Disposal

`Dispose()` is `IDisposable` and:

- Must be **idempotent** — calling twice is fine.
- Must be called **from the Unity main thread**. The adapter touches Unity
  objects during teardown; cross-thread disposal would race. The package
  calls `Dispose()` from `AthServices.Register` (when replacing) and
  `AthServices.Unregister`, both of which run on the main thread.
- May be **empty** for trivial hosts (the `MinimalHostAdapter` sample
  does exactly this).

Inside `Dispose()`:
- Unsubscribe from every host event you subscribed to in the constructor.
- Null any cached host references.
- Set an internal `_disposed` bool if you want to short-circuit redundant
  cleanup work.

Don't raise events from `Dispose()` — by the time you're disposing, the
bridge has already detached.

---

## 9. Adapter ownership

Once you call `AthServices.Register(adapter)`, **ATH owns the adapter's
lifecycle** until `Unregister` (or until `Register(replacement)` implicitly
unregisters the prior adapter).

- ATH may call `Dispose()` during `Unregister` or replacement `Register`.
- Do not keep a reference to the adapter after `Register` unless your host
  needs to introspect it — and even then, treat it as read-only.
- Do not register the same instance twice. Re-create or no-op.

`AthServices.Register(null)` throws `ArgumentNullException` — use
`Unregister()` for explicit teardown.

---

## 10. Reserved state keys

The built-in `AthStateDispatcher` (Editor-side, Phase 4) handles these
keys before falling back to `TryGetCustomState`. Adapters should not
redefine them.

```
in_gameplay, scene_name, host_name, bridge_ready, adapter_ready,
game_ready, player_alive, player_holding_seed, player_position,
player_velocity, spawn_attempts, is_paused, ghost_active, ghost_seed_active,
last_run_recording_frames, last_seed_recording_frames,
player_died_since_reset, player_spawned_since_reset,
goal_reached_since_reset, seed_consumed_since_reset, last_async
```

Any other key falls through to your adapter via `TryGetCustomState`.

---

## 11. Bootstrap registration order

The package's `AthBootstrap` runs `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`,
creating `[AthBridge]` before any scene's `Awake`. Host adapters should
register from `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` so:

1. Host singletons (instantiated in `Awake`) exist when the adapter
   constructor subscribes to their events.
2. The bridge is already up; `AthServices.Register` immediately calls
   `Bridge.AttachToAdapter`.

If you must register earlier (e.g., during `BeforeSceneLoad`), the bridge
will pick up the pre-registered adapter when its own `Awake` runs.

On scene reload, re-check `_adapter?.IsValid`. If `false`, dispose +
recreate + re-register.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void Bootstrap()
{
    if (_adapter == null)
    {
        _adapter = new MyAdapter();
        AthServices.Register(_adapter);
    }
    SceneManager.sceneLoaded += OnSceneLoaded;
}

private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    if (_adapter == null || !_adapter.IsValid)
    {
        AthServices.Unregister();
        _adapter = new MyAdapter();
        AthServices.Register(_adapter);
    }
}
```

---

## 12. Things this contract intentionally does NOT enforce

- Thread safety beyond "Dispose on main thread." Your adapter's other
  methods can be main-thread-only too — the bridge calls them all on
  the main thread.
- Whether `Request*` methods complete synchronously. For BTS in v0.1
  every action is synchronous, but a future host may need to dispatch
  async via `Bridge.TrackAsync` — that's a per-host choice.
- The semantics of `HostName`. Free-form string; used in logs and as
  `ath-state host_name`.
