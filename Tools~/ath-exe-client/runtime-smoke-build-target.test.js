'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const runtimeRoot = path.resolve(__dirname, '../../Runtime');

test('smoke harness compiles only the shared AITH runtime contract and omits interactive transports', () => {
  for (const sourceName of [
    'IAthHostAdapter.cs', 'AthHostEvents.cs', 'AthRuntimeFlag.cs', 'AthAsyncOpRecord.cs',
    'AthServices.cs', 'AthBridge.cs', 'AthBootstrap.cs', 'AthStateDispatcher.cs',
  ]) {
    const source = fs.readFileSync(path.join(runtimeRoot, sourceName), 'utf8');
    const guard = source.split(/\r?\n/, 1)[0];
    assert.match(guard, /\bSMOKE_HARNESS\b/, `${sourceName} must compile for the optional smoke-harness lane`);
    assert.match(guard, /\bATH_REMOTE\b/, `${sourceName} must retain the ATH_REMOTE host capability`);
  }

  for (const sourceName of [
    'Commands/AthRuntimeSmokeCommands.cs',
    'RemoteConsole/AthRemoteConsoleBootstrap.cs',
  ]) {
    const source = fs.readFileSync(path.join(runtimeRoot, sourceName), 'utf8');
    const guard = source.split(/\r?\n/, 1)[0];
    assert.doesNotMatch(guard, /\bSMOKE_HARNESS\b/, `${sourceName} must not enable an interactive transport in smoke harness`);
  }
});