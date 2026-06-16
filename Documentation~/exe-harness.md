# Driving a built player — the EXE remote-console harness

The editor MCP tools (`ath-cmd`, `ath-state`, `ath-wait`) drive Unity **in the editor**,
in-process. The editor never strips managed code, runs Obfuz, or produces a player, so an
entire bug class is structurally invisible to it: managed stripping, Obfuz, default-interface
method runtime-mod compile errors, `#if UNITY_EDITOR` `AssetDatabase` fallbacks leaving fields
null in the build, addressable trimming. Every one of those is green in the editor and only
appears in a built player.

The **EXE remote-console harness** is the slower, on-call tier that catches them. A
`DEVELOPMENT_BUILD` *or* an `ATH_REMOTE`-defined **non-dev release** player exposes the same
`IngameDebugConsole` command surface over a `127.0.0.1` loopback socket; an internal Node CLI
(`Tools~/ath-exe-client`) drives the shipping-shaped artifact with the same vocabulary already
used in-editor and asserts on the same `CMD:`/`OK:`/`ERR:` sentinels. Screenshots are only for
genuinely visual checks. It is **off by default, developer-only, and never in a true release**.
(Extends dirigible ADR-0031.)

## Two locks (off by default)

1. **Compile-time — the `ATH_REMOTE` scripting define.** Bakes the harness into a build,
   independent of Development Build, so a real RC (release stripping/Obfuz, dev-build **off**)
   can carry it. A true release omits the define → zero harness code, zero footprint.
2. **Runtime — the launch flag.** Even a harness-bearing build stays silent until launched with
   `-ath-remote-console` (no GameObject, no thread, no bound port otherwise).

## Enabling it

**Recommended — Project Settings ▸ ATH Remote** (or the `Tools ▸ ATH ▸ Remote Console`
checkbox). The toggle, for the **active build target**:

- adds/removes the `ATH_REMOTE` define, and
- writes/removes `Assets/AthRemoteHarness/link.xml` — **required**: under IL2CPP managed
  stripping the harness's `[ConsoleMethod]`s and types are reflection-invoked, so the linker
  strips them even though `ATH_REMOTE` is defined. The `link.xml` preserves the three harness
  assemblies. (Found the hard way on the first RC build: define present, harness stripped.)

Port / media dir / response timeout authored in the same panel are saved to
`ProjectSettings/AthRemote.json`, which the Node launcher reads.

### Host requirement for non-dev RC builds

Your host's ATH adapter + bootstrap (the `IAthHostAdapter` implementation) are almost certainly
gated `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. For a **non-dev** `ATH_REMOTE` build they must
also include `|| ATH_REMOTE`, or the adapter compiles out and `harness.state` reports
`adapter_present=false`. The package gate-sweep can't reach host code — this one is yours.

## Building + driving

1. Toggle `ATH_REMOTE` on for the target; build with your real release settings, **Development
   Build unchecked**.
2. Drive it with the Node client (Node ≥ 18, stdlib only):

```bash
cd <ath-package>/Tools~/ath-exe-client

node ath-exe.js launch  <path-to-player.exe>   # spawns with -ath-remote-console, waits for harness.ping
#   or, if you launched the exe yourself with `-ath-remote-console -ath-port 8787`:
node ath-exe.js attach  8787

node ath-exe.js cmd   "harness.ping"           # → {status:"success", ... pong=true}
node ath-exe.js state game_ready               # → {status:"ok", value:"true"}
node ath-exe.js wait  player_died --timeout 30000
node ath-exe.js snap  smoke                     # → a PNG under <persistentDataPath>/ath-media
```

`launch` owns the child (kills it on failed readiness) unless `--detach`; `--keep-open` leaves a
not-ready child running. Port resolves `--port` → `ProjectSettings/AthRemote.json` → a free
random port (`launch`) / `8787` (`attach`/manual).

### Commands & predicates

- **Any console command** via `cmd "<command …>"` — the full in-game vocabulary
  (`harness.ping`, `lifecycle.scene`, host commands, …).
- **`state <key>`** → `harness.state`: `game_ready`, `scene_name`, `player_alive`,
  `spawn_attempts`, `*_since_reset` edge flags, `async:<id>`, adapter custom keys, …
- **`wait <predicate>`** polls `harness.state`: `game_ready`, `player_spawned`, `player_died`,
  `spawn_attempts_at_least:N`, `async_done:<id>`, `state_equals:k=v`, `scene_loaded:Name`.
  `unknown_key`/malformed fail fast; `not_ready` polls until `--timeout`.
- **`snap <label>`** → `harness.snap`: in-player screenshot to the media dir; the client polls
  the PNG until written. **Same-machine only** — the path is the player's local filesystem.

## What it does and doesn't prove (observer-probe cost)

An `ATH_REMOTE` build is *not* byte-identical to the ship artifact — the define + `link.xml`
root a little extra. It still reproduces the bug class as long as everything else matches the
release: **managed-stripping level, IL2CPP/Mono backend, Obfuz settings, addressables profile,
build target, and the (non-)development-build flag**. Keep those identical to the real release
and the harness exercises the same stripped/obfuscated game code.

## Architecture

- In-player (gated `#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE`): `Runtime/RemoteConsole/`
  — `AthRemoteConsoleServer` (loopback listener, FIFO main-thread pump, one JSON response per
  connection), `AfterSceneLoad` bootstrap, pure `AthRemoteOptions`. `harness.state`/`harness.snap`
  run through the shared `AthStateDispatcher` / the server's screenshot coroutine.
- Out-of-process: `Tools~/ath-exe-client` — `protocol.js` (the wire contract: JSON envelope +
  sentinel grammar + predicates, unit-tested) and `ath-exe.js` (socket/process shell).
