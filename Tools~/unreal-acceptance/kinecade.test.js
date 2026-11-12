'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fixture = require('../ath-exe-client/fixture-smoke.json');
const { buildAcceptanceSpec, parseOptions } = require('./kinecade');

test('Kinecade baseline checks engine, host, readiness, protocol and reset without game dependencies', () => {
  const spec = buildAcceptanceSpec({});
  assert.equal(spec.engine, 'unreal');
  assert.ok(spec.steps.some(s => s.type === 'assert' && s.key === 'engine_name' && s.equals === 'unreal'));
  assert.ok(spec.steps.some(s => s.type === 'assert' && s.key === 'host_name' && s.equals === 'KinecadeSandbox'));
  assert.ok(spec.steps.some(s => s.type === 'wait' && s.predicate === 'game_ready'));
  assert.ok(spec.steps.some(s => s.type === 'command' && s.command === 'harness.reset'));
  assert.equal(spec.steps.some(s => s.type === 'screenshot'), false);
  assert.equal(JSON.stringify(spec).includes('player'), false);
});

test('rendered acceptance requires capture instead of silently skipping it', () => {
  const spec = buildAcceptanceSpec({ rendered: true, scene: 'Entry' });
  assert.ok(spec.steps.some(s => s.type === 'screenshot'));
  assert.equal(spec.steps.find(s => s.type === 'screenshot').timeoutMs, 30000);
  assert.ok(spec.steps.some(s => s.type === 'assert' && s.key === 'scene_name' && s.equals === 'Entry'));
});

test('host scenario steps follow baseline reset and precede final evidence', () => {
  const custom = [{ type: 'execute', command: 'acceptance.begin', arguments: { label: 'integration' } },
    { type: 'wait', predicate: 'state_equals:acceptance.phase=complete', timeoutMs: 2000 }];
  const spec = buildAcceptanceSpec({ rendered: true }, custom);
  const reset = spec.steps.findIndex(s => s.command === 'harness.reset');
  assert.deepEqual(spec.steps.slice(reset + 1, reset + 3), custom);
  assert.equal(spec.steps.at(-1).type, 'screenshot');
  assert.throws(() => buildAcceptanceSpec({}, { steps: custom }), /array/);
});

test('options reject invalid ports and unknown flags before any connection or evidence write', () => {
  assert.equal(parseOptions(['--port', '8877', '--rendered']).port, 8877);
  assert.equal(parseOptions(['--dry-run']).dryRun, true);
  for (const port of ['0', '65536', 'NaN', '1.5']) {
    assert.throws(() => parseOptions(['--port', port]), /port/);
  }
  assert.throws(() => parseOptions(['--pot', '8787']), /unknown/);
  assert.throws(() => parseOptions(['--project-root']), /requires/);
});

test('default acceptance includes the complete portable fixture and real Unreal state', () => {
  const spec = buildAcceptanceSpec({});
  for (const step of fixture.steps) assert.ok(spec.steps.some(candidate =>
    JSON.stringify(candidate) === JSON.stringify(step)), `missing fixture step: ${step.name}`);
  assert.ok(spec.steps.some(step => step.key === 'unreal.world_valid' && step.equals === 'true'));
  assert.ok(spec.steps.some(step => step.predicate === 'unreal.session_ready'));
  assert.ok(spec.steps.some(step => step.key === 'async_success:${operation.correlationId}'));
  assert.equal(spec.steps.some(step => step.type === 'screenshot'), false);
});

test('acceptance defaults to the consuming working directory without a machine-specific typo', () => {
  assert.equal(parseOptions([]).projectRoot, process.cwd());
  assert.equal(parseOptions(['--project-root', 'D:/Any/KinecadeSandbox']).projectRoot, 'D:/Any/KinecadeSandbox');
  assert.equal(path.basename(parseOptions([]).projectRoot).includes('KinecadeSandboxx'), false);
});

test('custom scenario remains immediately after baseline reset and default fixture stays isolated', () => {
  const custom = [{ type: 'command', command: 'custom.test' }];
  const spec = buildAcceptanceSpec({}, custom);
  const reset = spec.steps.findIndex(step => step.command === 'harness.reset');
  assert.deepEqual(spec.steps.slice(reset + 1, reset + 2), custom);
  const first = spec.steps.find(step => step.name === 'Reset fixture');
  assert.ok(first);
  first.command = 'altered';
  assert.equal(buildAcceptanceSpec({}).steps.find(step => step.name === 'Reset fixture').command, 'ath.fixture.reset');
  assert.equal(fixture.steps.find(step => step.name === 'Reset fixture').command, 'ath.fixture.reset');
});
