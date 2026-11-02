# Visual feature verification record

Baseline: `origin/dev` `896078d` (v0.6.1). Branch: `feat/visual-accessibility-testing`. Review and current CI: [PR #3 targeting dev](https://github.com/LlamaBrain/ai-test-harness/pull/3), [checks](https://github.com/LlamaBrain/ai-test-harness/pull/3/checks). This record describes executed local evidence; CI conclusions must be read from the checks for the reviewed commit. It is not accessibility certification.

## Implementation and integration

The feature reuses RuntimeSmoke compilation, typed assertions, canonical hashes, coordinator deadlines, single terminal outcomes, named capture receipts, safe artifact storage, JSONL/JUnit/failure reporting, registry/MCP commands and existing Captain trace emission. `IAthHostAdapter` is unchanged. Existing version-1 workflows remain valid. The cross-engine branch was reviewed without merging its divergent history; arbitrary externally captured Unreal/proprietary-engine PNGs use the same analyzer contract. Footage work was inspected for overlap, not treated as a new goal.

New components are the separately buildable C++20 CPU analysis library/executable under `Tools~/ath-visual`, visual operations within the existing `ath-exe` client, typed visual assertion/configuration support under `Runtime/RuntimeSmokes`, and reproducible verification scripts under `Tools~/visual-verification`. No engine SDK, AI service, new smoke runner, capture system, native Unity plugin or telemetry is introduced.

See [usage](visual-accessibility.md), [RuntimeSmoke integration](runtime-smoke-visual.md), [integration audit](visual-accessibility-integration.md), [dependency inventory](visual-dependencies.md), and [added/modified file inventory](visual-evidence/changed-files.txt). Commits before the final integration increment: `2ca51a8` audit, `5b83e04` CLI, `0ad0d56` native analyzer, `ead9c72` typed RuntimeSmoke/trace evidence, `eb9a484` measurement repairs, `75aea69` CI, `264b8ec` report integrity. Integration commit `1b29706` uses repository version 0.10.0; the deterministic-regression increment uses 0.10.1.

## Executed checks

- Existing Node baseline: 76 passed. Final expanded CLI suite: **96 passed**, including negative evidence, immutable writes, batch validation, comparison identity and adjustable-policy checks.
- MSVC Release native build and CTest: **2/2 passed**. Unit/golden tests cover thresholds, polarity, antialiasing, outlines, gradient/texture uncertainty, composited translucent panels, clipping, malformed input, separate/mixed text sizes and threshold-independent ambiguity.
- Real PP-OCR inference on **72 independently labeled PNGs / 216 threshold cases**, across Arial regular, DejaVu Sans regular and Times New Roman Bold, at 1080p, 1440p and 4K: **216 exact heights, 144 pass, 18 fail, 54 expected inconclusive**, zero unexpected outcomes or unmeasured cases. Each case checks the threshold below/at/above the independently derived glyph-mask height. Expected abstentions concern short/capital/number labels below the requested full-font threshold without representative descenders. They are not counted as established compliance. No general false-positive/false-negative rate is inferred from this corpus.
- **Eight real-model adversarial checks passed**, including reused detections, missing expected regions, unsupported expected text and adjustable-capability abstention. Executable tests reject malformed PNGs/manifests, unresolved scaling profiles and wrong-resolution policies, and accept correctly materialized 1080p/4K policies.
- Determinism regression: three additional modes passed (sequential repeated inputs, four concurrent processes, and repeated entries in a shared-model batch). Full structured results and ordering match independent baselines; input/model/executable digests remain unchanged. The maintained model script passes 11 total cases.
- Actual existing `ath-exe` CLI: **9/9 checks passed**, including pass/fail/inconclusive, batch audit/report/comparison and rejected policy mismatch/duplicate output. Receipt artifact digests were checked. Aggregate audit schema 2 retains the authoritative screen list; legacy aggregate audits require rerun.
- Actual isolated Unity **6000.3.10f1 / Test Framework 1.6.0: 100 passed, zero failed/skipped**, with zero failed individual XML testcase nodes. Eight real native RuntimeSmoke suite scenarios cover pass, fail, inconclusive, corrupt input, wrong correlation, missing analyzer, adjustable capability and unresolved scaling. The stock registry configuration route also verifies missing-config/missing-capture failures and named-capture/checkpoint/resume success without a custom factory. Legacy configuration isolation and explicit factory precedence pass.
- Dependency notice validator: **3 tests passed**. Actual installation retained 11 copyright notices and excluded prohibited video packages. Model bytes matched the pinned SHA-256 and 2,423,490-byte length. Verification-script self-tests: 2 font-checker and 1 CLI-checker tests passed.

The isolated Unity run includes all Runtime sources, actual trace MCP tooling and selected RuntimeSmoke tests. It excludes Editor-window view-model tests. Earlier older-Test-Framework attempts with inconsistent XML were rejected. The reproducible launcher also rejected a run whose analyzer changed during execution despite passing test counts.

## Reproduction and retained identities

See [verification scripts](../Tools~/visual-verification/README.md), [native build instructions](../Tools~/ath-visual/README.md), and `.github/workflows/visual-analysis.yml`. CI builds Windows/Linux without Unity/Unreal, validates schemas and notices, executes the real CLI and uploads evidence. A binary dependency cache supports repeated runs. No operating-system font is redistributed; generated fixtures retain font, PNG and ground-truth hashes.

Compact records under `visual-evidence/` contain the three font reports, CLI report, cold/warm measurements and sanitized Unity evidence (55 source snapshots and 47 dependency/tool inputs). Full local logs/images remain in ignored `Builds/visual/`; CI artifacts supply independently reproduced images and results. Final local analyzer SHA-256: `a454e9565b887f2e2384e563c90b6dda2723b7f90c765a888589bb30e820e76a`. Model SHA-256: `03f550c6b406fda8bf54bd8327815f6c7e2edd98cea02348c93d879254366587`.

## CPU measurements (2026-11-02)

Windows 11, AMD Ryzen 7 7800X3D, 8 cores/16 threads. PP-OCRv3 English CPU inference, final analyzer identity above, measured after concurrent verification finished.

| Resolution | Warm mean | Throughput | Fresh-process median | Peak process memory |
| --- | --- | --- | --- | --- |
| 1920 x 1080 | 247.24 ms | 4.04 images/s | 339.53 ms | 170,520,576 bytes |
| 2560 x 1440 | 268.05 ms | 3.73 images/s | 366.71 ms | 214,069,248 bytes |
| 3840 x 2160 | 305.22 ms | 3.28 images/s | 434.32 ms | 338,509,824 bytes |

Warm measurements reuse one model, five timed iterations per resolution, excluding PNG decode/process startup. Model load: 34.64 ms. Peak memory is a cumulative high-water mark. Fresh-process figures include decode/model loading, five processes per resolution, potentially warm filesystem caches; all 15 outcomes passed. The final 72-case Arial batch took 21.02 seconds including output, with model reuse. Small local samples are not performance guarantees. Full-resolution normalization remains a measured memory optimization opportunity; no GPU dependency was introduced.

## Licensing, limitations and remaining consuming-project work

AITH remains MIT. Fonttik source was evaluated but not copied; its GPL EAST weights are excluded. The selected PP-OCR weight object, its model-specific Apache license and upstream provenance are pinned. OpenCV/transitive notices must accompany redistributed binaries; the CI notice collector retains them. Fonts and model weights are not bundled with this source change. No unresolved restrictive model obligation is knowingly introduced; re-audit notices when changing dependency pins.

Screenshot detection is best effort. Strict manifests establish coverage only for declared elements, resolutions, scales and locales. Geometric glyph-role inference and uniform-background assumptions can produce inconclusive results for outlines, textured backgrounds or mixed lines. `expectedText` content recognition and non-Latin coverage are unsupported; no claim is made for them. Adjustable-size capability always needs separate runtime evidence. Resolution-scaled policies must be materialized for the exact image height; unresolved profiles fail closed.

Live consuming-game capture, an active MCP-server session, full Editor-window behavior and built-player launch were not executed. Consumers must register their workflows/capabilities, provide expected-region manifests and resolved policies, configure the trusted analyzer, and validate live capture bindings through existing AITH paths. Full Unreal runtime capture/manifest generation remains with the parallel cross-engine adapter work; its screenshots already work independently. No complete accessibility certification or complete application UI coverage is claimed.