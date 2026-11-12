'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn } = require('node:child_process');
const { assertNoSymlinkPath, writeAtomicNew } = require('./runtime-smoke-runner');
const codes = { pass: 0, fail: 1, inconclusive: 2, 'infrastructure-error': 3 };
const MAX_BATCH_ITEMS = 8;
const MAX_BATCH_INPUT_BYTES = 64 * 1024 * 1024;
const MAX_RESULT_BYTES = 16 * 1024 * 1024;
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
function safePath(file) {
  const absolute = path.resolve(file), root = path.parse(absolute).root;
  if (fs.lstatSync(root).isSymbolicLink()) throw new Error('Symbolic root refused');
  assertNoSymlinkPath(fs, absolute, root);
  return absolute;
}
function readEvidence(file, maximumBytes = 32 * 1024 * 1024) {
  const absolute = safePath(file), stat = fs.statSync(absolute);
  if (!stat.isFile() || stat.size < 1 || stat.size > maximumBytes) throw new Error('Evidence must be a regular file of 1..' + maximumBytes / (1024 * 1024) + ' MiB; actualBytes=' + stat.size + '; path=' + absolute);
  const bytes = fs.readFileSync(absolute);
  if (bytes.length !== stat.size) throw new Error('Evidence changed while reading');
  return { path: absolute, bytes, sha256: sha(bytes), byteLength: bytes.length };
}
function validateResult(result, code) {
  if (!result || result.schemaVersion !== 1 || !Object.hasOwn(codes, result.outcome) || codes[result.outcome] !== code) throw new Error('Invalid analyzer schema, outcome, or exit code');
  if (result.outcome === 'infrastructure-error') throw new Error('Analyzer infrastructure-error');
  if (!result.policy || ['id', 'version', 'source'].some(k => typeof result.policy[k] !== 'string' || !result.policy[k].trim())) throw new Error('Policy requires nonempty id, version and source');
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
  if (result.outcome === 'pass' && result.coverage.mode !== 'best-effort' && (result.coverage.expected < 1 || result.coverage.expected !== result.coverage.measured)) throw new Error('Strict pass requires exact nonempty coverage');
  for (const f of result.findings) {
    if (!['text.height', 'text.contrast', 'text.clipping', 'text.adjustability'].includes(f.ruleId)) throw new Error('Unknown visual rule');
    if (f.ruleId === 'text.adjustability' && (f.outcome !== 'inconclusive' || f.elementId !== '')) throw new Error('Adjustability requires an inconclusive policy finding');
    const key = JSON.stringify([f.elementId, f.ruleId]);
    if (keys.has(key)) throw new Error('Duplicate finding identity'); keys.add(key);
    if (f.outcome === 'pass') {
      if (!(f.detectionConfidence > 0)) throw new Error('Pass lacks detected evidence');
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
function validateBindings(result, input, manifest, policy) {
  const bytes = input.bytes;
  if (bytes.length < 24 || !bytes.subarray(0, 8).equals(Buffer.from('89504e470d0a1a0a', 'hex')) || bytes.toString('ascii', 12, 16) !== 'IHDR' || bytes.readUInt32BE(16) !== result.image.width || bytes.readUInt32BE(20) !== result.image.height) throw new Error('Decoded dimensions do not match input PNG');
  if (policy && (!result.policy || ['id', 'version', 'source', 'minHeightPx', 'minContrastRatio'].some(key => result.policy[key] !== policy[key]))) throw new Error('Analyzer policy identity or thresholds mismatch');
  if (!manifest) { if (result.coverage.mode !== 'best-effort') throw new Error('Required manifest missing'); return; }
  if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.elements) || !manifest.elements.length || result.coverage.mode === 'best-effort' || result.coverage.expected !== manifest.elements.length) throw new Error('Manifest coverage mismatch');
  for (const key of ['screenId', 'screenshotId', 'locale', 'uiScale']) if (!result.context || result.context[key] !== manifest[key] || manifest[key] == null) throw new Error('Manifest context identity mismatch');
  if (!manifest.resolution || ['width', 'height'].some(k => manifest.resolution[k] !== result.image[k])) throw new Error('Manifest resolution mismatch');
  const elements = new Map(manifest.elements.map((e, index) => {
    const id = Object.hasOwn(e, 'id') ? e.id : 'expected-' + index;
    if (typeof id !== 'string' || !id) throw new Error('Invalid manifest element identity');
    return [id, e];
  }));
  if (elements.size !== manifest.elements.length) throw new Error('Duplicate manifest elements');
  const seen = new Set();
  for (const f of result.findings) {
    if (f.ruleId === 'text.adjustability' && f.outcome === 'inconclusive') continue;
    const element = elements.get(f.elementId), b = f.bounds, e = element?.bounds;
    if (element && !Object.hasOwn(element, 'bounds')) {
      if (f.outcome !== 'inconclusive' || b !== null || f.expectedBounds !== null || f.measuredHeightPx !== null || f.contrastRatio !== null || f.detectionConfidence !== 0 || f.measurementConfidence !== 0) throw new Error('Unlocated expected text requires inconclusive evidence');
      seen.add(JSON.stringify([f.elementId, f.ruleId]));
      continue;
    }
    if (!e || !b || !f.expectedBounds || ['x', 'y', 'width', 'height'].some(k => !Number.isInteger(e[k]) || !Number.isInteger(b[k]) || e[k] !== f.expectedBounds[k]) || b.width <= 0 || b.height <= 0 || b.x < e.x || b.y < e.y || b.x + b.width > e.x + e.width || b.y + b.height > e.y + e.height) throw new Error('Finding identity or bounds differ from manifest');
    seen.add(JSON.stringify([f.elementId, f.ruleId]));
  }
  for (const id of elements.keys()) for (const rule of ['text.height', 'text.contrast', 'text.clipping']) if (!seen.has(JSON.stringify([id, rule]))) throw new Error('Required manifest finding missing');
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
  const executable = readEvidence(options.analyzer, 128 * 1024 * 1024); executable.kind = 'analyzer'; sources.push(executable);
  for (const name of ['manifest', 'policy', 'model']) if (options[name]) {
    const source = readEvidence(options[name], (name === 'model' ? 128 : 32) * 1024 * 1024); source.kind = name; sources.push(source);
    const staged = name === 'model' ? source.path : path.join(out, 'input-' + name + '.json');
    if (staged !== source.path) writeAtomicNew(fs, staged, source.bytes);
    args.push('--' + name, staged);
  }
  // Only the PNG header is needed to bind result dimensions. The exclusive
  // snapshot and digests retain the full evidence without retaining its buffer
  // for every screen while a detector batch is running.
  const header = Buffer.from(input.bytes.subarray(0, 24));
  for (const source of sources) delete source.bytes;
  input.bytes = header;
  return { options, timeoutMs, input, out, sources, args, effectivePolicy };
}
function finishAnalysis(prepared, execution) {
  const { options, input, out, sources } = prepared;
  let { effectivePolicy } = prepared;
  let manifest = null;
  for (const source of sources) {
    const verified = readEvidence(source.path, (['analyzer', 'model'].includes(source.kind) ? 128 : 32) * 1024 * 1024);
    if (verified.sha256 !== source.sha256) throw new Error('Evidence changed during analysis');
    if (source.kind === 'policy') effectivePolicy = JSON.parse(verified.bytes);
    if (source.kind === 'manifest') manifest = JSON.parse(verified.bytes);
  }
  const result = validateResult(JSON.parse(execution.stdout), execution.code);
  for (const source of [input, ...sources.filter(s => ['manifest', 'policy'].includes(s.kind))]) {
    const staged = path.join(out, source === input ? 'input.png' : 'input-' + source.kind + '.json');
    if (readEvidence(staged).sha256 !== source.sha256) throw new Error('Staged evidence changed during analysis');
  }
  validateBindings(result, input, manifest, effectivePolicy);
  if (effectivePolicy && (!result.policy || ['id', 'version', 'source', 'minHeightPx', 'minContrastRatio'].some(key => result.policy[key] !== effectivePolicy[key]))) throw new Error('Analyzer policy identity or thresholds mismatch');
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
    context: result.context || manifest,
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
  const results = new Array(optionsList.length);
  let prepared = [], encodedBytes = 0, invocation = 0, identity, fatalTerminationError;
  const failure = error => ({ result: { schemaVersion: 1, outcome: 'infrastructure-error', error: String(error.message || error).slice(0, 1024) }, exitCode: 3 });
  async function executeGroup() {
    let remaining = prepared;
    while (remaining.length) {
      const first = remaining[0], file = safePath(invocation++ === 0 ? batchFile : batchFile + '.' + (invocation - 1) + '.json');
      const items = remaining.map(p => {
        const item = { output: path.join(p.out, 'native-result.json') };
        for (let i = 0; i < p.args.length; i += 2) if (p.args[i] !== '--model') item[p.args[i].slice(2)] = p.args[i + 1];
        return item;
      });
      let execution;
      try {
        writeAtomicNew(fs, file, JSON.stringify({ schemaVersion: 2, items }) + '\n');
        const args = ['--batch', file];
        if (first.options.model) args.push('--model', path.resolve(first.options.model));
        execution = await invokeBatch(path.resolve(first.options.analyzer), args, remaining);
      } catch (error) { for (const p of remaining) results[p.index] = failure(error); break; }
      if (execution.terminationUnconfirmed) fatalTerminationError = execution.error;
      if (execution.error && (execution.timeoutIndex === undefined || execution.terminationUnconfirmed)) {
        // A protocol/exit failure invalidates the group's claimed computation.
        // Only a known, confirmed per-screen timeout can salvage prior results.
        for (const p of remaining) results[p.index] = failure(execution.error);
        break;
      }
      for (const [index, record] of execution.finished) {
        const p = remaining[index];
        try {
          const evidence = readEvidence(items[index].output, MAX_RESULT_BYTES);
          if (!Number.isInteger(record.resultBytes) || record.resultBytes < 1 || !/^[0-9a-f]{64}$/.test(record.resultSha256 || '') || record.resultBytes !== evidence.byteLength || record.resultSha256 !== evidence.sha256) throw new Error('Native result digest or size identity mismatch');
          const nativeResult = JSON.parse(evidence.bytes);
          if (nativeResult.outcome !== record.outcome) throw new Error('Native result differs from its completed item record');
          results[p.index] = finishAnalysis(p, { code: codes[nativeResult.outcome], stdout: JSON.stringify(nativeResult) });
          try { fs.unlinkSync(items[index].output); } // Owned intermediate; receipt owns result.json.
          catch (error) { if (!['ENOENT', 'EACCES', 'EPERM', 'EBUSY'].includes(error.code)) throw error; }
        } catch (error) { results[p.index] = failure(record.error || error); }
      }
      if (!execution.error) {
        for (const p of remaining) if (!results[p.index]) results[p.index] = failure('Native batch omitted a required item');
        break;
      }
      if (execution.timeoutIndex !== undefined && !execution.terminationUnconfirmed) {
        const timedOut = remaining[execution.timeoutIndex];
        results[timedOut.index] = failure(execution.error);
        // Completed screens retain their independent evidence. A killed screen
        // loses its detector, so only not-yet-started screens need a fresh one.
        remaining = remaining.filter((p, i) => i > execution.timeoutIndex && !results[p.index]);
      } else {
        for (const p of remaining) if (!results[p.index]) results[p.index] = failure(execution.error);
        break;
      }
    }
    prepared = []; encodedBytes = 0;
  }
  for (let index = 0; index < optionsList.length; index++) {
    if (fatalTerminationError) { results[index] = failure(fatalTerminationError); continue; }
    let item;
    try {
      item = prepareAnalysis(optionsList[index]); item.index = index;
      const key = JSON.stringify([path.resolve(item.options.analyzer), item.options.model ? path.resolve(item.options.model) : null]);
      if (identity && identity !== key) throw new Error('Batch requires the same analyzer and model');
      identity = key;
    } catch (error) { results[index] = failure(error); continue; }
    if (prepared.length && (prepared.length >= MAX_BATCH_ITEMS || encodedBytes + item.input.byteLength > MAX_BATCH_INPUT_BYTES)) await executeGroup();
    if (fatalTerminationError) { results[index] = failure(fatalTerminationError); continue; }
    prepared.push(item); encodedBytes += item.input.byteLength;
  }
  if (prepared.length) await executeGroup();
  if (fatalTerminationError) Object.defineProperties(results, {
    terminationUnconfirmed: { value: true },
    terminationError: { value: String(fatalTerminationError.message).slice(0, 1024) }
  });
  return results;
}

function invokeBatch(executable, args, prepared) {
  return new Promise(resolve => {
    const child = spawn(executable, args, { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    const finished = new Map();
    let stdout = '', bytes = 0, active, next = 0, complete, error, timeoutIndex, terminationTimer, terminating = false, settled = false;
    const startupMs = 30000;
    const totalTimer = setTimeout(() => terminate('Analyzer batch exceeded its total execution budget'), startupMs + prepared.reduce((sum, p) => sum + p.timeoutMs, 0));
    let itemTimer = setTimeout(() => terminate('Analyzer batch startup timed out'), startupMs);
    function done(code, terminationUnconfirmed = false) {
      if (settled) return; settled = true;
      clearTimeout(totalTimer); clearTimeout(itemTimer); clearTimeout(terminationTimer);
      if (!error && (!complete || finished.size !== prepared.length || code !== codes[complete.outcome])) error = new Error('Native batch omitted or contradicted its completion record');
      resolve({ code, finished, error, timeoutIndex, terminationUnconfirmed });
    }
    function terminate(reason, timedOut) {
      if (error || settled) return;
      error = new Error(reason); timeoutIndex = timedOut; terminating = true;
      clearTimeout(itemTimer);
      // kill() can emit error synchronously. Install the confirmation deadline
      // first, and only a close event can confirm that this worker has stopped.
      terminationTimer = setTimeout(() => {
        error = new Error(reason + '; process termination unconfirmed');
        child.stdout.destroy(); child.stderr.destroy(); child.unref(); done(null, true);
      }, 2000);
      try { child.kill('SIGKILL'); } catch { /* Await close or the confirmation deadline. */ }
    }
    function record(line) {
      if (error || !line.trim()) return;
      let value;
      try { value = JSON.parse(line); } catch { terminate('Invalid native batch progress JSON'); return; }
      if (!value || typeof value !== 'object' || Array.isArray(value)) { terminate('Invalid native batch progress record'); return; }
      if (value.schemaVersion !== 2) { terminate(value.error || 'Unsupported native batch progress schema'); return; }
      if (value.event === 'started' && !complete && active === undefined && value.index === next && next < prepared.length) {
        clearTimeout(itemTimer); active = next;
        const started = active;
        itemTimer = setTimeout(() => terminate('Analyzer screen timed out after ' + prepared[started].timeoutMs + 'ms', started), prepared[started].timeoutMs);
      } else if (value.event === 'finished' && !complete && active !== undefined && Number.isInteger(value.index) && active === value.index && Object.hasOwn(codes, value.outcome)) {
        clearTimeout(itemTimer); finished.set(active, value); next++; active = undefined;
        itemTimer = setTimeout(() => terminate('Analyzer batch stalled between screens'), startupMs);
      } else if (value.event === 'complete' && !complete && active === undefined && next === prepared.length && value.count === prepared.length && value.outcome === aggregate([...finished.values()].map(item => item.outcome))) {
        clearTimeout(itemTimer); complete = value;
      } else terminate('Native batch progress identity or ordering mismatch');
    }
    function receive(chunk, isError) {
      if (settled || error) return;
      bytes += Buffer.byteLength(chunk, 'utf8');
      if (bytes > 4 * 1024 * 1024) { terminate('Analyzer output exceeds 4 MiB'); return; }
      if (isError) return;
      stdout += chunk;
      if (stdout.length > 65536 && !stdout.includes('\n')) { terminate('Native batch progress line exceeds 64 KiB'); return; }
      let newline;
      while ((newline = stdout.indexOf('\n')) >= 0) { const line = stdout.slice(0, newline); stdout = stdout.slice(newline + 1); if (line.length > 65536) terminate('Native batch progress line exceeds 64 KiB'); else record(line); }
    }
    child.stdout.on('data', chunk => receive(chunk, false)); child.stderr.on('data', chunk => receive(chunk, true));
    child.on('error', cause => { if (!terminating && !settled) { error = cause; done(null); } });
    child.on('close', code => { if (stdout.trim() && !error) record(stdout); done(code); });
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
module.exports = { analyze, analyzeBatch, validateBatchResult, validateResult, validateBindings, command, invoke, readEvidence, safePath };
