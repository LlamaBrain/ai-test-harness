# RuntimeSmoke contracts

For operator flow, workflow authoring, mode selection, evidence ownership, and
failure handling, start with the [RuntimeSmoke usage guide](runtime-smoke-usage.md).
This page is the package contract reference; it does not establish consumer
integration or acceptance gates.

RuntimeSmoke documents are typed, checked-in workflows interpreted by the AITH runtime package. A host supplies the workflow's declared capabilities through `RuntimeSmokeCompileOptions.AvailableCapabilities`, implements `IRuntimeSmokeStepAdapter`, and provides cleanup and transcript-sink implementations. AITH owns include expansion, capability/target/mode validation, typed assertions, correlation checks, the workflow deadline, cleanup, and the terminal result.

## Workflow shape

```json
{
  "schemaVersion": 1,
  "id": "workflows/gameplay-ready",
  "kind": "workflow",
  "testClass": "RuntimeSmoke",
  "mode": "fsd",
  "proposition": "The gameplay scene is active and the player is alive.",
  "excludedBehavior": "save/load persistence",
  "scenario": "flat.colony_survival_7",
  "seed": "7319",
  "targets": ["editor", "development"],
  "requires": ["ath.start", "host.gameplay", "host.player.alive"],
  "timeoutSeconds": 120,
  "steps": [
    { "id": "begin", "type": "start" },
    {
      "id": "player-alive",
      "type": "assert",
      "assertion": {
        "assertionSchemaVersion": 1,
        "id": "player-alive",
        "name": "Player is alive",
        "predicate": "host.player.alive",
        "comparison": "equals",
        "expected": true
      }
    },
    { "id": "finish", "type": "terminal" }
  ]
}
```

Compile before executing any operation:

```csharp
var compiled = RuntimeSmokeDocumentCompiler.Compile(
    json, documentSource, RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
    new RuntimeSmokeCompileOptions { AvailableCapabilities = registeredCapabilities });
if (!compiled.Success) throw new InvalidOperationException("RuntimeSmoke document is invalid.");
```

`scenario` and `seed` are optional authored fixture identifiers. When present they are included in the canonical script hash, shown in workflow discovery and the Editor window, and used as the registry/coordinator defaults. A transport may not override an authored fixture with a different value: the registry rejects that start. Development-player launch specs must carry the same scenario and seed in their signed envelope; the outer AITH watcher verifies the armed record, every transcript record, and `result.json` against that exact launch identity. Unity's empty-string serialization is treated as absent only when the launch also declared the value absent.

Register only compiled workflows with a host-owned `IRuntimeSmokeWorkflowCatalog`, and install one `RuntimeSmokeRunRegistry` through `RuntimeSmokeRegistryService.Register`. The registry exposes data-driven `List`, `Start`, `GetStatus`, `Cancel`, and `Resume` operations over the same coordinator. It never accepts C# method names, raw console strings, or unregistered workflow IDs. Run correlations are immutable and cannot be reused within the process.

A `checkpoint` step uses its stable step ID as the checkpoint ID. The host adapter must implement `IRuntimeSmokeReviewCheckpointAdapter` to pause/resume simulation; the coordinator emits `review_ready` and `checkpoint_resumed`, and the registry publishes `ReviewReady` with the active checkpoint ID. `Resume(correlationId, checkpointId)` accepts only the current checkpoint once; stale, mismatched, duplicate, and non-review resumes fail. Cancellation and deadline expiry unwind the host pause and run normal cleanup. To observe a gameplay interval, author a bounded `sleep` with a human-readable reason between a review checkpoint and the next typed predicate/checkpoint; the coordinator owns the delay, cancellation, and deadline checks, so sleep is never delegated as a completion assertion.

The compiler rejects invalid headers, target/mode mismatches, unavailable capabilities, missing or cyclic includes, duplicate expanded step IDs, duplicate include parameters, unresolved variables, fragment start/terminal steps, unbounded sleep, invalid typed assertions, and non-FSD build-suite entries. It returns a canonical SHA-256 hash that is independent of JSON whitespace and map-key ordering. The root start is always first; the root terminal is always last.

## Assertion values and results

Assertion values are parsed by `RuntimeSmokeValueJsonParser`: missing (adapter-only), null, boolean, string, signed 64-bit integer, finite decimal, ordered arrays, and maps with unique ordinal string keys. Deep equality is type-sensitive. Numeric comparisons accept integer/decimal inputs; tolerance values must be authored as finite decimal JSON tokens (`1.0` is valid, integer token `1` is not), and absolute/relative tolerance boundaries are inclusive. String containment is ordinal, array containment is deep membership, and map containment is a recursive subset check. An optional assertion `classification` is a bounded stable identifier distinct from its protocol `failureCode`; it is projected consistently through failure-context JSON, JSONL/result evidence, JUnit properties, MCP status/export, and the Editor failure panel. When omitted on failure, the failure code is used as the classification.

Adapters return a `RuntimeSmokeStepResponse` with the request's exact correlation. An assert response supplies its typed actual value; AITH evaluates the authored assertion. Missing values, adapter errors, timeouts, wrong correlations, and mismatches cannot become PASS. Assertion results carry stable schema version, assertion identity, comparison and optional tolerance, predicate, workflow proposition, step, correlation, elapsed time, failure code, deterministic typed digests, bounded displays, and redaction flags.

## Execution and evidence

Assertion failures preserve their comparison, optional tolerance, predicate, step, workflow proposition, correlation, and elapsed time consistently in the assertion event, `result.json`, `failure-context.json`, and JUnit testcase properties. Compiled workflows can also carry a bounded, content-hashed investigation contract and repository-relative script identity into failure context. A matching player launch/armed envelope enriches the failure packet with player PID/start, executable hash, suite/build provenance, and a relative `logs/player.log` link; stale, mismatched run identity, or mismatched target/mode is omitted. Hosts may implement `IRuntimeSmokeFailureStateProvider` to capture a closed typed state snapshot before cleanup; the failure packet records its kind/digest and a bounded canonical display. The registry exposes `ExecuteInvestigationActionAsync` and Editor MCP tool `ath-runtime-smoke-investigate`: it accepts only an action ID declared by the failed workflow, runs once through the host's typed diagnostic bridge, caps/digests the returned value, appends the result to that correlation's existing transcript, and does not alter terminal outcome. Named capture receipts are checked against their declared action and safe run-local `captures/<label>.png` path, then included in failure context and Editor/MCP artifact projections; post-failure actions atomically refresh the on-disk failure context. Consumer runtime proof remains pending.

`RuntimeSmokeCoordinator.RunAsync` is the transport-neutral single-run entrypoint. It enforces one active run and non-reusable process-local correlations, checks cancellation/deadline while running and polling, verifies each adapter response correlation, runs assertions, invokes cleanup on every terminal path, and returns an ordered transcript with one terminal record. An adapter may return `Pending` for a predicate; AITH polls asynchronously until a terminal adapter response, cancellation, or the workflow deadline.

`RuntimeSmokeRunRegistry` provides host-neutral workflow discovery, correlated start/status/cancel, active-step, transcript, and failure-context snapshots. Discovery includes target, mode, authored scenario, seed, proposition, capabilities, and script hash. Editor MCP exposes `ath-runtime-smoke-list`, `ath-runtime-smoke-run`, `ath-runtime-smoke-status`, `ath-runtime-smoke-resume`, `ath-runtime-smoke-cancel`, `ath-runtime-smoke-export-context`, and `ath-runtime-smoke-investigate`; status now projects the same run/checkpoint/progress/artifact directory/artifact names and failed-run investigation actions shown by the Editor window. The development-only console surface is `runtime_smoke.list/run/status/cancel/resume/export_context`. Hosts register the catalog, step adapter, cleanup, transcript sink, and typed investigation adapter; transport wrappers do not implement a second runner. On a failed run the contained JSONL sink writes a bounded `failure-context.json`, and registry/MCP status plus `ath-runtime-smoke-export-context` expose the diagnosis packet with script/run identity, assertion schema/ID/classification, redacted expected/actual displays and digests, a host-state snapshot when the bridge provides one, an exact re-run invocation using a fresh correlation, transcript tail, and verified capture receipts. Declared investigation results are appended to the same transcript, and the run remains failed. The BTS consumer now has a read-only state/status/predicate/fresh-log/named-capture bridge in its isolated test checkout; live Editor/player investigation proof remains pending. `RuntimeSmokeLaunchEnvelopeVerifier` validates a launch record against exact PID, process start time, executable, fresh log path, terminal evidence, and watchdog deadline. `RuntimeSmokeLaunchClaimLedger` prevents duplicate run/correlation claims within its owning process. The internal Node EXE client now has a spec-driven `ath-exe runtime-smoke <spec.json>` outer launcher with isolated artifacts, exact build provenance, bounded process watch, and evidence validation. Its player-side package path now parses launch args, validates the complete envelope token and build-evidence snapshot, durably claims the actual executable/PID/start time, writes the armed record and transcript prefix, and starts the declared FSD workflow once the host registry registers. Package/consumer compilation and real built-player proof are still pending. The human checkpoint's resume→predicate→pauseAfter state machine remains pending; do not infer these capabilities from the registry API or launcher prototype.

`RuntimeSmokeJsonlTranscriptSink` is constructed with the selected host/worktree root, its output root (normally `Artifacts/RuntimeSmokes/`), the authored-script root, and a correlation ID. It rejects output outside the host, overlap with authored inputs, path traversal, reparse-point/junction traversal, and pre-existing run directories. It claims a previously unused `<outputRoot>/<correlationId>/`, appends UTF-8 JSONL records to `transcript.jsonl`, and writes the terminal `result.json`. It never deletes authored documents or generated evidence.

For built-artifact acceptance, the Node client also exposes `ath-exe runtime-smoke-suite <suite-spec.json>`. Hosts pass an ordered, host-validated list of FSD workflows; AITH verifies build-evidence binding, copies the whole player artifact to a fresh directory, attests source/copy identity, runs the ordered list, and writes aggregate `runtime-smoke-gate.json` plus `junit.xml`. Each entry may carry a stable `parityContract` ID, which AITH validates and preserves in both outputs. Persistence entries may declare an ordered multi-process continuation. AITH creates a run-scoped profile ID, binds it and stage identity into each signed process launch, adds `-autoload` to reload-stage launches, and requires every stage result to pass before the logical entry passes. The validated continuation is available on `RuntimeSmokeStepContext` for host adapters; the aggregate gate records only the profile ID digest. Hosts remain responsible for validating the contract's semantics and invoking this API against the exact intended build. See [RuntimeSmoke usage and authoring](runtime-smoke-usage.md) for the suite schema and output contract.

The editor test assembly in this package contains focused compiler, suite, assertion, coordinator, registry, projection, and containment tests. A consuming Unity project must list the package in its manifest `testables` and run the package filter together with its own host-workflow tests. These package tests are not equivalent to a registered-host Editor MCP operation, visible-player transport, HITL, or build-gate acceptance run.
