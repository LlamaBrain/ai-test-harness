# Visual analyzer dependency review

Review date: 2026-11-02. The Windows build resolved dependencies at vcpkg baseline `af839a44c17a927468f35c7a65e200aeba337da4`; installed package notices were collected and checked. AITH remains MIT licensed. Model weights were downloaded to temporary test storage and hash-verified; no weights or third-party source are committed.

| Component | Revision/version | License and provenance | Redistribution / release status | Unresolved work |
| --- | --- | --- | --- | --- |
| Fonttik | `ccfd0dcef8ce133f5fd7841b915f5ecc4d6f3202` | EA source, BSD-3-Clause; [source](https://github.com/electronicarts/fonttik) | Evaluated only; not bundled | None if no code is copied; retain BSD notices if adapted later |
| Fonttik EAST weights | Default model, not selected | Fonttik `NOTICE.txt` identifies GPLv3-or-later | Explicitly excluded | Must never enter release dependencies |
| OpenCV | 4.14.0 | Apache-2.0 for modern OpenCV; upstream OpenCV | Required native dependency; package notices must accompany binaries | Retain embedded notices; linked modules are core/imgproc/imgcodecs/dnn only |
| nlohmann/json | 3.12.0 | MIT, upstream nlohmann/json | Required native dependency; retain copyright/license | Retain MIT notice |
| PP-OCRv3 English ONNX | Zoo revision `48b10fa505aeb51fbd127fc60a5c660040e823b7`, `text_detection_en_ppocrv3_2023may.onnx` | Directory explicitly licenses all files Apache-2.0, copyright PaddlePaddle Authors; original English Paddle inference archive linked in README | Downloaded and hash-verified for testing; not bundled | Preserve license and applicable notices; retain conversion and upstream provenance |

The candidate ONNX Git LFS pointer declares SHA-256 `03f550c6b406fda8bf54bd8327815f6c7e2edd98cea02348c93d879254366587`, size 2,423,490 bytes. The downloaded 2,423,490-byte file matched this SHA-256 on 2026-11-02. `Tools~/visual-verification/model.json` pins the source and `fetch-model.py` reproduces download verification while retaining its license. The [model directory README](https://github.com/opencv/opencv_zoo/blob/48b10fa505aeb51fbd127fc60a5c660040e823b7/models/text_detection_ppocr/README.md) links the original Paddle models and describes the training datasets. Its [model-specific license](https://github.com/opencv/opencv_zoo/blob/48b10fa505aeb51fbd127fc60a5c660040e823b7/models/text_detection_ppocr/LICENSE) applies to the actual files, rather than relying only on the hosting repository license. Dataset provenance and any separately applicable terms must be retained in the final release review.

## Fonttik reuse assessment

Inspected `src/ContrastChecker.cpp`, `src/TextboxDetectionDB.cpp`, and `NOTICE.txt` at the pinned revision. Its contrast implementation derives a surrounding background mask by dilation and excludes a narrow antialiasing band. Its DB detector uses OpenCV detection followed by text-mask refinement and line merging. These are useful reference patterns for separating detection, segmentation, and measurement.

Do not import Fonttik wholesale: its notices include EAST GPL weights and FFmpeg, while this feature only needs PNG analysis. Reuse OpenCV's maintained primitives directly; retain conservative inconclusive results for backgrounds where mask separation is unreliable. Fonttik's fixed pixel line-merging distances and contrast rounding should not become unexamined AITH policy. No Fonttik implementation code was copied in this review.

Release validation must enumerate resolved native dependencies and copy their licenses. A dependency manifest alone does not prove redistribution compliance. No FFmpeg, H.264, online inference, engine SDK, or hosted service is required by the intended build.

## Resolved Windows transitive dependencies

Installed copyright files were inspected and collected by `Tools~/visual-verification/licenses.py` into `Builds/visual-dependency-notices` (11 notices with SHA-256 inventory). Source pins are governed by the vcpkg baseline; installed `vcpkg.spdx.json` records retain source references. Do not treat SPDX `NOASSERTION` fields as license approval: the actual copyright files are the evidence.

| Component | Version | License | Release obligation |
| --- | --- | --- | --- |
| Abseil | 20260107.1 | Apache-2.0 | Retain license/notices; linked DLL |
| Protocol Buffers | 7.36.2 | BSD-3-Clause | Retain copyright, conditions and disclaimer; linked DLL |
| libpng | 1.6.59 | PNG Reference Library License v2 | Retain bundled copyright/license; linked DLL |
| zlib | 1.3.2 | Zlib | Retain notice and do not misrepresent origin; linked DLL |
| FlatBuffers | 25.12.19 | Apache-2.0 | Retain license/notices for included headers/code |
| utf8-range | 7.36.2 | MIT | Retain copyright/license for included code |
| vcpkg-cmake | 2025-08-07 | Build tooling; notice retained | Not a runtime DLL |
| vcpkg-cmake-config | 2026-07-21 | Build tooling; notice retained | Not a runtime DLL |
| vcpkg-get-python-packages | 2025-04-05 | Build tooling; notice retained | Not a runtime DLL |

The actual executable output directory contains OpenCV core/dnn/imgcodecs/imgproc, Abseil, Protobuf, PNG and zlib DLLs. No video/videoio or FFmpeg DLL is copied there. The vcpkg port builds extra OpenCV modules in its dependency tree; those are not analyzer links or distribution requirements. CMake cache confirms `WITH_FFMPEG=OFF`, `WITH_IPP=OFF`, and `WITH_ITT=OFF`.

The port downloaded tiny-dnn while preparing its source cache. Searches of the OpenCV release `CMakeCache.txt`, `build.ninja`, and module sources found no tiny-dnn compilation references; no corresponding runtime library is shipped. This download is not treated as a bundled analyzer component. Recheck the installed dependency inventory when changing the vcpkg baseline or requested OpenCV features. Preserve the complete OpenCV package copyright file, including its embedded notices, rather than replacing it with an Apache-only summary.

Fixture generation uses Pillow 12.0.0 and an operator-provided licensed font. Font binaries are never copied into the repository or release. Fixture manifests record font digests and Pillow versions. CI uses DejaVu Sans on Linux and installed Arial on Windows, so reports must identify the font rather than pretending the two platform corpora are identical.
