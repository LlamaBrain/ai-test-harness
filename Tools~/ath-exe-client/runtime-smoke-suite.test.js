'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { prepareRuntimeSmokeArtifactCopy, runRuntimeSmokeSuite } = require('./runtime-smoke-runner');

function sha(value) { return crypto.createHash('sha256').update(value).digest('hex'); }
function fixture() {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-suite-'));
  const hostRoot = path.join(parent, 'host');
  const sourceArtifactRoot = path.join(hostRoot, 'Builds', 'Player');
  const executablePath = path.join(sourceArtifactRoot, 'Game.exe');
  const authoredRoot = path.join(hostRoot, 'Assets', 'RuntimeSmokes');
  const outputDirectory = path.join(hostRoot, 'Artifacts', 'gate-run-1');
  const buildEvidencePath = path.join(hostRoot, 'Artifacts', 'build.json');
  fs.mkdirSync(path.dirname(executablePath), { recursive: true });
  fs.mkdirSync(path.dirname(buildEvidencePath), { recursive: true });
  fs.mkdirSync(authoredRoot, { recursive: true });
  fs.mkdirSync(path.join(sourceArtifactRoot, 'Game_Data'), { recursive: true });
  fs.writeFileSync(executablePath, 'exact-player-bytes');
  fs.writeFileSync(path.join(sourceArtifactRoot, 'Game_Data', 'boot.config'), 'player data');
  fs.writeFileSync(buildEvidencePath, JSON.stringify({
    schemaVersion: 1, artifactPath: executablePath, artifactSha256: sha('exact-player-bytes'),
    revision: 'revision-1', version: '1.2.3', profileHash: 'd'.repeat(64), contentHash: 'e'.repeat(64),
    backend: 'IL2CPP', stripping: 'High', lane: 'development',
  }));
  return { parent, hostRoot, sourceArtifactRoot, executablePath, authoredRoot, outputDirectory, buildEvidencePath };
}
function suiteSpec(fx, entries) {
  return { schemaVersion: 1, suiteId: 'suites/build-parity', target: 'development', hostRoot: fx.hostRoot,
    authoredRoot: fx.authoredRoot, sourceArtifactRoot: fx.sourceArtifactRoot, sourceExecutablePath: fx.executablePath,
    buildEvidencePath: fx.buildEvidencePath, outputDirectory: fx.outputDirectory, maximumSuiteSeconds: 120, entries };
}
function entry(entryId, required = true) {
  return { entryId, required, workflowId: `workflows/${entryId}`, canonicalScriptHash: 'a'.repeat(64),
    runId: `run-${entryId}`, correlationId: `correlation-${entryId}`, timeoutSeconds: 30,
    requiredStepIds: ['ready'], mode: 'fsd', target: 'development', scenario: 'fixture-scene', seed: 704,
    proposition: `The ${entryId} fixture is valid.`, requiredCapabilities: ['ath.start', 'fixture.ready'],
    parityContract: `dirigible.${entryId}.v1` };
}

test('copies a whole artifact into a fresh directory and attests exact source/build provenance', () => {
  const fx = fixture();
  try {
    fs.mkdirSync(fx.outputDirectory, { recursive: true });
    const artifactCopy = prepareRuntimeSmokeArtifactCopy({ ...fx, sourceExecutablePath: fx.executablePath, artifactCopyDirectory: path.join(fx.outputDirectory, 'artifact-copy'), copyId: 'copy-1' });
    assert.equal(fs.readFileSync(artifactCopy.executablePath, 'utf8'), 'exact-player-bytes');
    assert.equal(fs.readFileSync(path.join(artifactCopy.artifactRoot, 'Game_Data', 'boot.config'), 'utf8'), 'player data');
    assert.equal(artifactCopy.attestation.sourceTreeSha256, artifactCopy.attestation.copyTreeSha256);
    assert.equal(artifactCopy.attestation.sourceExecutableSha256, artifactCopy.attestation.copyExecutableSha256);
    assert.equal(artifactCopy.attestation.sourceBuildEvidenceSha256, sha(fs.readFileSync(fx.buildEvidencePath)));
    assert.equal(JSON.parse(fs.readFileSync(artifactCopy.buildEvidencePath, 'utf8')).artifactPath, artifactCopy.executablePath);
    assert.equal(JSON.parse(fs.readFileSync(artifactCopy.attestationPath, 'utf8')).copyId, 'copy-1');
    assert.throws(() => prepareRuntimeSmokeArtifactCopy({ ...fx, sourceExecutablePath: fx.executablePath, artifactCopyDirectory: path.join(fx.outputDirectory, 'artifact-copy'), copyId: 'copy-2' }),
      (error) => error.code === 'artifact_copy_collision');
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('rejects artifact/build mismatch and output paths outside the host before copying', async () => {
  const fx = fixture();
  try {
    const evidence = JSON.parse(fs.readFileSync(fx.buildEvidencePath, 'utf8'));
    evidence.artifactSha256 = '0'.repeat(64);
    fs.writeFileSync(fx.buildEvidencePath, JSON.stringify(evidence));
    assert.throws(() => prepareRuntimeSmokeArtifactCopy({ ...fx, sourceExecutablePath: fx.executablePath, artifactCopyDirectory: path.join(fx.outputDirectory, 'artifact-copy'), copyId: 'copy-1' }),
      (error) => error.code === 'build_provenance_mismatch');
    assert.equal(fs.existsSync(fx.outputDirectory), false);
    fs.writeFileSync(fx.buildEvidencePath, JSON.stringify({ ...evidence, artifactSha256: sha('exact-player-bytes') }));
    await assert.rejects(runRuntimeSmokeSuite(suiteSpec({ ...fx, outputDirectory: path.join(fx.parent, 'outside') }, [entry('one')]),
      { launch: async () => { throw new Error('must not launch'); } }),
    (error) => error.code === 'artifact_root_escape');
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('aggregates required and optional outcomes into one gate document and JUnit projection', async () => {
  const fx = fixture();
  try {
    const seen = [];
    const result = await runRuntimeSmokeSuite(suiteSpec(fx, [entry('required'), entry('optional', false)]), {
      launch: async (options) => {
        seen.push(options);
        return options.workflowId.endsWith('/required')
          ? { accepted: true, state: 'passed', runDirectory: path.join(options.outputRoot, options.runId) }
          : { accepted: false, state: 'failed', failureCode: 'assertion_mismatch', diagnostic: 'optional mismatch' };
      },
    });
    assert.equal(result.accepted, true);
    assert.equal(result.suiteId, 'suites/build-parity');
    assert.equal(seen.length, 2);
    assert.equal(seen[0].executablePath, seen[1].executablePath);
    assert.notEqual(seen[0].correlationId, seen[1].correlationId);
    assert.equal(seen[0].mode, 'fsd');
    assert.deepEqual(seen[0].requiredCapabilities, ['ath.start', 'fixture.ready']);
    assert.equal(seen[0].parityContract, 'dirigible.required.v1');
    assert.equal(result.entries[1].accepted, false);
    assert.equal(result.entries[0].parityContract, 'dirigible.required.v1');
    assert.equal(result.artifactCopy.sourceTreeSha256, result.artifactCopy.copyTreeSha256);
    assert.match(result.junit, /<testsuites/);
    assert.match(result.junit, /<skipped/);
    assert.match(result.junit, /name="parityContract" value="dirigible\.optional\.v1"/);
    assert.match(result.junit, /name="requiredCapabilities" value="ath\.start,fixture\.ready"/);
    const gate = JSON.parse(fs.readFileSync(result.gatePath, 'utf8'));
    assert.equal(gate.accepted, true);
    assert.equal(gate.entries.length, 2);
    assert.equal(fs.readFileSync(result.junitPath, 'utf8').trimEnd(), result.junit);
    assert.equal(gate.entries[0].proposition, 'The required fixture is valid.');
    assert.equal(gate.entries[0].parityContract, 'dirigible.required.v1');
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('rejects malformed parity contracts before creating build-gate output', async () => {
  const fx = fixture();
  try {
    const invalid = entry('one');
    invalid.parityContract = 'dirigible/baseline';
    await assert.rejects(runRuntimeSmokeSuite(suiteSpec(fx, [invalid]), { launch: async () => { throw new Error('must not launch'); } }),
      (error) => error.code === 'suite_entry_invalid');
    assert.equal(fs.existsSync(fx.outputDirectory), false);
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('rejects overlong Windows player paths before claiming gate output or copying the build', { skip: process.platform !== 'win32' }, async () => {
  const fx = fixture();
  try {
    const longEntry = entry('long-path');
    longEntry.runId = 'r'.repeat(160);
    await assert.rejects(runRuntimeSmokeSuite(suiteSpec(fx, [longEntry]), { launch: async () => { throw new Error('must not launch'); } }),
      (error) => error.code === 'player_artifact_path_too_long' && /shorter output directory or run ID/.test(error.message));
    assert.equal(fs.existsSync(fx.outputDirectory), false);
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('required failure blocks the gate and a suite deadline prevents later launch', async () => {
  const fx = fixture();
  try {
    let current = Date.parse('2026-09-24T12:00:00.000Z');
    const now = () => new Date(current);
    let launches = 0;
    const result = await runRuntimeSmokeSuite({ ...suiteSpec(fx, [entry('first'), entry('second')]), maximumSuiteSeconds: 10 }, {
      now,
      launch: async (options) => {
        launches++;
        current += 11000;
        return { accepted: false, state: 'failed', failureCode: 'assertion_mismatch' };
      },
    });
    assert.equal(launches, 1);
    assert.equal(result.accepted, false);
    assert.equal(result.entries[1].failureCode, 'suite_deadline_exceeded');
    assert.match(result.junit, /suite_deadline_exceeded/);
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('detects mutation of the copied artifact before the next workflow and writes a failed gate atomically', async () => {
  const fx = fixture();
  try {
    let launches = 0;
    const result = await runRuntimeSmokeSuite(suiteSpec(fx, [entry('first'), entry('second')]), {
      launch: async (options) => {
        launches++;
        fs.writeFileSync(options.executablePath, 'mutated player');
        return { accepted: true, state: 'passed' };
      },
    });
    assert.equal(launches, 1);
    assert.equal(result.accepted, false);
    assert.equal(result.failureCode, 'artifact_copy_changed');
    assert.equal(fs.existsSync(result.gatePath), true);
    assert.equal(fs.existsSync(result.junitPath), true);
    assert.throws(() => fs.writeFileSync(result.gatePath, 'replace', { flag: 'wx' }));
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});

test('projects a post-run artifact integrity failure as a build-gate JUnit error', async () => {
  const fx = fixture();
  try {
    const result = await runRuntimeSmokeSuite(suiteSpec(fx, [entry('only')]), {
      launch: async (options) => {
        fs.writeFileSync(options.executablePath, 'mutated after pass');
        return { accepted: true, state: 'passed' };
      },
    });
    assert.equal(result.accepted, false);
    assert.equal(result.failureCode, 'artifact_copy_changed');
    assert.match(result.junit, /<error type="artifact_copy_changed"/);
  } finally { fs.rmSync(fx.parent, { recursive: true, force: true }); }
});
