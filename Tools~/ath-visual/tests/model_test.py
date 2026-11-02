"""Adversarial tests against a verified model and a separately labeled PNG.

Usage: model_test.py analyzer model.onnx screenshot.png manifest.json
The caller supplies its licensed font fixture and provenance-verified model.
"""
import copy
import concurrent.futures
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import tempfile

exe, model, image, manifest_path = [pathlib.Path(value).resolve() for value in sys.argv[1:]]
manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
assert len(manifest["elements"]) == 1
creation_flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0

with tempfile.TemporaryDirectory(prefix="ath-visual-model-test-") as directory:
    root = pathlib.Path(directory)
    policy = root / "policy.json"
    policy.write_text(json.dumps({"schemaVersion": 1, "id": "model-test", "version": "1", "source": "fixture://model-test", "minHeightPx": 1, "minContrastRatio": 4.5}))

    def run(document, outcome):
        path = root / "manifest.json"
        path.write_text(json.dumps(document))
        result = subprocess.run([str(exe), "--input", str(image), "--model", str(model), "--manifest", str(path), "--policy", str(policy)], capture_output=True, text=True, timeout=30, creationflags=creation_flags)
        value = json.loads(result.stdout)
        assert value["outcome"] == outcome, value
        assert result.returncode == {"pass": 0, "fail": 1, "inconclusive": 2, "infrastructure-error": 3}[outcome]
        return value

    assert run(manifest, "pass")["policy"]["source"] == "fixture://model-test"
    capability_policy = json.loads(policy.read_text())
    capability_policy["scope"] = "adjustable-size-capability"
    policy.write_text(json.dumps(capability_policy))
    result = run(manifest, "inconclusive")
    assert len([finding for finding in result["findings"] if finding["ruleId"] == "text.adjustability" and finding["outcome"] == "inconclusive"]) == 1
    capability_policy["minHeightPx"] = 10000
    policy.write_text(json.dumps(capability_policy))
    run(manifest, "fail")
    capability_policy["minHeightPx"] = 1
    capability_policy["scope"] = "unknown-capability"
    policy.write_text(json.dumps(capability_policy))
    run(manifest, "infrastructure-error")
    del capability_policy["scope"]
    policy.write_text(json.dumps(capability_policy))
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

    unnamed = copy.deepcopy(manifest)
    del unnamed["elements"][0]["id"]
    result = run(unnamed, "pass")
    assert {finding["elementId"] for finding in result["findings"]} == {"expected-0"}
    boundless = copy.deepcopy(manifest)
    del boundless["elements"][0]["bounds"]
    result = run(boundless, "inconclusive")
    assert result["coverage"]["expected"] == 1 and result["coverage"]["measured"] == 0
    assert len(result["findings"]) == 3
    assert all(finding["bounds"] is None and finding["expectedBounds"] is None and
               finding["measuredHeightPx"] is None and finding["contrastRatio"] is None and
               finding["detectionConfidence"] == 0 and finding["measurementConfidence"] == 0 and
               finding["outcome"] == "inconclusive" for finding in result["findings"])
    del boundless["elements"][0]["id"]
    assert {finding["elementId"] for finding in run(boundless, "inconclusive")["findings"]} == {"expected-0"}
    collision = copy.deepcopy(unnamed)
    collision["elements"].append({**manifest["elements"][0], "id": "expected-0"})
    run(collision, "infrastructure-error")
    documented_policy = json.loads(policy.read_text())
    undocumented_policy = {key: value for key, value in documented_policy.items() if key != "source"}
    policy.write_text(json.dumps(undocumented_policy))
    run(manifest, "infrastructure-error")
    for invalid_source in ("", " \t\r\n", 7):
        policy.write_text(json.dumps({**documented_policy, "source": invalid_source}))
        run(manifest, "infrastructure-error")
    policy.write_text(json.dumps(documented_policy))

    # Prepare distinct identities before starting concurrent readers. No worker
    # calls run(), which intentionally rewrites a shared manifest for earlier tests.
    fixed_policy = root / "determinism-policy.json"
    fixed_policy.write_bytes(policy.read_bytes())
    fixed_manifests = []
    for identity in ("a", "b"):
        document = copy.deepcopy(manifest)
        document["screenId"] = "determinism-" + identity
        document["elements"][0]["id"] = "element-" + identity
        path = root / ("determinism-" + identity + ".json")
        path.write_text(json.dumps(document), encoding="utf-8")
        fixed_manifests.append(path)
    immutable_paths = [exe, model, image, fixed_policy, *fixed_manifests]
    original_digests = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in immutable_paths}

    def fixed_run(path):
        process = subprocess.run([str(exe), "--input", str(image), "--model", str(model),
                                  "--manifest", str(path), "--policy", str(fixed_policy)],
                                 capture_output=True, text=True, timeout=30,
                                 creationflags=creation_flags)
        result = json.loads(process.stdout)
        assert process.returncode == 0 and result["outcome"] == "pass", result
        return result

    references = [fixed_run(path) for path in fixed_manifests]
    sequential_order = (0, 1, 0)
    sequential = [fixed_run(fixed_manifests[index]) for index in sequential_order]
    assert sequential == [references[index] for index in sequential_order], "Sequential results changed"

    concurrent_order = (1, 0, 1, 0)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as workers:
        concurrent_results = list(workers.map(fixed_run, [fixed_manifests[index] for index in concurrent_order]))
    assert concurrent_results == [references[index] for index in concurrent_order], "Concurrent process results changed"

    batch_path = root / "determinism-batch.json"
    batch_path.write_text(json.dumps({"schemaVersion": 1, "items": [
        {"input": str(image), "manifest": str(fixed_manifests[index]), "policy": str(fixed_policy)}
        for index in concurrent_order]}), encoding="utf-8")
    batch_process = subprocess.run([str(exe), "--batch", str(batch_path), "--model", str(model)],
                                   capture_output=True, text=True, timeout=30,
                                   creationflags=creation_flags)
    batch = json.loads(batch_process.stdout)
    assert batch_process.returncode == 0 and batch["schemaVersion"] == 1 and batch["outcome"] == "pass", batch
    assert batch["results"] == [references[index] for index in concurrent_order], "Shared-model batch changed results or item order"
    assert original_digests == {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in immutable_paths}, "Analysis modified immutable inputs"
print("Model regressions passed: 16 cases (8 acceptance/error, 5 optional-manifest/provenance, 3 determinism modes)")
