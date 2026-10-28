# Cross-engine migration verification

Recorded 2026-10-28 against source based on `2c93c78` plus the migration working
tree. Evidence explicitly identifies `2c93c78+working-tree`; it does not claim a
committed release revision, signed Captain implementation completion or remote
CI success. Shared tests, real Unity regression, native Unreal automation and
real Unreal client smokes, including a packaged Development executable, have
completed. Local runtime acceptance, including normal Shipping exclusion, is
established for the tested modes. Remote CI remains open, so delivery is not yet
CI-certified.

## Completed checks

| Gate | Actual outcome | Scope / evidence |
|---|---|---|
| Shared Node protocol/client/CLI/MCP/trace suite | **58 passed** | Full shared suite, including original protocol compatibility, fake loopback endpoints and copied tool-directory CLI/MCP operation; version comes from the tool package itself, with no parent UPM descriptor dependency. No engine required. |
| Portable Unreal installer and acceptance specifications | **15 passed** | Real temporary junction/link creation, verify-only, dry-run/conflict preservation, full default fixture composition and custom-scenario ordering. |
| Pure C# generic host contracts | **9 passed** | Engine-independent production-source compilation and checks for ownership, legacy events, bounded observations and session generations. |
| Unity EditMode regression | **34/34 passed** | Final actual Unity 6000.3.10f1 run; XML start time `2026-10-28 22:22:44Z`. Includes valid/malformed Unicode JSON, metadata ownership, replacement, duplicate bridges and shutdown disposal. The isolated host embeds the worktree's source; final source hashes match. |
| Unreal host compilation | **Succeeded** | Existing KinecadeSandbox `LyraEditor` target compiled the native `AthRuntime` and editor-only `AthEditor` modules against Unreal 5.8.3. |
| Native Unreal automation | **9/9 passed**, zero failed/not run | Consumer `.captain-sdlc/trace/native-automation/index.json`, exported HTML report and engine log. Use the bounded `StartsWith:ATH` filter. |
| Real PIE attachment and command/state/wait/event/async smoke | **26 steps passed** | Consumer `trace/runs/324e557c-a1fd-49f5-a491-05f53a53d67c.json`; external shared Node client against real KinecadeSandbox PIE. |
| Real rendered PIE smoke | **27 steps passed**, 1,810 ms | Consumer `trace/runs/b434f234-1cbb-4dd8-8dbc-c157960476a6.json`; includes a captured and viewed Game viewport PNG, `trace/media/kinecade-acceptance_EB791CDC425AF5F70EB237A126B1001B.png` (457x89, 62,155 bytes). This is the actual offscreen editor viewport size. |
| Real standalone rendered smoke | **27 steps passed** | Consumer `trace/runs/617d4fb5-e2fd-441d-b0d3-152f09859138.json`; shared client plus native runtime state, fixture and screenshot. |
| Rendered screenshot evidence | **Captured and viewed** | Standalone `trace/media/kinecade-acceptance_9A1DFCDA4499CAAB2EA1A48718941860.png`: 640x360, 275,033 bytes. Packaged `trace/media/kinecade-acceptance_4BC54EB843DCE43F99A5AE90DDC526B5.png`: 640x360, 295,909 bytes. Both are real rendered images. |
| Captain SDLC evidence | **Valid schema-v1 events emitted** | Consumer `trace/2026-10-28.jsonl` retains pass/fail, engine 5.8.3, Development mode, elapsed time, working-tree reference and trace-relative report/media artifacts. |
| Development game executable compilation | **Succeeded**, 132.90 seconds | Real Win64 Development executable built before the packaging run. |
| Development cook/stage/package/archive | **BUILD SUCCESSFUL**, 107.71 seconds | Successful AutomationTool retry cooked the existing overview map and retained a testable package; DX12-only validation scope and the initial failed cook are detailed below. |
| Real packaged Development rendered smoke | **27 steps passed**, 1,009 ms | Consumer `trace/runs/c66a846f-79bf-402a-854c-8a63c775fcb8.json`, including fixture/waits/events/correlated async, native state, screenshot and Captain trace. |
| Packaged module startup and graceful shutdown | **Passed**, process exit 0 | `trace/packaged-shutdown.log` records the enabled loopback listener starting on port 8793, an ordinary engine quit request and `ATH shutdown complete`. |
| Normal Shipping compilation / link exclusion | **Succeeded**, 715.76 seconds | Shipping receipt reports configuration `Shipping`; zero ATH products or link inputs. |
| Normal Shipping runtime exclusion | **Passed** | `trace/shipping-exclusion.json`: staged Shipping executable remained alive with the ATH enable flag supplied; all three loopback probes were unreachable. Receipt/link checks separately confirm zero ATH code inputs. |

All `trace/...` paths above are relative to the consuming project's
`.captain-sdlc/` directory. On this validation machine that project is
`E:/Personal/Unreal/KinecadeSandbox`. Runtime evidence is retained there rather
than in ATH's source tree.

The testable packaged Development build is
`E:/Personal/Unreal/KinecadeSandbox/Saved/ATHBuild/active/Windows/LyraGame.exe`.
It is a local Win64 DX12 acceptance artifact, not a published release.

The final Unity regression was strengthened before its successful run: a test
destroys the active bridge with a live registered host, verifies that destroying
a duplicate bridge does not tear down that host, and uses a marker to prove real
PlayMode-exit cleanup. The old behavior was reproduced as a failing test before
the fix. `AthBridge.OnDestroy` now unregisters the host only for the active bridge.

The completed native tests are:

- `ATH.Editor.PIERoundTripAndRestart`.
- `ATH.Protocol.ArgumentsAndBounds`.
- `ATH.Protocol.EscapingAndEnvelope`.
- `ATH.Runtime.AdapterOwnershipAndGeneration`.
- `ATH.Runtime.DispatcherErrorsAndCorrelation`.
- `ATH.Runtime.NativeCommandRegistration`.
- `ATH.Runtime.TransportLifecycleBounds`.
- `ATH.Runtime.WorldInvalidationAndPendingOperations`.
- `ATH.Session.StickyEventsAndAsync`.

These tests exercise real module loading, Game Thread dispatch, native state and
registration, world/UObject invalidation, pending-operation handling, transport
bounds and PIE restart. The external PIE smoke additionally establishes that the
shared Node client works against a real engine endpoint. Rendered evidence was
captured in PIE, standalone and the packaged Development executable. Screenshot
dimensions follow the active Game viewport; the offscreen PIE layout was smaller
than the 640x360 standalone and packaged viewports.

## Retained failures and limits

The initial rendered screenshot check hit its 10-second client deadline, leaving
a failed smoke event and a valid PNG that arrived late. That evidence was retained.
The rendered acceptance step was given an explicit 30-second deadline and rerun;
the successful 27-step smoke completed in 1,061 ms. A successful rerun does not
erase the original timeout.

A bare `Automation RunTests ATH` selector also matches unrelated engine tests
such as `Path.*`. The initial run selected 392 tests. It was followed by
`Automation RunTests StartsWith:ATH`; the bounded nine-test report above is the
acceptance result. Do not attribute unrelated engine tests to ATH.

The first packaged cook failed with 26 errors because `-SkipCookingEditorContent`
filtered Engine EditorMaterials still referenced by the stock host's Landmass
startup content. The successful retry removed that filter. Validation cooked
the existing overview map as its explicit entry, using temporary cooker overrides
to exclude D3D11 SM5 and Vulkan SM6 shader targets. Lyra's normal asset manager
also cooked its referenced primary assets and maps. Runtime acceptance exercised
the overview map. No host ini files were edited. This is
Win64 DX12 packaged acceptance, not validation of those other rendering targets
or every Lyra experience. See [the repeatable package command](unreal.md#packaged-development-validation).

| Remaining gate | Current state | Evidence still required |
|---|---|---|
| Instrumented release-shaped scope | Packaged Development acceptance passed for one Win64 DX12 map | Broader production content/platform coverage remains outside the demonstrated slice; ordinary Shipping debugging is not enabled. |
| Remote CI | Workflow added; **not run remotely** | Actual pipeline result at the delivered commit. |
| Full delivery acceptance | Local tested runtime modes passed; **not CI green** | Run the remote workflow at a delivered commit. No commit or publication is authorized by this record. |

## Reproduce Unity regression without importing generated hosts

`Tools~/unity-regression/setup.ps1` stages an isolated Unity test project. It uses
an existing dependency project's cached Unity packages and plugin assets, while
embedding an exact copy of this worktree's `Runtime/`, `Editor/`, `Tests/` and
package descriptor. No host gameplay is copied. The local validation used
BeforeTheShade's dependency caches and Unity 6000.3.10f1; it did not edit or run
the primary BeforeTheShade project.

```powershell
& '<ath>/Tools~/unity-regression/setup.ps1' -DependencyProject '<existing-unity-project>' -OutputRoot '<ignored-active-output>/unity'
& '<Unity.exe>' -batchmode -nographics -projectPath '<ignored-active-output>/unity' -runTests -testPlatform EditMode -testFilter 'LlamaBrainLabs.Ath.Editor.Tests' -testResults '<ignored-active-output>/unity-results.xml' -logFile '<ignored-active-output>/unity.log' --UNITY_MCP_KEEP_CONNECTED=false --UNITY_MCP_START_SERVER=false --CI=true
```

The three trailing flags disable the external Unity MCP dependency's automatic
connection/server behavior during CI validation. They do not alter ATH runtime
behavior. A direct local UPM reference to the ATH checkout would recursively
import the generated test host under `artifacts/`; the embedded source snapshot
avoids that recursion and must be refreshed after source changes. Keep generated
outputs in the task's fixed ignored active slot, and retain compact XML/log proof.

See [shared tooling](shared-tooling.md), [Unreal acceptance](unreal.md) and
[architecture](cross-engine-architecture.md) for commands and compatibility scope.
