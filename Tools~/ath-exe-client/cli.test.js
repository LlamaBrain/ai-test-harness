'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { parseArgv } = require('./ath-exe');

async function serverFor(t, respond) {
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    let buffer = '';
    socket.setEncoding('utf8');
    socket.on('data', chunk => {
      buffer += chunk;
      if (buffer.includes('\n')) socket.end(JSON.stringify(respond(buffer.slice(0, buffer.indexOf('\n')))) + '\n');
    });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { for (const socket of sockets) socket.destroy(); server.close(); });
  return server.address().port;
}
function cli(args) {
  return new Promise(resolve => execFile(process.execPath, [path.join(__dirname, 'ath-exe.js'), ...args],
    { windowsHide: true, shell: false, timeout: 5000 }, (error, stdout, stderr) => resolve({ code: error?.code || 0, stdout, stderr })));
}

test('tool directory runs independently of the Unity package root', async t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-portable-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  for (const file of ['ath-exe.js', 'client.js', 'protocol.js', 'report.js', 'mcp-server.js', 'package.json'])
    fs.copyFileSync(path.join(__dirname, file), path.join(dir, file));
  const result = await new Promise(resolve => execFile(process.execPath, [path.join(dir, 'ath-exe.js'), 'version'],
    { cwd: dir, windowsHide: true, shell: false, timeout: 5000 },
    (error, stdout, stderr) => resolve({ code: error?.code || 0, stdout, stderr })));
  assert.equal(result.code, 0, result.stderr);
  assert.equal(JSON.parse(result.stdout).athVersion, require('./package.json').version);
  assert.equal(typeof require(path.join(dir, 'mcp-server.js')).createMcpServer, 'function');
});
const ok = (command, payload) => ({ correlationId: 'cli', status: 'success', lines: [`OK:${command} id=cli ${payload}`], elapsedMs: 0, truncated: false });

test('boolean flags leave command/specification positionals intact', () => {
  assert.deepEqual(parseArgv(['--raw', 'bare.command', '--detach']), { positionals: ['bare.command'], flags: { raw: true, detach: true } });
  assert.deepEqual(parseArgv(['--trace', 'smoke.json', '--null-rhi']), { positionals: ['smoke.json'], flags: { trace: true, 'null-rhi': true } });
});

test('legacy CLI handles state and raw dispatch with unchanged wire vocabulary', async t => {
  const port = await serverFor(t, command => {
    if (command === '!raw bare.command') return { correlationId: 'cli', status: 'success', lines: [] };
    assert.equal(command, 'harness.state custom.key');
    return ok('harness.state', 'status=ok value=example');
  });
  const state = await cli(['state', 'custom.key', '--port', String(port)]);
  assert.equal(state.code, 0);
  assert.equal(JSON.parse(state.stdout).value, 'example');
  const raw = await cli(['cmd', '--raw', 'bare.command', '--port', String(port)]);
  assert.equal(raw.code, 0);
});

test('CLI capabilities and smoke failure have machine-readable output and exit status', async t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-cli-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const port = await serverFor(t, command => command === 'harness.capabilities'
    ? ok('harness.capabilities', 'value="{\\"protocolVersion\\":1,\\"engine\\":\\"unreal\\",\\"hostName\\":\\"Host\\",\\"capabilities\\":[\\"state\\"],\\"stateKeys\\":[],\\"commands\\":[]}"')
    : ok('harness.state', 'status=ok value=false'));
  const capabilities = await cli(['capabilities', '--port', String(port)]);
  assert.equal(capabilities.code, 0);
  assert.equal(JSON.parse(capabilities.stdout).engine, 'unreal');
  const spec = path.join(dir, 'smoke.json');
  fs.writeFileSync(spec, JSON.stringify({ name: 'failure', steps: [{ type: 'assert', key: 'custom.ready', equals: true }] }));
  const report = path.join(dir, 'report.json');
  const result = await cli(['smoke', spec, '--port', String(port), '--trace', '--project-root', dir, '--out', report]);
  assert.equal(result.code, 1);
  const parsed = JSON.parse(result.stdout);
  assert.equal(parsed.result, 'fail');
  assert.ok(fs.existsSync(parsed.tracePath));
  assert.equal(JSON.parse(fs.readFileSync(report, 'utf8')).failedStep, 'Step 1');
});

test('CLI cannot report success for malformed/no-response state', async t => {
  const port = await serverFor(t, () => ({ correlationId: 'x', status: 'failed', failReason: 'no_response', lines: [] }));
  const result = await cli(['wait', 'state_equals:empty=', '--port', String(port), '--timeout', '100']);
  assert.equal(result.code, 1);
  assert.equal(JSON.parse(result.stdout).result, 'fail');
});
