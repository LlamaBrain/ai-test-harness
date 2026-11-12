# AI Test Harness

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com)

By **Michael Tiller** (<contact@michaeltiller.com>), a
[LlamaBrain](https://github.com/LlamaBrain) project in the
[Captain SDLC](https://github.com/LlamaBrain/captain-sdlc) pipeline.

ATH is a runtime testing and evidence framework for Unity and Unreal Engine.
Agents use shared commands, named state, predicate waits, events, correlated
asynchronous operations, screenshots and smoke reports through one CLI/MCP
surface. Native engine integrations remain independently installable.

The integration candidate is version `0.6.2`. Existing cross-engine verification
is tied to the revisions recorded in the [verification matrix](Documentation~/verification.md);
consumer integration, current CI and exact-artifact acceptance must be assessed
against the actual revision and host evidence.

```text
Claude / Codex -> shared ATH MCP / CLI -> protocol, predicates, smoke, evidence
                                                   |
                              Unity integration / Unreal native plugin
                                       |                   |
                                  Unity project       Unreal project
```

Unity's host-neutral RuntimeSmoke compiler, coordinator, Editor window, typed MCP
operations and built-player runners execute checked-in host workflows. Start with
the [RuntimeSmoke usage guide](Documentation~/runtime-smoke-usage.md) and
[contract](Documentation~/runtime-smokes.md). Direct commands, procedural smoke
skills and caller-supplied trace results are diagnostics, not a substitute for
the structured Unity RuntimeSmoke result and host build-gate evidence.

The shared Node tooling builds and tests without either engine installed. The
Unreal plugin uses native C++ and does not consume .NET assemblies or depend on
Lyra. KinecadeSandbox, an existing Lyra project, is the real Unreal acceptance
host. ATH-owned fixtures and native world state prove ATH without changing Lyra
gameplay.

## Quick start

```sh
cd <ath>/Tools~/ath-exe-client
npm test
npm run check
npm link
ath version
ath attach 8787
ath cmd "harness.ping" --port 8787
ath capabilities --port 8787
ath state ath.fixture.ready --port 8787
ath cmd "ath.fixture.increment" --port 8787
ath wait "state_equals:ath.fixture.counter=1" --port 8787
ath smoke fixture-smoke.json --port 8787 --project-root <consuming-project> --trace --rendered
```

Use Node 18 or newer. `ath-exe` remains an alias, and direct
`node Tools~/ath-exe-client/ath-exe.js ...` invocation remains supported. Runtime
operations require a launched, explicitly instrumented host with the fixture
capability. This fixture workflow is separate from Unity RuntimeSmoke acceptance.
Omit `--rendered` only for an explicitly identified nonvisual fixture run.

Read [shared CLI/MCP usage](Documentation~/shared-tooling.md), the
[wire protocol](Documentation~/protocol.md), and
[architecture / compatibility audit](Documentation~/cross-engine-architecture.md).

## Unity installation and compatibility

The root remains the existing UPM package
`com.llamabrainlabs.ai-test-harness`. Existing `IAthHostAdapter`, `AthServices`,
console commands, Editor MCP tools, remote protocol and build gates are preserved.
The new generic host surface is additive; legacy adapters do not acquire new
mandatory members.

1. Configure the consuming project's OpenUPM registries:

   ```json
   "scopedRegistries": [
     {"name":"OpenUPM (ivanmurzak)","url":"https://package.openupm.com","scopes":["com.ivanmurzak"]},
     {"name":"OpenUPM (yasirkula)","url":"https://package.openupm.com","scopes":["com.yasirkula"]}
   ]
   ```

2. Install `com.yasirkula.ingamedebugconsole` (or the existing asset version with
   `IngameDebugConsole.Runtime`). It remains a hard peer dependency. Unity MCP
   `com.ivanmurzak.unity.mcp@0.76.0` remains declared in `package.json`.
3. Add the package reference to `Packages/manifest.json`:

   ```json
   "com.llamabrainlabs.ai-test-harness": "file:../../ai-test-harness"
   ```

4. Implement and register the existing adapter:

   ```csharp
   [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
   private static void RegisterAth()
       => LlamaBrainLabs.Ath.AthServices.Register(new MyAthAdapter());
   ```

5. Register the host's checked-in RuntimeSmoke workflows and run them through the
   [authoring and usage guide](Documentation~/runtime-smoke-usage.md). Built-player
   acceptance binds the declared workflow and suite results to the exact artifact.

See [adapter contract](Documentation~/adapter-contract.md), the Package Manager
**Minimal Host Adapter** sample, [Unity operator guide](Documentation~/using-ath.md),
and [Unity player remote console](Documentation~/exe-harness.md).

Unity runtime instrumentation stays behind
`UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE`, with a separate runtime
remote-console opt-in. Normal uninstrumented releases do not bind a listener.
The existing Unity Editor tools remain available, including optional Recorder
video; the shared MCP server does not replace Editor controls.
`ath-snap` and `ath-record` supply optional review artifacts; Recorder reports
`recorder_unavailable` when absent. Direct `ath-exe cmd --raw` supports bare
overload diagnostics. Capture and raw dispatch do not themselves establish a
RuntimeSmoke verdict.

## Unreal installation

Link or install `Unreal~/Ath` into an existing project's Plugins directory, build
its native Editor target, and launch with `-ath-remote-console -ath-port=8787`.
The module is excluded from normal Shipping/Test targets. Supported
instrumentation uses non-Shipping builds with explicit runtime enablement.

Follow [Unreal installation and KinecadeSandbox acceptance](Documentation~/unreal.md)
for exact local engine/project observations, PIE selection, standalone,
packaged Development, lifecycle and screenshot verification. Read
[verification.md](Documentation~/verification.md) before interpreting any mode
as verified.

## Evidence and skills

Shared smoke/trace emission needs no Unity Editor. It writes Captain
`ath.smoke.completed` schema-v1 context and safe artifact references into the
**consuming project's** `.captain-sdlc/trace/`. Its caller-supplied result does
not replace RuntimeSmoke evidence. See [trace events](Documentation~/trace-events.md).

Guidance and the Unreal fixture workflow live under [Skills/](Skills/README.md).
The retired BeforeTheShade full-loop reference is preserved under
[HostWorkflows~/BeforeTheShade/](HostWorkflows~/BeforeTheShade/ath-smoke-fullloop/SKILL.md),
outside generic distribution. Host gameplay acceptance belongs in the consuming
project's checked-in workflows.

## Repository layout

| Path | Responsibility |
|---|---|
| `Runtime/`, `Editor/`, `Tests/`, `Samples~/` | Unity UPM integration, RuntimeSmoke and regression tests. |
| `Tools~/ath-exe-client/` | Shared protocol, client, CLI, MCP, RuntimeSmoke runners and trace reporting. |
| `Unreal~/Ath/` | Reusable native plugin and ATH fixture. |
| `Tools~/unreal-acceptance/` | KinecadeSandbox consumer acceptance workflow. |
| `Documentation~/` | Architecture, contracts, protocol, installation and evidence. |
| `Skills/`, `HostWorkflows~/` | Generic workflows and isolated legacy host procedures. |
