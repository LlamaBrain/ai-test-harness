---
name: ath-exe-smoke
description: Host-agnostic template for a built-player (EXE) smoke. Drives a DEVELOPMENT_BUILD or ATH_REMOTE non-dev release player over the loopback socket via the internal Node client (Tools~/ath-exe-client), asserting that the shipping-shaped artifact boots, the harness command surface survives stripping, and game state is reachable — the build-vs-editor parity tier the editor smokes structurally cannot cover. Copy into your host project and fill in the placeholder scenario steps.
version: 0.3.0
---

# Built-player (EXE) smoke — template

Proves **build behavior**, not editor behavior: that a real built player boots, the harness
command surface is present after IL2CPP managed stripping, and game state is reachable over the
socket. This is the on-call tier you deploy when a build misbehaves (stripping / Obfuz /
`#if UNITY_EDITOR` fallbacks / addressable trim) — the class of bug invisible to `ath-smoke-*`.

This is a **host-agnostic template**. Copy it into your host project's
`.claude/skills/` and replace the `<…>` placeholders with your scenario. See
`Documentation~/exe-harness.md` for build/enable details.

## How to invoke these tools (read first)

The exe is driven by the **Node client**, run through Bash — *not* the editor MCP tools:

```bash
cd <ath-package>/Tools~/ath-exe-client
node ath-exe.js <cmd|state|wait|snap|launch|attach> …
```

The final trace-emit step uses the **editor** MCP tool `ath-trace-emit` (the editor must be
connected). For a fully headless CI run, record the pass/fail verdict yourself.

## Prerequisites (one-time, see exe-harness.md)

- Project Settings ▸ ATH Remote → enable `ATH_REMOTE` for the target (writes the define + the
  `link.xml` that survives stripping).
- For a **non-dev** RC build: your host adapter/bootstrap gates include `|| ATH_REMOTE`.
- Build with real release settings (Development Build optional — the point is non-dev parity).

## Steps

### Step 0 — version pre-flight (fail-fast)

```bash
node ath-exe.js launch "<path-to-player.exe>"      # waits for harness.ping
node ath-exe.js state package_version
# Frontmatter declares version: 0.3.0
# expected: value == "0.3.0"; if not, the built player is a stale package — rebuild,
# or re-copy this skill from <ath-package>/Skills/ath-exe-smoke.
```

### Step 1 — boot + readiness

```bash
node ath-exe.js cmd  "harness.ping"                 # status:success, pong=true
node ath-exe.js state adapter_ready                 # value:true  (false ⇒ host gate missing || ATH_REMOTE)
node ath-exe.js wait game_ready --timeout 30000     # satisfied once the host reports ready
node ath-exe.js state scene_name                    # value: <your gameplay scene>
```

If `adapter_ready` is `false` in a non-dev build, the harness transport works but the host
adapter compiled out — add `|| ATH_REMOTE` to the host adapter/bootstrap gates and rebuild.

### Step 2 — baseline state

```bash
node ath-exe.js state player_alive                  # <expected>
node ath-exe.js state spawn_attempts                # <expected>
# … your invariants
```

### Step 3 — drive the scenario  «replace this block»

```bash
node ath-exe.js cmd  "<your.command args>"
node ath-exe.js wait <predicate> --timeout <ms>     # e.g. player_died, state_equals:k=v, async_done:<id>
node ath-exe.js cmd  "harness.reset"                # clear edge-sticky flags between sequenced waits
```

### Step 4 — visual evidence (only for genuinely visual checks)

```bash
node ath-exe.js snap "<label>" --out <local-dir>    # PNG to the player's media dir; client polls it
```

### Step 5 — emit the trace (editor connected)

```bash
unity-mcp-cli run-tool ath-trace-emit --input '{"result":"pass","skill":"ath-exe-smoke","summary":"<what passed> in a non-dev release build"}'
```

## PASS criteria

1. Step 0 version matches — the **built** player carries the expected package version.
2. `harness.ping` round-trips over the socket → the transport survived into the build.
3. `adapter_ready` / `game_ready` true → harness types survived stripping (the `link.xml`
   worked) and the host adapter is wired in a non-dev build.
4. Your Step-3 scenario predicate(s) reach `satisfied` within their timeouts.
5. (Optional) the Step-4 PNG exists and is non-empty.

## Failure handling

- `harness.ping` never succeeds → the listener isn't bound: confirm the build has `ATH_REMOTE`,
  the exe was launched with `-ath-remote-console`, and the port matches.
- `ping` works but `adapter_ready=false` → host adapter stripped/compiled-out: `link.xml`
  preservation and the host `|| ATH_REMOTE` gate (Step 1 note).
- A command returns `status:failed failReason:command_error` → fetch the player log
  (`<player>_Data/Player.log` / the platform console log) for the in-game exception.
- Capture a `snap` and the relevant `state` dumps before triaging.
