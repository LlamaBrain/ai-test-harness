'use strict';
// Integration tests for the client socket path. A fake TCP server stands in for
// the in-player AthRemoteConsoleServer: it reads one newline-delimited command
// and replies with one newline-delimited JSON envelope — exactly the contract.
// This proves sendCommand's connect/write/read-one-line/parse path without Unity.
//   node --test

const test = require('node:test');
const assert = require('node:assert');
const net = require('net');
const { sendCommand, getFreePort, parseArgv } = require('./ath-exe');

// Spin up a fake server. `respond(commandLine)` -> the JSON object to send back.
function fakeServer(respond) {
  return new Promise((resolve) => {
    const server = net.createServer((sock) => {
      let buf = '';
      sock.on('data', (d) => {
        buf += d.toString('utf8');
        const nl = buf.indexOf('\n');
        if (nl === -1) return;
        const command = buf.slice(0, nl);
        sock.write(JSON.stringify(respond(command)) + '\n');
        // one command per connection
      });
    });
    server.listen(0, '127.0.0.1', () => resolve({ server, port: server.address().port }));
  });
}

test('sendCommand: round-trips a JSON envelope from a fake server', async () => {
  const { server, port } = await fakeServer((command) => ({
    correlationId: 'srv1',
    status: 'success',
    failReason: '',
    lines: [`CMD:${command} id=srv1 args=`, `OK:${command} id=srv1 pong=true`],
    elapsedMs: 3,
    truncated: false,
  }));
  try {
    const resp = await sendCommand(port, 'harness.ping');
    assert.equal(resp.status, 'success');
    assert.equal(resp.correlationId, 'srv1');
    assert.ok(resp.lines.some((l) => l.startsWith('OK:harness.ping')));
  } finally {
    server.close();
  }
});

test('sendCommand: surfaces a failed envelope (busy)', async () => {
  const { server, port } = await fakeServer(() => ({
    correlationId: '', status: 'failed', failReason: 'busy', lines: [], elapsedMs: 0, truncated: false,
  }));
  try {
    const resp = await sendCommand(port, 'anything');
    assert.equal(resp.status, 'failed');
    assert.equal(resp.failReason, 'busy');
  } finally {
    server.close();
  }
});

test('sendCommand: rejects on connection refused', async () => {
  const port = await getFreePort(); // nothing listening here
  await assert.rejects(() => sendCommand(port, 'harness.ping', { timeoutMs: 1000 }));
});

test('parseArgv: positionals + value flags + bool flags', () => {
  const { positionals, flags } = parseArgv(['harness.ping', '--port', '8787', '--detach', '--timeout-ms=500']);
  assert.deepEqual(positionals, ['harness.ping']);
  assert.equal(flags.port, '8787');
  assert.equal(flags.detach, true);
  assert.equal(flags['timeout-ms'], '500');
});
