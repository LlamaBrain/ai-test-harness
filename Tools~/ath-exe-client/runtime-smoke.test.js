'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { validateRuntimeSmokeEvidence, createRuntimeSmokeLaunch, launchRuntimeSmokeProcess, observeProcessIdentity, createBuildToken } = require('./runtime-smoke-runner');
const validate = (value, expected = value.launch) => validateRuntimeSmokeEvidence(value, expected);

const startedUtc = '2026-09-23T18:00:00.000Z';
const processStartedUtc = '2026-09-23T18:00:02.000Z';
const terminalUtc = '2026-09-23T18:00:05.000Z';
const exitedUtc = '2026-09-23T18:00:06.000Z';
const hash = 'a'.repeat(64);
const nonce = 'b'.repeat(64);

function stableJson(value) {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`;
  if (value && typeof value === 'object') return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(',')}}`;
  return JSON.stringify(value);
}
function buildTokenFor(envelope) {
  return require('node:crypto').createHash('sha256')
    .update(stableJson({ ...envelope, buildToken: null }), 'utf8').digest('hex');
}

test('launch build-token canonicalization matches the Unity player claim contract', () => {
  const launch = {
    artifactRoot: 'C:/runs/r1', backend: 'IL2CPP', buildEvidenceHash: '1'.repeat(64),
    buildEvidenceSnapshotPath: 'C:/runs/r1/build-evidence.json', buildRevision: 'rev-1', buildToken: null,
    buildVersion: '1.2.3', contentHash: '2'.repeat(64), correlationId: 'corr-1',
    executablePath: 'C:/build/game.exe', executableSha256: '3'.repeat(64), hostRoot: 'C:/runs',
    authoredRoot: 'C:/runs/Assets/RuntimeSmokes', logPath: 'C:/runs/r1/logs/player.log', mode: 'fsd', nonce: '4'.repeat(64), outputRoot: 'C:/runs/output', processId: null,
    processStartUtc: null, profileHash: '5'.repeat(64), requiredStepIds: ['ready', 'finish'],
    runDirectory: 'C:/runs/r1', runId: 'r1', scenario: 'scenario-a', schemaVersion: 1,
    scriptHash: '6'.repeat(64), seed: '42', startedUtc: '2026-09-23T18:00:00.000Z',
    state: 'awaiting_player', stripping: 'High', suiteId: 'suite-a', target: 'development',
    timeoutSeconds: 90, workflowId: 'workflows/test',
  };
  assert.equal(createBuildToken(launch), 'd0c037f9baf8fdcff2d6d509f5f26f565c5d5b329ac9e1badd2013e0eccf8450');
});

function evidence() {
  const value = {
    launch: {
      schemaVersion: 1, runId: 'run-1', correlationId: 'corr-1', workflowId: 'workflows/test',
      scriptHash: hash, nonce, buildToken: null, startedUtc, timeoutSeconds: 60,
      hostRoot: 'C:\\Host', outputRoot: 'C:\\Host\\Artifacts', authoredRoot: 'C:\\Host\\Assets\\RuntimeSmokes',
      executablePath: 'C:\\Build\\game.exe', executableSha256: hash, logPath: 'C:\\Runs\\run-1\\logs\\player.log',
      buildRevision: 'rev-1', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      scenario: 'fixture', seed: '42',
      requiredStepIds: ['ready'],
    },
    armed: null,
    transcript: null,
    process: null,
    log: null,
    result: null,
  };
  const launch = value.launch;
  launch.buildToken = buildTokenFor(launch);
  value.armed = {
      state: 'armed', runId: 'run-1', correlationId: 'corr-1', scriptHash: hash,
      nonce, buildToken: launch.buildToken, processId: 17, processStartUtc: processStartedUtc,
      executableSha256: hash, buildRevision: 'rev-1', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      scenario: 'fixture', seed: '42',
  };
  value.transcript = [
      { sequence: 0, type: 'armed', status: 'armed', correlationId: 'corr-1', workflowId: 'workflows/test', scriptHash: hash, scenario: 'fixture', seed: '42', timestampUtc: processStartedUtc },
      { sequence: 1, type: 'run_started', status: 'running', correlationId: 'corr-1', workflowId: 'workflows/test', scriptHash: hash, scenario: 'fixture', seed: '42', timestampUtc: processStartedUtc },
      { sequence: 2, type: 'step_completed', stepId: 'ready', status: 'passed', correlationId: 'corr-1', workflowId: 'workflows/test', scriptHash: hash, scenario: 'fixture', seed: '42', timestampUtc: '2026-09-23T18:00:04.000Z' },
      { sequence: 3, type: 'run_terminal', status: 'passed', correlationId: 'corr-1', workflowId: 'workflows/test', scriptHash: hash, scenario: 'fixture', seed: '42', timestampUtc: terminalUtc },
  ];
  value.process = { processId: 17, processStartUtc: processStartedUtc, exitedUtc, exitCode: 0,
      executablePath: 'C:\\Build\\game.exe', executableSha256: hash },
  value.log = { path: 'C:\\Runs\\run-1\\logs\\player.log', length: 100, lastWriteUtc: terminalUtc };
  value.result = { correlationId: 'corr-1', workflowId: 'workflows/test', scriptHash: hash,
    scenario: 'fixture', seed: '42', terminalState: 'passed', startedUtc: processStartedUtc, endedUtc: terminalUtc, failureCode: null };
  return value;
}

test('accepts exactly one complete PASS bound to the armed process and launch identity', () => {
  assert.equal(validate(evidence()).accepted, true);
});

test('accepts a durable runner armed prelude written before the player process starts', () => {
  const sample = evidence();
  sample.transcript[0].timestampUtc = startedUtc;
  assert.equal(validate(sample).accepted, true);
});

test('rejects scenario or seed drift in armed identity and every transcript event', () => {
  for (const mutate of [
    (sample) => { sample.armed.scenario = 'other'; },
    (sample) => { sample.armed.seed = '43'; },
    (sample) => { delete sample.armed.scenario; },
  ]) {
    const sample = evidence();
    mutate(sample);
    assert.equal(validate(sample).code, 'provenance_mismatch');
  }
  for (const mutate of [
    (sample) => { sample.transcript[2].scenario = 'other'; },
    (sample) => { sample.transcript[2].seed = '43'; },
    (sample) => { delete sample.transcript[2].seed; },
  ]) {
    const sample = evidence();
    mutate(sample);
    assert.equal(validate(sample).code, 'identity_mismatch');
  }
});

test('rejects structured result scenario or seed drift from the claimed launch', () => {
  for (const mutate of [
    (sample) => { sample.result.scenario = 'other'; },
    (sample) => { sample.result.seed = '43'; },
    (sample) => { delete sample.result.scenario; },
  ]) {
    const sample = evidence();
    mutate(sample);
    assert.equal(validate(sample).code, 'result_mismatch');
  }
});

test('treats Unity-serialized empty scenario and seed as absent without allowing identity drift', () => {
  const sample = evidence();
  sample.launch.scenario = null;
  sample.launch.seed = null;
  sample.launch.buildToken = buildTokenFor(sample.launch);
  sample.armed.buildToken = sample.launch.buildToken;
  sample.armed.scenario = '';
  sample.armed.seed = '';
  for (const record of sample.transcript) {
    record.scenario = '';
    record.seed = '';
  }
  sample.result.scenario = '';
  sample.result.seed = '';
  assert.equal(validate(sample).accepted, true);
  sample.transcript[1].scenario = 'different-fixture';
  assert.equal(validate(sample).code, 'identity_mismatch');
});

test('observes the current Windows process image and start time through the OS', { skip: process.platform !== 'win32' }, () => {
  const identity = observeProcessIdentity(process.pid);
  assert.equal(path.resolve(identity.executablePath).toLowerCase(), path.resolve(process.execPath).toLowerCase());
  assert.ok(Number.isFinite(Date.parse(identity.processStartUtc)));
});

test('rejects missing or duplicate terminal records', () => {
  const missing = evidence();
  missing.transcript.pop();
  assert.equal(validate(missing).code, 'terminal_missing');

  const duplicate = evidence();
  duplicate.transcript.push({ ...duplicate.transcript.at(-1), sequence: 4 });
  assert.equal(validate(duplicate).code, 'terminal_duplicate');
});

test('rejects non-PASS terminal, wrong run identity, and non-monotonic sequence', () => {
  const failed = evidence();
  failed.transcript.at(-1).status = 'failed';
  assert.equal(validate(failed).code, 'terminal_not_pass');

  const mismatch = evidence();
  mismatch.transcript[1].correlationId = 'other';
  assert.equal(validate(mismatch).code, 'identity_mismatch');

  const unordered = evidence();
  unordered.transcript[1].sequence = 4;
  assert.equal(validate(unordered).code, 'sequence_invalid');
});

test('rejects an explicit failed step or assertion even if a later terminal claims PASS', () => {
  for (const [type, status] of [['step_failed', 'failed'], ['assertion_failed', 'failed'],
    ['run_timeout', 'timeout'], ['protocol_error', 'error']]) {
    const sample = evidence();
    sample.transcript.splice(3, 0, { ...sample.transcript[2], sequence: 3, type, status });
    sample.transcript.forEach((record, index) => { record.sequence = index; });
    sample.transcript.at(-1).sequence = 4;
    assert.equal(validate(sample).accepted, false, type);
  }
});

test('rejects evidence outside process lifetime or a nonzero process exit', () => {
  const early = evidence();
  early.transcript.at(-1).timestampUtc = startedUtc;
  assert.equal(validate(early).code, 'timestamp_invalid');

  const late = evidence();
  late.transcript.at(-1).timestampUtc = '2026-09-23T18:00:07.000Z';
  assert.equal(validate(late).code, 'timestamp_invalid');

  const exit = evidence();
  exit.process.exitCode = 1;
  assert.equal(validate(exit).code, 'process_exit_failed');

  const expired = evidence();
  expired.launch.timeoutSeconds = 5;
  expired.launch.buildToken = buildTokenFor(expired.launch);
  expired.armed.buildToken = expired.launch.buildToken;
  assert.equal(validate(expired).code, 'watchdog_timeout');
});

test('rejects stale, empty, or wrong-path player logs and changed executable provenance', () => {
  const stale = evidence();
  stale.log.lastWriteUtc = startedUtc;
  assert.equal(validate(stale).code, 'log_invalid');

  const empty = evidence();
  empty.log.length = 0;
  assert.equal(validate(empty).code, 'log_invalid');

  const binary = evidence();
  binary.process.executableSha256 = 'f'.repeat(64);
  assert.equal(validate(binary).code, 'provenance_mismatch');
});

test('requires structured result identity and terminal state to agree with the transcript', () => {
  const missingStep = evidence();
  missingStep.transcript.splice(2, 1);
  missingStep.transcript.forEach((record, index) => { record.sequence = index; });
  assert.equal(validate(missingStep).code, 'steps_missing');

  const mismatch = evidence();
  mismatch.result.correlationId = 'other';
  assert.equal(validate(mismatch).code, 'result_mismatch');

  const wrongWorkflow = evidence();
  wrongWorkflow.transcript[1].workflowId = 'workflows/other';
  assert.equal(validate(wrongWorkflow).code, 'identity_mismatch');

  const disagreement = evidence();
  disagreement.result.terminalState = 'failed';
  assert.equal(validate(disagreement).code, 'result_mismatch');
  const reversed = evidence();
  reversed.result.startedUtc = '2026-09-23T18:00:06.000Z';
  assert.equal(validate(reversed).code, 'result_mismatch');
  const resultAfterTerminal = evidence();
  resultAfterTerminal.result.endedUtc = '2026-09-23T18:00:06.000Z';
  assert.equal(validate(resultAfterTerminal).code, 'result_mismatch');
});

test('rejects wrong nonce, build token, or watched process identity', () => {
  for (const mutation of [
    (e) => { e.armed.nonce = 'd'.repeat(64); },
    (e) => { e.armed.buildToken = 'd'.repeat(64); },
    (e) => { e.process.processId++; },
  ]) {
    const sample = evidence();
    mutation(sample);
    assert.equal(validate(sample).code, 'provenance_mismatch');
  }
});

test('rejects a launch envelope changed after the runner created its trust anchor', () => {
  const sample = evidence();
  const trusted = structuredClone(sample.launch);
  sample.launch.buildRevision = 'forged-revision';
  sample.armed.buildRevision = 'forged-revision';
  for (const record of sample.transcript) record.scriptHash = 'f'.repeat(64);
  sample.result.scriptHash = 'f'.repeat(64);
  assert.equal(validate(sample, trusted).code, 'launch_tampered');
});

test('rejects a pre-seeded or non-empty artifact root before launching', async () => {
  const { prepareRuntimeSmokeRun } = require('./runtime-smoke-runner');
  const fs = {
    lstatSync: () => ({ isSymbolicLink: () => false, isDirectory: () => true }),
    readdirSync: () => ['result.json'],
  };
  await assert.rejects(
    prepareRuntimeSmokeRun({ artifactRoot: '/runs/run-1', fs }),
    (error) => error.code === 'artifact_root_not_empty'
  );
});

test('creates a fresh, provenance-bound launch envelope and isolated run folders', () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-launch-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts', 'RuntimeSmokes');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'exact-build-bytes');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({
      schemaVersion: 1,
      artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('exact-build-bytes').digest('hex'),
      revision: 'rev-1',
      version: '1.2.3',
      profileHash: 'd'.repeat(64),
      contentHash: 'e'.repeat(64),
      backend: 'IL2CPP',
      stripping: 'High',
      lane: 'development',
    }));

    const launchOptions = {
      hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'run-1', correlationId: 'corr-1', workflowId: 'workflows/test', canonicalScriptHash: '797cfa2aedb128f90eca43b0487a036147f6e05f0a249e9d5c639b9edf9e9b06',
      target: 'development', mode: 'fsd', suiteId: 'suites/test', scenario: 'fixture', seed: 42,
      timeoutSeconds: 60, requiredStepIds: ['ready', 'completed'], now: () => new Date(startedUtc), randomBytes: () => Buffer.from(nonce, 'hex'),
      requiredStepIds: ['ready', 'completed'],
    };
    assert.throws(() => createRuntimeSmokeLaunch({ ...launchOptions, seed: '' }),
      (error) => error.code === 'launch_input_invalid');
    assert.throws(() => createRuntimeSmokeLaunch({ ...launchOptions, runId: 'raw-hash-only', canonicalScriptHash: undefined, scriptHash: hash }),
      (error) => error.code === 'launch_input_invalid');
    const result = createRuntimeSmokeLaunch(launchOptions);
    const stringSeed = createRuntimeSmokeLaunch({
      ...launchOptions, runId: 'string-seed-run', correlationId: 'string-seed-correlation', seed: 'read-only-v1',
    });
    assert.equal(stringSeed.envelope.seed, 'read-only-v1');
    assert.throws(() => createRuntimeSmokeLaunch({
      ...launchOptions, runId: 'bad-seed-run', correlationId: 'bad-seed-correlation', seed: 'bad/seed',
    }), (error) => error.code === 'launch_input_invalid');

    assert.equal(result.envelope.executableSha256, require('node:crypto').createHash('sha256').update('exact-build-bytes').digest('hex'));
    const compiledHash = '797cfa2aedb128f90eca43b0487a036147f6e05f0a249e9d5c639b9edf9e9b06';
    assert.equal(result.envelope.scriptHash, compiledHash);
    const prelude = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'transcript.jsonl'), 'utf8').trim());
    assert.equal(prelude.scriptHash, compiledHash);
    assert.match(result.envelope.nonce, /^[0-9a-f]{64}$/);
    assert.match(result.envelope.buildToken, /^[0-9a-f]{64}$/);
    assert.equal(fs.existsSync(path.join(result.runDirectory, 'scratch')), true);
    assert.equal(fs.existsSync(path.join(result.runDirectory, 'logs')), true);
    assert.equal(fs.existsSync(path.join(result.runDirectory, 'captures')), true);
    assert.deepEqual(JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-launch.json'), 'utf8')), result.envelope);
    assert.equal(JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'build-evidence.json'), 'utf8')).artifactSha256,
      result.envelope.executableSha256);
    assert.deepEqual(result.args.slice(0, 2), ['-runtimeSmoke', 'workflows/test']);
    assert.ok(result.args.includes('-runtimeSmokeArtifactRoot'));
    assert.deepEqual(result.envelope.requiredStepIds, ['ready', 'completed']);
    assert.ok(result.args.includes('-logFile'));
    assert.equal(result.args[result.args.indexOf('-logFile') + 1], result.logPath);
  } finally {
    fs.rmSync(parent, { recursive: true, force: true });
  }
});

test('rejects a legacy-Windows overlong player log path before claiming a run directory', { skip: process.platform !== 'win32' }, () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-long-player-path-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts', 'RuntimeSmokes');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    const runId = 'r'.repeat(160);
    assert.throws(() => createRuntimeSmokeLaunch({ hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId, correlationId: 'long-path-corr', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 60, requiredStepIds: ['ready'] }),
    (error) => error.code === 'player_artifact_path_too_long' && /-logFile/.test(error.message) && /shorter/.test(error.message));
    assert.equal(fs.existsSync(path.join(outputRoot, runId)), false);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});

test('does not overwrite a colliding launch run directory or accept changed build bytes', () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-collision-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'changed-bytes');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development', artifactSha256: '0'.repeat(64) }));
    const options = { hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'collision', correlationId: 'corr-1', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 60, requiredStepIds: ['ready'] };
    assert.throws(() => createRuntimeSmokeLaunch(options), (error) => error.code === 'build_provenance_mismatch');
    const evidenceDocument = JSON.parse(fs.readFileSync(buildEvidencePath, 'utf8'));
    evidenceDocument.artifactSha256 = require('node:crypto').createHash('sha256').update('changed-bytes').digest('hex');
    fs.writeFileSync(buildEvidencePath, JSON.stringify(evidenceDocument));
    const colliding = path.join(outputRoot, 'collision');
    fs.mkdirSync(colliding);
    fs.writeFileSync(path.join(colliding, 'keep.txt'), 'preserve');
    assert.throws(() => createRuntimeSmokeLaunch(options), (error) => error.code === 'run_directory_collision');
    assert.equal(fs.readFileSync(path.join(colliding, 'keep.txt'), 'utf8'), 'preserve');
  } finally {
    fs.rmSync(parent, { recursive: true, force: true });
  }
});

test('requires the build evidence file to remain within the selected host', () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-evidence-path-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(parent, 'outside-build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    assert.throws(() => createRuntimeSmokeLaunch({ hostRoot, outputRoot, authoredRoot, executablePath,
      buildEvidencePath, runId: 'run-1', correlationId: 'corr-1', workflowId: 'workflows/test',
      canonicalScriptHash: hash, target: 'development', mode: 'fsd', timeoutSeconds: 60, requiredStepIds: ['ready'] }),
    (error) => error.code === 'build_provenance_mismatch');
  } finally {
    fs.rmSync(parent, { recursive: true, force: true });
  }
});

test('rejects run IDs that traverse or escape the output root', () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-runid-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    for (const runId of ['.', '..', '../escape', 'nested/run']) {
      assert.throws(() => createRuntimeSmokeLaunch({ hostRoot, outputRoot, authoredRoot, executablePath,
        buildEvidencePath, runId, correlationId: 'corr-1', workflowId: 'workflows/test', canonicalScriptHash: hash,
        target: 'development', mode: 'fsd', timeoutSeconds: 60, requiredStepIds: ['ready'] }),
      (error) => error.code === 'launch_input_invalid' || error.code === 'artifact_root_escape');
    }
    assert.equal(fs.existsSync(hostRoot + path.sep + 'runtime-smoke-launch.json'), false);
  } finally {
    fs.rmSync(parent, { recursive: true, force: true });
  }
});

test('waits for late player evidence, writes its process watch record, and validates terminal evidence', async () => {
  const EventEmitter = require('node:events');
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-process-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    const child = new EventEmitter();
    child.pid = 4242;
    child.kill = () => true;
    let capturedArgs;
    const result = await launchRuntimeSmokeProcess({
      hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'process-run', correlationId: 'process-corr', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 30, requiredStepIds: ['ready'],
      spawnProcess: (_exe, args) => {
        capturedArgs = args;
        const runDirectory = args[args.indexOf('-runtimeSmokeArtifactRoot') + 1];
        const launchAtSpawn = JSON.parse(fs.readFileSync(path.join(runDirectory, 'runtime-smoke-launch.json'), 'utf8'));
        assert.equal(fs.existsSync(path.dirname(launchAtSpawn.logPath)), true, 'Unity log parent must exist before spawn');
        assert.equal(fs.existsSync(path.dirname(path.join(runDirectory, 'logs', 'player.stdout.txt'))), true,
          'captured process output parent must exist before spawn');
        assert.match(launchAtSpawn.nonce, /^[0-9a-f]{64}$/);
        assert.match(launchAtSpawn.buildToken, /^[0-9a-f]{64}$/);
        assert.equal(args[args.indexOf('-runtimeSmokeNonce') + 1], launchAtSpawn.nonce);
        assert.equal(args[args.indexOf('-runtimeSmokeBuildToken') + 1], launchAtSpawn.buildToken);
        const transcriptPath = path.join(runDirectory, 'transcript.jsonl');
        assert.equal(fs.existsSync(transcriptPath), true, 'armed metadata transcript must be durable before spawn');
        const preSpawnLines = fs.readFileSync(transcriptPath, 'utf8').trimEnd().split(/\r?\n/);
        assert.equal(preSpawnLines.length, 1);
        const preSpawnArmed = JSON.parse(preSpawnLines[0]);
        assert.equal(preSpawnArmed.sequence, 0);
        assert.equal(preSpawnArmed.type, 'armed');
        assert.equal(preSpawnArmed.status, 'armed');
        assert.equal(preSpawnArmed.correlationId, launchAtSpawn.correlationId);
        assert.equal(preSpawnArmed.workflowId, launchAtSpawn.workflowId);
        assert.equal(preSpawnArmed.scriptHash, launchAtSpawn.scriptHash);
        assert.equal(fs.existsSync(path.join(runDirectory, 'runtime-smoke-armed.json')), false,
          'player claim identity must not be pre-seeded');
        setImmediate(() => child.emit('spawn'));
        setTimeout(() => {
          const launch = JSON.parse(fs.readFileSync(path.join(runDirectory, 'runtime-smoke-launch.json'), 'utf8'));
          const processRecord = JSON.parse(fs.readFileSync(path.join(runDirectory, 'runtime-smoke-process.json'), 'utf8'));
          const terminalTime = new Date().toISOString();
          const common = { correlationId: launch.correlationId, workflowId: launch.workflowId, scriptHash: launch.scriptHash,
            scenario: launch.scenario, seed: launch.seed };
          const armed = { ...common, state: 'armed', runId: launch.runId, nonce: launch.nonce,
            buildToken: launch.buildToken, processId: processRecord.processId, processStartUtc: processRecord.processStartUtc,
            executableSha256: launch.executableSha256, buildRevision: launch.buildRevision,
            profileHash: launch.profileHash, contentHash: launch.contentHash, scenario: launch.scenario, seed: launch.seed };
          const transcript = [
            { ...common, sequence: 1, type: 'run_started', status: 'running', timestampUtc: processRecord.processStartUtc },
            { ...common, sequence: 2, type: 'step_completed', stepId: 'ready', status: 'passed', timestampUtc: terminalTime },
            { ...common, sequence: 3, type: 'run_terminal', status: 'passed', timestampUtc: terminalTime },
          ];
          fs.writeFileSync(path.join(runDirectory, 'runtime-smoke-armed.json'), JSON.stringify(armed));
          fs.appendFileSync(path.join(runDirectory, 'transcript.jsonl'), transcript.map(JSON.stringify).join('\n') + '\n');
          fs.writeFileSync(path.join(runDirectory, 'result.json'), JSON.stringify({ ...common, terminalState: 'passed',
            startedUtc: processRecord.processStartUtc, endedUtc: terminalTime, failureCode: null }));
          fs.writeFileSync(launch.logPath, 'fresh player log');
          setImmediate(() => { child.emit('exit', 0, null); child.emit('close', 0, null); });
        }, 400);
        return child;
      },
      observeProcessIdentity: () => ({ executablePath, processStartUtc: new Date().toISOString() }),
    });
    assert.equal(result.accepted, true, JSON.stringify(result));
    assert.equal(capturedArgs[capturedArgs.indexOf('-runtimeSmoke') + 1], 'workflows/test');
    assert.equal(fs.existsSync(path.join(result.runDirectory, 'runtime-smoke-process.json')), true);
    const processExit = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-process-exit.json'), 'utf8'));
    assert.equal(processExit.exitCode, 0);
    assert.equal(processExit.exitCodeUnavailableReason, null);
    const watch = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-watch.json'), 'utf8'));
    assert.equal(watch.state, 'passed');
    assert.equal(watch.exitCode, 0);
  } finally {
    fs.rmSync(parent, { recursive: true, force: true });
  }
});

test('records synchronous process-launch failure without treating it as an infrastructure crash', async () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-spawn-failure-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    const result = await launchRuntimeSmokeProcess({ hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'spawn-failure', correlationId: 'spawn-failure-corr', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 2, requiredStepIds: ['ready'],
      spawnProcess: () => { throw new Error('executable was not started'); } });
    assert.equal(result.accepted, false);
    assert.equal(result.failureCode, 'player_spawn_failed');
    const watch = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-watch.json'), 'utf8'));
    assert.equal(watch.state, 'failed');
    assert.equal(watch.exitCode, null);
    assert.equal(watch.exitCodeUnavailableReason, 'exit_event_not_observed');
    assert.match(watch.diagnostic, /executable was not started/);
    const processExit = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-process-exit.json'), 'utf8'));
    assert.equal(processExit.exitCodeUnavailableReason, 'exit_event_not_observed');
    assert.match(processExit.nativeLaunchError, /executable was not started/);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});

test('persists the child exit status and captured native stderr when the player exits before RuntimeSmoke evidence', async () => {
  const EventEmitter = require('node:events');
  const { PassThrough } = require('node:stream');
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-early-exit-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.pid = 5353;
    child.kill = () => true;
    const resultPromise = launchRuntimeSmokeProcess({ hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'early-exit', correlationId: 'early-exit-corr', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 10, requiredStepIds: ['ready'],
      spawnProcess: () => {
        setImmediate(() => child.emit('spawn'));
        setTimeout(() => {
          child.stderr.end('UnityPlayer initialization failed: missing runtime dependency\n');
          child.emit('exit', 127, null);
          child.emit('close', 127, null);
        }, 10);
        return child;
      },
      observeProcessIdentity: () => ({ executablePath, processStartUtc: new Date().toISOString() }),
    });
    const result = await resultPromise;
    const processExit = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-process-exit.json'), 'utf8'));
    const watch = JSON.parse(fs.readFileSync(path.join(result.runDirectory, 'runtime-smoke-watch.json'), 'utf8'));
    assert.equal(processExit.exitCode, 127);
    assert.equal(processExit.signal, null);
    assert.equal(watch.exitCode, 127);
    assert.equal(watch.exitCodeUnavailableReason, null);
    assert.match(fs.readFileSync(path.join(result.runDirectory, 'logs', 'player.stderr.txt'), 'utf8'), /missing runtime dependency/);
    assert.match(result.diagnostic, /exitCode=127/);
    assert.match(result.diagnostic, /missing runtime dependency/);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});

test('rejects a spawned process whose observed executable image differs from the requested artifact', async () => {
  const EventEmitter = require('node:events');
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-image-mismatch-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    const differentImage = path.join(buildRoot, 'other.exe');
    fs.writeFileSync(executablePath, 'build');
    fs.writeFileSync(differentImage, 'different');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    const child = new EventEmitter();
    child.pid = 777;
    child.kill = () => { setImmediate(() => { child.emit('exit', 1, null); child.emit('close', 1, null); }); return true; };
    const result = await launchRuntimeSmokeProcess({ hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'image-mismatch', correlationId: 'image-mismatch-corr', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 3, requiredStepIds: ['ready'],
      spawnProcess: () => { setImmediate(() => child.emit('spawn')); return child; },
      observeProcessIdentity: () => ({ executablePath: differentImage, processStartUtc: new Date().toISOString() }) });
    assert.equal(result.accepted, false);
    assert.equal(result.failureCode, 'launched_image_mismatch');
    assert.equal(result.terminationConfirmed, true);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});

test('returns a timeout as termination-unconfirmed when the owned process ignores termination', async () => {
  const EventEmitter = require('node:events');
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-timeout-'));
  try {
    const hostRoot = path.join(parent, 'host');
    const outputRoot = path.join(hostRoot, 'Artifacts');
    const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
    const buildRoot = path.join(hostRoot, 'Builds');
    fs.mkdirSync(outputRoot, { recursive: true });
    fs.mkdirSync(authoredRoot, { recursive: true });
    fs.mkdirSync(buildRoot, { recursive: true });
    const executablePath = path.join(buildRoot, 'game.exe');
    fs.writeFileSync(executablePath, 'build');
    const buildEvidencePath = path.join(buildRoot, 'build.json');
    fs.writeFileSync(buildEvidencePath, JSON.stringify({ schemaVersion: 1, artifactPath: executablePath,
      artifactSha256: require('node:crypto').createHash('sha256').update('build').digest('hex'),
      revision: 'rev-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
      backend: 'IL2CPP', stripping: 'High', lane: 'development' }));
    const child = new EventEmitter();
    child.pid = 778;
    child.kill = () => true;
    const result = await launchRuntimeSmokeProcess({ hostRoot, outputRoot, authoredRoot, executablePath, buildEvidencePath,
      runId: 'timeout-run', correlationId: 'timeout-corr', workflowId: 'workflows/test', canonicalScriptHash: hash,
      target: 'development', mode: 'fsd', timeoutSeconds: 1, killGraceMs: 10, requiredStepIds: ['ready'],
      spawnProcess: () => { setImmediate(() => child.emit('spawn')); return child; },
      observeProcessIdentity: () => ({ executablePath, processStartUtc: new Date().toISOString() }) });
    assert.equal(result.failureCode, 'watchdog_timeout');
    assert.equal(result.terminationConfirmed, false);
    assert.equal(result.processState, 'termination_unconfirmed');
    assert.equal(result.processId, child.pid);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});
