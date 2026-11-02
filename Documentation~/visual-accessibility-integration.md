# Visual accessibility integration audit

Status: M0 audit and proposed integration contract. The interfaces below are **not implemented or verified** by this document. Native implementation and executable schema tests must establish the final supported subset.

## Baseline and boundaries

Audited 2026-11-02 against `origin/dev` at `896078d` (v0.6.1). Implementation branch: `feat/visual-accessibility-testing`. `origin/feat/footage-capture` at `2c93c78` is an ancestor of dev (verified with `git merge-base --is-ancestor`); do not duplicate capture. Parallel `origin/captain/cross-engine-unreal` at `c416064` has diverged and was inspected without merging.

| Responsibility | Reuse / extension |
| --- | --- |
| Typed workflow compilation | `RuntimeSmokeDocumentCompiler`: preserve version-1 start, command, assert, wait, sleep, checkpoint and terminal behavior. |
| Assertion evaluation and terminal outcome | `RuntimeSmokeAssertionEvaluator`, `RuntimeSmokeCoordinator`: consume authoritative measurement through typed assertions; preserve one terminal outcome. |
| Capture ownership | `RuntimeSmokeCaptureArtifactWriter`, registry capture actions, `ath-snap` and `harness.snap`: no new capture pipeline. |
| Evidence | Existing run directories, capture receipts, JSONL, failure context and JUnit projection. Extend references for analysis results and annotations. |
| Operator invocation | Add `visual` dispatch to existing `ath-exe.js`; reuse CLI parsing, without socket or game launch for existing PNGs. |
| MCP | Existing RuntimeSmoke execution/status remains authoritative. Optional direct audit tool returns structured diagnostics, never overrides a run outcome. |
| Trace | Extend owned payload through `AthTraceWriter` and `AthTraceEmitter`, retaining `.captain-sdlc/trace/` storage and canonical envelope. |
| Pixel analysis | New standalone C++20 library/executable in `Tools~/ath-visual/`; no engine headers or APIs. |

The capture writer currently returns `artifact`, `bytes` and `sha256`. It bounds PNG bytes to 32 MiB, rejects reparse points and unsafe names, and uses exclusive creation. Its PNG validation checks the signature only. A visual integration must additionally decode a complete image, bound decoded dimensions/memory, and verify the receipt digest before analysis. Screenshot dispatch is not readiness: asynchronous capture must complete before input verification.

## Proposed version-1 analysis contract

All coordinates are integer screenshot pixels, origin top-left; rectangles use `x`, `y`, `width`, `height`, with exclusive right/bottom edges. Reject non-finite numbers, negative dimensions and unknown schema versions. Results sort by element ID, then top/left coordinates, then rule ID. No authored executable or shell arguments are allowed.

Input manifest object:

- `schemaVersion`: integer `1`.
- `screenId`, `screenshotId`, `locale`: nonempty strings.
- `resolution`: object with positive integer `width` and `height`, matching decoded input.
- `uiScale`: positive finite number.
- `policy`: object with `id`, `version`, `sha256`; hash binds the exact policy bytes.
- `expectedText`: array of objects with unique nonempty `elementId`, optional `text`, optional `bounds` rectangle, and boolean `required` (default true).

Policy object: `schemaVersion: 1`, `id`, `version`, `source` (URL and source revision/date), `minimumRenderedHeightPx`, `minimumContrastRatio`, and `requireAdjustableSizeEvidence`. Project overrides are policy data and participate in the policy digest. Resolution-specific thresholds must be explicit. Adjustable-size capability requires independently captured state/control evidence; a single large-text image cannot satisfy that requirement.

Result object:

- `schemaVersion: 1`, `analyzerVersion`, `mode` (`best-effort` or `manifest`).
- `outcome`: `pass`, `fail`, `inconclusive`, or `infrastructure-error`; direct CLI exit codes proposed as 0, 1, 2, 3 respectively.
- `input`: screenshot SHA-256, byte length, decoded width/height and screenshot identity.
- `policy`: exact ID/version/digest; optional manifest digest.
- `coverage`: expected, detected, matched and unmeasurable counts plus explicit limitations.
- `findings`: ordered array with stable `ruleId`, outcome, optional element ID, bounds, measured value/unit, threshold, detection confidence, measurement confidence and diagnostic.
- `artifacts`: run-relative references and digests for JSON/annotations. Never overwrite the screenshot.

Initial rule identifiers: `text.rendered-height`, `text.contrast`, `text.viewport-clipping`, `text.expected-region`, `text.coverage`, `text.legibility-regression`. The 2:1 edge inclusion criterion belongs to rendered-height measurement; it is distinct from the policy contrast threshold. An empty detection set is inconclusive. Missing required manifest elements and required unmeasurable elements cannot pass. Best-effort findings cannot establish complete interface coverage.

## Proposed RuntimeSmoke extension

Prefer retaining `type: "assert"`, adding a typed `visual` operand containing `captureLabel`, `policyId`, `policySha256`, optional `manifestId`/`manifestSha256`, and `mode`. Require declared `visual.analysis.v1` capability. A trusted adapter wrapper resolves the capture and policy from run-owned evidence, invokes the fixed configured analyzer, validates its result, and supplies a typed value to the existing evaluator. Reject conflicting ordinary predicate and visual operands, unsupported comparisons, unknown fields/versions and absent references.

Compiler parsing and validation must explicitly recognize this operand. Existing canonical JSON hashing already covers authored document content; test that every visual input changes the hash and old documents retain their hashes. No change to `IAthHostAdapter` is justified. Use `RuntimeSmokeStepContext` correlation, workflow/step identities and deadline. A validated visual pass is necessary for typed success; infrastructure errors remain adapter errors and inconclusive measurements prevent acceptance. Preserve cancellation/deadline handling in the coordinator.

Attach result/annotation receipts to existing JSONL and failure context, then project into existing results/JUnit. Never synthesize a second terminal record or accept an external report after a workflow already passed. Integration requires actual coordinator execution, not merely a CLI report alongside a run.

## Cross-engine reconciliation

The parallel branch's `Documentation~/cross-engine-architecture.md` and `protocol.md` preserve newline-delimited loopback commands, correlated sentinels, ERR precedence and asynchronous dispatch/completion separation. It extracts `client.js`, `mcp-server.js` and `report.js` under the existing Node directory. Screenshot support is optional; headless/NullRHI cannot claim screenshot success.

Keep analyzer input/output file contracts independent of those changes. Add CLI dispatch in a small module that the future shared client/MCP surface can call. Do not import its earlier procedural smoke acceptance as a replacement for current RuntimeSmoke. External Unreal PNGs can use the analyzer immediately once implemented; native capture/manifests remain a later adapter concern.

## Licensing and trace review gates

Before selecting dependencies, inspect Fonttik's source and bundled notices; its GPLv3 EAST model must not enter the distribution. Record exact OpenCV, JSON library and model weight versions, hashes, origin, licenses, notices and redistribution obligations. Repository licensing alone is insufficient evidence for model weights. No dependency or model approval is asserted here. No FFmpeg/video dependencies are needed.

Reviewed committed Captain `trace-schema.md` and `captain-sdlc-conventions.md` at `3d575d7e3c227ba919aeeacd91de65f1fe2389b9` using the local `E:/Personal/captain-sdlc` checkout. The canonical trace document labels its broader design as planning; AITH's existing emitter is the concrete implementation to extend. Preserve envelope fields `schema_version: 1`, `event_id`, `timestamp`, `tool`, `tool_version`, `kind`, `refs`, optional `parents`/`links`, and `payload`. A payload change requires a tool version bump, not an envelope version change.

Existing local writer emits `ath.smoke.completed`; add visual policy identity, outcome, findings summary, verified artifact references and optional baseline/build identity inside its owned payload. Baseline regressions can reuse canonical `ath.regression.detected` with baseline/current references and the `regresses` relation. Do not invent task IDs or causal event IDs when unavailable. Trace remains append-only daily `YYYY-MM-DD.jsonl` under the consuming project's `.captain-sdlc/trace/`; trace and `side-store/` remain ignored. Keep large measurement evidence in AITH artifacts and reference it. Sensitive text, where retained, belongs in the existing side-store convention rather than a new telemetry system. Trace is supplementary cross-tool evidence and does not become RuntimeSmoke acceptance authority.

## Verification baseline and required follow-through

Executed `node --test` in `Tools~/ath-exe-client` on 2026-11-02: **76 passed, 0 failed**, reported duration 2009.0511 ms. This verifies existing Node behavior only, not Unity execution or visual analysis. No native benchmarks or visual reliability measurements exist yet.

Protect the existing Editor suites: DocumentCompiler, Assertion, Coordinator, CaptureArtifactWriter, Capture, RunRegistry, JUnitProjection and SuiteExecutor tests. Add negative tests for tampered receipts, partial PNGs, wrong correlation, missing analyzer, malformed outputs, timeouts, empty detections and missing required regions. Exercise legacy document compatibility and deterministic hashing/output. Native golden fixtures need independently measured glyph extents and explicit false-positive/false-negative reporting, including threshold boundaries, different resolutions, labels without descenders, antialiasing, outlines, textured backgrounds and localization. Run CMake/CTest, example analysis, schema/license checks and CI before claiming the native slice verified.
