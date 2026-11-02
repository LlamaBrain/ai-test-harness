'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { analyze, validateResult, invoke, command, validateBatchResult } = require('./visual');
const valid = () => ({ schemaVersion: 1, outcome: 'pass', policy: { minHeightPx: 24, minContrastRatio: 4.5 }, image: { width: 100, height: 100 }, findings: ['text.height','text.contrast','text.clipping'].map(ruleId => ({ ruleId, elementId: 'title', outcome: 'pass', bounds: { x: 1, y: 1, width: 40, height: 30 }, measuredHeightPx: 30, contrastRatio: 7, measurementConfidence: 1, detectionConfidence: 1, message: 'ok' })), coverage: { mode: 'manifest', expected: 1, measured: 1, limitations: [] } });
test('unknown schemas, empty evidence and uncertain findings cannot pass', () => {
  validateResult(valid(), 0);
  for (const change of [{ schemaVersion: 2 }, { findings: [] }, { outcome: 'unknown' }, { coverage: { mode: 'manifest', expected: 2, measured: 1, limitations: [] } }]) assert.throws(() => validateResult({ ...valid(), ...change }, 0));
  const result = valid(); result.findings[0].outcome = 'inconclusive'; assert.throws(() => validateResult(result, 0));
  assert.throws(() => validateResult(valid(), 1));
  const outside = valid(); outside.findings[0].bounds.x = 99;
  assert.throws(() => validateResult(outside, 0), /in-viewport/);
  const unmeasured = valid(); unmeasured.findings[0].measurementConfidence = 0;
  assert.throws(() => validateResult(unmeasured, 0), /measured/);
  const duplicated = valid(); duplicated.findings.push(duplicated.findings[0]);
  assert.throws(() => validateResult(duplicated, 0), /Duplicate/);
});
test('strict pass requires positive detection and exact nonempty coverage', () => {
  for (const confidence of [0, null]) { const result = valid(); result.findings[0].detectionConfidence = confidence; assert.throws(() => validateResult(result, 0)); }
  const result = valid(); result.coverage.expected = 0; assert.throws(() => validateResult(result, 0));
});

test('adjustability is an inconclusive policy finding rather than a measured element', () => {
  const result = valid(); result.outcome = 'inconclusive';
  const finding = { ruleId: 'text.adjustability', elementId: '', outcome: 'inconclusive', bounds: null, measuredHeightPx: null, contrastRatio: null, measurementConfidence: null, detectionConfidence: null, message: 'User adjustment requires independent evidence' };
  result.findings.push(finding); validateResult(result, 2);
  finding.elementId = 'title'; assert.throws(() => validateResult(result, 2));
});

test('strict results cannot substitute manifest identity or omit required elements', () => {
  const { validateBindings } = require('./visual');
  const result = valid(); result.context = { screenId: 'menu', screenshotId: 'menu.png', locale: 'en', uiScale: 1 };
  result.findings.forEach(f => { f.expectedBounds = { ...f.bounds }; });
  const manifest = { schemaVersion: 1, ...result.context, resolution: result.image, elements: [{ id: 'title', bounds: result.findings[0].bounds }] };
  const bytes = Buffer.alloc(24); Buffer.from('89504e470d0a1a0a', 'hex').copy(bytes); bytes.write('IHDR', 12); bytes.writeUInt32BE(100, 16); bytes.writeUInt32BE(100, 20);
  validateBindings(result, { bytes }, manifest, result.policy);
  for (const mutate of [r => { r.context.locale = 'fr'; }, r => { r.findings[0].elementId = 'other'; }, r => { r.findings.pop(); }, r => { r.coverage.expected = 0; }, r => { r.findings[0].bounds.x = 99; }]) {
    const altered = structuredClone(result); mutate(altered); assert.throws(() => validateBindings(altered, { bytes }, manifest, result.policy));
  }
});

test('missing analyzer fails closed and duplicate output is refused', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-visual-test-'));
  try {
    const input = path.join(root, 'input.png'); fs.writeFileSync(input, Buffer.from('89504e470d0a1a0a', 'hex'));
    await assert.rejects(analyze({ input, out: path.join(root, 'run'), analyzer: path.join(root, 'missing.exe') }));
    await assert.rejects(analyze({ input, out: path.join(root, 'run'), analyzer: process.execPath }), /exist/i);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
test('bounded subprocess transport reports exit and rejects timeout and excessive output', async () => {
  const run = await invoke(process.execPath, ['-e', 'process.stdout.write("ok");process.exitCode=2'], 5000);
  assert.equal(run.code, 2); assert.equal(run.stdout, 'ok');
  await assert.rejects(invoke(process.execPath, ['-e', 'setInterval(()=>{},1000)'], 100), /timed out/);
  await assert.rejects(invoke(process.execPath, ['-e', 'process.stdout.write("x".repeat(5*1024*1024))'], 5000), /exceeds/);
});
test('CLI misuse returns structured infrastructure error without game connectivity', async () => {
  let result; assert.equal(await command([], {}, x => { result = x; }), 3);
  assert.equal(result.outcome, 'infrastructure-error');
});
test('tampered receipt digest fails before creating output', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-visual-test-'));
  try {
    const input = path.join(root, 'input.png'); fs.writeFileSync(input, Buffer.from('89504e470d0a1a0a', 'hex'));
    await assert.rejects(analyze({ input, out: path.join(root, 'run'), analyzer: process.execPath, expectedSha256: '0'.repeat(64) }), /digest mismatch/);
    assert.equal(fs.existsSync(path.join(root, 'run')), false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
test('junction or symbolic parent cannot redirect input or output evidence', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-visual-test-'));
  try {
    const actual = path.join(root, 'actual'); fs.mkdirSync(actual);
    const input = path.join(actual, 'input.png'); fs.writeFileSync(input, Buffer.from('89504e470d0a1a0a', 'hex'));
    const link = path.join(root, 'link'); fs.symlinkSync(actual, link, process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(analyze({ input: path.join(link, 'input.png'), out: path.join(root, 'run'), analyzer: process.execPath }), /Symbolic/);
    await assert.rejects(analyze({ input, out: path.join(link, 'run'), analyzer: process.execPath }), /Symbolic/);
    assert.equal(fs.existsSync(path.join(actual, 'run')), false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
test('batch result requires exact count and consistent aggregate outcome', () => {
  assert.throws(() => validateBatchResult({ schemaVersion: 1, outcome: 'pass', results: [] }, 0, 1));
  assert.throws(() => validateBatchResult({ schemaVersion: 1, outcome: 'pass', results: [{ schemaVersion: 1, outcome: 'infrastructure-error', error: 'bad PNG' }] }, 0, 1));
  assert.equal(validateBatchResult({ schemaVersion: 1, outcome: 'pass', results: [valid()] }, 0, 1).length, 1);
});
