"""Real executable negative tests; no engine, OCR service or Python packages."""
import json
import os
import pathlib
import subprocess
import sys
import tempfile

exe = pathlib.Path(sys.argv[1]).resolve()
fixture = pathlib.Path(sys.argv[2]).resolve()


def invoke(*args, expected):
    process = subprocess.run([str(exe), *map(str, args)], capture_output=True, text=True, timeout=30,
                             creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    assert process.returncode == expected, (process.returncode, process.stdout, process.stderr)
    result = json.loads(process.stdout)
    assert result["schemaVersion"] == 1
    return result


with tempfile.TemporaryDirectory(prefix="ath-visual-test-") as temp:
    root = pathlib.Path(temp)
    assert invoke(expected=3)["outcome"] == "infrastructure-error"
    assert invoke("--input", fixture, expected=2)["outcome"] == "inconclusive"
    scaled_policy = root / "scaled-policy.json"
    policy = {"schemaVersion": 1, "id": "resolution-profile", "version": "1", "source": "fixture://cli-test", "minHeightPx": 26,
              "minContrastRatio": 4.5, "referenceHeight": 1080, "scaling": "linear-height"}
    scaled_policy.write_text(json.dumps(policy))
    fixture_4k = fixture.parent / "fixture-2160.png"
    assert invoke("--input", fixture_4k, "--policy", scaled_policy, expected=3)["outcome"] == "infrastructure-error"
    policy["analyzedHeight"] = 1080
    scaled_policy.write_text(json.dumps(policy))
    invoke("--input", fixture, "--policy", scaled_policy, expected=2)
    invoke("--input", fixture_4k, "--policy", scaled_policy, expected=3)
    policy["analyzedHeight"] = 2160
    policy["minHeightPx"] = 52
    scaled_policy.write_text(json.dumps(policy))
    invoke("--input", fixture_4k, "--policy", scaled_policy, expected=2)
    missing_bounds = root / "boundless.json"
    missing_bounds.write_text(json.dumps({"schemaVersion": 1, "screenId": "boundless", "screenshotId": fixture.name,
        "locale": "en", "uiScale": 1, "resolution": {"width": 1920, "height": 1080}, "elements": [{}]}))
    annotation = root / "boundless-annotation.png"
    result = invoke("--input", fixture, "--manifest", missing_bounds, "--annotated", annotation, expected=2)
    assert result["coverage"]["expected"] == 1 and result["coverage"]["measured"] == 0
    assert len(result["findings"]) == 3 and annotation.is_file()
    corrupt = root / "corrupt.png"
    data = bytearray(fixture.read_bytes())
    data[29] ^= 1  # IHDR checksum, independently tampered.
    corrupt.write_bytes(data)
    assert invoke("--input", corrupt, expected=3)["outcome"] == "infrastructure-error"
    truncated = root / "truncated.png"
    truncated.write_bytes(fixture.read_bytes()[:-12])
    assert invoke("--input", truncated, expected=3)["outcome"] == "infrastructure-error"
    existing = root / "annotation.png"
    existing.write_bytes(b"preserve me")
    assert invoke("--input", fixture, "--annotated", existing, expected=3)["outcome"] == "infrastructure-error"
    assert existing.read_bytes() == b"preserve me"
    batch = root / "batch.json"
    batch.write_text(json.dumps({"schemaVersion": 1, "items": [{"input": str(fixture)}, {"input": str(root / "absent.png")}, {"input": str(fixture)}]}))
    results = invoke("--batch", batch, expected=3)["results"]
    assert [r["outcome"] for r in results] == ["inconclusive", "infrastructure-error", "inconclusive"]
print("Native executable evidence and batch negative tests passed")
