# Visual accessibility verification

Consult the [verification record](visual-verification.md) for executed tests, exact identities and current CI. Screenshot analysis provides bounded evidence for tested states; it does not certify accessibility or establish complete UI coverage.

## Analyze existing screenshots

Build the standalone CPU executable using [the native instructions](../Tools~/ath-visual/README.md). No Unity, Unreal, AI account, or online inference is required. Optional model setup uses `python Tools~/visual-verification/fetch-model.py <new-model-directory>`; this downloads pinned weights and their license. Transfer that directory to an offline CI machine if needed. Analysis itself never downloads a model.

```sh
node Tools~/ath-exe-client/ath-exe.js visual analyze menu.png --analyzer /path/to/ath-visual --model /path/to/model.onnx --manifest menu.json --profile xag101-console --out results/menu-run-1
node Tools~/ath-exe-client/ath-exe.js visual report results/menu-run-1 --format json
node Tools~/ath-exe-client/ath-exe.js visual compare results/menu-baseline results/menu-run-1
node Tools~/ath-exe-client/ath-exe.js visual audit captures --manifest audit.json --analyzer /path/to/ath-visual --model /path/to/model.onnx --out results/audit-1
```

Output directories must be new. `--timeout-ms` bounds execution; `ATH_VISUAL_ANALYZER` may replace `--analyzer`. When consuming an existing screenshot receipt, supply its hash with `--expected-sha256`. Exit codes are 0 pass, 1 fail, 2 inconclusive, and 3 infrastructure error. Treat every nonzero code as an unaccepted verification gate. `result.json`, `receipt.json`, and `annotated.png` retain measurements and artifact identities; the original screenshot is never annotated in place. Comparison requires compatible screen, resolution, locale, scale, and policy identity and reports decreases in measured size or contrast.

`xag101-console` and `xag101-pc` apply their documented default-size targets. `steam-adjustable` evaluates the reachable-size target but adds an inconclusive adjustability finding: a screenshot cannot demonstrate that a user can adjust the setting. Profile JSON retains source, version, and scope. Use `--policy project-policy.json` for project-specific thresholds instead of editing the analyzer.

## Engine-neutral manifest

Individual analysis results and receipts use schema version 1. Aggregate audit results use schema version 2 and retain `input-audit-manifest.json`; reporting verifies the exact required screen list against that retained input. Older aggregate audits must be rerun. Receipt metadata alone cannot establish comparison identity: reports verify the retained screenshot, manifest, policy and result bytes. These digests protect evidence consistency inside the trusted run directory; they are not signatures against an owner replacing the entire directory.

```json
{
  "schemaVersion": 1,
  "screenId": "settings",
  "screenshotId": "menu.png",
  "resolution": {"width": 1920, "height": 1080},
  "uiScale": 1,
  "locale": "en-US",
  "elements": [
    {"id": "subtitle-example", "bounds": {"x": 30, "y": 40, "width": 450, "height": 80}, "representativeGlyphs": true}
  ]
}
```

Supplied bounds isolate a single line and include background padding. Element IDs are optional: omitted IDs become `expected-<zero-based-index>`; collisions with explicit IDs are rejected. Bounds are also optional, but a required region without location evidence remains explicitly inconclusive with null measurements and incomplete coverage. This does not silently match it to unrelated detected text. Declare `representativeGlyphs` only when the rendered sample contains the ascender/descender coverage appropriate to the policy; uppercase-only or numeric labels cannot justify that declaration merely to obtain a verdict. A short visible span without representative coverage is inconclusive. Optional `expectedText` is currently not recognized: a nonempty value prevents a clean overall result.

Custom policy files require nonblank `id`, `version`, and `source`, plus `minHeightPx` and `minContrastRatio`. For example: `{"schemaVersion":1,"id":"project-ui","version":"1","source":"project://accessibility-policy","minHeightPx":26,"minContrastRatio":4.5}`. The external policy file is bound to the manifest/run by its verified receipt or authored RuntimeSmoke digest. Resolution-scaled policies must be materialized for the image's exact height; use `--profile` for the built-in profiles.

An audit document groups screens: `{"schemaVersion":1,"screens":[{"id":"settings","screenshot":"menu.png","manifest":"menu.json"}]}`. Screenshot paths are relative to the capture directory; manifest paths are relative to the audit file. Screen identity must match the per-screen manifest. Missing inputs and insufficient evidence never pass.

## RuntimeSmoke and MCP

For the stock host-owned registry, configure `ATH_VISUAL_CONFIG` and use the existing checkpoint/capture/resume MCP flow described in [RuntimeSmoke visual assertions](runtime-smoke-visual.md#stock-registry-configuration-editor-mcp-and-player-transports). It resolves completed named-capture receipts from the correlated transcript. A custom factory remains available for hosts with their own capture binding.

Retain the existing capture operation and completed `RuntimeSmokeCaptureArtifactReceipt`. Configure the registry's optional `visualAnalyzerFactory` to construct a `RuntimeSmokeVisualAnalyzer` from trusted executable/model paths, the run artifact directory, registered policy/manifest dictionaries, and a capture-label resolver. The resolver returns `RuntimeSmokeVisualCapture` binding the existing receipt to workflow and correlation. Configuration belongs to the consuming host, not to authored shell commands or `IAthHostAdapter` mutations.

Declare `visual.analysis.v1` in workflow `requires` and the host's available capabilities. An ordinary version-1 `assert` step can contain:

```json
{
  "assertionSchemaVersion": 1,
  "id": "menu-legibility",
  "name": "Expected menu text is legible",
  "predicate": "visual.analysis.v1",
  "comparison": "equals",
  "expected": "pass",
  "visual": {
    "schemaVersion": 1,
    "captureLabel": "menu",
    "policyId": "project-ui",
    "policySha256": "REPLACE_WITH_SHA256_OF_POLICY_BYTES",
    "manifestId": "settings",
    "manifestSha256": "REPLACE_WITH_SHA256_OF_MANIFEST_BYTES",
    "mode": "manifest"
  }
}
```

Replace the digest placeholders with actual lowercase SHA-256 strings. The compiler rejects missing capabilities, unsafe identifiers, and incompatible assertion forms. RuntimeSmoke retains its single terminal outcome and deadline. A generic host response cannot satisfy visual verification. Missing/corrupt inputs, analyzer failures, and unmeasurable required text cannot produce success. Evidence is retained in the existing transcript, results, failure context, and JUnit projection.

Agents use existing `ath-runtime-smoke-list`, `ath-runtime-smoke-run`, and `ath-runtime-smoke-status` MCP tools. The status transcript contains `visualEvidenceJson`; follow its verified artifact references to the detailed findings and annotation. CLI analysis remains useful outside a running engine, but its receipt alone does not retroactively approve a RuntimeSmoke run. External Unreal or proprietary-engine PNGs use the same CLI and manifest contract.

## Limits and verification

The current measurement method assumes isolated text against a sufficiently uniform local background. It uses linearized sRGB luminance and a 2:1 inclusion boundary, independently of the policy contrast threshold. Complex backgrounds, outlines, mixed-size lines, unsupported scripts, and incomplete glyph evidence need broader validation. Detector confidence is separate from measurement confidence. Strict success requires text detection to corroborate expected regions; a manifest of arbitrary rectangles is not proof of text presence.

See [integration audit](visual-accessibility-integration.md), [dependency inventory](visual-dependencies.md), and the native CTest fixtures. The GitHub workflow builds without an engine, validates examples and schema, and retains dependency notices. A green unit test is not evidence that all resolutions, locales, or consuming-game states were tested.
