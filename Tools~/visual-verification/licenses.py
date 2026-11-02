"""Retain installed dependency notices; reject accidental screenshot-MVP video dependencies."""
import hashlib
import json
import pathlib
import sys

installed = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
status = installed / 'vcpkg' / 'status'
if not status.is_file():
    raise ValueError('Expected completed vcpkg installed tree with status database')
entries = []
for paragraph in status.read_text().split('\n\n'):
    fields = dict(line.split(': ', 1) for line in paragraph.splitlines() if ': ' in line and not line.startswith(' '))
    if fields.get('Status') != 'install ok installed' or 'Feature' in fields:
        continue
    name = fields['Package']
    if any(token in name.lower() for token in ('ffmpeg', 'x264', 'x265', 'openh264')):
        raise ValueError('Video dependency prohibited in screenshot build: ' + name)
    triplet = fields['Architecture']
    notice = installed / triplet / 'share' / name / 'copyright'
    if not notice.is_file() or notice.stat().st_size == 0:
        raise ValueError('Missing redistribution notice: ' + str(notice))
    entries.append((name, triplet, fields.get('Version', fields.get('Version-Semver', 'unknown')), notice.read_bytes()))
if not {'opencv4', 'nlohmann-json', 'libpng', 'zlib'}.issubset({e[0] for e in entries}):
    raise ValueError('Required dependency set is incomplete')
output.mkdir(parents=True, exist_ok=False)
records = []
for name, triplet, version, data in sorted(entries):
    filename = f'{name}-{triplet}.LICENSE'
    (output / filename).write_bytes(data)
    records.append({'name': name, 'version': version, 'triplet': triplet,
                    'notice': filename, 'noticeSha256': hashlib.sha256(data).hexdigest()})
(output / 'inventory.json').write_text(json.dumps({'schemaVersion': 1, 'dependencies': records}, indent=2))
print(f'Retained {len(records)} dependency notices; no prohibited video packages installed')
