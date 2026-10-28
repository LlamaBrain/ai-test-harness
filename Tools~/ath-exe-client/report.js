'use strict';

// Captain trace schema and ATH payload match Editor/McpSkills/AthTraceWriter.cs.
// The consuming project is explicit, so no Unity/Unreal lifecycle is required.
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { performance } = require('node:perf_hooks');
const { commandSucceeded } = require('./client');
const VERSION = require('./package.json').version;

function validArtifact(value) {
  return typeof value === 'string' && value.length > 0 && !/[\\:\0\r\n]/.test(value) &&
    !path.posix.isAbsolute(value) && value.split('/').every(part => part && part !== '.' && part !== '..');
}

function ensureTraceDirectory(projectRoot) {
  const stateDir = path.join(path.resolve(projectRoot), '.captain-sdlc');
  const traceDir = path.join(stateDir, 'trace');
  fs.mkdirSync(traceDir, { recursive: true });
  const ignorePath = path.join(stateDir, '.gitignore');
  let existing = '';
  try { existing = fs.readFileSync(ignorePath, 'utf8'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const present = new Set(existing.split(/\r?\n/).map(line => line.trim()));
  const missing = ['trace/', 'side-store/'].filter(line => !present.has(line));
  if (missing.length) fs.appendFileSync(ignorePath, (existing && !existing.endsWith('\n') ? '\n' : '') + missing.join('\n') + '\n', 'utf8');
  return traceDir;
}

/** Append exactly one standard smoke event; artifacts remain trace-relative. */
function emitTrace(projectRoot, result, metadata = {}) {
  if (typeof projectRoot !== 'string' || !projectRoot) throw new Error('projectRoot is required for trace evidence');
  if (!['pass', 'fail'].includes(result.result)) throw new Error('smoke result must be pass or fail');
  const artifacts = result.artifacts || [];
  if (!Array.isArray(artifacts) || !artifacts.every(validArtifact)) throw new Error('unsafe trace artifact path');
  const timestamp = new Date().toISOString();
  const event = {
    schema_version: 1, event_id: randomUUID(), timestamp, tool: 'ath', tool_version: VERSION,
    kind: 'ath.smoke.completed',
    refs: { project: metadata.project || path.basename(path.resolve(projectRoot)), commit: metadata.commit || null,
      release: null, design_doc: metadata.designDoc || null, task_id: metadata.taskId || null },
    payload: { skill: result.name || 'ath-smoke', skill_version: metadata.skillVersion || VERSION,
      result: result.result, failed_step: result.result === 'pass' ? null : result.failedStep || null,
      summary: result.summary || null, artifacts,
      smoke_test_id: result.name || 'ath-smoke',
      engine: result.engine || metadata.engine || null,
      engine_version: result.engineVersion || metadata.engineVersion || null,
      build_mode: result.buildMode || metadata.buildMode || null,
      elapsed_ms: Number.isFinite(result.elapsedMs) ? result.elapsedMs : null,
      diagnostics: result.diagnostics || null },
  };
  const dir = ensureTraceDirectory(projectRoot);
  const output = path.join(dir, timestamp.slice(0, 10) + '.jsonl');
  fs.appendFileSync(output, JSON.stringify(event) + '\n', 'utf8');
  return { path: output, event };
}

/** Deterministic sequential smoke with first-failure reporting. A failed command
 * never passes because it merely dispatched; use an explicit wait/assert step
 * for host asynchronous completion. Reports retain all observed step results. */
async function runSmoke(client, specification, { projectRoot, trace = false, metadata = {}, screenshotDir, signal } = {}) {
  if (!specification || typeof specification.name !== 'string' || !specification.name || !Array.isArray(specification.steps) || !specification.steps.length)
    throw new Error('smoke specification requires a name and nonempty steps');
  if (trace && !projectRoot) throw new Error('projectRoot is required for trace evidence');
  const start = performance.now();
  const result = { name: specification.name, result: 'pass', failedStep: null, summary: null, artifacts: [], steps: [], elapsedMs: 0 };
  const references = new Map();
  const expand = value => {
    if (typeof value !== 'string') return value;
    return value.replace(/\$\{([A-Za-z0-9_-]+)\.([A-Za-z0-9_]+)\}/g, (_, id, field) => {
      const response = references.get(id);
      const resolved = response && (Object.hasOwn(response, field) ? response[field] : response.fields?.[field]);
      if (resolved == null || typeof resolved === 'object') throw new Error(`unresolved smoke reference: ${id}.${field}`);
      return String(resolved);
    });
  };
  const outputDir = screenshotDir || (trace ? path.join(projectRoot, '.captain-sdlc', 'trace', 'media') : undefined);
  let current = 'capabilities';
  try {
    if (specification.engine || specification.requires?.length) {
      const capabilities = await client.capabilities({ signal });
      result.engine = capabilities.engine;
      result.hostName = capabilities.hostName;
      result.engineVersion = capabilities.engineVersion || null;
      result.buildMode = capabilities.buildMode || null;
      if (specification.engine && specification.engine !== capabilities.engine) throw new Error(`expected engine ${specification.engine}, got ${capabilities.engine}`);
      if (specification.requires && (!Array.isArray(specification.requires) || specification.requires.some(capability => !capabilities.capabilities.includes(capability))))
        throw new Error('required host capability unavailable');
    }
    for (const [index, rawStep] of specification.steps.entries()) {
      const step = rawStep && { ...rawStep };
      current = step?.name || `Step ${index + 1}`;
      const observed = { name: current, type: step?.type, result: 'pass' };
      result.steps.push(observed);
      try {
        if (step?.id && (typeof step.id !== 'string' || !/^[A-Za-z0-9_-]+$/.test(step.id) || references.has(step.id)))
          throw new Error('smoke step id must be a unique reference token');
        if (step) for (const key of ['command', 'predicate', 'key', 'label', 'equals']) step[key] = expand(step[key]);
        switch (step?.type) {
          case 'command':
            observed.response = await client.command(step.command, { raw: step.raw === true, timeoutMs: step.timeoutMs, signal });
            if (!commandSucceeded(observed.response, step.raw === true)) throw new Error(observed.response.fields.reason || observed.response.failReason || 'command failed');
            break;
          case 'execute':
            observed.response = await client.execute(step.command, step.arguments || {}, { timeoutMs: step.timeoutMs, signal });
            if (!commandSucceeded(observed.response)) throw new Error(observed.response.fields.reason || observed.response.failReason || 'host command failed');
            break;
          case 'wait':
            observed.response = await client.wait(step.predicate, { timeoutMs: step.timeoutMs, signal });
            if (observed.response.result !== 'satisfied') throw new Error(`wait ${observed.response.result}: ${step.predicate}`);
            break;
          case 'assert':
            observed.response = await client.state(step.key, { timeoutMs: step.timeoutMs, signal });
            if (observed.response.status !== 'ok' || observed.response.value !== String(step.equals))
              throw new Error(`state ${step.key}: expected ${JSON.stringify(String(step.equals))}, got ${observed.response.status} ${JSON.stringify(observed.response.value)}`);
            break;
          case 'screenshot': {
            observed.response = await client.screenshot(step.label || 'smoke', { timeoutMs: step.timeoutMs, outDir: outputDir, signal });
            if (observed.response.status !== 'ok') throw new Error(`screenshot ${observed.response.status}`);
            if (trace) {
              const relative = path.relative(path.join(projectRoot, '.captain-sdlc', 'trace'), observed.response.path).split(path.sep).join('/');
              if (!validArtifact(relative)) throw new Error('screenshot output must be inside trace directory');
              result.artifacts.push(relative);
            }
            break;
          }
          default: throw new Error(`unknown smoke step type: ${step?.type}`);
        }
        if (step.id) references.set(step.id, observed.response);
      } catch (error) { observed.result = 'fail'; observed.error = error.message; throw error; }
    }
    result.summary = `${specification.name}: ${result.steps.length} steps passed`;
  } catch (error) {
    result.result = 'fail';
    result.failedStep = current;
    result.summary = error.message;
    result.diagnostics = { message: error.message, step: current, observation: result.steps.at(-1)?.response || null };
  }
  result.elapsedMs = Math.round(performance.now() - start);
  if (trace) {
    const dir = ensureTraceDirectory(projectRoot);
    const evidenceName = 'runs/' + randomUUID() + '.json';
    const evidencePath = path.join(dir, evidenceName);
    fs.mkdirSync(path.dirname(evidencePath), { recursive: true });
    fs.writeFileSync(evidencePath, JSON.stringify(result, null, 2) + '\n', 'utf8');
    result.artifacts.push(evidenceName);
    result.reportPath = evidencePath;
    result.tracePath = emitTrace(projectRoot, result, metadata).path;
  }
  return result;
}

module.exports = { runSmoke, emitTrace, validArtifact, ensureTraceDirectory };
