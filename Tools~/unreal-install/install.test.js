'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { installPlugin, parseOptions } = require('./install');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-install-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const source = path.join(root, 'Reusable ATH');
  const project = path.join(root, 'Any Unreal Project');
  fs.mkdirSync(source);
  fs.mkdirSync(project);
  fs.writeFileSync(path.join(source, 'Ath.uplugin'), JSON.stringify({ FileVersion: 3,
    Modules: [{ Name: 'AthRuntime', Type: 'Runtime' }] }));
  fs.writeFileSync(path.join(project, 'Game.uproject'), JSON.stringify({ FileVersion: 3 }));
  return { root, source, project, destination: path.join(project, 'Plugins', 'Ath') };
}

test('dry-run resolves a portable plugin link and writes nothing', t => {
  const input = fixture(t);
  const result = installPlugin({ ...input, dryRun: true });
  assert.equal(result.status, 'planned');
  assert.equal(result.projectFile, path.join(input.project, 'Game.uproject'));
  assert.equal(result.source, fs.realpathSync(input.source));
  assert.equal(result.destination, input.destination);
  assert.equal(fs.existsSync(path.dirname(input.destination)), false);
});

test('install and verify retain a single source implementation without copying', t => {
  const input = fixture(t);
  assert.equal(installPlugin(input).status, 'installed');
  assert.equal(fs.lstatSync(input.destination).isSymbolicLink(), true);
  assert.equal(fs.realpathSync(input.destination), fs.realpathSync(input.source));
  assert.equal(installPlugin({ ...input, verifyOnly: true }).status, 'verified');
  assert.equal(installPlugin(input).status, 'verified');
  assert.equal(fs.existsSync(path.join(input.source, 'Ath.uplugin')), true);
});

test('existing directory or foreign/dangling link refuses even dry-run and preserves data', t => {
  const input = fixture(t);
  fs.mkdirSync(input.destination, { recursive: true });
  fs.writeFileSync(path.join(input.destination, 'keep.txt'), 'user plugin');
  assert.throws(() => installPlugin({ ...input, dryRun: true }), /existing|conflict/i);
  assert.throws(() => installPlugin(input), /existing|conflict/i);
  assert.equal(fs.readFileSync(path.join(input.destination, 'keep.txt'), 'utf8'), 'user plugin');
  const other = fixture(t);
  fs.mkdirSync(path.dirname(other.destination));
  fs.symlinkSync(other.root, other.destination, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => installPlugin({ ...other, dryRun: true }), /existing|conflict/i);
  const broken = fixture(t);
  fs.mkdirSync(path.dirname(broken.destination));
  fs.symlinkSync(path.join(broken.root, 'missing'), broken.destination, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => installPlugin(broken), /existing|conflict/i);
});

test('verify-only never creates a missing destination', t => {
  const input = fixture(t);
  assert.throws(() => installPlugin({ ...input, verifyOnly: true }), /not installed|missing/i);
  assert.equal(fs.existsSync(path.dirname(input.destination)), false);
});

test('unrelated or malformed projects and plugin sources fail before writing', t => {
  const input = fixture(t);
  fs.writeFileSync(path.join(input.project, 'Other.uproject'), '{}');
  assert.throws(() => installPlugin(input), /exactly one|ambiguous/i);
  assert.equal(installPlugin({ ...input, project: path.join(input.project, 'Game.uproject'), dryRun: true }).status, 'planned');
  fs.writeFileSync(path.join(input.source, 'Ath.uplugin'), '{broken');
  assert.throws(() => installPlugin({ ...input, project: path.join(input.project, 'Game.uproject') }), /plugin|JSON/i);
  assert.equal(fs.existsSync(path.dirname(input.destination)), false);
});

test('linked Plugins parent cannot redirect writes outside the selected project', t => {
  const input = fixture(t);
  const outside = path.join(input.root, 'foreign plugins');
  fs.mkdirSync(outside);
  fs.symlinkSync(outside, path.dirname(input.destination), process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => installPlugin(input), /Plugins|link|outside/i);
  assert.equal(fs.existsSync(path.join(outside, 'Ath')), false);
});

test('CLI options require an explicit project and use a repository-relative source default', () => {
  assert.throws(() => parseOptions([]), /project/i);
  const options = parseOptions(['--project', 'Game.uproject', '--dry-run']);
  assert.equal(options.project, 'Game.uproject');
  assert.equal(options.dryRun, true);
  assert.equal(options.source, path.resolve(__dirname, '../../Unreal~/Ath'));
  assert.throws(() => parseOptions(['--project', 'Game.uproject', '--verify-only', '--dry-run']), /exclusive|together/i);
  assert.throws(() => parseOptions(['--project']), /requires/i);
  assert.throws(() => parseOptions(['--project', 'Game.uproject', '--force']), /unknown/i);
});

test('CLI dry-run and conflict return truthful exit status without launching a process', t => {
  const input = fixture(t);
  const command = [path.join(__dirname, 'install.js'), '--project', input.project, '--source', input.source, '--dry-run'];
  const success = spawnSync(process.execPath, command, { encoding: 'utf8', windowsHide: true });
  assert.equal(success.status, 0, success.stderr);
  assert.equal(JSON.parse(success.stdout).status, 'planned');
  assert.equal(fs.existsSync(path.dirname(input.destination)), false);
  fs.mkdirSync(input.destination, { recursive: true });
  const failure = spawnSync(process.execPath, command, { encoding: 'utf8', windowsHide: true });
  assert.notEqual(failure.status, 0);
  assert.match(failure.stderr, /existing|conflict/i);
});
