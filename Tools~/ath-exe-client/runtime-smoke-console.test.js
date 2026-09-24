'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const commandsPath = path.join(__dirname, '..', '..', 'Runtime', 'Commands', 'AthRuntimeSmokeCommands.cs');
const commandsSource = fs.readFileSync(commandsPath, 'utf8');
const editorToolsPath = path.join(__dirname, '..', '..', 'Editor', 'McpSkills', 'Tool_AthRuntimeSmoke.cs');
const editorToolsSource = fs.readFileSync(editorToolsPath, 'utf8');

test('development console exposes declared RuntimeSmoke investigation actions', () => {
  assert.match(commandsSource, /ConsoleMethod\("runtime_smoke\.investigate"/);
  assert.match(commandsSource, /public static async void Investigate\(/);
  assert.match(commandsSource, /ExecuteInvestigationActionAsync\(runCorrelation,\s*actionId\)/);
  assert.match(commandsSource, /RUNTIME_SMOKE_INVESTIGATION id=/);
});

test('Editor MCP exposes declared capture actions separately from failure-only investigation', () => {
  assert.match(editorToolsSource, /McpPluginTool\("ath-runtime-smoke-capture"/);
  assert.match(editorToolsSource, /registry\.CaptureAsync\(correlationId,\s*actionId\)/);
});
