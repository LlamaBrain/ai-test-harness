# AITH native visual analyzer

Engine-independent C++20 executable and reusable library. CPU-only OpenCV DNN
PP-OCR detection is optional; manifest bounds allow deterministic region analysis
without a model. Authoritative success additionally requires text detection to
corroborate each expected region; without a model the overall audit cannot pass.
No model weights are bundled.

```sh
cmake -S Tools~/ath-visual -B Builds/visual -DCMAKE_TOOLCHAIN_FILE=/path/to/vcpkg/scripts/buildsystems/vcpkg.cmake
cmake --build Builds/visual --config Release
ctest --test-dir Builds/visual -C Release --output-on-failure
ath-visual --input capture.png --manifest manifest.json --policy policy.json --annotated annotation.png
```

The vcpkg manifest pins its registry and disables OpenCV default features. Only
PNG and DNN features are requested: no FFmpeg or video encoding dependency.
Alternatively provide installed OpenCV >=4.8 and nlohmann/json >=3.11 CMake packages.

JSON is emitted to stdout. Optional `--output` and `--annotated` exclusively create
new files, never replacing original evidence. Exit codes: pass 0, fail 1,
inconclusive 2, infrastructure error 3. Inputs are bounded to 32 MiB and 32 million
pixels. Analysis requires an opaque 8-bit PNG; transparent screenshot pixels must
be composited against the actual rendered background before analysis.

`--batch batch.json --model model.onnx` loads the model once. A batch has
`schemaVersion: 1` and a nonempty `items` array (maximum 1024). Each item supplies
`input` and optional `manifest`, `policy`, `annotated` paths. Results are returned
in item order in `results`; an infrastructure error is retained per item and does
not discard other results. Aggregate precedence is infrastructure error, fail,
inconclusive, pass. Batch items cannot select another executable or model.
Library callers can retain an `AnalysisSession` to reuse loaded model state;
one session is sequential and must not be called concurrently.

## Version 1 inputs

Policy: `{"schemaVersion":1,"id":"project","version":"1","minHeightPx":26,"minContrastRatio":4.5}`.
The default is an explicitly custom threshold, not a certification profile.
Policy `scope` defaults to `default-rendered-size`. The other supported scope,
`adjustable-size-capability`, adds the stable `text.adjustability` inconclusive
finding: a single screenshot cannot prove the user can change text size. Pixel
failures retain fail precedence; otherwise this capability scope is inconclusive.
Unknown scopes are infrastructure errors rather than silently reduced audits.
Profiles with `scaling` / `referenceHeight` must first be materialized by AITH
for the target resolution. The native tool requires `scaling: "linear-height"`
and an integer `analyzedHeight` matching the decoded PNG, then uses the supplied
resolved `minHeightPx` without scaling it again. Raw bundled profile files and
profiles resolved for another resolution are rejected. RuntimeSmoke hosts must
register the resulting per-resolution policy file and its SHA-256 digest.

Manifest:
```json
{
  "schemaVersion": 1,
  "screenId": "menu",
  "screenshotId": "capture.png",
  "locale": "en-US",
  "uiScale": 1.0,
  "resolution": { "width": 1920, "height": 1080 },
  "elements": [
    { "id": "menu", "bounds": { "x": 20, "y": 20, "width": 300, "height": 80 }, "representativeGlyphs": true }
  ]
}
```

Bounds must isolate one text line with background padding. `representativeGlyphs`
declares that the test text includes the representative ascending/descending
extents needed for a minimum-height comparison. Without this declaration, a
short visible span is inconclusive rather than a confirmed failure. The measured
height is the visible mask span, not OCR box height or nominal font size.

## Measurement assumptions and limitations

The library converts sRGB channels to linear relative luminance. It estimates
background from the region perimeter and rejects materially varied borders.
Pixels at least 2:1 against that background contribute to the glyph boundary.
Connected components exclude isolated one/two-pixel noise. Contrast uses per-component foreground estimates and the estimated background, independently
of the size threshold. This is a conservative initial uniform-background method;
it is not sufficient for textured backgrounds, outlines, multiline regions,
mixed-size runs, or unknown font coverage. Empty regions never pass.
Measurement confidence is a heuristic quality indicator, not a calibrated
probability. Detector confidence is reported directly from PP-OCR.

The component grouping is not text recognition. Manifest region identity is
supplied by the caller; expected text content is not verified. Requests containing
nonempty `expectedText` therefore force an inconclusive overall outcome.
Use `--model` with a provenance-verified PP-OCRv3 model to corroborate that a
region contains detected text. Model confidence and pixel measurement confidence
are reported separately. No detections never implies a clean audit.
Best-effort detection reports its coverage limitation and cannot certify complete
UI coverage. An adjustable-size capability cannot be established by one image.

The parent AITH wrapper must bind screenshot hashes, policy and manifest hashes,
run identity, deadlines, safe run-owned output containment and immutable receipts.
This lower-level tool does not create an alternative capture or test runner.
