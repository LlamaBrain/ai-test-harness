"""Exercise real PNG decoding and expected exit codes; no game engine required."""
import json
import pathlib
import subprocess
import sys
import jsonschema

executable = pathlib.Path(sys.argv[1]).resolve()
root = pathlib.Path(sys.argv[2]).resolve()
schema = json.loads((pathlib.Path(__file__).parent.parent / 'ath-visual' / 'result.schema.json').read_text())
manifest = {'schemaVersion': 1, 'screenId': 'golden', 'screenshotId': 'fixture-1080.png',
            'locale': 'en', 'uiScale': 1, 'resolution': {'width': 1920, 'height': 1080},
            'elements': [{'id': 'label', 'representativeGlyphs': True,
                          'bounds': {'x': 10, 'y': 10, 'width': 80, 'height': 40}}]}
manifest_path = root / 'example-manifest.json'
manifest_path.write_text(json.dumps(manifest))
for name, threshold, expected, code in [('unconfirmed', 20, 'inconclusive', 2), ('fail', 21, 'fail', 1)]:
    policy = root / f'example-{name}-policy.json'
    policy.write_text(json.dumps({'schemaVersion': 1, 'id': 'fixture', 'version': '1',
                                  'minHeightPx': threshold, 'minContrastRatio': 4.5}))
    run = subprocess.run([str(executable), '--input', str(root / 'fixture-1080.png'),
                          '--manifest', str(manifest_path), '--policy', str(policy)],
                         capture_output=True, text=True, timeout=30)
    result = json.loads(run.stdout)
    jsonschema.validate(result, schema)
    (root / f'example-{name}-result.json').write_text(json.dumps(result, indent=2))
    assert run.returncode == code and result['outcome'] == expected, (run.returncode, result)
    assert all(f['measuredHeightPx'] == 20 for f in result['findings'])
print('Real PNG missing-detector/failure examples and result schema verified')
