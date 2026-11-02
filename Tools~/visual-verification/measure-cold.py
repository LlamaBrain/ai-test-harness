"""Measure OS process startup, model loading, PNG decoding and analysis together."""
import argparse
import hashlib
import json
import os
import pathlib
import platform
import statistics
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('--analyzer', required=True)
parser.add_argument('--model', required=True)
parser.add_argument('--fixtures', required=True)
parser.add_argument('--out', required=True)
args = parser.parse_args()
analyzer, model, fixtures = map(pathlib.Path, (args.analyzer, args.model, args.fixtures))
runs = []
for height in (1080, 1440, 2160):
    image = fixtures / f'representative-{height}-light.png'
    manifest = image.with_suffix('.json')
    timings, outcomes = [], []
    for _ in range(5):
        start = time.perf_counter()
        process = subprocess.run([str(analyzer.resolve()), '--input', str(image.resolve()),
                                  '--manifest', str(manifest.resolve()), '--model', str(model.resolve())],
                                 capture_output=True, text=True, timeout=60,
                                 creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        timings.append((time.perf_counter() - start) * 1000)
        result = json.loads(process.stdout)
        if result.get('schemaVersion') != 1 or process.returncode not in (0, 1, 2):
            raise ValueError('Benchmark cannot treat infrastructure failure as analysis throughput')
        outcomes.append(result['outcome'])
    runs.append({'height': height, 'samplesMs': timings, 'medianMs': statistics.median(timings),
                 'meanMs': statistics.mean(timings), 'outcomes': outcomes,
                 'imageSha256': hashlib.sha256(image.read_bytes()).hexdigest()})
report = {'schemaVersion': 1, 'scope': 'Fresh OS process each sample; includes model load and PNG decode; filesystem caches may be warm',
          'platform': platform.platform(), 'analyzerSha256': hashlib.sha256(analyzer.read_bytes()).hexdigest(),
          'modelSha256': hashlib.sha256(model.read_bytes()).hexdigest(), 'runs': runs}
with pathlib.Path(args.out).open('x', encoding='utf-8') as stream:
    json.dump(report, stream, indent=2)
print(json.dumps(report, indent=2))
