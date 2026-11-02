# Visual feature verification record

Baseline: `origin/dev` `896078d` (v0.6.1). Branch: `feat/visual-accessibility-testing`. Review and current CI: [PR #3 targeting dev](https://github.com/LlamaBrain/ai-test-harness/pull/3), [checks](https://github.com/LlamaBrain/ai-test-harness/pull/3/checks). This record describes executed local evidence; CI conclusions must be read from the checks for the reviewed commit. It is not accessibility certification.

## Implementation and integration

The feature reuses RuntimeSmoke compilation, typed assertions, canonical hashes, coordinator deadlines, single terminal outcomes, named capture receipts, safe artifact storage, JSONL/JUnit/failure reporting, registry/MCP commands and existing Captain trace emission. `IAthHostAdapter` is unchanged. Existing version-1 workflows remain valid. The cross-engine branch was reviewed without merging its divergent history; arbitrary externally captured Unreal/proprietary-engine PNGs use the same analyzer contract. Footage work was inspected for overlap, not treated as a new goal.

New components are the separately buildable C++20 CPU analysis library/executable under `Tools~/ath-visual`, visual operations within the existing `ath-exe` client, typed visual assertion/configuration support under `Runtime/RuntimeSmokes`, and reproducible verification scripts under `Tools~/visual-verification`. No engine SDK, AI service, new smoke runner, capture system, native Unity plugin or telemetry is introduced.

See [usage](visual-accessibility.md), [RuntimeSmoke integration](runtime-smoke-visual.md), [integration audit](visual-accessibility-integration.md), [dependency inventory](visual-dependencies.md), and [added/modified file inventory](visual-evidence/changed-files.txt). Commits before the final integration increment: `2ca51a8` audit, `5b83e04` CLI, `0ad0d56` native analyzer, `ead9c72` typed RuntimeSmoke/trace evidence, `eb9a484` measurement repairs, `75aea69` CI, `264b8ec` report integrity. Integration commit `1b29706` uses repository version 0.10.0; determinism commit `0da22e4` uses 0.10.1. The schema and Linux integration correction uses 0.10.2.

## Executed checks

- Existing Node baseline: 76 passed. Final expanded CLI suite: **98 passed**, including negative evidence, immutable writes, batch validation, comparison identity and adjustable-policy checks.
- MSVC Release native build and CTest: **2/2 passed**. Unit/golden tests cover thresholds, polarity, antialiasing, outlines, gradient/texture uncertainty, composited translucent panels, clipping, malformed input, separate/mixed text sizes and threshold-independent ambiguity.
- Real PP-OCR inference on **72 independently labeled PNGs / 216 threshold cases**, across Arial regular, DejaVu Sans regular and Times New Roman Bold, at 1080p, 1440p and 4K: **216 exact heights, 144 pass, 18 fail, 54 expected inconclusive**, zero unexpected outcomes or unmeasured cases. Each case checks the threshold below/at/above the independently derived glyph-mask height. Expected abstentions concern short/capital/number labels below the requested full-font threshold without representative descenders. They are not counted as established compliance. No general false-positive/false-negative rate is inferred from this corpus.
- **Eight real-model adversarial checks passed**, including reused detections, missing expected regions, unsupported expected text and adjustable-capability abstention. Executable tests reject malformed PNGs/manifests, unresolved scaling profiles and wrong-resolution policies, and accept correctly materialized 1080p/4K policies.
- Determinism regression: three additional modes passed (sequential repeated inputs, four concurrent processes, and repeated entries in a shared-model batch). Full structured results and ordering match independent baselines; input/model/executable digests remain unchanged. The maintained model script now passes 16 grouped cases, including optional manifest fields and required policy provenance.
- Actual existing `ath-exe` CLI: **13/13 checks passed**, including pass/fail/inconclusive, batch audit/report/comparison and rejected policy mismatch/duplicate output. Receipt artifact digests were checked. Aggregate audit schema 2 retains the authoritative screen list; legacy aggregate audits require rerun.
- Actual isolated Unity **6000.3.10f1 / Test Framework 1.6.0: 100 passed, zero failed/skipped**, with zero failed individual XML testcase nodes. Eleven real native RuntimeSmoke suite scenarios cover pass, fail, inconclusive, corrupt input, wrong correlation, missing analyzer, adjustable capability and unresolved scaling, omitted IDs, unlocated regions and missing policy provenance. The stock registry configuration route also verifies missing-config/missing-capture failures and named-capture/checkpoint/resume success without a custom factory. Legacy configuration isolation and explicit factory precedence pass.
- Dependency notice validator: **3 tests passed**. Actual installation retained 11 copyright notices and excluded prohibited video packages. Model bytes matched the pinned SHA-256 and 2,423,490-byte length. Verification-script self-tests: 2 font-checker and 1 CLI-checker tests passed.

The isolated Unity run includes all Runtime sources, actual trace MCP tooling and selected RuntimeSmoke tests. All ten existing Editor-window view-model/projection tests are included; the real window and RuntimeSmoke MCP tool compile against installed dependencies. Interactive window rendering was not exercised. Earlier older-Test-Framework attempts with inconsistent XML were rejected. The reproducible launcher also rejected a run whose analyzer changed during execution despite passing test counts.

## Reproduction and retained identities

See [verification scripts](../Tools~/visual-verification/README.md), [native build instructions](../Tools~/ath-visual/README.md), and `.github/workflows/visual-analysis.yml`. CI builds Windows/Linux without Unity/Unreal, validates schemas and notices, executes the real CLI and uploads evidence. A binary dependency cache supports repeated runs. No operating-system font is redistributed; generated fixtures retain font, PNG and ground-truth hashes.

Compact records under `visual-evidence/` contain the three font reports, CLI report, cold/warm measurements and sanitized Unity evidence (59 source snapshots and 47 dependency/tool inputs). Full local logs/images remain in ignored `Builds/visual/`; CI artifacts supply independently reproduced images and results. Final local analyzer SHA-256: `136e46e617bfd97ff0e204ec53e6408bb04108b71e5b0cd7bde1c0b45301b937`. Model SHA-256: `03f550c6b406fda8bf54bd8327815f6c7e2edd98cea02348c93d879254366587`.

## CPU measurements (2026-11-02)

Windows 11, AMD Ryzen 7 7800X3D, 8 cores/16 threads. PP-OCRv3 English CPU inference, final analyzer identity above, measured after concurrent verification finished.

| Resolution | Warm mean | Throughput | Fresh-process median | Peak process memory |
| --- | --- | --- | --- | --- |
| 1920 x 1080 | 219.11 ms | 4.56 images/s | 297.65 ms | 170,459,136 bytes |
| 2560 x 1440 | 236.39 ms | 4.23 images/s | 332.94 ms | 214,454,272 bytes |
| 3840 x 2160 | 256.94 ms | 3.89 images/s | 407.54 ms | 338,526,208 bytes |

Warm measurements reuse one model, five timed iterations per resolution, excluding PNG decode/process startup. Model load: 31.10 ms. Peak memory is a cumulative high-water mark. Fresh-process figures include decode/model loading, five processes per resolution, potentially warm filesystem caches; all 15 outcomes passed. The final 72-case Arial batch took 25.75 seconds including output, with model reuse (other verification may overlap corpus runs). Small local samples are not performance guarantees. Full-resolution normalization remains a measured memory optimization opportunity; no GPU dependency was introduced.

## Licensing, limitations and remaining consuming-project work

AITH remains MIT. Fonttik source was evaluated but not copied; its GPL EAST weights are excluded. The selected PP-OCR weight object, its model-specific Apache license and upstream provenance are pinned. OpenCV/transitive notices must accompany redistributed binaries; the CI notice collector retains them. Fonts and model weights are not bundled with this source change. No unresolved restrictive model obligation is knowingly introduced; re-audit notices when changing dependency pins.

Screenshot detection is best effort. Strict manifests establish coverage only for declared elements, resolutions, scales and locales. Geometric glyph-role inference and uniform-background assumptions can produce inconclusive results for outlines, textured backgrounds or mixed lines. `expectedText` content recognition and non-Latin coverage are unsupported; no claim is made for them. Adjustable-size capability always needs separate runtime evidence. Resolution-scaled policies must be materialized for the exact image height; unresolved profiles fail closed.

Live consuming-game capture, an active MCP-server session, interactive Editor-window behavior and built-player launch were not executed. Consumers must register their workflows/capabilities, provide expected-region manifests and resolved policies, configure the trusted analyzer, and validate live capture bindings through existing AITH paths. Full Unreal runtime capture/manifest generation remains with the parallel cross-engine adapter work; its screenshots already work independently. No complete accessibility certification or complete application UI coverage is claimed.

## CI integration correction

The first completed Linux job on commit `1b29706` built successfully and passed native, schema, license, Node and labeled-font checks, then failed CLI integration with a 32 MiB evidence-size diagnostic (run 41498148274, job 125458401433, artifact 13170350353). The correction separates trusted executable/model reads (128 MiB) from screenshot reads (32 MiB), retains offending path/actual size in errors, and records tool byte counts before CLI execution. This is not a green CI claim: current checks remain authoritative. Successful dependency builds are now cached before test execution, so later test failures do not discard the dependency cache.
