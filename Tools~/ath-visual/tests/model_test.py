"""Adversarial tests against a verified model and a separately labeled PNG.

Usage: model_test.py analyzer model.onnx screenshot.png manifest.json
The caller supplies its licensed font fixture and provenance-verified model.
"""
import copy
import json
import pathlib
import subprocess
import sys
import tempfile

exe, model, image, manifest_path = [pathlib.Path(value).resolve() for value in sys.argv[1:]]
manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
assert len(manifest["elements"]) == 1

with tempfile.TemporaryDirectory(prefix="ath-visual-model-test-") as directory:
    root = pathlib.Path(directory)
    policy = root / "policy.json"
    policy.write_text(json.dumps({"schemaVersion": 1, "id": "model-test", "version": "1", "minHeightPx": 1, "minContrastRatio": 4.5}))

    def run(document, outcome):
        path = root / "manifest.json"
        path.write_text(json.dumps(document))
        result = subprocess.run([str(exe), "--input", str(image), "--model", str(model), "--manifest", str(path), "--policy", str(policy)], capture_output=True, text=True, timeout=30)
        value = json.loads(result.stdout)
        assert value["outcome"] == outcome, value
        assert result.returncode == {"pass": 0, "fail": 1, "inconclusive": 2, "infrastructure-error": 3}[outcome]
        return value

    run(manifest, "pass")
    duplicated = copy.deepcopy(manifest)
    duplicate = copy.deepcopy(duplicated["elements"][0])
    duplicate["id"] += "-second"
    duplicated["elements"].append(duplicate)
    result = run(duplicated, "inconclusive")
    assert all(finding["detectionConfidence"] == 0 for finding in result["findings"])
    expected_text = copy.deepcopy(manifest)
    expected_text["elements"][0]["expectedText"] = "Unverified recognition must not pass"
    run(expected_text, "inconclusive")
    absent = copy.deepcopy(manifest)
    absent["elements"].append({"id": "missing", "bounds": {"x": 0, "y": manifest["resolution"]["height"] - 50, "width": 40, "height": 40}})
    run(absent, "inconclusive")
    invalid = copy.deepcopy(manifest)
    invalid["elements"][0]["bounds"]["x"] = 2**64 - 1
    run(invalid, "infrastructure-error")
print("Verified model positive, duplicate-detection, missing-region, unrecognized-text and malformed-manifest checks passed")
