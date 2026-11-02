"""Check native measurements against independently generated font masks.

Exit 0 requires measured ground truth, expected outcomes, and real pass/fail examples.
Inconclusive detector output is reported separately and never counted as correct detection.
"""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import time
import unittest


def summarize(cases, results):
    if len(cases) != len(results):
        raise ValueError('Native batch did not return every requested fixture')
    counts = {'pass': 0, 'fail': 0, 'inconclusive': 0, 'infrastructure-error': 0,
              'heightMatches': 0, 'heightMismatches': 0, 'unmeasured': 0,
              'falsePositives': 0, 'falseNegatives': 0, 'unexpectedInconclusive': 0,
              'expectedInconclusive': 0, 'unexpectedPasses': 0, 'unexpectedFailures': 0,
              'heightOutcomeMismatches': 0, 'unconfirmedPasses': 0}
    details = []
    for case, result in zip(cases, results):
        outcome = result.get('outcome')
        if result.get('schemaVersion') != 1 or outcome not in ('pass', 'fail', 'inconclusive', 'infrastructure-error'):
            raise ValueError('Unsupported native result schema or outcome')
        counts[outcome] += 1
        findings = [f for f in result.get('findings', []) if f.get('ruleId') == 'text.height' and f.get('elementId') == 'label']
        measured = findings[0].get('measuredHeightPx') if len(findings) == 1 else None
        if measured is None:
            counts['unmeasured'] += 1
        elif measured == case['expectedVisibleHeightPx']:
            counts['heightMatches'] += 1
        else:
            counts['heightMismatches'] += 1
        expected = case['expectedOutcome']
        height_outcome = findings[0].get('outcome') if len(findings) == 1 else None
        if height_outcome != expected:
            counts['heightOutcomeMismatches'] += 1
        if outcome == 'pass' and (not findings or not (findings[0].get('detectionConfidence', 0) > 0) or not (findings[0].get('measurementConfidence', 0) > 0)):
            counts['unconfirmedPasses'] += 1
        if outcome == 'fail' and expected != 'fail':
            counts['falsePositives' if expected == 'pass' else 'unexpectedFailures'] += 1
        if outcome == 'pass' and expected != 'pass':
            counts['falseNegatives' if expected == 'fail' else 'unexpectedPasses'] += 1
        if outcome == 'inconclusive':
            counts['expectedInconclusive' if expected == 'inconclusive' else 'unexpectedInconclusive'] += 1
        details.append({**case, 'actualOutcome': outcome, 'measuredHeightPx': measured,
                        'heightOutcome': height_outcome,
                        'detectionConfidence': findings[0].get('detectionConfidence') if findings else None,
                        'measurementConfidence': findings[0].get('measurementConfidence') if findings else None})
    verified = counts['pass'] > 0 and counts['fail'] > 0 and not any(counts[k] for k in (
        'heightMismatches', 'unmeasured', 'heightOutcomeMismatches', 'unconfirmedPasses', 'falsePositives', 'falseNegatives', 'unexpectedPasses', 'unexpectedFailures', 'unexpectedInconclusive', 'infrastructure-error'))
    return {'schemaVersion': 1, 'verified': verified, 'counts': counts, 'cases': details,
            'scope': 'Labeled font mask corpus only; not accessibility certification or general detector reliability'}


class SummaryTests(unittest.TestCase):
    def test_uncertain_detection_is_not_success(self):
        case = {'expectedVisibleHeightPx': 20, 'expectedOutcome': 'pass'}
        result = {'schemaVersion': 1, 'outcome': 'inconclusive', 'findings': [
            {'ruleId': 'text.height', 'elementId': 'label', 'measuredHeightPx': 20, 'outcome': 'inconclusive'}]}
        summary = summarize([case], [result])
        self.assertFalse(summary['verified'])
        self.assertEqual(summary['counts']['unexpectedInconclusive'], 1)
        self.assertEqual(summary['counts']['pass'], 0)

    def test_both_real_outcomes_and_correct_height_required(self):
        cases = [{'expectedVisibleHeightPx': 20, 'expectedOutcome': outcome} for outcome in ('pass', 'fail')]
        results = [{'schemaVersion': 1, 'outcome': case['expectedOutcome'], 'findings': [
            {'ruleId': 'text.height', 'elementId': 'label', 'measuredHeightPx': 20, 'outcome': case['expectedOutcome'], 'detectionConfidence': 1, 'measurementConfidence': 1}]} for case in cases]
        self.assertTrue(summarize(cases, results)['verified'])
        results[1]['outcome'] = 'pass'
        self.assertFalse(summarize(cases, results)['verified'])
        self.assertEqual(summarize(cases, results)['counts']['falseNegatives'], 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--analyzer')
    parser.add_argument('--fixtures')
    parser.add_argument('--out')
    parser.add_argument('--model')
    parser.add_argument('--timeout', type=float, default=300)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        return 0 if unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(SummaryTests)).wasSuccessful() else 1
    if not args.analyzer or not args.fixtures or not args.out or not 0 < args.timeout <= 3600:
        parser.error('--analyzer, --fixtures, --out and timeout in (0,3600] are required')
    root, out = pathlib.Path(args.fixtures).resolve(), pathlib.Path(args.out).resolve()
    truth_bytes = (root / 'ground-truth.json').read_bytes()
    truth = json.loads(truth_bytes)
    if truth.get('schemaVersion') != 1 or not truth.get('fixtures'):
        raise ValueError('Ground truth schema or fixtures missing')
    out.mkdir(parents=True, exist_ok=False)
    cases, items = [], []
    for record in truth['fixtures']:
        image, manifest = (root / record['image']).resolve(), (root / record['manifest']).resolve()
        if not image.is_relative_to(root) or not manifest.is_relative_to(root):
            raise ValueError('Ground-truth path escapes fixture directory')
        if hashlib.sha256(image.read_bytes()).hexdigest() != record['imageSha256']:
            raise ValueError('Fixture PNG digest mismatch: ' + record['image'])
        for delta in (-1, 0, 1):
            expected = 'pass' if delta <= 0 else 'fail' if record['representativeGlyphs'] else 'inconclusive'
            threshold = record['expectedVisibleHeightPx'] + delta
            policy = out / f'policy-{len(items):03}.json'
            policy.write_text(json.dumps({'schemaVersion': 1, 'id': 'font-ground-truth', 'version': '1',
                'source': 'fixture://independent-glyph-mask', 'minHeightPx': threshold, 'minContrastRatio': 4.5}), encoding='utf-8')
            items.append({'input': str(image), 'manifest': str(manifest), 'policy': str(policy),
                          'annotated': str(out / f'annotated-{len(items):03}.png')})
            cases.append({'image': record['image'], 'expectedVisibleHeightPx': record['expectedVisibleHeightPx'],
                          'threshold': threshold, 'expectedOutcome': expected})
    batch = out / 'batch.json'
    batch.write_text(json.dumps({'schemaVersion': 1, 'items': items}), encoding='utf-8')
    command = [str(pathlib.Path(args.analyzer).resolve()), '--batch', str(batch)]
    if args.model:
        command += ['--model', str(pathlib.Path(args.model).resolve())]
    started = time.perf_counter()
    process = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', timeout=args.timeout,
                             creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    elapsed = time.perf_counter() - started
    (out / 'native.stdout.json').write_text(process.stdout, encoding='utf-8')
    (out / 'native.stderr.txt').write_text(process.stderr, encoding='utf-8')
    batch_result = json.loads(process.stdout)
    if batch_result.get('schemaVersion') != 1 or process.returncode != {'pass': 0, 'fail': 1, 'inconclusive': 2, 'infrastructure-error': 3}.get(batch_result.get('outcome')):
        raise ValueError('Native batch outcome and process exit code disagree')
    summary = summarize(cases, batch_result.get('results', []))
    summary.update({'elapsedSeconds': elapsed, 'imageChecks': len(items), 'fontSha256': truth['fontSha256'],
                    'groundTruthSha256': hashlib.sha256(truth_bytes).hexdigest(),
                    'analyzerSha256': hashlib.sha256(pathlib.Path(args.analyzer).read_bytes()).hexdigest(),
                    'modelSha256': hashlib.sha256(pathlib.Path(args.model).read_bytes()).hexdigest() if args.model else None})
    (out / 'font-verification.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in summary.items() if key != 'cases'}, indent=2))
    return 0 if summary['verified'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
