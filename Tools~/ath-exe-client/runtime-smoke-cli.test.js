'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { runRuntimeSmokeSpec, runRuntimeSmokeSuiteSpec } = require('./ath-exe');

test('runtime-smoke CLI reads a JSON launch spec and returns runner acceptance', async () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-cli-'));
  try {
    const specPath = path.join(parent, 'launch.json');
    fs.writeFileSync(specPath, JSON.stringify({ hostRoot: parent, runId: 'smoke-1', canonicalScriptHash: '797cfa2aedb128f90eca43b0487a036147f6e05f0a249e9d5c639b9edf9e9b06' }));
    const output = [];
    let seen;
    const code = await runRuntimeSmokeSpec(specPath, {
      launch: async (spec) => { seen = spec; return { accepted: true, state: 'passed', runDirectory: path.join(parent, 'Artifacts', 'smoke-1') }; },
      print: (value) => output.push(value),
    });
    assert.equal(code, 0);
    assert.equal(seen.runId, 'smoke-1');
    assert.deepEqual(output[0], { accepted: true, state: 'passed', runDirectory: path.join(parent, 'Artifacts', 'smoke-1') });
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});

test('runtime-smoke CLI rejects malformed or missing launch specs without invoking player launch', async () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-cli-invalid-'));
  try {
    const specPath = path.join(parent, 'launch.json');
    fs.writeFileSync(specPath, '{bad json');
    let launched = false;
    const output = [];
    const code = await runRuntimeSmokeSpec(specPath, {
      launch: async () => { launched = true; }, print: (value) => output.push(value),
    });
    assert.equal(code, 2);
    assert.equal(launched, false);
    assert.equal(output[0].accepted, false);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});

test('runtime-smoke CLI rejects raw scriptHash before invoking player launch', async () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-cli-raw-hash-'));
  try {
    const specPath = path.join(parent, 'launch.json');
    fs.writeFileSync(specPath, JSON.stringify({ runId: 'smoke-raw', scriptHash: '9'.repeat(64) }));
    let launched = false;
    const output = [];
    const code = await runRuntimeSmokeSpec(specPath, {
      launch: async () => { launched = true; }, print: (value) => output.push(value),
    });
    assert.equal(code, 2);
    assert.equal(launched, false);
    assert.equal(output[0].failureCode, 'launch_input_invalid');
    assert.match(output[0].diagnostic, /canonicalScriptHash/);
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});
test('runtime-smoke-suite CLI routes one suite spec to the shared AITH suite runner', async () => {
  const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-runtime-smoke-suite-cli-'));
  try {
    const specPath = path.join(parent, 'suite.json');
    const spec = { schemaVersion: 1, suiteId: 'suites/build-parity', entries: [] };
    fs.writeFileSync(specPath, JSON.stringify(spec));
    const output = [];
    let seen;
    const code = await runRuntimeSmokeSuiteSpec(specPath, {
      runSuite: async (value) => { seen = value; return { accepted: true, state: 'passed', suiteId: value.suiteId }; },
      print: (value) => output.push(value),
    });
    assert.equal(code, 0);
    assert.deepEqual(seen, spec);
    assert.deepEqual(output[0], { accepted: true, state: 'passed', suiteId: 'suites/build-parity' });
  } finally { fs.rmSync(parent, { recursive: true, force: true }); }
});