'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn } = require('node:child_process');
const { assertNoSymlinkPath, writeAtomicNew } = require('./runtime-smoke-runner');
const codes = { pass: 0, fail: 1, inconclusive: 2, 'infrastructure-error': 3 };
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
function safePath(file) {
  const absolute = path.resolve(file), root = path.parse(absolute).root;
  if (fs.lstatSync(root).isSymbolicLink()) throw new Error('Symbolic root refused');
  assertNoSymlinkPath(fs, absolute, root);
  return absolute;
}
function readEvidence(file) {
  const absolute = safePath(file), stat = fs.statSync(absolute);
  if (!stat.isFile() || stat.size < 1 || stat.size > 32 * 1024 * 1024) throw new Error('Evidence must be a regular file of 1..32 MiB');
  const bytes = fs.readFileSync(absolute);
  if (bytes.length !== stat.size) throw new Error('Evidence changed while reading');
  return { path: absolute, bytes, sha256: sha(bytes), byteLength: bytes.length };
}
function validateResult(result, code) {
  if (!result || result.schemaVersion !== 1 || !Object.hasOwn(codes, result.outcome) || codes[result.outcome] !== code) throw new Error('Invalid analyzer schema, outcome, or exit code');
  if (result.outcome === 'infrastructure-error') throw new Error('Analyzer infrastructure-error');
  if (!result.image || !['width', 'height'].every(k => Number.isInteger(result.image[k]) && result.image[k] > 0 && result.image[k] <= 32768) || !Array.isArray(result.findings) || !result.coverage || !Array.isArray(result.coverage.limitations)) throw new Error('Incomplete analyzer evidence');
  if (!['manifest', 'manifest-driven', 'best-effort'].includes(result.coverage.mode) || !Number.isInteger(result.coverage.expected) || result.coverage.expected < 0 || !Number.isInteger(result.coverage.measured) || result.coverage.measured < 0 || result.coverage.limitations.some(x => typeof x !== 'string')) throw new Error('Invalid coverage');
  for (const f of result.findings) {
    if (!f || typeof f.ruleId !== 'string' || !f.ruleId || !['pass', 'fail', 'inconclusive'].includes(f.outcome) || typeof f.elementId !== 'string' || typeof f.message !== 'string') throw new Error('Invalid finding');
    for (const name of ['measurementConfidence', 'detectionConfidence']) if (f[name] !== null && (!Number.isFinite(f[name]) || f[name] < 0 || f[name] > 1)) throw new Error('Invalid confidence');
    for (const name of ['measuredHeightPx', 'contrastRatio']) if (f[name] != null && (!Number.isFinite(f[name]) || f[name] < 0)) throw new Error('Invalid measurement');
    if (f.bounds != null && !['x', 'y', 'width', 'height'].every(k => Number.isFinite(f.bounds[k]))) throw new Error('Invalid finding bounds');
  }
  if (result.outcome === 'pass' && (!result.findings.length || result.findings.some(f => f.outcome !== 'pass') || !Number.isInteger(result.coverage.measured) || result.coverage.measured < 1 || !Number.isInteger(result.coverage.expected) || result.coverage.expected > result.coverage.measured)) throw new Error('Insufficient evidence for pass');
  const keys = new Set();
  for (const f of result.findings) {
    if (!['text.height', 'text.contrast', 'text.clipping', 'text.adjustability'].includes(f.ruleId)) throw new Error('Unknown visual rule');
    const key = JSON.stringify([f.elementId, f.ruleId]);
    if (keys.has(key)) throw new Error('Duplicate finding identity'); keys.add(key);
    if (f.outcome === 'pass') {
      if (!f.bounds || !['x', 'y', 'width', 'height'].every(k => Number.isInteger(f.bounds[k])) || f.bounds.x < 0 || f.bounds.y < 0 || f.bounds.width < 1 || f.bounds.height < 1 || f.bounds.x + f.bounds.width > result.image.width || f.bounds.y + f.bounds.height > result.image.height || !(f.measurementConfidence > 0)) throw new Error('Pass lacks measured in-viewport evidence');
      if (f.ruleId === 'text.height' && (!result.policy || !Number.isFinite(result.policy.minHeightPx) || result.policy.minHeightPx <= 0 || !Number.isFinite(f.measuredHeightPx) || f.measuredHeightPx < result.policy.minHeightPx || f.measuredHeightPx > f.bounds.height)) throw new Error('Height pass contradicts measured policy evidence');
      if (f.ruleId === 'text.contrast' && (!result.policy || !Number.isFinite(result.policy.minContrastRatio) || result.policy.minContrastRatio < 1 || !Number.isFinite(f.contrastRatio) || f.contrastRatio < result.policy.minContrastRatio || f.contrastRatio > 21)) throw new Error('Contrast pass contradicts measured policy evidence');
    }
  }
  if (result.outcome === 'pass') {
    const elements = new Set(result.findings.map(f => f.elementId));
    if (elements.size !== result.coverage.measured || [...elements].some(id => !id || ['text.height', 'text.contrast', 'text.clipping'].some(rule => !keys.has(JSON.stringify([id, rule]))))) throw new Error('Pass lacks required rule coverage');
  }
  return result;
}
function invoke(executable, args, timeoutMs) {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    let stdout = '', stderr = '', bytes = 0, error, terminationTimer;
    function terminate(reason) {
      if (error) return;
      error = new Error(reason); child.kill('SIGKILL');
      terminationTimer = setTimeout(() => { child.stdout.destroy(); child.stderr.destroy(); child.unref(); reject(new Error(reason + '; process termination unconfirmed')); }, 2000);
    }
    const timer = setTimeout(() => terminate('Analyzer timed out'), timeoutMs);
    function receive(chunk, isError) {
      bytes += Buffer.byteLength(chunk, 'utf8');
      if (bytes > 4 * 1024 * 1024) { terminate('Analyzer output exceeds 4 MiB'); return; }
      if (isError) stderr += chunk.toString(); else stdout += chunk.toString();
    }
    child.stdout.on('data', c => receive(c, false)); child.stderr.on('data', c => receive(c, true));
    child.on('error', e => { clearTimeout(timer); clearTimeout(terminationTimer); reject(e); });
    child.on('close', code => { clearTimeout(timer); clearTimeout(terminationTimer); if (error) reject(error); else resolve({ code, stdout, stderr }); });
  });
}
function prepareAnalysis(options) {
  const timeoutMs = options.timeoutMs === undefined ? 30000 : Number(options.timeoutMs);
  if (!Number.isInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 3600000) throw new Error('timeout-ms must be 1..3600000');
  if (!options.input || !options.out || !options.analyzer) throw new Error('visual analyze requires input, --out NEW_DIRECTORY and --analyzer executable (or ATH_VISUAL_ANALYZER)');
  const input = readEvidence(options.input);
  if (!input.bytes.subarray(0, 8).equals(Buffer.from('89504e470d0a1a0a', 'hex'))) throw new Error('Input is not PNG');
  if (options.expectedSha256 && options.expectedSha256 !== input.sha256) throw new Error('Capture receipt digest mismatch');
  const out = safePath(options.out);
  fs.mkdirSync(out);
  let effectivePolicy;
  if (options.profile) {
    if (options.policy) throw new Error('Choose --profile or a custom --policy file');
    if (input.bytes.length < 24) throw new Error('Incomplete PNG header');
    effectivePolicy = require('./visual-profiles').resolveProfile(options.profile, input.bytes.readUInt32BE(20));
    options = { ...options, policy: path.join(out, 'policy.json') };
    writeAtomicNew(fs, options.policy, JSON.stringify(effectivePolicy, null, 2) + '\n');
  }
  // Analyze an exclusive snapshot of the verified bytes, not a concurrently writable capture.
  const inputSnapshot = path.join(out, 'input.png');
  writeAtomicNew(fs, inputSnapshot, input.bytes);
  const args = ['--input', inputSnapshot, '--annotated', path.join(out, 'annotated.png')], sources = [input];
  const executable = readEvidence(options.analyzer); executable.kind = 'analyzer'; sources.push(executable);
  for (const name of ['manifest', 'policy', 'model']) if (options[name]) {
    const source = readEvidence(options[name]); source.kind = name; sources.push(source);
    const staged = name === 'model' ? source.path : path.join(out, 'input-' + name + '.json');
    if (staged !== source.path) writeAtomicNew(fs, staged, source.bytes);
    args.push('--' + name, staged);
  }
  return { options, timeoutMs, input, out, sources, args, effectivePolicy };
}
function finishAnalysis(prepared, execution) {
  const { options, input, out, sources } = prepared;
  let { effectivePolicy } = prepared;
  for (const source of sources) if (readEvidence(source.path).sha256 !== source.sha256) throw new Error('Evidence changed during analysis');
  const result = validateResult(JSON.parse(execution.stdout), execution.code);
  const policySource = sources.find(s => s.kind === 'policy');
  if (policySource) effectivePolicy = JSON.parse(policySource.bytes);
  if (effectivePolicy && (!result.policy || ['id', 'version', 'minHeightPx', 'minContrastRatio'].some(key => result.policy[key] !== effectivePolicy[key]))) throw new Error('Analyzer policy identity or thresholds mismatch');
  require('./visual-profiles').applyProfileScope(result, effectivePolicy);
  if (input.bytes.length < 24 || input.bytes.toString('ascii', 12, 16) !== 'IHDR' || input.bytes.readUInt32BE(16) !== result.image.width || input.bytes.readUInt32BE(20) !== result.image.height) throw new Error('Decoded dimensions do not match input PNG');
  const resultBytes = Buffer.from(JSON.stringify(result, null, 2) + '\n');
  safePath(out); writeAtomicNew(fs, path.join(out, 'result.json'), resultBytes);
  const artifacts = [{ path: 'result.json', sha256: sha(resultBytes), byteLength: resultBytes.length },
    { path: 'input.png', sha256: input.sha256, byteLength: input.byteLength }];
  for (const source of sources.filter(s => s.kind === 'manifest' || s.kind === 'policy')) artifacts.push({ path: 'input-' + source.kind + '.json', sha256: source.sha256, byteLength: source.byteLength });
  const annotation = path.join(out, 'annotated.png');
  if (fs.existsSync(annotation)) { const a = readEvidence(annotation); artifacts.push({ path: 'annotated.png', sha256: a.sha256, byteLength: a.byteLength }); }
  const receipt = { schemaVersion: 1, correlationId: options.correlationId || null, workflowId: options.workflowId || null, outcome: result.outcome,
    context: result.context || (options.manifest ? JSON.parse(sources.find(s => s.kind === 'manifest').bytes) : null),
    policySha256: sources.find(s => s.kind === 'policy')?.sha256 || (result.policy ? sha(Buffer.from(JSON.stringify(result.policy))) : null),
    input: { path: input.path, sha256: input.sha256, byteLength: input.byteLength, ...result.image },
    sources: sources.slice(1).map(({ kind, path, sha256, byteLength }) => ({ kind, path, sha256, byteLength })), artifacts };
  writeAtomicNew(fs, path.join(out, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
  return { result, receipt, exitCode: codes[result.outcome] };
}
async function analyze(options) {
  const prepared = prepareAnalysis(options);
  return finishAnalysis(prepared, await invoke(path.resolve(options.analyzer), prepared.args, prepared.timeoutMs));
}
function aggregate(outcomes) { return outcomes.includes('infrastructure-error') ? 'infrastructure-error' : outcomes.includes('fail') ? 'fail' : !outcomes.length || outcomes.includes('inconclusive') ? 'inconclusive' : 'pass'; }
function validateBatchResult(batch, code, count) {
  if (!batch || batch.schemaVersion !== 1 || !Array.isArray(batch.results) || batch.results.length !== count || !Object.hasOwn(codes, batch.outcome) || codes[batch.outcome] !== code || batch.outcome !== aggregate(batch.results.map(r => r?.outcome))) throw new Error('Invalid batch schema, count or aggregate outcome');
  for (const result of batch.results) {
    if (result.outcome === 'infrastructure-error') { if (result.schemaVersion !== 1 || typeof result.error !== 'string') throw new Error('Invalid batch infrastructure error'); }
    else validateResult(result, codes[result.outcome]);
  }
  return batch.results;
}
async function analyzeBatch(optionsList, batchFile) {
  if (!Array.isArray(optionsList) || !optionsList.length || optionsList.length > 1024) throw new Error('Batch requires 1..1024 inputs');
  const prepared = optionsList.map(prepareAnalysis);
  const first = prepared[0];
  if (prepared.some(p => path.resolve(p.options.analyzer) !== path.resolve(first.options.analyzer) || p.options.model !== first.options.model)) throw new Error('Batch requires the same analyzer and model');
  const items = prepared.map(p => {
    const item = {};
    for (let i = 0; i < p.args.length; i += 2) if (p.args[i] !== '--model') item[p.args[i].slice(2)] = p.args[i + 1];
    return item;
  });
  const file = safePath(batchFile);
  writeAtomicNew(fs, file, JSON.stringify({ schemaVersion: 1, items }) + '\n');
  const args = ['--batch', file];
  if (first.options.model) args.push('--model', path.resolve(first.options.model));
  const execution = await invoke(path.resolve(first.options.analyzer), args, first.timeoutMs);
  const results = validateBatchResult(JSON.parse(execution.stdout), execution.code, prepared.length);
  return results.map((result, i) => {
    try { return finishAnalysis(prepared[i], { code: codes[result.outcome], stdout: JSON.stringify(result) }); }
    catch (error) { return { result: { schemaVersion: 1, outcome: 'infrastructure-error', error: String(error.message).slice(0, 1024) }, exitCode: 3 }; }
  });
}
async function command(positionals, flags, print = value => process.stdout.write(JSON.stringify(value, null, 2) + '\n')) {
  try {
    const reports = require('./visual-reports');
    if (positionals[0] === 'compare' && positionals.length === 3) {
      const result = reports.compareResults(reports.readRun(positionals[1]), reports.readRun(positionals[2]));
      if (flags.out) writeAtomicNew(fs, safePath(flags.out), JSON.stringify(result, null, 2) + '\n');
      print(result); return codes[result.outcome];
    }
    if (positionals[0] === 'report' && positionals.length === 2) {
      if (flags.format && flags.format !== 'json') throw new Error('Only --format json is supported');
      const result = reports.report(positionals[1]); print(result); return codes[result.outcome || result.result.outcome];
    }
    if (positionals[0] === 'audit' && positionals.length === 2) {
      const result = await reports.audit({ input: positionals[1], out: flags.out, analyzer: flags.analyzer || process.env.ATH_VISUAL_ANALYZER, manifest: flags.manifest, policy: flags.policy, profile: flags.profile, model: flags.model, timeoutMs: flags['timeout-ms'], correlationId: flags['correlation-id'], workflowId: flags['workflow-id'] });
      print(result); return codes[result.outcome];
    }
    if (positionals.length !== 2 || positionals[0] !== 'analyze') throw new Error('usage: ath-exe visual analyze screenshot.png --out NEW_DIRECTORY --analyzer PATH [--manifest PATH] [--policy PATH] [--model PATH]');
    const output = await analyze({ input: positionals[1], out: flags.out, analyzer: flags.analyzer || process.env.ATH_VISUAL_ANALYZER, manifest: flags.manifest, policy: flags.policy, profile: flags.profile, model: flags.model, timeoutMs: flags['timeout-ms'], expectedSha256: flags['expected-sha256'], correlationId: flags['correlation-id'], workflowId: flags['workflow-id'] });
    print(output); return output.exitCode;
  } catch (error) { print({ schemaVersion: 1, outcome: 'infrastructure-error', message: String(error.message).slice(0, 1024) }); return 3; }
}
module.exports = { analyze, analyzeBatch, validateBatchResult, validateResult, command, invoke, readEvidence, safePath };
