"""Verify real native audits at scale, bounded resources, and invalid-item isolation.

Requires the same Pillow development dependency as font-fixtures.py. Uses the actual
ath-exe CLI, native executable and pinned model; it never treats INCONCLUSIVE as PASS.
Memory is sampled process-tree RSS, not a claim about unsampled allocation peaks.
"""
import argparse
import ctypes
import hashlib
import json
import os
import pathlib
import platform
import shutil
import signal
import subprocess
import time


CODES = {'pass': 0, 'fail': 1, 'inconclusive': 2, 'infrastructure-error': 3}
MIB = 1024 * 1024


def digest(file):
    value = hashlib.sha256()
    with pathlib.Path(file).open('rb') as stream:
        for block in iter(lambda: stream.read(MIB), b''):
            value.update(block)
    return value.hexdigest()


def write_json(file, value):
    with pathlib.Path(file).open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')


def process_snapshot(root_pid):
    """Return pid -> (parent, executable, current RSS, OS peak RSS, start token)."""
    if os.name == 'nt':
        from ctypes import wintypes

        class Entry(ctypes.Structure):
            _fields_ = [('dwSize', wintypes.DWORD), ('cntUsage', wintypes.DWORD),
                        ('pid', wintypes.DWORD), ('heap', ctypes.c_size_t),
                        ('module', wintypes.DWORD), ('threads', wintypes.DWORD),
                        ('parent', wintypes.DWORD), ('priority', wintypes.LONG),
                        ('flags', wintypes.DWORD), ('name', wintypes.WCHAR * 260)]

        class Memory(ctypes.Structure):
            _fields_ = [('cb', wintypes.DWORD), ('faults', wintypes.DWORD),
                        ('peak', ctypes.c_size_t), ('rss', ctypes.c_size_t),
                        ('peak_paged', ctypes.c_size_t), ('paged', ctypes.c_size_t),
                        ('peak_nonpaged', ctypes.c_size_t), ('nonpaged', ctypes.c_size_t),
                        ('pagefile', ctypes.c_size_t), ('peak_pagefile', ctypes.c_size_t)]

        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        psapi = ctypes.WinDLL('psapi', use_last_error=True)
        kernel.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
        kernel.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
        kernel.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(Entry)]
        kernel.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(Entry)]
        kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME),
                                          ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME),
                                          ctypes.POINTER(wintypes.FILETIME)]
        psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(Memory), wintypes.DWORD]
        snapshot = kernel.CreateToolhelp32Snapshot(2, 0)
        if snapshot == ctypes.c_void_p(-1).value:
            raise OSError(ctypes.get_last_error(), 'Cannot inspect owned process tree')
        result = {}
        try:
            entry = Entry()
            entry.dwSize = ctypes.sizeof(entry)
            available = kernel.Process32FirstW(snapshot, ctypes.byref(entry))
            while available:
                result[entry.pid] = (entry.parent, entry.name, 0, 0)
                available = kernel.Process32NextW(snapshot, ctypes.byref(entry))
        finally:
            kernel.CloseHandle(snapshot)
        owned = {root_pid}
        while True:
            children = {pid for pid, (parent, *_rest) in result.items() if parent in owned}
            if children.issubset(owned):
                break
            owned.update(children)
        sampled = {}
        for pid in owned & result.keys():
            rss = peak = start = 0
            handle = kernel.OpenProcess(0x0400 | 0x0010, False, pid)
            if handle:
                try:
                    memory = Memory()
                    memory.cb = ctypes.sizeof(memory)
                    if psapi.GetProcessMemoryInfo(handle, ctypes.byref(memory), memory.cb):
                        rss, peak = memory.rss, memory.peak
                    created, exited, kernel_time, user_time = (wintypes.FILETIME() for _ in range(4))
                    if kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited),
                                              ctypes.byref(kernel_time), ctypes.byref(user_time)):
                        start = (created.dwHighDateTime << 32) | created.dwLowDateTime
                finally:
                    kernel.CloseHandle(handle)
            parent, name, *_rest = result[pid]
            sampled[pid] = (parent, name, rss, peak, start)
        return sampled
    result = {}
    for directory in pathlib.Path('/proc').iterdir():
        if not directory.name.isdigit():
            continue
        try:
            fields = {}
            for line in (directory / 'status').read_text().splitlines():
                key, value = line.split(':', 1)
                fields[key] = value.strip()
            result[int(directory.name)] = (
                int(fields['PPid']), fields['Name'],
                int(fields.get('VmRSS', '0 kB').split()[0]) * 1024,
                int(fields.get('VmHWM', '0 kB').split()[0]) * 1024,
                int((directory / 'stat').read_text().rsplit(')', 1)[1].split()[19]))
        except (FileNotFoundError, ProcessLookupError, PermissionError, KeyError):
            continue
    return result


def terminate_owned(process):
    if process.poll() is not None:
        return
    if os.name == 'nt':
        subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                       creationflags=subprocess.CREATE_NO_WINDOW, timeout=10, check=False)
    else:
        os.killpg(process.pid, signal.SIGKILL)
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        raise RuntimeError('Owned workload termination could not be confirmed')


def monitored_run(command, output, timeout, analyzer_name, maximum_output):
    stdout_path, stderr_path = output.with_suffix('.stdout.json'), output.with_suffix('.stderr.txt')
    metrics = {'requestedSampleIntervalMs': 50, 'samples': 0, 'largestSampleGapMs': 0,
               'sampledProcessTreePeakBytes': 0,
               'sampledNodePeakBytes': 0, 'sampledNativePeakBytes': 0,
               'maxObservedLiveNativeProcesses': 0, 'observedNode': False,
               'observedNative': False,
               'processCountScope': 'Sampled PID/start-token lifetimes; a lower bound because short processes can fall between samples'}
    started = last_sample = time.monotonic()
    tracked, native_pids = set(), set()
    with stdout_path.open('xb') as stdout, stderr_path.open('xb') as stderr:
        process = subprocess.Popen(command, stdout=stdout, stderr=stderr,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0,
                                   start_new_session=os.name != 'nt')
        try:
            while True:
                now = time.monotonic()
                metrics['largestSampleGapMs'] = max(metrics['largestSampleGapMs'], round((now - last_sample) * 1000, 3))
                metrics['samples'] += 1
                last_sample = now
                snapshot = process_snapshot(process.pid)
                owned = {process.pid}
                while True:
                    descendants = {pid for pid, (parent, *_rest) in snapshot.items() if parent in owned}
                    if descendants.issubset(owned):
                        break
                    owned.update(descendants)
                tracked.update((pid, snapshot[pid][4]) for pid in owned if pid in snapshot and snapshot[pid][4])
                records = [snapshot[pid] for pid in owned if pid in snapshot]
                metrics['sampledProcessTreePeakBytes'] = max(metrics['sampledProcessTreePeakBytes'],
                                                           sum(record[2] for record in records))
                native = [record for record in records if record[1].lower() == analyzer_name.lower()]
                native_pids.update((pid, snapshot[pid][4]) for pid in owned if pid in snapshot and snapshot[pid][4]
                                   and snapshot[pid][1].lower() == analyzer_name.lower())
                node = [record for record in records if record[1].lower() in ('node', 'node.exe')]
                metrics['observedNode'] |= bool(node)
                metrics['observedNative'] |= bool(native)
                metrics['maxObservedLiveNativeProcesses'] = max(metrics['maxObservedLiveNativeProcesses'], len(native))
                metrics['sampledNodePeakBytes'] = max(metrics['sampledNodePeakBytes'],
                                                    max((max(record[2:4]) for record in node), default=0))
                metrics['sampledNativePeakBytes'] = max(metrics['sampledNativePeakBytes'],
                                                      max((max(record[2:4]) for record in native), default=0))
                if stdout_path.stat().st_size + stderr_path.stat().st_size > maximum_output:
                    raise AssertionError('Workload stdout/stderr exceeded the verification bound')
                if process.poll() is not None:
                    break
                if time.monotonic() - started > timeout:
                    raise TimeoutError(f'Whole workload exceeded {timeout}s verification deadline')
                time.sleep(.05)
        except BaseException:
            terminate_owned(process)
            raise
    metrics.update({'elapsedSeconds': round(time.monotonic() - started, 3),
                    'exitCode': process.returncode, 'stdoutBytes': stdout_path.stat().st_size,
                    'stderrBytes': stderr_path.stat().st_size, 'observedProcessCount': len(tracked),
                    'observedNativeProcessCount': len(native_pids)})
    if metrics['stdoutBytes'] > maximum_output:
        raise AssertionError('Workload stdout exceeded the verification bound')
    return json.loads(stdout_path.read_text(encoding='utf-8-sig')), metrics


def compose_fixture(file, out):
    from PIL import Image, ImageChops
    if file.is_dir():
        file = file / 'representative-1080-light.png'
    document = json.loads(file.with_suffix('.json').read_text(encoding='utf-8'))
    source = Image.open(file).convert('RGB')
    element = next(element for element in document['elements'] if 'bounds' in element)
    bounds = element['bounds']
    crop = source.crop((bounds['x'], bounds['y'], bounds['x'] + bounds['width'], bounds['y'] + bounds['height']))
    background = source.getpixel((0, 0))
    channels = crop.split()
    if background not in ((0, 0, 0), (255, 255, 255)) or any(
            ImageChops.difference(channels[0], channel).getbbox() for channel in channels[1:]):
        raise ValueError('The independent crop-height oracle requires an achromatic black/white real-font fixture')
    gray = crop.convert('L')
    mask = gray.point((lambda v: 255 if v >= 64 else 0) if sum(background) < 384
                      else (lambda v: 255 if v <= 183 else 0))
    extent = mask.getbbox()
    if extent is None:
        raise ValueError('The supplied real-font region has no measurable glyph mask')
    truth = extent[3] - extent[1]
    margin = 40
    width = max(source.width, 2 * crop.width + 3 * margin)
    height = max(source.height, 2 * crop.height + 3 * margin)
    image = Image.new('RGB', (width, height), background)
    regions = []
    for index, (column, row) in enumerate(((0, 0), (1, 0), (0, 1), (1, 1))):
        x, y = margin + column * (crop.width + margin), margin + row * (crop.height + margin)
        image.paste(crop, (x, y))
        regions.append({'id': f'label-{index}', 'bounds': {'x': x, 'y': y, 'width': crop.width, 'height': crop.height},
                        'representativeGlyphs': True})
    image_path = out / 'four-region-fixture.png'
    image.save(image_path)
    return image_path, {'schemaVersion': 1, 'resolution': {'width': width, 'height': height},
                        'uiScale': document['uiScale'], 'locale': document['locale'], 'elements': regions}, truth, file


def make_audit(root, image, template, count, invalid_index=None, invalid_kind=None):
    inputs = root / 'inputs'
    inputs.mkdir(parents=True, exist_ok=False)
    screens = []
    for index in range(count):
        identity = f'screen-{index:04d}'
        screenshot, manifest = identity + '.png', identity + '.json'
        screen = {'id': identity, 'screenshot': screenshot, 'manifest': manifest}
        write_json(root / manifest, {**template, 'screenId': identity, 'screenshotId': screenshot})
        if index == invalid_index:
            if invalid_kind == 'malformed':
                (inputs / screenshot).write_bytes(b'not a PNG')
        else:
            try:
                os.link(image, inputs / screenshot)
            except OSError:
                shutil.copyfile(image, inputs / screenshot)
        screens.append(screen)
    # Deliberately reverse source order; output must retain authoritative sorted identities.
    write_json(root / 'audit-manifest.json', {'schemaVersion': 1, 'screens': list(reversed(screens))})
    return inputs, screens


def verify_receipt(directory, expected, screen, region_ids):
    receipt = json.loads((directory / 'receipt.json').read_text(encoding='utf-8'))
    result = json.loads((directory / 'result.json').read_text(encoding='utf-8'))
    if receipt.get('schemaVersion') != 1 or receipt.get('outcome') != expected or result.get('outcome') != expected:
        raise AssertionError('Per-screen outcome/receipt mismatch')
    if receipt.get('context', {}).get('screenId') != screen['id'] or result.get('context', {}).get('screenshotId') != screen['screenshot']:
        raise AssertionError('Per-screen identity mismatch')
    seen = set()
    for artifact in receipt.get('artifacts', []):
        relative = pathlib.PurePosixPath(artifact['path'])
        if relative.is_absolute() or '..' in relative.parts or str(relative) in seen:
            raise AssertionError('Unsafe or repeated artifact path')
        file = (directory / str(relative)).resolve()
        if not file.is_relative_to(directory.resolve()) or file.stat().st_size != artifact['byteLength'] or digest(file) != artifact['sha256']:
            raise AssertionError('Per-screen artifact identity mismatch')
        seen.add(str(relative))
    if not {'input.png', 'result.json', 'annotated.png', 'input-manifest.json', 'input-policy.json'}.issubset(seen):
        raise AssertionError('Per-screen evidence is incomplete')
    for rule in ('text.height', 'text.contrast', 'text.clipping'):
        findings = [finding for finding in result['findings'] if finding['ruleId'] == rule]
        if {finding['elementId'] for finding in findings} != region_ids or len(findings) != len(region_ids):
            raise AssertionError('The complete declared region list was not evaluated')
    return result, (directory / 'result.json').stat().st_size


def normalize_findings(result):
    keys = ('ruleId', 'elementId', 'outcome', 'measuredHeightPx', 'contrastRatio', 'bounds', 'expectedBounds')
    return [{key: finding.get(key) for key in keys} for finding in result['findings']]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('analyzer', 'model', 'fixture', 'out'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--node', default='node')
    parser.add_argument('--sizes', type=int, nargs='+', default=[100, 256, 1024])
    parser.add_argument('--per-screen-timeout-ms', type=int, default=30000)
    parser.add_argument('--max-node-mib', type=int, default=512)
    parser.add_argument('--max-tree-mib', type=int, default=1024)
    parser.add_argument('--max-output-mib', type=int, default=4)
    parser.add_argument('--max-evidence-mib', type=int, default=512)
    args = parser.parse_args()
    if any(not 1 <= size <= 1024 for size in args.sizes) or not 1 <= args.per_screen_timeout_ms <= 3600000:
        parser.error('sizes must be 1..1024 and per-screen timeout 1..3600000ms')
    out = pathlib.Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=False)
    analyzer, model = pathlib.Path(args.analyzer).resolve(), pathlib.Path(args.model).resolve()
    model_spec = json.loads((pathlib.Path(__file__).parent / 'model.json').read_text(encoding='utf-8'))
    if model.stat().st_size != model_spec['bytes'] or digest(model) != model_spec['sha256']:
        raise ValueError('Native workload requires the pinned real detector model')
    image, template, height, source_fixture = compose_fixture(pathlib.Path(args.fixture).resolve(), out)
    regions = {element['id'] for element in template['elements']}
    policy = out / 'policy.json'
    write_json(policy, {'schemaVersion': 1, 'id': 'batch-workload', 'version': '1',
                        'source': 'fixture://independent-real-font-crop', 'minHeightPx': max(1, height - 1), 'minContrastRatio': 4.5})
    cli = pathlib.Path(__file__).resolve().parent.parent / 'ath-exe-client' / 'ath-exe.js'
    repository = pathlib.Path(__file__).resolve().parents[2]
    source_paths = ['Tools~/ath-exe-client/ath-exe.js', 'Tools~/ath-exe-client/visual.js',
                    'Tools~/ath-exe-client/visual-reports.js', 'Tools~/ath-visual/src/main.cpp',
                    'Tools~/ath-visual/src/analysis.cpp', 'Tools~/ath-visual/src/analysis.hpp',
                    'Tools~/ath-visual/src/batch_digest.hpp',
                    'Tools~/visual-verification/check-batch-workloads.py']
    source_snapshot = {name: digest(repository / name) for name in source_paths}
    process_options = {'capture_output': True, 'text': True, 'encoding': 'utf-8', 'timeout': 10,
                       'creationflags': subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0}
    head = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=repository, check=True, **process_options).stdout.strip()
    node_version = subprocess.run([args.node, '--version'], check=True, **process_options).stdout.strip()
    checks = []
    summary = {'schemaVersion': 1, 'verified': False, 'analyzerSha256': digest(analyzer),
               'modelSha256': digest(model), 'fixtureSha256': digest(image),
               'policySha256': digest(policy),
               'sourceHead': head, 'sourceSnapshots': source_snapshot,
               'sourceFixture': {'path': str(source_fixture), 'sha256': digest(source_fixture),
                                 'manifestSha256': digest(source_fixture.with_suffix('.json'))},
               'environment': {'platform': platform.platform(), 'machine': platform.machine(),
                               'processor': platform.processor(), 'logicalProcessors': os.cpu_count(),
                               'python': platform.python_version(), 'node': node_version,
                               'nativeConcurrency': 1, 'opencvThreadCount': 'not overridden'},
               'bounds': {'nodeRssBytes': args.max_node_mib * MIB, 'processTreeRssBytes': args.max_tree_mib * MIB,
                          'stdoutAndStderrBytes': args.max_output_mib * MIB, 'evidenceBytesPerWorkload': args.max_evidence_mib * MIB},
               'independentVisibleHeightPx': height, 'regionsPerScreenshot': len(regions),
               'checks': checks, 'timingScope': 'Process-inclusive local workload timings, not a generalized throughput guarantee',
               'scope': 'Real-font audit execution and evidence only; not accessibility certification or general detector accuracy'}
    baseline = None

    def run(name, count, invalid_index=None, invalid_kind=None, timeout_ms=None, deadline_probe=False):
        nonlocal baseline
        root = out / name
        root.mkdir()
        inputs, screens = make_audit(root, image, template, count, invalid_index, invalid_kind)
        selected_timeout = timeout_ms if timeout_ms is not None else args.per_screen_timeout_ms
        command = [args.node, str(cli), 'visual', 'audit', str(inputs), '--manifest', str(root / 'audit-manifest.json'),
                   '--out', str(root / 'evidence'), '--analyzer', str(analyzer), '--model', str(model),
                   '--policy', str(policy), '--timeout-ms', str(selected_timeout)]
        result, metrics = monitored_run(command, root / 'process', 30 if deadline_probe else max(120, count * 2 + 60),
                                        analyzer.name, args.max_output_mib * MIB)
        expected = 'infrastructure-error' if invalid_index is not None or deadline_probe else 'pass'
        if result.get('schemaVersion') != 2 or result.get('outcome') != expected or metrics['exitCode'] != CODES[expected]:
            raise AssertionError(f'{name}: expected {expected}, got {result.get("outcome")}/{metrics["exitCode"]}')
        entries = result.get('entries', [])
        if [entry.get('id') for entry in entries] != [screen['id'] for screen in screens]:
            raise AssertionError(f'{name}: incomplete or nondeterministic authoritative screen list')
        retained = json.loads((root / 'evidence' / 'input-audit-manifest.json').read_text(encoding='utf-8'))
        authoritative_hash = digest(root / 'audit-manifest.json')
        if (len(retained['screens']) != len(screens) or
                {screen['id'] for screen in retained['screens']} != {screen['id'] for screen in screens} or
                digest(root / 'evidence' / 'input-audit-manifest.json') != authoritative_hash or
                result.get('manifestSha256') != authoritative_hash):
            raise AssertionError('The authoritative manifest was not retained')
        if json.loads((root / 'evidence' / 'audit.json').read_text(encoding='utf-8')) != result:
            raise AssertionError('The CLI audit result differs from persisted authoritative evidence')
        receipt_count = payload_bytes = 0
        for index, (screen, entry) in enumerate(zip(screens, entries)):
            if deadline_probe or index == invalid_index:
                if entry.get('outcome') != 'infrastructure-error' or entry.get('receipt'):
                    raise AssertionError(f'{name}: invalid screen was skipped or falsely certified')
                if deadline_probe and not any(word in entry.get('message', '').lower() for word in ('timed out', 'deadline')):
                    raise AssertionError('The deadline probe did not fail because of a deadline')
                continue
            if entry.get('outcome') != 'pass' or entry.get('receipt') != screen['id'] + '/receipt.json':
                raise AssertionError(f'{name}: one invalid item affected a valid screen')
            per_screen, size = verify_receipt(root / 'evidence' / screen['id'], 'pass', screen, regions)
            payload_bytes += size
            normalized = normalize_findings(per_screen)
            if baseline is None:
                baseline = normalized
            elif normalized != baseline:
                raise AssertionError(f'{name}: valid screen differs from the independently evaluated baseline')
            receipt_count += 1
        if receipt_count != (0 if deadline_probe else count - (invalid_index is not None)):
            raise AssertionError('Incorrect per-screen evidence count')
        if not deadline_probe and (not metrics['observedNode'] or not metrics['observedNative'] or
                                   not metrics['sampledNodePeakBytes'] or not metrics['sampledNativePeakBytes']):
            raise AssertionError('Memory sampling did not observe the real Node/native process tree')
        if not deadline_probe and metrics['maxObservedLiveNativeProcesses'] > 1:
            raise AssertionError('Native workload concurrency exceeded the bounded single-worker design')
        if count >= 100 and metrics['observedNativeProcessCount'] >= count:
            raise AssertionError('The real workload did not reuse a detector process across screenshots')
        if metrics['sampledNodePeakBytes'] > args.max_node_mib * MIB or metrics['sampledProcessTreePeakBytes'] > args.max_tree_mib * MIB:
            raise AssertionError(f'{name}: resident memory exceeded the fixed workload bound')
        evidence_bytes = sum(file.stat().st_size for file in (root / 'evidence').rglob('*') if file.is_file())
        if evidence_bytes > args.max_evidence_mib * MIB:
            raise AssertionError(f'{name}: evidence exceeded bounded verification disk budget')
        if count == 1024 and payload_bytes <= 4 * MIB:
            raise AssertionError('The 1024-screen fixture did not exercise the old 4MiB aggregate result-output limit')
        record = {'name': name, 'screens': count, 'invalidIndex': invalid_index, 'invalidKind': invalid_kind,
                  'outcome': expected, 'receiptCount': receipt_count, 'resultPayloadBytes': payload_bytes,
                  'evidenceBytes': evidence_bytes, 'perScreenTimeoutMs': selected_timeout, **metrics}
        if not deadline_probe:
            record['screensPerObservedNativeProcess'] = round(receipt_count / metrics['observedNativeProcessCount'], 3)
            record['elapsedSecondsPerScreen'] = round(metrics['elapsedSeconds'] / count, 6)
        checks.append(record)
        write_json(root / 'verification.json', record)
        print(json.dumps(record), flush=True)
        return record

    def compare_detector_reuse(grouped):
        count = 8
        root = out / 'reuse-single-processes'
        root.mkdir()
        inputs, screens = make_audit(root, image, template, count)
        (root / 'evidence').mkdir()
        records = []
        for index, screen in enumerate(screens):
            directory = root / 'evidence' / screen['id']
            command = [args.node, str(cli), 'visual', 'analyze', str(inputs / screen['screenshot']),
                       '--manifest', str(root / screen['manifest']), '--out', str(directory),
                       '--analyzer', str(analyzer), '--model', str(model), '--policy', str(policy),
                       '--timeout-ms', str(args.per_screen_timeout_ms)]
            value, metrics = monitored_run(command, root / f'process-{index}',
                                            max(60, args.per_screen_timeout_ms / 1000 + 30),
                                            analyzer.name, args.max_output_mib * MIB)
            if metrics['exitCode'] != 0 or value.get('result', {}).get('outcome') != 'pass':
                raise AssertionError('The single-process detector-reuse baseline did not pass')
            result, _size = verify_receipt(directory, 'pass', screen, regions)
            if normalize_findings(result) != baseline or metrics['observedNativeProcessCount'] != 1:
                raise AssertionError('The detector-reuse comparison did not evaluate matching inputs in distinct native processes')
            if (metrics['sampledNodePeakBytes'] > args.max_node_mib * MIB or
                    metrics['sampledProcessTreePeakBytes'] > args.max_tree_mib * MIB):
                raise AssertionError('The detector-reuse single-process baseline exceeded the resource bound')
            records.append(metrics)
        single_seconds = round(sum(record['elapsedSeconds'] for record in records), 3)
        single_processes = sum(record['observedNativeProcessCount'] for record in records)
        if grouped['observedNativeProcessCount'] >= single_processes:
            raise AssertionError('Grouped processing did not reuse the detector process')
        comparison = {'screens': count, 'groupedElapsedSeconds': grouped['elapsedSeconds'],
                      'singleProcessElapsedSeconds': single_seconds,
                      'groupedNativeProcesses': grouped['observedNativeProcessCount'],
                      'singleNativeProcesses': single_processes,
                      'singleToGroupedElapsedRatio': round(single_seconds / grouped['elapsedSeconds'], 4),
                      'singleRuns': records,
                      'scope': 'Eight-screen audit versus eight sequential analyze commands with the same image/model/policy; process-inclusive local timings, not a pure model-load benchmark or guaranteed speedup'}
        write_json(root / 'verification.json', comparison)
        print(json.dumps({'detectorReuseComparison': comparison}), flush=True)
        return comparison

    try:
        run('baseline', 1)
        summary['detectorReuseComparison'] = compare_detector_reuse(run('reuse-grouped', 8))
        for size in args.sizes:
            run(f'audit-{size}', size)
        for kind in ('missing', 'malformed'):
            for label, index in (('first', 0), ('middle', 3), ('last', 6)):
                run(f'isolation-{kind}-{label}', 7, index, kind)
        run('deadline-probe', 3, timeout_ms=1, deadline_probe=True)
        if (source_snapshot != {name: digest(repository / name) for name in source_paths} or
                digest(analyzer) != summary['analyzerSha256'] or digest(model) != summary['modelSha256'] or
                digest(image) != summary['fixtureSha256'] or digest(policy) != summary['policySha256']):
            raise AssertionError('Runtime source, native executable, model or fixture changed during workload verification')
        summary['verified'] = True
    finally:
        write_json(out / 'verification.json', summary)
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
