'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { AthClient, sendCommand, buildLaunchArgs } = require('./client');
const { runSmoke, emitTrace } = require('./report');
const proto = require('./protocol');

function envelope(command, payload, status = 'success') {
  return { correlationId: 'test1', status, failReason: '', elapsedMs: 1,
    truncated: false, lines: [`CMD:${command} id=test1`, `OK:${command} id=test1 ${payload}`] };
}
function quote(value) {
  return '"' + String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n') + '"';
}
async function serve(t, respond) {
  const sockets = new Set();
  const server = net.createServer(socket => {
    sockets.add(socket);
    socket.on('error', () => {});
    socket.on('close', () => sockets.delete(socket));
    let input = '';
    socket.setEncoding('utf8');
    socket.on('data', chunk => {
      input += chunk;
      if (!input.includes('\n')) return;
      const result = respond(input.slice(0, input.indexOf('\n')), socket);
      if (result !== undefined) socket.end(JSON.stringify(result) + '\n');
    });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => { for (const socket of sockets) socket.destroy(); server.close(); });
  return new AthClient({ port: server.address().port, pollIntervalMs: 5, timeoutMs: 200 });
}
function temp(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-shared-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  return dir;
}

for (const engine of ['unity', 'unreal']) {
  test(`${engine}: same state, capabilities, host execution and event wait interface`, async t => {
    let calls = 0;
    const client = await serve(t, wire => {
      const [command, ...args] = wire.split(' ');
      if (command === 'harness.capabilities') return envelope(command, 'value=' + quote(JSON.stringify({
        protocolVersion: 1, engine, hostName: 'Host', capabilities: ['state', 'commands', 'events'],
        stateKeys: ['custom.counter'], commands: ['custom.echo'] })));
      if (command === 'harness.execute') {
        assert.equal(args[0], 'custom.echo');
        assert.deepEqual(JSON.parse(Buffer.from(args[1], 'base64').toString('utf8')), { text: 'héllo "world"' });
        return envelope(command, 'value="echoed"');
      }
      assert.equal(command, 'harness.state');
      if (args[0] === 'custom.counter') return envelope(command, 'status=ok value=42');
      assert.equal(args[0], 'event:custom.ready');
      return envelope(command, `status=ok value=${++calls > 1}`);
    });
    assert.equal((await client.capabilities()).engine, engine);
    assert.deepEqual(await client.state('custom.counter'), { status: 'ok', value: '42' });
    assert.equal((await client.execute('custom.echo', { text: 'héllo "world"' })).status, 'success');
    assert.equal((await client.wait('event:custom.ready', { timeoutMs: 200 })).result, 'satisfied');
  });
}

test('failed or missing state terminal cannot satisfy an empty string equality', () => {
  for (const response of [
    { correlationId: 'x', status: 'failed', failReason: 'busy', lines: [] },
    { correlationId: 'x', status: 'success', lines: [] },
    { correlationId: 'x', status: 'failed', lines: ['OK:harness.state id=x status=ok value=""'] },
    { correlationId: 'x', status: 'success', lines: ['OK:other id=x status=ok value=""'] },
    { correlationId: 'x', status: 'success', lines: ['OK:harness.state id=x status=ok'] },
  ]) {
    const state = proto.readState(response);
    assert.notEqual(state.status, 'ok');
    assert.notEqual(proto.evalPredicate({ op: 'eq', expected: '' }, state), 'satisfied');
  }
  assert.equal(proto.evalPredicate({ op: 'eq', expected: '' }, { status: 'unsupported', value: '' }), 'fail');
});

test('wait fails immediately for unsupported state and preserves diagnostic', async t => {
  let calls = 0;
  const client = await serve(t, () => {
    calls++;
    return { correlationId: 'x', status: 'failed', failReason: 'command_error',
      lines: ['ERR:harness.state id=x reason=unknown_key'] };
  });
  const result = await client.wait('custom.unknown', { timeoutMs: 100 });
  assert.equal(result.result, 'fail');
  assert.equal(result.state.status, 'unknown_key');
  assert.equal(calls, 1);
});

test('wait deadline bounds the in-flight command even if server never replies', async t => {
  const client = await serve(t, () => undefined);
  const start = Date.now();
  const result = await client.wait('game_ready', { timeoutMs: 45 });
  assert.equal(result.result, 'timeout');
  assert.ok(Date.now() - start < 400);
});

test('absolute socket timeout cannot be extended by trickled bytes', async t => {
  const client = await serve(t, (_, socket) => {
    const timer = setInterval(() => { if (!socket.destroyed) socket.write(' '); }, 5);
    socket.once('close', () => clearInterval(timer));
  });
  await assert.rejects(sendCommand(client.port, 'harness.ping', { timeoutMs: 30 }), /timeout/);
});

test('socket cancellation and pre-cancellation reject promptly', async t => {
  const client = await serve(t, () => undefined);
  const controller = new AbortController();
  const waiting = client.wait('game_ready', { timeoutMs: 2000, signal: controller.signal });
  setTimeout(() => controller.abort(), 10);
  await assert.rejects(waiting, { code: 'ABORT_ERR' });
  await assert.rejects(client.command('harness.ping', { signal: controller.signal }), { code: 'ABORT_ERR' });
});

test('socket decodes split UTF-8 sequences and rejects oversized responses', async t => {
  const client = await serve(t, (_, socket) => {
    const bytes = Buffer.from(JSON.stringify(envelope('harness.state', 'status=ok value="café"')) + '\n');
    const split = bytes.indexOf(Buffer.from('é')) + 1;
    socket.write(bytes.subarray(0, split));
    setTimeout(() => socket.end(bytes.subarray(split)), 5);
  });
  assert.equal((await client.state('unicode')).value, 'café');
  const large = await serve(t, (_, socket) => { socket.end('x'.repeat(300)); });
  await assert.rejects(sendCommand(large.port, 'harness.ping', { maxResponseBytes: 100 }), /too large/);
});

test('commands and state keys cannot inject a second wire request', async () => {
  const client = new AthClient({ port: 8787 });
  await assert.rejects(client.command('harness.ping\nharness.reset'), /single.line/);
  await assert.rejects(client.state('bad key'), /state key/);
  await assert.rejects(client.execute('bad command', {}), /command name/);
  assert.throws(() => new AthClient({ port: NaN }), /port/);
});

test('capabilities rejects unsupported protocol versions and malformed metadata', async t => {
  const client = await serve(t, () => envelope('harness.capabilities', 'value=' + quote('{"protocolVersion":99}')));
  await assert.rejects(client.capabilities(), /protocol/);
});

test('command fails closed without a terminal but raw dispatch remains compatible', async t => {
  const client = await serve(t, () => ({ correlationId: 'x', status: 'success', lines: [] }));
  assert.equal((await client.command('anything')).status, 'failed');
  assert.equal((await client.command('anything', { raw: true })).status, 'success');
});

test('screenshot requires a complete PNG, copies into evidence dir and respects deadline', async t => {
  const dir = temp(t);
  const image = path.join(dir, 'shot.png');
  // Header + IHDR dimensions + final IEND (the runtime encoder owns CRC validation).
  const png = Buffer.concat([Buffer.from('89504e470d0a1a0a0000000d494844520000000200000003080600000000000000', 'hex'),
    Buffer.from('0000000049454e44ae426082', 'hex')]);
  fs.writeFileSync(image, png.subarray(0, 24));
  const client = await serve(t, () => envelope('harness.snap', `status=pending path=${quote(image)}`));
  setTimeout(() => fs.writeFileSync(image, png), 20);
  const result = await client.screenshot('test', { timeoutMs: 300, outDir: path.join(dir, 'media') });
  assert.equal(result.status, 'ok');
  assert.equal(result.width, 2);
  assert.equal(result.height, 3);
  assert.ok(fs.existsSync(result.path));
  fs.writeFileSync(image, 'not a png');
  assert.equal((await client.screenshot('invalid', { timeoutMs: 25 })).status, 'timeout');
});

test('smoke runs arbitrary state assertions and always records a failed result', async t => {
  const dir = temp(t);
  const client = await serve(t, wire => {
    if (wire === 'harness.ping') return envelope('harness.ping', 'pong=true');
    return envelope('harness.state', 'status=ok value=42');
  });
  const success = await runSmoke(client, { name: 'generic', steps: [
    { type: 'command', command: 'harness.ping' },
    { type: 'assert', key: 'custom.counter', equals: '42' },
  ] });
  assert.equal(success.result, 'pass');
  const failed = await runSmoke(client, { name: 'failure', steps: [
    { name: 'counter', type: 'assert', key: 'custom.counter', equals: '99' },
    { type: 'command', command: 'must.not.run' },
  ] }, { projectRoot: dir, trace: true, metadata: { project: 'host' } });
  assert.equal(failed.result, 'fail');
  assert.equal(failed.failedStep, 'counter');
  assert.equal(failed.steps.length, 1);
  const event = JSON.parse(fs.readFileSync(failed.tracePath, 'utf8').trim());
  assert.equal(event.kind, 'ath.smoke.completed');
  assert.equal(event.payload.result, 'fail');
  assert.equal(event.refs.project, 'host');
  assert.equal(event.schema_version, 1);
  assert.match(event.event_id, /^[0-9a-f-]{36}$/);
});

test('trace appends both verdicts and preserves ignore contents, rejects unsafe artifacts', t => {
  const dir = temp(t);
  fs.mkdirSync(path.join(dir, '.captain-sdlc'));
  fs.writeFileSync(path.join(dir, '.captain-sdlc', '.gitignore'), '# retain\nother/');
  const base = { name: 'test', result: 'pass', failedStep: null, summary: 'ok', artifacts: ['media/shot.png'] };
  const first = emitTrace(dir, base);
  emitTrace(dir, { ...base, result: 'fail', failedStep: 'test' });
  const lines = fs.readFileSync(first.path, 'utf8').trim().split('\n').map(JSON.parse);
  assert.equal(lines.length, 2);
  assert.equal(lines[0].payload.result, 'pass');
  assert.equal(lines[1].payload.result, 'fail');
  const ignore = fs.readFileSync(path.join(dir, '.captain-sdlc', '.gitignore'), 'utf8');
  assert.equal(ignore, '# retain\nother/\ntrace/\nside-store/\n');
  for (const artifact of ['../escape', '/absolute', 'C:/drive', 'media\\escape', 'media/../escape', 'media//empty']) {
    assert.throws(() => emitTrace(dir, { ...base, artifacts: [artifact] }), /artifact/);
  }
});

test('launch argv keeps Unity defaults and supports Unreal editor/project and packaged games', () => {
  assert.deepEqual(buildLaunchArgs({ port: 1234 }), ['-ath-remote-console', '-ath-port', '1234']);
  assert.deepEqual(buildLaunchArgs({ engine: 'unreal', port: 1234, project: 'C:/My Project/Game.uproject',
    map: '/Game/Test', nullRhi: true }), [
    'C:/My Project/Game.uproject', '/Game/Test', '-game', '-unattended', '-nosplash', '-nullrhi',
    '-ath-remote-console', '-ath-port=1234',
  ]);
  assert.deepEqual(buildLaunchArgs({ engine: 'unreal', port: 1234 }), [
    '-unattended', '-nosplash', '-ath-remote-console', '-ath-port=1234',
  ]);
  assert.throws(() => buildLaunchArgs({ engine: 'other', port: 1234 }), /engine/);
});

test('smoke rejects engine/capability mismatch before issuing gameplay commands', async t => {
  const commands = [];
  const client = await serve(t, command => {
    commands.push(command);
    return envelope('harness.capabilities', 'value=' + quote(JSON.stringify({ protocolVersion: 1, engine: 'unreal',
      hostName: 'minimal', capabilities: ['state'], stateKeys: [], commands: [] })));
  });
  const mismatch = await runSmoke(client, { name: 'wrong-engine', engine: 'unity', steps: [{ type: 'command', command: 'never' }] });
  assert.equal(mismatch.result, 'fail');
  assert.equal(mismatch.failedStep, 'capabilities');
  const unsupported = await runSmoke(client, { name: 'unsupported', requires: ['player.kill'], steps: [{ type: 'command', command: 'never' }] });
  assert.equal(unsupported.result, 'fail');
  assert.deepEqual(commands, ['harness.capabilities', 'harness.capabilities']);
});

test('trace evidence records connection failures and failed asynchronous completion', async t => {
  const dir = temp(t);
  const client = await serve(t, command => command === 'harness.state async_success:op1'
    ? envelope('harness.state', 'status=ok value=false')
    : envelope('harness.state', 'status=ok value=done'));
  const result = await runSmoke(client, { name: 'async-outcome', steps: [
    { type: 'wait', predicate: 'async_done:op1', timeoutMs: 100 },
    { type: 'assert', key: 'async_success:op1', equals: true },
  ] }, { projectRoot: dir, trace: true });
  assert.equal(result.result, 'fail');
  assert.equal(result.failedStep, 'Step 2');
  assert.equal(JSON.parse(fs.readFileSync(result.reportPath, 'utf8')).steps[1].response.value, 'false');
});
