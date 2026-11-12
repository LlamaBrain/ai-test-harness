#!/usr/bin/env node
'use strict';

// Acceptance environment only. The reusable ATH client has no Kinecade dependency.
const fs = require('node:fs');
const path = require('node:path');
const fixture = require('../ath-exe-client/fixture-smoke.json');

function buildAcceptanceSpec(options = {}, scenario = []) {
  if (!Array.isArray(scenario)) throw new TypeError('scenario must be an array of smoke steps');
  const requires = [...new Set(fixture.requires)];
  if (options.rendered) requires.push('screenshot');
  const steps = [
    { name: 'Wire round trip', type: 'command', command: 'harness.ping' },
    { name: 'Engine identity', type: 'assert', key: 'engine_name', equals: 'unreal' },
    { name: 'Acceptance project identity', type: 'assert', key: 'host_name', equals: 'KinecadeSandbox' },
    { name: 'Runtime bridge', type: 'assert', key: 'bridge_ready', equals: 'true' },
    { name: 'World readiness', type: 'wait', predicate: 'game_ready', timeoutMs: 60000 },
    { name: 'Native Unreal session readiness', type: 'wait', predicate: 'unreal.session_ready', timeoutMs: 60000 },
    { name: 'Native Unreal world validity', type: 'assert', key: 'unreal.world_valid', equals: 'true' },
  ];
  if (options.scene) steps.push({ name: 'Selected map', type: 'assert', key: 'scene_name', equals: options.scene });
  steps.push({ name: 'Reset before scenario', type: 'command', command: 'harness.reset' }, ...scenario);
  // Reuse the complete generic fixture, including event resets and correlated
  // asynchronous success; a project identity ping alone does not prove ATH.
  steps.push(...JSON.parse(JSON.stringify(fixture.steps)));
  if (options.rendered) steps.push({ name: 'Rendered viewport evidence', type: 'screenshot', label: 'kinecade-acceptance', timeoutMs: 30000 });
  return { name: 'kinecade-unreal-acceptance', engine: 'unreal', requires, steps };
}

function parseOptions(argv) {
  const options = { port: 8787, projectRoot: process.cwd() };
  const values = {
    '--port': 'port', '--project-root': 'projectRoot', '--scene': 'scene',
    '--scenario': 'scenario', '--out': 'out', '--commit': 'commit',
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--rendered') { options.rendered = true; continue; }
    if (arg === '--dry-run') { options.dryRun = true; continue; }
    const key = values[arg];
    if (!key) throw new Error(`unknown option: ${arg}`);
    if (i + 1 >= argv.length || argv[i + 1].startsWith('--')) throw new Error(`${arg} requires a value`);
    options[key] = argv[++i];
  }
  options.port = Number(options.port);
  if (!Number.isInteger(options.port) || options.port < 1 || options.port > 65535) throw new Error('port must be an integer from 1 to 65535');
  return options;
}

async function main(argv = process.argv.slice(2)) {
  const options = parseOptions(argv);
  const scenario = options.scenario ? JSON.parse(fs.readFileSync(options.scenario, 'utf8')) : [];
  const spec = buildAcceptanceSpec(options, scenario);
  if (options.dryRun) {
    process.stdout.write(JSON.stringify(spec, null, 2) + '\n');
    return 0;
  }
  const projectRoot = path.resolve(options.projectRoot);
  if (!fs.existsSync(path.join(projectRoot, 'KinecadeSandbox.uproject')))
    throw new Error('--project-root must select the consuming KinecadeSandbox project directory');
  const { AthClient } = require('../ath-exe-client/client');
  const { runSmoke } = require('../ath-exe-client/report');
  const result = await runSmoke(new AthClient({ port: options.port }), spec, {
    projectRoot, trace: true,
    metadata: { project: 'kinecade-sandbox', commit: options.commit },
  });
  if (options.out) {
    const out = path.resolve(options.out);
    fs.mkdirSync(path.dirname(out), { recursive: true });
    fs.writeFileSync(out, JSON.stringify(result, null, 2) + '\n');
  }
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
  return result.result === 'pass' ? 0 : 1;
}

if (require.main === module) {
  main().then(code => { process.exitCode = code; }).catch(error => {
    process.stderr.write(`kinecade-acceptance: ${error.message}\n`);
    process.exitCode = 1;
  });
}

module.exports = { buildAcceptanceSpec, parseOptions, main };
