---
name: ath-unreal-smoke
description: Prove ATH command/state/wait/event/async/evidence behavior inside any instrumented Unreal project using ATH-owned fixtures and native world state. No Lyra or gameplay dependencies.
version: 0.3.1
---

# Unreal ATH fixture smoke

Use the native plugin and shared Node client described in
`Documentation~/unreal.md`. The system under test is ATH. Do not add gameplay,
physics controls or project-specific mechanics to make this smoke pass.

## Setup

Identify the existing consuming `.uproject`, installed Unreal engine, ATH checkout
and endpoint port. Install `Unreal~/Ath` through a development plugin link and
compile the project's existing Editor target. Launch with
`-ath-remote-console -ath-port=<port>` and enter PIE, or use standalone. A normal
Shipping build intentionally has no ATH endpoint.

On Windows use hidden background launches and `tty:true` terminal execution. Do
not close an unrelated user-owned engine session.

## Procedure

1. Run `ath version --port <port>` and confirm ATH `0.3.1` / protocol 1. Run
   `ath capabilities --port <port>`; require Unreal plus state, commands, events,
   asynchronous completion and reset. Confirm the intended world/PIE instance.
2. Run `ath state unreal.world_name --port <port>` and
   `ath state unreal.world_valid --port <port>`. A real runtime world must be
   valid. Editor preview state is insufficient.
3. Run the executable generic fixture suite:

   ```sh
   node <ath>/Tools~/ath-exe-client/ath-exe.js smoke <ath>/Tools~/ath-exe-client/fixture-smoke.json --port <port> --project-root <consuming-project> --trace --rendered
   ```

   This proves fixture readiness, command mutation, predicates, event latching and
   reset, a correlated asynchronous completion with successful result, cleanup,
   and a completed runtime PNG. Retain the JSON result and Captain trace path.
4. For an explicitly nonvisual NullRHI/server run, omit `--rendered`. Record
   screenshot acceptance as unverified; never call the visual gate passed.
5. End PIE while an operation is pending, restart PIE and repeat the smoke.
   Confirm prior-session state/completions cannot satisfy the fresh session and
   a new client can reconnect. Record endpoint behavior and engine logs.
6. Repeat separately for standalone and a packaged Development executable. Do
   not infer packaged success from Editor or standalone. Report native
   `Automation RunTests StartsWith:ATH` outcomes only when the engine runner
   completed. The prefix is required: bare `ATH` also matches unrelated engine
   test names such as `Path.*`.

For KinecadeSandbox acceptance, use its existing Lyra maps and the separate
`Tools~/unreal-acceptance/kinecade.js --rendered` workflow. That host-specific
identity check is not required for another project installing ATH.

## Verdict and evidence

Pass only the gates that actually ran. The shared smoke emits
`ath.smoke.completed` on pass and fail under the consuming project's
`.captain-sdlc/trace/`, with trace-relative report and screenshot artifacts.
Do not write host acceptance evidence into ATH's source tree.

On failure retain the first failed step, structured response, world/fixture
state and a screenshot when rendering is available. Distinguish setup/build
blockers from ATH runtime failures. Leave an existing interactive session intact
for diagnosis. Native compilation, PIE lifecycle, screenshot and packaged gates
remain unverified until supported by actual logs/artifacts.
