# RuntimeSmoke usage and authoring

RuntimeSmoke is AITH's supported smoke orchestration and acceptance contract. A smoke is a checked-in, deterministic workflow executed through the same coordinator whether the target is an Editor session or a built development player. Typed adapter results and the structured run result establish PASS. Commands, console markers, screenshots, log matches, and manually assembled wait sequences can help investigate behavior; they do not independently establish smoke acceptance.

This guide describes the AITH package API and operator surfaces. Each consuming game owns its workflow documents, game-domain bridge, predicates, fixtures, and build entrypoints. A host is ready to run a workflow only after it registers the compiled catalog, step adapter, cleanup, transcript sink, and any declared checkpoint/investigation adapters. AITH package tests do not prove a host bridge or player build. Consumer quickstarts must name tested workflow IDs and revisions.

## Discover and run a host workflow

In Unity Editor, open **Window > AI Test Harness > RuntimeSmoke**. Choose target and mode, filter the registered workflows, select one, and use Start. The window reads the same `RuntimeSmokeRunRegistry` state as MCP, including the active step, checkpoint, transcript, result, and artifacts. If the host has not registered a catalog, the window reports that state; do not substitute a console sequence as acceptance.

Progress is weighted by the compiled steps' declared `seconds` value when present; other steps count as one unit. The percent reports completed weight, while active execution remains indeterminate because an adapter step can take an unknown amount of time. MCP status and the Editor window expose the same completed weight, total weight, and percent.

The Editor MCP tools operate on the same registry:

```text
ath-runtime-smoke-list(target="editor", mode="fsd")
ath-runtime-smoke-run(workflowId="workflows/<host-workflow>", target="editor", mode="fsd", correlationId="<new-unique-id>")
ath-runtime-smoke-status(correlationId="<same-id>")
```

Use the host's exact workflow ID. Generate a new correlation for each attempt and retain it for every status, resume, cancel, export, and investigation request in that attempt. The run tool returns immediately; poll status until a terminal state or review checkpoint. Never reuse a failed, cancelled, timed-out, or already-claimed correlation.

The development-only in-game command surface is `runtime_smoke.list`, `runtime_smoke.run`, `runtime_smoke.status`, `runtime_smoke.cancel`, `runtime_smoke.resume`, and `runtime_smoke.export_context`. It is a transport to declared workflows, not an arbitrary script executor. Normal release builds must not expose this surface. Use `ath-exe`'s direct `cmd`, `state`, `wait`, and `snap` commands only for bounded diagnostics; their output is not a RuntimeSmoke PASS.

## Author workflows and fragments

Put host-authored JSON and Unity sidecars under the host's `Assets/.../RuntimeSmokes/` tree. A top-level document has a stable `workflows/...` ID, one proposition, a test class of `RuntimeSmoke`, supported targets, a mode, required capabilities, bounded deadline, start/terminal steps, and ordered actions/assertions. Reusable setup, action, and assertion fragments are included into that run: they do not start their own run or publish a terminal result. Include expansion and validation finish before gameplay mutation.

Example workflow shape (the `host.*` capabilities and adapter responses are supplied by the consuming project):

```json
{
  "schemaVersion": 1,
  "id": "workflows/player-alive",
  "kind": "workflow",
  "testClass": "RuntimeSmoke",
  "mode": "fsd",
  "proposition": "The declared gameplay fixture starts with a living player.",
  "excludedBehavior": "save/load persistence",
  "scenario": "flat.example_fixture",
  "seed": "7319",
  "targets": ["editor", "development"],
  "requires": ["ath.start", "host.player.alive"],
  "timeoutSeconds": 120,
  "steps": [
    { "id": "begin", "type": "start" },
    { "id": "assert-player-alive", "type": "assert", "assertion": {
      "assertionSchemaVersion": 1,
      "id": "player-alive",
      "name": "Fixture player is alive",
      "predicate": "host.player.alive",
      "comparison": "equals",
      "expected": true
    }},
    { "id": "finish", "type": "terminal" }
  ]
}
```

Compile through `RuntimeSmokeDocumentCompiler.Compile` with the actual target, mode, document source, and registered capability set. Reject compiler diagnostics before execution. The compiler validates includes, cycles, expanded step IDs, variables, parameters, target/mode compatibility, capability availability, bounded `sleep`, typed assertion schema, and build-suite entries, and returns a canonical hash independent of JSON formatting and map-key order. Keep scenario and seed authored in the script when they define the fixture; a transport cannot silently substitute different values.

Fragments inherit the parent's run ID, correlation, artifact directory, target, deadline, cancellation, and transcript. `sleep` is only bounded pacing with an authored reason. It never proves readiness or completion. Read [the RuntimeSmoke contract](runtime-smokes.md) for the complete document schema and adapter/compiler API.

## Assertions, modes, and review

Assertions use schema version 1 and typed values: missing (adapter-only), null, boolean, string, signed 64-bit integer, finite decimal, ordered array, or map with unique ordinal string keys. The AITH evaluator owns comparisons. `equals` is type-sensitive deep equality; numeric operators accept numeric values; tolerance boundaries are inclusive and require finite decimal tolerance values. Redaction and display caps are applied only after comparison. A missing predicate value, adapter exception, timeout, invalid schema, wrong correlation, or mismatch is a structured failure and cannot pass.

`fsd` is deterministic unattended execution. `ai_observed` and `hitl` require an Editor or development target and a declared interactive workflow; a headless run is diagnostic and cannot satisfy visible-player or human acceptance. A `checkpoint` step pauses through the host's review adapter. Inspect the named checkpoint and evidence before issuing `ath-runtime-smoke-resume` with the same correlation and exact checkpoint ID. Resume is single-use; stale, duplicate, or mismatched resumes fail. Cancel or deadline cleanup must restore host state.

The coordinator supports checkpoint/resume and cleanup. The composed human-review pattern where a human resumes, an intervening predicate is proven, and a `pauseAfter` rule pauses a second time remains a separate integration gate until the host and coordinator state machine prove it. Do not claim that pattern merely because one checkpoint can pause and resume.

## Suites and built-player runs

A host may validate and execute a required suite through `RuntimeSmokeSuiteExecutor`. Suite entries declare workflow IDs and requiredness; the suite validator rejects workflows that are not FSD for a build gate, unsupported target/mode combinations, and invalid deadlines. A required entry failure fails the suite. A package suite pass alone does not approve a build for human testing: the host verifier must bind the required suite result to the exact artifact's hash and provenance. A missing host build-gate integration remains incomplete.

The AITH Node client runs a declared FSD workflow against a specific development or `smoke_harness` executable:

```powershell
node Tools~/ath-exe-client/ath-exe.js runtime-smoke path/to/launch-spec.json
```

The spec must supply `hostRoot`, `outputRoot`, `authoredRoot`, `executablePath`, `buildEvidencePath`, `runId`, `correlationId`, `workflowId`, `canonicalScriptHash` (the compiled expanded workflow hash emitted by the host/catalog/build declaration), `timeoutSeconds`, `target`, `mode: "fsd"`, and non-empty unique ordered `requiredStepIds`; `seed` and `suiteId` are optional. The executable and evidence must stay inside the host root; evidence must prove that exact executable, revision, profile/content hashes, backend, stripping, and lane. The launcher claims a fresh output directory, writes launch/process/watch evidence, starts the artifact with the signed envelope, and accepts only a fresh matching armed record, ordered transcript, one terminal result, clean process exit, and valid provenance. The command exits zero only when `accepted` is true. Do not use separate `ath-exe cmd/wait` steps as the build acceptance path.

### Reusable built-artifact suite and build gate

Hosts compile and expand their checked-in suite document with `RuntimeSmokeSuiteValidator`, then pass that validated, ordered workflow list to the public AITH CLI. The host owns document discovery, suite membership, target/capability policy, and the build-evidence path; AITH owns the isolated artifact copy, per-workflow launches, copy attestation, aggregation, and JUnit projection.

```powershell
node Tools~/ath-exe-client/ath-exe.js runtime-smoke-suite path/to/runtime-smoke-suite-spec.json
```

The version 1 suite spec has this shape:

```json
{
  "schemaVersion": 1,
  "suiteId": "suites/building-parity",
  "target": "development",
  "hostRoot": "C:/project",
  "authoredRoot": "C:/project/Assets/RuntimeSmokes",
  "sourceArtifactRoot": "C:/project/Builds/Development",
  "sourceExecutablePath": "C:/project/Builds/Development/Game.exe",
  "buildEvidencePath": "C:/project/Artifacts/DirigibleEaVerifier/build-development.json",
  "outputDirectory": "C:/project/Artifacts/RuntimeSmokeGates/build-2026-09-24T120000Z",
  "maximumSuiteSeconds": 900,
  "entries": [
    {
      "entryId": "building-loop-7x7",
      "required": true,
      "workflowId": "workflows/building-loop-7x7",
      "canonicalScriptHash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "runId": "building-loop-7x7-run-20260924T120000Z",
      "correlationId": "building-loop-7x7-20260924T120000Z",
      "timeoutSeconds": 300,
      "target": "development",
      "mode": "fsd",
      "requiredStepIds": ["fixture-ready", "assertions-complete"],
      "scenario": "world-baseline",
      "seed": 704,
      "requiredCapabilities": ["ath.start", "fixture.ready"],
      "proposition": "The baseline world is ready and satisfies its assertions.",
      "parityContract": "dirigible.baseline-world.v1"
    }
  ]
}
```

The output directory must be new and remain inside the host, separate from authored inputs and the source build. Each suite entry must have a unique ID, workflow, run ID, and correlation; entries must be FSD and target the suite lane. `requiredCapabilities` is an optional unique list of stable capability IDs, `proposition` is an optional bounded description, and `parityContract` is an optional stable host-authored contract ID (for example `dirigible.baseline-world.v1`). AITH validates and carries these declarations into gate JSON and JUnit testcase properties; the host compiler remains responsible for validating contract content and workflow compatibility. The canonical hash must come from the host compiler's expanded workflow, never a raw authored-file hash. AITH validates that the original build evidence names and hashes the exact source executable, recursively rejects links in the artifact tree, and copies the whole player directory to `artifact-copy/payload/`. It hashes source and copy trees plus both executables, snapshots build evidence for the copied executable, writes `runtime-smoke-copy-attestation.json`, and rechecks source, copy, attestation, and evidence before, between, and after workflow launches.

A persistence workflow may declare a `continuation` when its assertion requires two or more separate player processes. Keep the logical suite entry's ordinary unique identity and describe each process under `continuation.stages`:

```json
{
  "entryId": "bedroll-persistence",
  "required": true,
  "workflowId": "workflows/bedroll-save",
  "canonicalScriptHash": "1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "runId": "bedroll-save-20260926",
  "correlationId": "bedroll-save-correlation-20260926",
  "timeoutSeconds": 600,
  "target": "development",
  "mode": "fsd",
  "requiredStepIds": ["profile-saved"],
  "continuation": {
    "continuationId": "bedroll-save-reload",
    "stages": [
      { "stageId": "save", "workflowId": "workflows/bedroll-save", "canonicalScriptHash": "1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "runId": "bedroll-save-20260926", "correlationId": "bedroll-save-correlation-20260926", "requiredStepIds": ["profile-saved"], "profileAction": "create" },
      { "stageId": "reload", "workflowId": "workflows/bedroll-reload", "canonicalScriptHash": "2123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "runId": "bedroll-reload-20260926", "correlationId": "bedroll-reload-correlation-20260926", "requiredStepIds": ["same-actor-and-beds-restored"], "profileAction": "load" }
    ]
  }
}
```

The first stage must create the profile and every later stage loads that same run-scoped profile into a fresh process. AITH creates an opaque profile ID, places the `-autoload` argument on load stages, and binds continuation ID, ordered stage identity, action, and profile ID into each process launch token. The typed player `RuntimeSmokeStepContext` exposes the validated continuation fields to declared host adapters. Each stage has its own fresh evidence directory, transcript, process watch, correlation, and terminal result; the logical entry passes only if every stage passes in order. Aggregate gate evidence contains the stage records and a SHA-256 of the generated profile ID, not its raw value. Never put `-execcmds` or arbitrary command text in the suite continuation.

Workflows run in declared order against that same byte-identical fresh copy, each with an independent run directory, correlation, transcript, result, and cleanup. The suite deadline caps each workflow's process timeout. The gate passes only if artifact provenance remains intact and every required entry passes; optional nonpasses remain visible as skipped JUnit cases. The atomic outputs are `runtime-smoke-gate.json` and `junit.xml`. The gate binds source/copy paths and hashes, source executable timestamp, original build-evidence hash and build revision/version/profile/content/backend/stripping/lane, the attestation receipt, and every workflow's result, transcript, player-log hash, and evidence paths. Assertion/predicate failures become JUnit failures; runtime, deadline, provenance, or protocol failures become JUnit errors. Gate-level integrity failures also produce a JUnit error even when every workflow had previously passed.

Treat the gate JSON plus each run's structured result/transcript as authority. Never promote an artifact from an Editor-only result, a prior player, a stale source revision, or a marker-only log. The AITH suite API does not replace host build-profile validation, build-gate declaration, or human review; the consumer must invoke this runner on the exact artifact just built and preserve these outputs.

The run owns only its newly claimed directory under the selected run output root, normally `Artifacts/RuntimeSmokes/<runId>/`. It may contain `scratch/`, `logs/`, `captures/`, launch and process records, `transcript.jsonl`, `result.json`, and failure context. These are run-local artifacts, not disposable inputs. The transcript sink and launcher reject authored-input overlap, path traversal, symlink/junction escapes, and existing run directories. AITH does not automatically delete authored workflows, previous evidence, or conflicting run directories. Preserve evidence for diagnosis and choose a fresh run ID/correlation for a rerun.

## Investigate failures and rerun safely

Treat `result.json` and the JSONL transcript as the run authority. For a failure, inspect the failed step/assertion, exact expected and actual typed values or digests, classification/failure code, correlation, script hash, and declared evidence. In Editor, use the failure panel or `ath-runtime-smoke-export-context`; run `ath-runtime-smoke-investigate` only with an action declared by that failed workflow. Investigation appends typed output to the failed run and cannot change its terminal result. Captures must be declared and remain under the run-local `captures/` directory.

Decide whether the defect is in the workflow/test contract or the product. Repair a script when its fixture, action, predicate, or acceptance rule is wrong; repair product code when the declared behavior is broken. Keep any investigation mutation scoped and authored through the project workflow. Then start a new correlation and preserve both evidence bundles. Never retry under an existing evidence identity or weaken assertions to make the run green.

## Related references and current limits

- [RuntimeSmoke contracts](runtime-smokes.md): document, adapter, assertion, suite, evidence, and containment APIs.
- [EXE transport](exe-harness.md): low-level remote console diagnostics and build setup; use the RuntimeSmoke launch command for smoke acceptance.
- [Host adapter contract](adapter-contract.md): lifecycle and state integration.
- [Migration audit](runtime-smoke-migration-audit.md): AITH documentation dispositions and known stale references.

The AITH package has focused compiler/coordinator/assertion/registry/window/runner tests. Consumer Editor/MCP, visible-player, human-review, and exact-artifact required-suite acceptance must be recorded by each host at its tested revisions. This package guide does not claim those consumer gates have passed.
