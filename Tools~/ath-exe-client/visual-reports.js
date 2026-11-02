'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { isWithin, writeAtomicNew } = require('./runtime-smoke-runner');
const codes = { pass: 0, fail: 1, inconclusive: 2, 'infrastructure-error': 3 };
const order = (a, b) => a < b ? -1 : a > b ? 1 : 0;
function visual() { return require('./visual'); }
function relativeFile(value) { return typeof value === 'string' && value.length > 0 && !path.isAbsolute(value) && !value.split(/[\\/]/).some(p => p === '..' || p === '.' || !p) && !value.includes(':'); }
function validateAuditManifest(manifest) {
  if (!manifest || manifest.schemaVersion !== 1 || !Array.isArray(manifest.screens) || !manifest.screens.length || manifest.screens.length > 10000) throw new Error('Audit manifest requires schemaVersion 1 and 1..10000 expected screens');
  const ids = new Set(), screenshots = new Set();
  for (const screen of manifest.screens) {
    if (!screen || !/^[A-Za-z0-9_-]{1,100}$/.test(screen.id || '') || ids.has(screen.id) || !relativeFile(screen.screenshot) || !relativeFile(screen.manifest) || screenshots.has(screen.screenshot) || (screen.policy !== undefined && !relativeFile(screen.policy))) throw new Error('Invalid or duplicate audit screen identity/path');
    ids.add(screen.id); screenshots.add(screen.screenshot);
  }
  return [...manifest.screens].sort((a, b) => order(a.id, b.id));
}
function aggregate(outcomes) { return outcomes.includes('infrastructure-error') ? 'infrastructure-error' : outcomes.includes('fail') ? 'fail' : !outcomes.length || outcomes.includes('inconclusive') ? 'inconclusive' : 'pass'; }
function readRun(directory) {
  const root = visual().safePath(directory);
  const receipt = JSON.parse(visual().readEvidence(path.join(root, 'receipt.json')).bytes);
  if (!receipt || receipt.schemaVersion !== 1 || !Array.isArray(receipt.artifacts) || !receipt.input || !/^[a-f0-9]{64}$/.test(receipt.input.sha256 || '')) throw new Error('Invalid analysis receipt');
  const seen = new Set(); let result;
  for (const artifact of receipt.artifacts) {
    if (!relativeFile(artifact.path) || seen.has(artifact.path)) throw new Error('Invalid artifact path or duplicate');
    seen.add(artifact.path);
    const file = path.resolve(root, artifact.path);
    if (!isWithin(file, root)) throw new Error('Artifact escapes run');
    const evidence = visual().readEvidence(file);
    if (evidence.sha256 !== artifact.sha256 || evidence.byteLength !== artifact.byteLength) throw new Error('Analysis artifact digest mismatch');
    if (artifact.path === 'result.json') result = JSON.parse(evidence.bytes);
  }
  if (!result || result.outcome !== receipt.outcome) throw new Error('Receipt result missing or outcome mismatch');
  visual().validateResult(result, codes[result.outcome]);
  return { result, receipt };
}
function comparisonIdentity(run) {
  const context = run.receipt.context || run.result.context;
  if (!context || !context.screenId || !context.locale || !Number.isFinite(context.uiScale) || context.uiScale <= 0 || !/^[a-f0-9]{64}$/.test(run.receipt.policySha256 || '')) throw new Error('Comparison identity requires screen, locale, UI scale and policy digest');
  return JSON.stringify([context.screenId, context.locale, context.uiScale, run.result.image.width, run.result.image.height, run.receipt.policySha256]);
}
function compareResults(baseline, current) {
  visual().validateResult(baseline.result, codes[baseline.result.outcome]); visual().validateResult(current.result, codes[current.result.outcome]);
  if (comparisonIdentity(baseline) !== comparisonIdentity(current)) throw new Error('Baseline and current identity are incompatible');
  function index(result) {
    const map = new Map();
    for (const finding of result.findings) { const key = JSON.stringify([finding.elementId, finding.ruleId]); if (map.has(key)) throw new Error('Ambiguous finding identity'); map.set(key, finding); }
    return map;
  }
  const old = index(baseline.result), now = index(current.result), findings = [];
  for (const key of [...new Set([...old.keys(), ...now.keys()])].sort(order)) {
    const before = old.get(key), after = now.get(key);
    let outcome = 'pass', reason = 'No measured regression';
    if (!before || !after || before.outcome === 'inconclusive' || after.outcome === 'inconclusive') { outcome = 'inconclusive'; reason = 'Missing or inconclusive comparable evidence'; }
    else if (after.outcome === 'fail' || ['measuredHeightPx', 'contrastRatio'].some(field => Number.isFinite(before[field]) && Number.isFinite(after[field]) && after[field] < before[field])) { outcome = 'fail'; reason = 'Current finding fails policy or measured legibility decreased'; }
    findings.push({ ruleId: 'visual.regression', elementId: (after || before).elementId, comparedRuleId: (after || before).ruleId, outcome, reason, baseline: before || null, current: after || null });
  }
  return { schemaVersion: 1, outcome: aggregate([baseline.result.outcome === 'inconclusive' ? 'inconclusive' : 'pass', current.result.outcome, ...findings.map(f => f.outcome)]), baseline: baseline.receipt.input.sha256, current: current.receipt.input.sha256, findings };
}
async function audit(options) {
  if (!options.manifest || !options.out) throw new Error('audit requires --manifest and --out NEW_DIRECTORY');
  const source = visual().readEvidence(options.manifest), screens = validateAuditManifest(JSON.parse(source.bytes));
  const root = visual().safePath(options.input), manifestRoot = path.dirname(source.path), out = visual().safePath(options.out);
  fs.mkdirSync(out);
  const entries = [], pending = [];
  for (const screen of screens) {
    try {
      const manifest = path.join(manifestRoot, screen.manifest);
      const expected = JSON.parse(visual().readEvidence(manifest).bytes);
      if (expected.screenId !== screen.id || expected.screenshotId !== screen.screenshot || typeof expected.locale !== 'string' || !expected.locale || !Number.isFinite(expected.uiScale) || expected.uiScale <= 0 || !Array.isArray(expected.elements) || !expected.elements.length) throw new Error('Strict audit requires matching screenId/screenshotId, locale, UI scale and expected elements');
      pending.push({ screen, options: { ...options, input: path.join(root, screen.screenshot), manifest, policy: screen.policy ? path.join(manifestRoot, screen.policy) : options.policy, out: path.join(out, screen.id) } });
    } catch (error) { entries.push({ id: screen.id, outcome: 'infrastructure-error', message: String(error.message).slice(0, 1024) }); }
  }
  // Native batches share one loaded detector. Bound each batch to its declared timeout.
  for (let offset = 0; offset < pending.length; offset += 1024) {
    const group = pending.slice(offset, offset + 1024);
    try {
      const results = await visual().analyzeBatch(group.map(item => item.options), path.join(out, 'batch-' + offset + '.json'));
      for (let i = 0; i < group.length; i++) {
        const id = group[i].screen.id, analyzed = results[i];
        if (analyzed.result.outcome === 'infrastructure-error') entries.push({ id, outcome: 'infrastructure-error', message: analyzed.result.error });
        else if (!['manifest', 'manifest-driven'].includes(analyzed.result.coverage.mode)) entries.push({ id, outcome: 'infrastructure-error', message: 'Strict audit requires manifest coverage' });
        else entries.push({ id, outcome: analyzed.result.outcome, receipt: id + '/receipt.json' });
      }
    } catch (error) { for (const item of group) entries.push({ id: item.screen.id, outcome: 'infrastructure-error', message: String(error.message).slice(0, 1024) }); }
  }
  entries.sort((a, b) => order(a.id, b.id));
  if (visual().readEvidence(source.path).sha256 !== source.sha256) throw new Error('Audit manifest changed');
  const result = { schemaVersion: 1, outcome: aggregate(entries.map(e => e.outcome)), manifestSha256: source.sha256, entries };
  writeAtomicNew(fs, path.join(out, 'audit.json'), JSON.stringify(result, null, 2) + '\n');
  return result;
}
function report(directory) {
  const root = visual().safePath(directory);
  if (fs.existsSync(path.join(root, 'receipt.json'))) return { schemaVersion: 1, ...readRun(root) };
  const auditResult = JSON.parse(visual().readEvidence(path.join(root, 'audit.json')).bytes);
  if (auditResult.schemaVersion !== 1 || !Array.isArray(auditResult.entries) || !auditResult.entries.length) throw new Error('Invalid audit report');
  const ids = new Set();
  const entries = auditResult.entries.map(entry => {
    if (!/^[A-Za-z0-9_-]{1,100}$/.test(entry.id || '') || ids.has(entry.id) || !Object.hasOwn(codes, entry.outcome)) throw new Error('Invalid report entry');
    ids.add(entry.id);
    if (entry.outcome === 'infrastructure-error') return entry;
    const run = readRun(path.join(root, entry.id));
    if (run.result.outcome !== entry.outcome) throw new Error('Audit entry outcome mismatch');
    return { id: entry.id, ...run };
  });
  return { schemaVersion: 1, outcome: aggregate(auditResult.entries.map(e => e.outcome)), entries };
}
module.exports = { audit, report, readRun, compareResults, validateAuditManifest };
