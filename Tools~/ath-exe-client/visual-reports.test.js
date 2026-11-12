'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { compareResults, validateAuditManifest, readRun, audit, report } = require('./visual-reports');
const rawSample = (outcome = 'pass', height = 40) => ({ result: { schemaVersion: 1, outcome, policy: { id: 'test', version: '1', source: 'fixture://test', minHeightPx: 24, minContrastRatio: 4.5 }, image: { width: 1920, height: 1080 }, findings: ['text.height','text.contrast','text.clipping'].map(ruleId => ({ ruleId, elementId: 'title', bounds: { x: 10, y: 10, width: 100, height: 60 }, outcome, measuredHeightPx: height, contrastRatio: 7, measurementConfidence: 1, detectionConfidence: 1, message: 'test' })), coverage: { mode: 'manifest', expected: 1, measured: 1, limitations: [] } }, receipt: { context: { screenId: 'menu', locale: 'en', uiScale: 1 }, policySha256: 'a'.repeat(64), input: { sha256: 'b'.repeat(64) } } });
const sample = (...args) => { const value = rawSample(...args); value.result.context = structuredClone(value.receipt.context); return value; };
test('regression comparison fails reduced size and never promotes uncertainty', () => {
  assert.equal(compareResults(sample(), sample()).outcome, 'pass');
  assert.equal(compareResults(sample(), sample('pass', 39)).outcome, 'fail');
  assert.equal(compareResults(sample(), sample('pass', 39)).findings.filter(f => f.outcome === 'fail').length, 1);
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
test('comparison rejects receipt-only identity substitutions', () => {
  const baseline = sample(), current = sample();
  baseline.result.context = structuredClone(baseline.receipt.context);
  current.result.context = { ...current.receipt.context, screenId: 'other' };
  assert.throws(() => compareResults(baseline, current), /identity/);
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
    assert.throws(() => readRun(root), /required|missing/i);
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
    const stored = JSON.parse(fs.readFileSync(path.join(out, 'audit.json')));
    stored.entries.push({ id: 'extra', outcome: 'infrastructure-error' });
    fs.writeFileSync(path.join(out, 'audit.json'), JSON.stringify(stored));
    assert.throws(() => report(out), /screen missing/);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test('missing and malformed screenshots are isolated at the start, middle and end', async () => {
  for (const invalid of [0,2,4]) for (const malformed of [false,true]) {
    await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,reports,createScreens})=>{
      const fixture=createScreens(5,{invalid,malformed});
      const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out:path.join(root,'audit'),analyzer:process.execPath});
      assert.deepEqual(result.entries.map(entry=>entry.id),fixture.screens.map(screen=>screen.id));
      assert.deepEqual(result.entries.map(entry=>entry.outcome),fixture.screens.map((_,i)=>i===invalid?'infrastructure-error':'pass'));
      assert.equal(reports.report(path.join(root,'audit')).entries.length,5);
    });
  }
});

test('audits bound detector batches and give each screen its own execution budget', async () => {
  await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,log,reports,createScreens})=>{
    const fixture=createScreens(19,{delayMs:80});
    const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out:path.join(root,'audit'),analyzer:process.execPath,timeoutMs:250});
    assert.equal(result.entries.length,19);assert.equal(result.outcome,'pass');
    const batches=fs.readFileSync(log,'utf8').trim().split('\n').map(line=>JSON.parse(line));
    assert.ok(batches.every(batch=>batch.count<=8),'native batches are limited to eight screens');
    assert.deepEqual(result.entries.map(entry=>entry.id),fixture.screens.map(screen=>screen.id));
  });
});

test('an expired screen preserves completed evidence and resumes unstarted screens', async () => {
  for (const slow of [0,2,4]) await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,log,reports,createScreens})=>{
    const fixture=createScreens(5,{delayMs:Array.from({length:5},(_,index)=>index===slow?700:0)});
    const out=path.join(root,'audit');
    const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out,analyzer:process.execPath,timeoutMs:250});
    assert.deepEqual(result.entries.map(entry=>entry.outcome),fixture.screens.map((_,index)=>index===slow?'infrastructure-error':'pass'));
    assert.match(result.entries[slow].message,/screen timed out/);
    assert.equal(reports.report(out).entries.length,5);
    const calls=fs.readFileSync(log,'utf8').trim().split('\n').map(JSON.parse);
    assert.deepEqual(calls.map(call=>call.count),slow===4?[5]:[5,4-slow]);
  });
});

test('result payloads exceeding the stdout budget remain complete on disk', async () => {
  await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,reports,createScreens})=>{
    const fixture=createScreens(8,{regions:1024});
    const out=path.join(root,'audit');
    const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out,analyzer:process.execPath});
    assert.equal(result.outcome,'pass');assert.equal(result.entries.length,8);
    const bytes=result.entries.reduce((sum,entry)=>sum+fs.statSync(path.join(out,entry.id,'result.json')).size,0);
    assert.ok(bytes>4*1024*1024,'the real result payload exceeds the bounded stdout channel');
    assert.equal(reports.report(out).entries.length,8);
  });
});

test('encoded input byte limits bound groups before hundreds of buffers can accumulate', async () => {
  await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,log,reports,createScreens})=>{
    const fixture=createScreens(4,{encodedBytes:17*1024*1024});
    const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out:path.join(root,'audit'),analyzer:process.execPath});
    assert.equal(result.outcome,'pass');assert.equal(result.entries.length,4);
    const calls=fs.readFileSync(log,'utf8').trim().split('\n').map(JSON.parse);
    assert.deepEqual(calls.map(call=>call.count),[3,1]);
    assert.ok(calls.every(call=>call.inputBytes<=64*1024*1024));
  });
});

test('batch results cannot substitute unbound or changed file bytes', async () => {
  for (const mode of ['tamper','missing-digest']) await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,reports,createScreens})=>{
    const fixture=createScreens(3,{resultMode:['',mode,'']});
    const out=path.join(root,'audit');
    const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out,analyzer:process.execPath});
    assert.deepEqual(result.entries.map(entry=>entry.outcome),['pass','infrastructure-error','pass']);
    assert.match(result.entries[1].message,/digest|identity|size/i);
    assert.equal(fs.existsSync(path.join(out,fixture.screens[1].id,'receipt.json')),false);
    assert.equal(reports.report(out).outcome,'infrastructure-error');
  });
});

test('finished items cannot pass without a valid native terminal contract', async () => {
  for (const terminalMode of ['missing-complete','wrong-aggregate','wrong-exit']) await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,reports,createScreens})=>{
    const fixture=createScreens(3,{terminalMode});
    const out=path.join(root,'audit');
    const result=await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out,analyzer:process.execPath});
    assert.deepEqual(result.entries.map(entry=>entry.outcome),['infrastructure-error','infrastructure-error','infrastructure-error'],terminalMode);
    assert.equal(reports.report(out).outcome,'infrastructure-error');
  });
});

test('malformed native progress returns bounded item errors instead of escaping the batch', async () => {
  for (const terminalMode of ['finished-without-index','null-progress']) await require('./visual-batch-fixture').withFakeAnalyzer(async ({root,visual,createScreens})=>{
    const fixture=createScreens(2,{terminalMode});
    const results=await visual.analyzeBatch(fixture.screens.map(screen=>({input:path.join(root,screen.screenshot),manifest:path.join(root,screen.manifest),policy:fixture.policy,out:path.join(root,screen.id),analyzer:process.execPath})),path.join(root,'batch.json'));
    assert.equal(results.length,2);
    assert.ok(results.every(result=>result.result.outcome==='infrastructure-error'));
    assert.ok(results.every(result=>result.result.error.length<=1024));
  });
});

test('report binds input, manifest, policy and context to retained evidence', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-visual-binding-'));
  try {
    const run = sample(); run.result.context.screenshotId = 'menu.png'; run.receipt.context.screenshotId = 'menu.png';
    const png = Buffer.alloc(24); Buffer.from('89504e470d0a1a0a', 'hex').copy(png); png.write('IHDR', 12); png.writeUInt32BE(1920, 16); png.writeUInt32BE(1080, 20);
    const manifest = { schemaVersion: 1, ...run.result.context, resolution: run.result.image, elements: [{ id: 'title', bounds: run.result.findings[0].bounds }] };
    run.result.findings.forEach(f => { f.expectedBounds = f.bounds; });
    const artifacts = { 'input.png': png, 'input-manifest.json': Buffer.from(JSON.stringify(manifest)), 'input-policy.json': Buffer.from(JSON.stringify(run.result.policy)), 'result.json': Buffer.from(JSON.stringify(run.result)) };
    run.receipt.artifacts = Object.entries(artifacts).map(([name, bytes]) => { fs.writeFileSync(path.join(root, name), bytes); return { path: name, sha256: crypto.createHash('sha256').update(bytes).digest('hex'), byteLength: bytes.length }; });
    run.receipt.schemaVersion = 1; run.receipt.outcome = 'pass';
    run.receipt.input = { ...run.receipt.artifacts[0], ...run.result.image }; run.receipt.policySha256 = run.receipt.artifacts[2].sha256;
    const save = () => fs.writeFileSync(path.join(root, 'receipt.json'), JSON.stringify(run.receipt));
    save(); assert.equal(readRun(root).result.outcome, 'pass');
    for (const mutate of [r => { r.context.screenId = 'other'; }, r => { r.input.sha256 = 'f'.repeat(64); }, r => { r.policySha256 = 'e'.repeat(64); }, r => { r.artifacts = r.artifacts.filter(a => a.path !== 'input-manifest.json'); }]) {
      const original = structuredClone(run.receipt); mutate(run.receipt); save(); assert.throws(() => readRun(root)); run.receipt = original;
    }
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
