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

`RuntimeSmokeVisualTests` covers compiler capability/identity checks, canonical hashing, generic-host bypass rejection, malformed passing findings, capture tampering and native execution. The native integration test requires `ATH_VISUAL_ANALYZER`, `ATH_VISUAL_MODEL`, and `ATH_VISUAL_FIXTURE` pointing to its native golden fixture; absent native inputs explicitly skip that test. It executes passing, failing, inconclusive, corrupt-image, wrong-correlation, missing-analyzer and adjustable-size-capability cases through the real suite executor and coordinator. A skipped native test is not integration proof. Record actual executed results separately from this usage contract.

Screenshot analysis remains bounded by the native detector and measurement limitations; this is not accessibility certification or evidence of adjustable-size controls. A manifest verifies only its declared screens and regions, not complete application coverage.

## Stock registry configuration (Editor MCP and player transports)

The host-owned registry now uses `ATH_VISUAL_CONFIG` when a workflow contains visual assertions and no explicit `visualAnalyzerFactory` was supplied. Nonvisual workflows never read this variable. Explicit factories take precedence. Set the variable before starting Unity or the built player to a normalized absolute path containing this version-1 JSON:

```json
{
  "schemaVersion": 1,
  "executable": "C:\\aith\\ath-visual.exe",
  "model": "C:\\aith\\text_detection_en_ppocrv3_2023may.onnx",
  "policies": { "project": "C:\\project\\visual\\policy.json" },
  "manifests": { "menu": "C:\\project\\visual\\menu.json" }
}
```

Use normalized absolute platform paths; `model` may be null, but then strict PASS is unavailable. The configuration is operator input, never workflow input. Unknown fields, unsupported versions, unsafe IDs, relative paths, reparse-point configuration paths and oversized configuration files are rejected. The typed authored policy/manifest SHA-256 values must still match the configured files. The host catalog must expose `visual.analysis.v1` to compiler capability validation.

For the existing Editor MCP route, author a HITL workflow with a checkpoint before the visual assertion and a declared `named_capture` investigation action with `captureLabel: "menu"`. Retain the existing investigation context, scenario and seed requirements. Start with `ath-runtime-smoke-run`; at review-ready invoke `ath-runtime-smoke-capture` for the declared action, wait for its completed verified receipt, then use `ath-runtime-smoke-resume` for that checkpoint. The registry binds only successful, durably recorded capture receipts with the same correlation, workflow and canonical hash. It revalidates receipt bytes before analysis. A missing capture or configuration fails the authoritative assertion. The existing host capture bridge supplies the screenshot; this feature adds no capture ownership system.

The same registry fallback is available to development players through inherited `ATH_VISUAL_CONFIG`; executable/policy/model paths must exist on the player machine. Built-player FSD hosts must arrange the capture and its trusted binding before the assertion (or use their explicit analyzer factory). Automatic live-game capture within the stock FSD runner has not been demonstrated. The tested stock route uses the actual registry capture/checkpoint/resume API and a pre-existing PNG, including absent configuration and absent capture failures; it does not claim a live MCP server/game session was exercised.

A policy with `scope: "adjustable-size-capability"` requires exactly one supplemental `text.adjustability` finding with an inconclusive outcome and no pixel measurements. The adapter rejects an overall PASS for that scope: a single screenshot cannot prove that users can change text size. Height/contrast findings remain available as independently measured evidence.

Configured RuntimeSmoke policies use literal rendered-pixel thresholds. Materialize a separate policy for each tested resolution/scale and author its exact SHA-256. Bundled CLI profiles have `scaling: "linear-height"` and `referenceHeight`; resolve their threshold for the screenshot height and set `analyzedHeight` to that exact height before using them directly with RuntimeSmoke/native analysis. The native boundary rejects an unresolved scaling profile or a mismatching analyzed height, so a raw 1080p profile cannot silently pass a 4K check with the wrong threshold. The CLI's named-profile resolver performs this materialization automatically.
