# Visual verification helpers

## Native batch audits

After building the native analyzer, fetching the pinned model, and generating a
real-font fixture with `font-fixtures.py`, run:

```powershell
python Tools~/visual-verification/check-batch-workloads.py `
  --analyzer $nativeAnalyzerPath --model $verifiedModelPath `
  --fixture $representativeFixturePath --out $freshOutputDirectory
```

The output directory must be new. The helper runs the actual `ath-exe visual
audit` command against 100, 256 and 1,024 screenshots, each containing four
declared regions copied from the real-font fixture. It verifies every receipt,
artifact digest, result count and deterministic screen order; exercises missing
and malformed screenshots at the beginning, middle and end; and checks a real
one-millisecond per-screen deadline. It also compares one eight-screen worker
with eight single-screen processes to report detector reuse costs.

`verification.json` records runtime source and tool hashes, process-inclusive
timings, output and evidence sizes, and sampled Node/process-tree resident
memory. Default acceptance limits are 512 MiB Node RSS, 1 GiB process-tree RSS,
4 MiB per command output stream, and 512 MiB evidence per workload. Sampling
cannot establish an unsampled allocation peak. These repeated real-font regions
exercise batch transport and evidence handling; they do not establish broader
detector coverage. The Windows/Linux workflow runs this helper independently.

## Unity / RuntimeSmoke

This is an optional verification helper, not another smoke runner or a new acceptance gate. It runs the repository's existing NUnit tests. Authored runtime acceptance still belongs to `RuntimeSmokeSuiteExecutor` and `RuntimeSmokeCoordinator`. The engine-free native build/tests remain independently usable without Unity.

The verified environment was Unity **6000.3.10f1**, Unity Test Framework **1.6.0**, and the real installed MCP/console dependencies from a consuming project. Unity 2022.3 compiled the sources, but Test Framework 1.4.3 produced inconsistent teardown XML (passing totals with a failed final testcase); that report was not accepted as green. This helper checks individual testcase nodes as well as the process exit and aggregate result.

## Inputs

- An installed/licensed Unity 6000.3 editor executable.
- The built `ath-visual` executable and its normal native dependency files.
- The verified PP-OCR ONNX weights described in `Documentation~/visual-dependencies.md`.
- Generated `representative-1080-light.png` and its adjacent `.json` manifest from the native Arial fixture generator. Use the original manifest with representative glyph evidence. The integration test changes only the threshold or representative-glyph declaration for its negative cases.
- A consuming project's actual MCP dependency DLL directory (including `McpPlugin.dll`, `McpPlugin.Common.dll`, `ReflectorNet.dll` and their dependencies), and its real `IngameDebugConsole.Runtime.dll`. Do not substitute source stubs. The original validation used the installed MCP 0.81 host dependencies; this does not establish compatibility with every MCP version.
- A **fresh** disposable project directory. Existing directories are refused, and the helper never deletes a project.

From PowerShell, supply your local paths:

```powershell
./Tools~/visual-verification/verify-unity.ps1 `
  -UnityEditor $unityEditorPath `
  -Analyzer $nativeAnalyzerPath `
  -Model $verifiedModelPath `
  -Fixture $representativeFixturePath `
  -McpDependencyDirectory $installedMcpDllDirectory `
  -ConsoleAssembly $installedConsoleAssembly `
  -ProjectDirectory $freshTemporaryProjectPath
```

Use `-PrepareOnly` to inspect the exact source/dependency setup without launching Unity. The setup includes standard Unity package dependencies; their first installation may require access to Unity's package registry. Windows launches use `Start-Process -WindowStyle Hidden`. The helper restores its caller's environment variables, bounds the owned Unity process lifetime, and retains reports under the generated project's `Verification/` directory.

## Exact source selection

The isolated project copies all `Runtime/**/*.cs`; all `Tests/Editor/RuntimeSmoke*Tests.cs`, including `RuntimeSmokeEditorWindowViewModelTests.cs`; and `AthTraceWriterTests.cs`. No RuntimeSmoke test class is excluded.

The Editor assembly contains the actual `AssemblyInfo.cs`, `AthTraceWriter.cs`, `AthTraceEmitter.cs`, `AthMediaUtil.cs`, `Tool_AthTraceEmit.cs`, `Tool_AthRuntimeSmoke.cs`, `RuntimeSmokeWindow.cs`, and `RuntimeSmokeEditorWindowViewModel.cs`. This verifies both MCP tools and the window source against real dependencies, and executes the existing window reducer/projection tests without starting an MCP server or opening a window. Existing assembly names and the test assembly declaration are retained. Copied source hashes and actual native/model/dependency hashes are recorded; the repository source is not edited.

The optional native integration test receives `ATH_VISUAL_ANALYZER`, `ATH_VISUAL_MODEL`, and `ATH_VISUAL_FIXTURE`. It runs eleven required-entry suites through the authoritative RuntimeSmoke suite executor: pass, fail, inconclusive, corrupt PNG, wrong correlation, missing analyzer, unsupported adjustable-size capability, unresolved scaling profiles, missing policy source, omitted element IDs, and boundless expected elements. It also exercises the configured stock registry through checkpoint/capture/resume, including missing configuration and missing capture. It verifies existing capture-writer receipts, single terminal outcomes, and linked JSONL/JUnit evidence. Missing test inputs are explicitly skipped by the test itself; this full-verification helper does not treat a skipped native test as successful integration proof.

## Retained evidence and limits

`Verification/setup.json` records inputs and source hashes. `results.xml` and `editor.log` are Unity's actual outputs. `verification.json` records the exit, individual testcase outcomes and input/source integrity checks. A zero-test run or contradictory aggregate/case outcomes cannot pass.

The finalized registry run executed **110 tests, all passing, zero skipped**. These tests use an isolated project and pre-existing PNGs; they do not prove live consuming-game capture, built-player launch, interactive Editor-window rendering, or complete UI coverage. Running the same NUnit tests in an existing consuming Unity project is also supported: configure the three environment variables before starting Unity, install the project's normal AITH dependencies, and run its EditMode tests. No additional product acceptance criteria are introduced by this helper.
