"""Optional setup only. Analysis itself never accesses the network."""
import hashlib
import json
import pathlib
import sys
import urllib.request

spec = json.loads((pathlib.Path(__file__).parent / 'model.json').read_text())
destination = pathlib.Path(sys.argv[1]).resolve()
destination.mkdir(parents=True, exist_ok=True)
with urllib.request.urlopen(spec['url'], timeout=60) as response:
    data = response.read(spec['bytes'] + 1)
if len(data) != spec['bytes'] or hashlib.sha256(data).hexdigest() != spec['sha256']:
    raise ValueError('Model bytes do not match pinned provenance')
with urllib.request.urlopen(spec['licenseUrl'], timeout=60) as response:
    license_bytes = response.read(1024 * 1024)
with (destination / (spec['file'] + '.LICENSE')).open('xb') as stream:
    stream.write(license_bytes)
with (destination / spec['file']).open('xb') as stream:
    stream.write(data)
print(json.dumps({'model': str(destination / spec['file']), 'sha256': spec['sha256']}))
