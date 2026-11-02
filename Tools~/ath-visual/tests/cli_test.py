"""Real executable negative tests; no engine, OCR service or Python packages."""
import json
import pathlib
import subprocess
import sys
import tempfile

exe = pathlib.Path(sys.argv[1]).resolve()
fixture = pathlib.Path(sys.argv[2]).resolve()


def invoke(*args, expected):
    process = subprocess.run([str(exe), *map(str, args)], capture_output=True, text=True, timeout=30)
    assert process.returncode == expected, (process.returncode, process.stdout, process.stderr)
    result = json.loads(process.stdout)
    assert result["schemaVersion"] == 1
    return result


with tempfile.TemporaryDirectory(prefix="ath-visual-test-") as temp:
    root = pathlib.Path(temp)
    assert invoke(expected=3)["outcome"] == "infrastructure-error"
    assert invoke("--input", fixture, expected=2)["outcome"] == "inconclusive"
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
