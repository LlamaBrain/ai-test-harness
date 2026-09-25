const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..', '..');
const packageVersion = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8')).version;
const releaseVersion = fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim().replace(/^v/, '');
const runtime = fs.readFileSync(path.join(root, 'Runtime', 'AthRuntimeFlag.cs'), 'utf8');
const runtimeVersion = runtime.match(/PackageVersion\s*=\s*"([^"]+)"/)?.[1];

test('package version surfaces stay in sync', () => {
  assert.ok(runtimeVersion, 'AthRuntimeFlag.PackageVersion is declared');
  assert.equal(releaseVersion, packageVersion, 'VERSION matches package.json');
  assert.equal(runtimeVersion, packageVersion, 'AthRuntimeFlag.PackageVersion matches package.json');
});
