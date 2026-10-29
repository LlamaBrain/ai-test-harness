'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { PassThrough } = require('node:stream');
const { AthClient } = require('./client');
const { commandSucceeded } = require('./client');
const proto = require('./protocol');
const { runSmoke, emitTrace } = require('./report');

test('generic fixture smoke exercises command/state/event/async over the actual socket client', async t => {
  const net = require('node:net');
  let counter = 0, active = true, changed = false, complete = false;
  let sequence = 0;
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket);
    socket.on('close', () => sockets.delete(socket));
    socket.setEncoding('utf8');
    let input = '';
    socket.on('data', chunk => {
      input += chunk;
      if (!input.includes('\n')) return;
      const [command, ...args] = input.trim().split(' ');
      const id = String(++sequence);
      let fields = '';
      if (command === 'harness.capabilities') fields = 'value=' + JSON.stringify(JSON.stringify({
        protocolVersion: 1, engine: 'unreal', hostName: 'FixtureHost', engineVersion: 'test', buildMode: 'Development',
        capabilities: ['state', 'commands', 'events', 'async', 'reset'], commands: [], stateKeys: [],
      }));
      else if (command === 'harness.state') {
        const state = { 'ath.fixture.ready': true, 'ath.fixture.counter': counter, 'ath.fixture.active': active,
          'event:ath.fixture.changed': changed };
        const value = args[0].startsWith('async_success:') ? complete : args[0].startsWith('async:') ? (complete ? 'done' : 'missing') : state[args[0]];
        fields = `status=${value === 'missing' ? 'not_ready' : 'ok'} value=${value}`;
      } else if (command === 'ath.fixture.reset') { counter = 0; active = true; changed = false; complete = false; }
      else if (command === 'ath.fixture.increment') { counter++; changed = true; }
      else if (command === 'harness.reset') changed = false;
      else if (command === 'harness.execute') { active = JSON.parse(Buffer.from(args[1], 'base64').toString()).active; changed = true; }
      else if (command === 'ath.fixture.async_increment') setTimeout(() => { counter++; complete = true; changed = true; }, 15);
      else assert.equal(command, 'harness.ping');
      socket.end(JSON.stringify({ correlationId: id, status: 'success', lines: [`CMD:${command} id=${id}`, `OK:${command} id=${id} ${fields}`] }) + '\n');
    });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { for (const socket of sockets) socket.destroy(); server.close(); });
  const result = await runSmoke(new AthClient({ port: server.address().port, pollIntervalMs: 5 }), require('./fixture-smoke.json'));
  assert.equal(result.result, 'pass', result.summary);
  assert.equal(result.steps.length, 18);
  assert.equal(result.engine, 'unreal');
  assert.equal(counter, 0);
  assert.equal(active, true);
});

test('client rejects success terminal belonging to a different command', async () => {
  const net = require('node:net');
  const server = net.createServer(socket => socket.once('data', () => socket.end(JSON.stringify({
    correlationId: 'one', status: 'success', lines: ['OK:other.command id=one'],
  }) + '\n')));
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    assert.equal((await new AthClient({ port: server.address().port }).command('harness.ping')).status, 'failed');
  } finally { server.close(); }
});

test('correlated command success requires the envelope to identify its terminal', () => {
  assert.equal(commandSucceeded({ status: 'success', correlationId: null, lines: ['OK:harness.ping id=other'] }), false);
  assert.equal(commandSucceeded({ status: 'success', correlationId: '', lines: ['OK:harness.ping id='] }), false);
  assert.equal(commandSucceeded({ status: 'success', correlationId: 'one', lines: ['OK:harness.ping id=other'] }), false);
  assert.equal(commandSucceeded({ status: 'success', correlationId: null, lines: [] }, true), true);
  assert.equal(proto.readState({ status: 'success', correlationId: null,
    lines: ['OK:harness.state id=other status=ok value=true'] }).status, 'error');
});

test('trace adds engine/build/elapsed diagnostics without changing schema v1', t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-trace-meta-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const { event } = emitTrace(dir, { name: 'fixture', result: 'fail', failedStep: 'async', elapsedMs: 31,
    engine: 'unreal', engineVersion: '5.8', buildMode: 'Development', diagnostics: { reason: 'world_destroyed' } },
  { project: 'host', commit: 'deadbeef' });
  assert.equal(event.schema_version, 1);
  assert.equal(event.payload.engine, 'unreal');
  assert.equal(event.payload.engine_version, '5.8');
  assert.equal(event.payload.build_mode, 'Development');
  assert.equal(event.payload.elapsed_ms, 31);
  assert.equal(event.payload.smoke_test_id, 'fixture');
  assert.deepEqual(event.payload.diagnostics, { reason: 'world_destroyed' });
});

test('smoke binds actual async correlation ID and checks completion success', async () => {
  const calls = [];
  const client = {
    async command(command) { return { status: 'success', correlationId: 'actual123', lines: ['OK:ath.fixture.async_increment id=actual123'], fields: { status: 'pending' } }; },
    async wait(predicate) { calls.push(predicate); return { result: 'satisfied' }; },
    async state(key) { calls.push(key); return { status: 'ok', value: 'true' }; },
  };
  const result = await runSmoke(client, { name: 'async', steps: [
    { name: 'start', id: 'operation', type: 'command', command: 'ath.fixture.async_increment' },
    { type: 'wait', predicate: 'async_done:${operation.correlationId}' },
    { type: 'assert', key: 'async_success:${operation.correlationId}', equals: 'true' },
  ] });
  assert.equal(result.result, 'pass');
  assert.deepEqual(calls, ['async_done:actual123', 'async_success:actual123']);
});

test('smoke rejects unresolved references and reports diagnostic instead of polling a literal', async () => {
  let queried = false;
  const result = await runSmoke({ wait() { queried = true; } }, { name: 'bad', steps: [
    { type: 'wait', predicate: 'async_done:${missing.correlationId}' },
  ] });
  assert.equal(result.result, 'fail');
  assert.equal(queried, false);
  assert.match(result.summary, /reference/);
});

test('MCP initialization, tools/list, tools/call and errors use isolated tool names', async () => {
  const { createMcpServer } = require('./mcp-server');
  const server = createMcpServer({ client: { async state(key) { return { status: 'ok', value: key }; } }, prefix: 'unreal-' });
  const init = await server.handle({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-03-26' } });
  assert.equal(init.result.protocolVersion, '2025-03-26');
  const list = await server.handle({ jsonrpc: '2.0', id: 2, method: 'tools/list' });
  assert.ok(list.result.tools.some(tool => tool.name === 'unreal-ath-state'));
  const state = await server.handle({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'unreal-ath-state', arguments: { key: 'fixture' } } });
  assert.equal(JSON.parse(state.result.content[0].text).value, 'fixture');
  const bad = await server.handle({ jsonrpc: '2.0', id: 4, method: 'tools/call', params: { name: 'ath-state', arguments: {} } });
  assert.equal(bad.error.code, -32602);
  assert.equal(await server.handle({ jsonrpc: '2.0', method: 'notifications/initialized' }), null);
  const invalid = await server.handle({ jsonrpc: '2.0', id: 5, method: 'tools/call', params: { name: 'unreal-ath-state', arguments: {} } });
  assert.equal(invalid.result.isError, true);
});

test('MCP stdio handles split records and bounds oversized input', async () => {
  const { createMcpServer } = require('./mcp-server');
  const input = new PassThrough(), output = new PassThrough();
  const chunks = [];
  output.on('data', chunk => chunks.push(chunk.toString()));
  const closed = createMcpServer({ client: {}, maxInputBytes: 200 }).listen(input, output);
  input.write('{"jsonrpc":"2.0","id":1,"method":"');
  input.write('ping"}\n');
  input.end('x'.repeat(201));
  await closed;
  const records = chunks.join('').trim().split('\n').map(JSON.parse);
  assert.deepEqual(records.find(record => record.id === 1).result, {});
  assert.equal(records.find(record => record.id === null).error.code, -32700);
});

test('MCP cancellation interrupts waits and concurrency stays bounded', async () => {
  const { createMcpServer } = require('./mcp-server');
  const server = createMcpServer({ maxConcurrent: 1, client: { wait(_, { signal }) {
    return new Promise((_, reject) => signal.addEventListener('abort', () => reject(new Error('cancelled')), { once: true }));
  } } });
  await server.handle({ jsonrpc: '2.0', id: 0, method: 'initialize', params: { protocolVersion: '2025-03-26' } });
  const waiting = server.handle({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: 'ath-wait', arguments: { predicate: 'ready' } } });
  const busy = await server.handle({ jsonrpc: '2.0', id: 2, method: 'tools/call', params: { name: 'ath-state', arguments: { key: 'ready' } } });
  assert.equal(busy.error.code, -32000);
  await server.handle({ jsonrpc: '2.0', method: 'notifications/cancelled', params: { requestId: 1 } });
  assert.equal((await waiting).result.isError, true);
});

test('standalone MCP process exposes tools over newline stdio without engine installation', async t => {
  const { spawn } = require('node:child_process');
  const child = spawn(process.execPath, [path.join(__dirname, 'mcp-server.js'), '--prefix', 'test-'],
    { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, shell: false });
  t.after(() => { if (child.exitCode == null) child.kill(); });
  const readline = require('node:readline');
  const lines = readline.createInterface({ input: child.stdout });
  const pending = new Map();
  lines.on('line', line => { const message = JSON.parse(line); pending.get(message.id)?.(message); });
  let stderr = '';
  child.stderr.on('data', bytes => { stderr += bytes.toString(); });
  const request = (id, method, params) => new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('MCP subprocess deadline')), 3000);
    pending.set(id, value => { clearTimeout(timer); pending.delete(id); resolve(value); });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
  const initialize = await request(1, 'initialize', { protocolVersion: '2025-03-26' });
  assert.equal(initialize.result.serverInfo.name, 'ath');
  const list = await request(2, 'tools/list');
  assert.equal(list.result.tools.length, 6);
  assert.ok(list.result.tools.every(tool => tool.name.startsWith('test-ath-')));
  const failure = await request(3, 'tools/call', { name: 'test-ath-trace-emit', arguments: { result: 'fail', skill: 'fixture' } });
  assert.equal(failure.result.isError, true);
  assert.match(failure.result.content[0].text, /project/i);
  const exited = new Promise((resolve, reject) => { child.once('exit', resolve); child.once('error', reject); });
  child.stdin.end();
  assert.equal(await exited, 0);
  assert.equal(stderr, '');
});
