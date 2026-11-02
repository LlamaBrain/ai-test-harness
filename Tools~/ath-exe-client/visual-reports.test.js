'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { compareResults, validateAuditManifest, readRun, audit, report } = require('./visual-reports');
const sample = (outcome = 'pass', height = 40) => ({ result: { schemaVersion: 1, outcome, policy: { minHeightPx: 24, minContrastRatio: 4.5 }, image: { width: 1920, height: 1080 }, findings: ['text.height','text.contrast','text.clipping'].map(ruleId => ({ ruleId, elementId: 'title', bounds: { x: 10, y: 10, width: 100, height: 60 }, outcome, measuredHeightPx: height, contrastRatio: 7, measurementConfidence: 1, detectionConfidence: 1, message: 'test' })), coverage: { mode: 'manifest', expected: 1, measured: 1, limitations: [] } }, receipt: { context: { screenId: 'menu', locale: 'en', uiScale: 1 }, policySha256: 'a'.repeat(64), input: { sha256: 'b'.repeat(64) } } });
test('regression comparison fails reduced size and never promotes uncertainty', () => {
  assert.equal(compareResults(sample(), sample()).outcome, 'pass');
  assert.equal(compareResults(sample(), sample('pass', 39)).outcome, 'fail');
  assert.equal(compareResults(sample('inconclusive'), sample()).outcome, 'inconclusive');
  assert.equal(compareResults(sample(), sample('inconclusive')).outcome, 'inconclusive');
});
test('comparison rejects incompatible baseline identity', () => {
  for (const key of ['screenId', 'locale', 'uiScale']) {
    const current = sample(); current.receipt.context[key] = 'different';
    assert.throws(() => compareResults(sample(), current), /identity/);
  }
  const current = sample(); current.receipt.policySha256 = 'c'.repeat(64);
  assert.throws(() => compareResults(sample(), current), /identity/);
});
test('audit manifest rejects missing, duplicated and escaping expected screens', () => {
  const screen = { id: 'menu', screenshot: 'menu.png', manifest: 'menu.json' };
  assert.deepEqual(validateAuditManifest({ schemaVersion: 1, screens: [screen] }), [screen]);
  for (const screens of [[], [screen, screen], [{ ...screen, screenshot: '../escape.png' }], [{ ...screen, manifest: '/absolute.json' }]]) assert.throws(() => validateAuditManifest({ schemaVersion: 1, screens }));
});
test('report validates stored result digests and rejects tampering', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-visual-report-'));
  try {
    const run = sample(), bytes = Buffer.from(JSON.stringify(run.result));
    run.receipt.schemaVersion = 1; run.receipt.outcome = 'pass';
    run.receipt.artifacts = [{ path: 'result.json', byteLength: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex') }];
    fs.writeFileSync(path.join(root, 'result.json'), bytes); fs.writeFileSync(path.join(root, 'receipt.json'), JSON.stringify(run.receipt));
    assert.equal(readRun(root).result.outcome, 'pass');
    fs.appendFileSync(path.join(root, 'result.json'), ' ');
    assert.throws(() => report(root), /digest mismatch/);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
test('missing expected audit input produces durable infrastructure error', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-visual-audit-'));
  try {
    const manifest = path.join(root, 'audit-manifest.json'), out = path.join(root, 'run');
    fs.writeFileSync(manifest, JSON.stringify({ schemaVersion: 1, screens: [{ id: 'menu', screenshot: 'menu.png', manifest: 'missing.json' }] }));
    const result = await audit({ input: root, manifest, out, analyzer: process.execPath });
    assert.equal(result.outcome, 'infrastructure-error');
    assert.equal(report(out).outcome, 'infrastructure-error');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
