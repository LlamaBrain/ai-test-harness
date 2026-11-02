# RuntimeSmoke visual assertions

Visual verification is an additive version-1 typed assertion. RuntimeSmoke remains the acceptance authority; a CLI audit does not change an existing run outcome. The first implementation supports strict manifest mode inside workflows. Best-effort inspection remains available outside workflow acceptance.

Declare `visual.analysis.v1` in the workflow's `requires` and include this assertion step (replace the two digests with SHA-256 of the exact policy and manifest bytes):

```json
{
  "id": "check-menu",
  "type": "assert",
  "assertion": {
    "assertionSchemaVersion": 1,
    "id": "menu-legibility",
    "name": "Expected menu text is legible",
    "predicate": "visual.analysis.v1",
    "comparison": "equals",
    "expected": "pass",
    "visual": {
      "schemaVersion": 1,
      "captureLabel": "menu",
      "policyId": "project",
      "policySha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "manifestId": "menu",
      "manifestSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
      "mode": "manifest"
    }
  }
}
```

The compiler rejects unsafe identifiers, unknown visual fields/versions, missing capability declarations and comparisons other than `equals "pass"`. Policy and manifest digests participate in the existing canonical workflow hash. Existing nonvisual documents retain their version-1 behavior.

## Host integration

Supply the optional `visualAnalyzerFactory` when constructing `RuntimeSmokeRunRegistry`. The factory receives the compiled workflow, correlation ID and existing run directory, and returns a `RuntimeSmokeVisualAnalyzer`. `RuntimeSmokeSuiteExecutor.RunAsync` also accepts a `visualAnalyzerFactory` receiving the suite entry and correlation; the host selects that entry's claimed artifact directory. For direct coordinator execution, pass the same instance through `RunAsync(..., visualAnalyzer: analyzer)`.

Its constructor accepts a trusted executable path, the claimed run directory, dictionaries mapping policy/manifest identifiers to local files, a capture resolver, and an optional verified PP-OCR model path. These are host configuration, not workflow-provided paths or shell commands. A strict passing outcome requires a configured text detection model. Advertise the capability only for hosts with this configuration.

The capture resolver returns `RuntimeSmokeVisualCapture(correlationId, workflowId, receipt)` using the receipt from the existing `RuntimeSmokeCaptureArtifactWriter.WritePng`. Retain that binding after asynchronous capture has actually completed. Return no binding while capture is pending. The analyzer rejects absent receipts, wrong workflow/correlation, label mismatches and changed capture bytes. Do not reconstruct receipts from an agent-provided report. No changes to `IAthHostAdapter` or engine capture ownership are required.

The current host must arrange capture before the visual assertion using its existing command/capture implementation. This extension does not automatically instrument every host or generate manifests. Unreal and other engines may analyze external screenshots immediately with the native/CLI surface; their RuntimeSmoke host integration requires equivalent trusted bindings.

## Execution and evidence

The coordinator routes visual assertions only to the configured concrete analyzer, so an ordinary host predicate returning `"pass"` cannot bypass verification. The subprocess uses no shell, has bounded stdout/stderr, inherits the workflow deadline, and is terminated on coordinator cancellation/timeout.

Evidence goes into a new run-owned `visual-<step-id-sha256>/` directory. It contains verified input snapshots, `result.json`, `annotated.png` and `receipt.json`; original screenshots remain untouched. Files use exclusive creation. Existing reparse-point protections apply. Capture, policy, manifest, executable and model digests are verified and retained; result dimensions, policy thresholds, coverage, per-element rule identities, bounds, confidence and measurements are checked before acceptance.

`fail` and `inconclusive` become typed assertion mismatches. Invalid input, missing executable, invalid result, process failure and integrity violations become predicate errors; timeouts remain timeouts. None can pass. Exactly one existing RuntimeSmoke terminal is emitted. JSONL, result documents, failure context and JUnit include `visualEvidenceJson` containing correlated artifact references and digests. Full findings remain in the referenced result artifact.

## Verification

`RuntimeSmokeVisualTests` covers compiler capability/identity checks, canonical hashing, generic-host bypass rejection, malformed passing findings, capture tampering and native execution. The native integration test requires `ATH_VISUAL_ANALYZER`, `ATH_VISUAL_MODEL`, and `ATH_VISUAL_FIXTURE` pointing to its native golden fixture; absent native inputs explicitly skip that test. It executes passing, failing, inconclusive, corrupt-image, wrong-correlation and missing-analyzer cases through the real suite executor and coordinator. A skipped native test is not integration proof. Record actual executed results separately from this usage contract.

Screenshot analysis remains bounded by the native detector and measurement limitations; this is not accessibility certification or evidence of adjustable-size controls. A manifest verifies only its declared screens and regions, not complete application coverage.
