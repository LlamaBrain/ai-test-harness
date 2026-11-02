"""Exercise the real ath-exe CLI and native analyzer without an engine installation."""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import unittest


def validate_receipt(directory, expected):
    root = pathlib.Path(directory).resolve()
    receipt = json.loads((root / 'receipt.json').read_text(encoding='utf-8'))
    if receipt.get('schemaVersion') != 1 or receipt.get('outcome') != expected or not receipt.get('artifacts'):
        raise ValueError('Missing expected receipt outcome')
    seen = set()
    for artifact in receipt['artifacts']:
        relative = pathlib.PurePosixPath(artifact['path'])
        if relative.is_absolute() or '..' in relative.parts or artifact['path'] in seen:
            raise ValueError('Unsafe or duplicate receipt path')
        seen.add(artifact['path'])
        file = (root / artifact['path']).resolve()
        if not file.is_relative_to(root):
            raise ValueError('Receipt artifact escaped output root')
        data = file.read_bytes()
        if len(data) != artifact['byteLength'] or hashlib.sha256(data).hexdigest() != artifact['sha256']:
            raise ValueError('Receipt artifact digest mismatch')
    if not {'input.png', 'result.json', 'annotated.png', 'input-manifest.json', 'input-policy.json'}.issubset(seen):
        raise ValueError('Required reproducible artifacts missing')
    return receipt


def check_response(code, value, expected):
    outcome = value.get('outcome', value.get('result', {}).get('outcome'))
    expected_code = {'pass': 0, 'fail': 1, 'inconclusive': 2, 'infrastructure-error': 3}[expected]
    if code != expected_code or outcome != expected:
        raise AssertionError(f'Expected {expected}/{expected_code}, received {outcome}/{code}')


class ResponseTests(unittest.TestCase):
    def test_infrastructure_error_cannot_be_success(self):
        with self.assertRaises(AssertionError):
            check_response(0, {'outcome': 'infrastructure-error'}, 'pass')
        with self.assertRaises(AssertionError):
            check_response(2, {'outcome': 'inconclusive'}, 'pass')
        check_response(1, {'result': {'outcome': 'fail'}}, 'fail')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ('analyzer', 'model', 'fixtures', 'out'):
        parser.add_argument('--' + option)
    parser.add_argument('--node', default='node')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        return 0 if unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(ResponseTests)).wasSuccessful() else 1
    if not all((args.analyzer, args.model, args.fixtures, args.out)):
        parser.error('--analyzer --model --fixtures --out are required')
    fixtures, out = pathlib.Path(args.fixtures).resolve(), pathlib.Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=False)
    ground = json.loads((fixtures / 'ground-truth.json').read_text(encoding='utf-8'))
    selected = [f for f in ground['fixtures'] if f['image'].startswith('representative-1080-')]
    if len(selected) != 2:
        raise ValueError('Two representative 1080p fixtures required')
    selected.sort(key=lambda item: item['image'])
    first = selected[0]
    threshold = min(item['expectedVisibleHeightPx'] for item in selected) - 1
    policy = {'schemaVersion': 1, 'id': 'cli-integration', 'version': '1', 'source': 'fixture://independent-mask',
              'minHeightPx': threshold, 'minContrastRatio': 4.5}
    pass_policy, fail_policy, adjustable_policy = [out / (name + '-policy.json') for name in ('pass', 'fail', 'adjustable')]
    pass_policy.write_text(json.dumps(policy), encoding='utf-8')
    fail_policy.write_text(json.dumps({**policy, 'minHeightPx': first['expectedVisibleHeightPx'] + 1}), encoding='utf-8')
    adjustable_policy.write_text(json.dumps({**policy, 'scope': 'adjustable-size-capability'}), encoding='utf-8')
    cli = pathlib.Path(__file__).resolve().parent.parent / 'ath-exe-client' / 'ath-exe.js'
    common = ['--analyzer', str(pathlib.Path(args.analyzer).resolve()), '--model', str(pathlib.Path(args.model).resolve()), '--timeout-ms', '30000']
    checks = []

    def run(name, arguments, expected):
        process = subprocess.run([args.node, str(cli), 'visual', *arguments], capture_output=True,
                                 text=True, encoding='utf-8', timeout=40,
                                 creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        (out / (name + '.stdout.json')).write_text(process.stdout, encoding='utf-8')
        (out / (name + '.stderr.txt')).write_text(process.stderr, encoding='utf-8')
        value = json.loads(process.stdout)
        check_response(process.returncode, value, expected)
        checks.append({'operation': name, 'expected': expected, 'exitCode': process.returncode})
        return value

    image, manifest = fixtures / first['image'], fixtures / first['manifest']
    base = ['analyze', str(image), '--manifest', str(manifest), *common]
    run('analyze-pass', [*base, '--policy', str(pass_policy), '--out', str(out / 'pass'), '--correlation-id', 'cli-e2e', '--workflow-id', 'visual.fixture'], 'pass')
    receipt = validate_receipt(out / 'pass', 'pass')
    if receipt['correlationId'] != 'cli-e2e' or receipt['workflowId'] != 'visual.fixture':
        raise AssertionError('CLI receipt lost run correlation')
    run('analyze-fail', [*base, '--policy', str(fail_policy), '--out', str(out / 'fail')], 'fail')
    validate_receipt(out / 'fail', 'fail')
    run('adjustability-unproven', [*base, '--policy', str(adjustable_policy), '--out', str(out / 'adjustable')], 'inconclusive')
    validate_receipt(out / 'adjustable', 'inconclusive')
    run('report', ['report', str(out / 'pass'), '--format', 'json'], 'pass')
    run('compare-identical', ['compare', str(out / 'pass'), str(out / 'pass'), '--out', str(out / 'comparison.json')], 'pass')
    run('compare-policy-mismatch', ['compare', str(out / 'pass'), str(out / 'fail')], 'infrastructure-error')
    screens = []
    for record in selected:
        document = json.loads((fixtures / record['manifest']).read_text(encoding='utf-8'))
        (out / record['manifest']).write_text(json.dumps(document), encoding='utf-8')
        screens.append({'id': document['screenId'], 'screenshot': record['image'], 'manifest': record['manifest']})
    audit_manifest = out / 'audit-manifest.json'
    audit_manifest.write_text(json.dumps({'schemaVersion': 1, 'screens': screens}), encoding='utf-8')
    run('audit', ['audit', str(fixtures), '--manifest', str(audit_manifest), '--policy', str(pass_policy), '--out', str(out / 'audit'), *common], 'pass')
    run('audit-report', ['report', str(out / 'audit'), '--format', 'json'], 'pass')
    for screen in screens:
        validate_receipt(out / 'audit' / screen['id'], 'pass')
    run('duplicate-output', [*base, '--policy', str(pass_policy), '--out', str(out / 'pass')], 'infrastructure-error')
    summary = {'schemaVersion': 1, 'verified': True, 'checks': checks,
               'analyzerSha256': hashlib.sha256(pathlib.Path(args.analyzer).read_bytes()).hexdigest(),
               'modelSha256': hashlib.sha256(pathlib.Path(args.model).read_bytes()).hexdigest()}
    (out / 'cli-verification.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
