# Unreal installation and acceptance testing

The reusable plugin lives at `Unreal~/Ath/Ath.uplugin`. It provides the
`AthRuntime` native module and has no dependency on Lyra, KinecadeSandbox or any
gameplay plugin. Install it into an existing C++ Unreal project; no separate
sample project is required.

## Local validation host audit

KinecadeSandbox was inspected at `E:/Personal/Unreal/KinecadeSandbox` on
2026-10-28. Its `.uproject` associates with Unreal 5.8; the installed engine's
`Engine/Build/Build.version` identifies 5.8.3, changelist 58210709, under
`G:/Epic Games/UE_5.8`. Engine source headers are the authority for the installed
API signatures.

The relevant official references are Epic's
[plugin installation guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/working-with-plugins-in-unreal-engine),
[project descriptor plugin directories](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Projects/FProjectDescriptor),
[core ticker](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Core/FTSTicker),
[world lifecycle delegates](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FWorldDelegates),
and [screenshot request API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/FScreenshotRequest/RequestScreenshot).
The implementation uses these native facilities with the installed 5.8 headers;
method availability in another engine minor version requires compilation there.

The project contains `LyraGame` and `LyraEditor` modules and Lyra Editor, Game,
Client and Server targets. `LyraEditor.Target.cs` uses BuildSettings V7. Existing
plugins include ShooterCore, ShooterMaps, ShooterExplorer, TopDownArena,
ShooterTests, RuntimeTests and Gauntlet. `ShooterTests` includes native actor,
network, animation and map tests; ATH does not alter or depend on those tests.

`Config/DefaultEngine.ini` selects:

- Game: `/Game/System/FrontEnd/Maps/L_LyraFrontEnd`.
- Editor: `/Game/System/DefaultEditorMap/L_DefaultEditorOverview`.
- Game mode: `/Game/B_LyraGameMode.B_LyraGameMode_C`.

Use those existing maps or another already installed map with a valid runtime
world. A full combat experience or Kinecade implementation is unnecessary.
This work tests ATH, with stock Lyra as the integration environment.

## Install without duplicating the maintained plugin

The portable installer creates a development link into the host's Plugins
directory. It defaults to this ATH checkout's source and requires an explicit
consuming project. First inspect the plan, then install and verify:

```sh
node <ath>/Tools~/unreal-install/install.js --project <Game.uproject> --dry-run
node <ath>/Tools~/unreal-install/install.js --project <Game.uproject>
node <ath>/Tools~/unreal-install/install.js --project <Game.uproject> --verify-only
```

Windows uses a directory junction; other platforms use a directory symlink.
Existing matching links verify idempotently. The installer refuses ordinary
existing plugin directories, foreign/dangling links and redirected Plugins
parents, without removing or overwriting them. It does not change `.uproject`
contents, copy the plugin implementation or launch the engine. `--source` selects
a different maintained plugin checkout. See the
[installer details](../Tools~/unreal-install/README.md).

The equivalent manual development link on the local validation machine is:

```powershell
$ath = 'E:/Personal/ai-test-harness/.captain-sdlc/worktrees/cross-engine-unreal'
$project = 'E:/Personal/Unreal/KinecadeSandbox'
New-Item -ItemType Junction -Path "$project/Plugins/Ath" -Target "$ath/Unreal~/Ath"
```

Inspect any existing destination before creating the link; do not replace another
session's plugin or generated work. A project-owned `AdditionalPluginDirectories`
entry pointing at the ATH `Unreal~` directory is an alternative when supported by
the project's tooling. Keep the maintained implementation in ATH. Packaged
distribution may copy a pinned plugin release as an explicit packaging step.

Enable `Ath` in the consuming project's Plugins configuration when required and
regenerate project files using that engine's normal tools. Build the existing
host Editor target:

```powershell
& 'G:/Epic Games/UE_5.8/Engine/Build/BatchFiles/Build.bat' LyraEditor Win64 Development '-Project=E:/Personal/Unreal/KinecadeSandbox/KinecadeSandbox.uproject' -WaitMutex
```

Changing a descriptor or compiling a plugin is not proof of loading or runtime
success. Retain compiler exit status, engine logs and remote smoke results
separately. Actual outcomes are listed in [verification.md](verification.md).

## Enablement and session targeting

The module is denied in normal Shipping and Test configurations. Within a
supported build, the endpoint remains disabled without `-ath-remote-console`.
The listener binds loopback only. Launch flags are:

```text
-ath-remote-console -ath-port=8787 -ath-pie-instance=0
```

The PIE selector is optional. Without it, the plugin deterministically chooses
the lowest PIE instance; a valid Game world is the standalone fallback. Editor
preview worlds do not satisfy runtime readiness. A single process exposes one
target world at a time. Separate processes use separate ports. Confirm the actual
selection through `unreal.world_name` and the advertised world/PIE metadata.

Socket processing and command dispatch run on the Game Thread through a
nonblocking ticker. Limits are eight connections, 8 KiB requests, 64 KiB responses
and a 5-second transport deadline. Registered handlers must return promptly and
report asynchronous work separately. Native commands never forward arbitrary
remote Unreal console text or source code.

## Fixture and real engine state

The ATH fixture is independent of Lyra classes:

| State | Meaning |
|---|---|
| `ath.fixture.ready` | Fixture has a valid selected runtime world. |
| `ath.fixture.counter` | Integer action counter. |
| `ath.fixture.active` | Boolean fixture flag. |
| `ath.fixture.last_action` | Last fixture mutation. |
| `event:ath.fixture.changed` | Sticky mutation event. |

Commands include `ath.fixture.increment`, `ath.fixture.reset`,
`ath.fixture.set_active`, and `ath.fixture.async_increment`. The asynchronous
command accepts a bounded delay, returns a correlated dispatch, and later makes
`async:<id>` pollable. A completed failure still counts as completion; query
success/diagnostic state before passing the smoke.

Native state includes engine version, selected world validity/name, actor count,
delta time, play mode and readiness. These read actual Unreal objects and provide
the integration proof beyond the synthetic counter. Use capability discovery for
the exact supported keys.

```sh
ath capabilities --port 8787
ath cmd "harness.ping" --port 8787
ath state unreal.world_name --port 8787
ath state unreal.world_valid --port 8787
ath cmd "ath.fixture.reset" --port 8787
ath cmd "harness.reset" --port 8787
ath cmd "ath.fixture.increment" --port 8787
ath wait "state_equals:ath.fixture.counter=1" --port 8787
ath wait "event:ath.fixture.changed" --port 8787
ath cmd "harness.reset" --port 8787
ath state event:ath.fixture.changed --port 8787
```

The final event read must be false. Reset before a new action, never between the
action and its observation.

## Repeatable acceptance workflow

1. Build the plugin in the existing KinecadeSandbox host.
2. Launch Editor with runtime opt-in, start PIE, and confirm capabilities identify
   Unreal and the expected project/world. A visible editor is needed only when
   a person is operating it; automated helpers should launch hidden.
3. Run the generic fixture smoke from the shared CLI and retain its report/trace
   under KinecadeSandbox's `.captain-sdlc/trace/`.
4. Run `node <ath>/Tools~/unreal-acceptance/kinecade.js --port 8787 --project-root
   <KinecadeSandbox> --rendered` for rendered host acceptance. Omit `--rendered`
   only for the separately identified nonvisual run.
   This consumer suite includes the complete shared fixture steps by default,
   plus KinecadeSandbox identity and native Unreal world readiness/validity. Its
   `--dry-run` prints the specification without connecting or writing evidence.
5. Capture a PNG; require nonzero dimensions and complete image data. NullRHI or
   dedicated-server execution must report visual acceptance as unverified.
6. Stop PIE during a pending operation. The listener remains available for
   reconnect, while selected-world cleanup closes clients and invalidates
   fixture/events/completions. Confirm stale completion cannot satisfy the next
   session and restarting PIE permits a fresh connection and fixture reset.
7. Repeat the shared smoke against standalone and a packaged Development game.
   Retain each mode's evidence separately. A passing Editor smoke cannot stand in
   for packaged execution.

For standalone with the existing content:

```sh
ath launch "G:/Epic Games/UE_5.8/Engine/Binaries/Win64/UnrealEditor.exe" --engine unreal --project "E:/Personal/Unreal/KinecadeSandbox/KinecadeSandbox.uproject" --map /Game/System/FrontEnd/Maps/L_LyraFrontEnd --port 8787 --detach
ath smoke <ath>/Tools~/ath-exe-client/fixture-smoke.json --port 8787 --project-root E:/Personal/Unreal/KinecadeSandbox --trace
```

Use the host's normal Unreal AutomationTool BuildCookRun pipeline for packaged
Development. Keep compiled/cooked outputs in one ignored active build directory
and retain only compact reports/logs as proof. Launch the resulting executable
with `--engine unreal` and repeat the same smoke. An instrumented release-shaped
build uses Development with production-like content/settings; normal Shipping
continues to exclude ATH. Enabling instrumentation in a real Shipping target is
not supported by this plugin's default descriptor.

### Packaged Development validation

The actual KinecadeSandbox package completed successfully and its rendered
27-step shared-client smoke passed. The testable local artifact is
`E:/Personal/Unreal/KinecadeSandbox/Saved/ATHBuild/active/Windows/LyraGame.exe`.
The successful AutomationTool run took 107.71 seconds after the executable had
compiled separately. Repeat the tested scope using the existing Lyra target/map:

```powershell
$athEngine = '<engine-root>'
$athHostRoot = '<KinecadeSandbox>'
$athProjectFile = "$athHostRoot/KinecadeSandbox.uproject"
$athCookOverrides = '-ini:Engine:[/Script/WindowsTargetPlatform.WindowsTargetSettings]:-D3D11TargetedShaderFormats=PCD3D_SM5 -ini:Engine:[/Script/WindowsTargetPlatform.WindowsTargetSettings]:-VulkanTargetedShaderFormats=SF_VULKAN_SM6'
& "$athEngine/Engine/Build/BatchFiles/Build.bat" LyraGame Win64 Development "-Project=$athProjectFile" -WaitMutex
& "$athEngine/Engine/Build/BatchFiles/RunUAT.bat" BuildCookRun "-project=$athProjectFile" -target=LyraGame -platform=Win64 -clientconfig=Development -skipbuild -cook -iterate -map=/Game/System/DefaultEditorMap/L_DefaultEditorOverview -CookMapsOnly -stage -pak -archive "-archivedirectory=$athHostRoot/Saved/ATHBuild/active" "-AdditionalCookerOptions=$athCookOverrides" -unattended -utf8output
```

These cooker-only ini overrides remove D3D11 SM5 and Vulkan SM6 from this run;
they do not edit host configuration files. The verified artifact is Win64 DX12
with the existing overview map. It does not establish other rendering targets or
every Lyra experience. Do not add `-SkipCookingEditorContent` on this stock host:
the initial cook produced 26 errors because Landmass startup content still
referenced Engine EditorMaterials. Removing that filter fixed the cook; the
failed attempt is retained alongside the successful retry.

Launch the archive and run the same rendered consumer acceptance suite:

```sh
ath launch <KinecadeSandbox>/Saved/ATHBuild/active/Windows/LyraGame.exe --engine unreal --map /Game/System/DefaultEditorMap/L_DefaultEditorOverview --port 8787 --detach
node <ath>/Tools~/unreal-acceptance/kinecade.js --project-root <KinecadeSandbox> --port 8787 --rendered
```

The recorded packaged smoke passed 27 steps in 1,009 ms. Its report is
`trace/runs/c66a846f-79bf-402a-854c-8a63c775fcb8.json` beneath the consuming
project's `.captain-sdlc/`; its viewed PNG is 640x360, 295,909 bytes. See
[verification.md](verification.md) for exact artifact references and provenance.

### Native automation and current mode coverage

Use `Automation RunTests StartsWith:ATH` for plugin-native tests and retain the
exported results. A bare `ATH` filter also selects unrelated engine test names
such as `Path.*`; the prefix filter bounds the run to this plugin's suite.
No native test is counted as passed merely
because its source exists. Lifecycle, UObject invalidation and packaged gates
need actual engine execution; see the verification matrix.

The completed local validation compiled `AthRuntime` and `AthEditor` in the real
KinecadeSandbox `LyraEditor` target, passed nine native tests, drove a 26-step
external PIE smoke and 27-step rendered PIE, standalone and packaged Development
smokes. Viewed PNGs and valid Captain traces were captured in all rendered modes;
the offscreen PIE viewport was 457x89, and standalone/package were 640x360. Normal
Shipping also compiled successfully in 715.76 seconds, with a Shipping receipt
and zero ATH products/link inputs. The staged Shipping process stayed alive with
the ATH enable flag supplied, and three loopback probes found no ATH endpoint.
The runtime proof is `trace/shipping-exclusion.json`. Remote CI remains pending. See
[verification.md](verification.md) for exact report paths, retained failures and
open gates; compilation exclusion is distinct from the Shipping runtime check.

## Extension contract

Register a shared native `IAthHost` with `FAthRuntime::RegisterHost`, or expose
cached state and delegates through `UAthHostSubsystem`. Every registration,
mutation, query and teardown belongs on the Game Thread; validate UObject
references on use. Getters remain cheap and side-effect-free. Unregister or
replace the host at its actual lifecycle boundary. The runtime releases prior
ownership and clears observations so old events or completions do not leak into
a replacement session. See [cross-engine-architecture.md](cross-engine-architecture.md).

`IAthHost::Shutdown()` is an explicit, idempotent cancellation hook invoked before
replacement/unregistration, even when the caller still retains a shared owner.
For delayed callbacks capture `FAthRuntime::GetSessionGeneration()` when starting
work, then call `ObserveEventForSession(generation, name)` or
`CompleteAsyncForSession(generation, name, id, success, error)` on the Game Thread.
Prior-session callbacks are ignored after full reset, replacement or world cleanup.
Immediate `ObserveEvent`/`CompleteAsync` address the current session. Adapters must
still cancel game-owned work and avoid dereferencing destroyed UObjects; ATH
cannot preempt a native handler that blocks the Game Thread.
Screenshot capture uses the selected game's viewport and initially writes under
`<project>/Saved/ATH/Screenshots`; the shared client copies complete evidence into
the consuming project's Captain trace/media directory. NullRHI, dedicated server
or an unavailable viewport returns `rendering_unavailable`. Screenshot requests
are engine-global, so multiple simultaneous PIE worlds withhold screenshot
capability and return `multiple_pie_worlds`; command/state selection remains
deterministic through `-ath-pie-instance`.

The editor-only `AthEditor` module supplies local `ath.editor.pie.start` and
`ath.editor.pie.stop` controls for automation. Those editor console controls do
not expose arbitrary remote console execution. Editor PIE and standalone remain
separate lifecycle modes and need separate acceptance evidence.
