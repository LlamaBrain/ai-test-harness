'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const path = require('node:path');
const { spawn } = require('node:child_process');
const cli = process.env.ATH_RAW_CLI_SOURCE || path.join(__dirname, 'ath-exe.js');
async function observeCommand(raw) {
  const observed = [];
  const server = net.createServer(socket => {
    let text = '';
    socket.on('data', chunk => {
      text += chunk.toString('utf8');
      if (!text.includes('\n')) return;
      observed.push(text.split('\n')[0]);
      socket.end(JSON.stringify({correlationId:'test',status:'success',failReason:'',lines:[],elapsedMs:0,truncated:false}) + '\n');
    });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const args = [cli, 'cmd', 'harness.ping', '--port', String(server.address().port)];
    if (raw) args.push('--raw');
    const child = spawn(process.execPath, args, {windowsHide:true, stdio:['ignore','pipe','pipe']});
    let output = ''; let errors = '';
    child.stdout.on('data', x => output += x);
    child.stderr.on('data', x => errors += x);
    const code = await new Promise((resolve,reject) => { child.once('error',reject); child.once('close',resolve); });
    assert.equal(code,0,errors);
    assert.equal(JSON.parse(output).status,'success');
    assert.equal(observed.length,1);
    return observed[0];
  } finally { await new Promise(resolve => server.close(resolve)); }
}
test('CLI raw command dispatch sends the bare-overload prefix over the real socket', async () => {
  assert.equal(await observeCommand(true),'!raw harness.ping');
});
test('CLI normal command dispatch preserves tagged transport behavior', async () => {
  assert.equal(await observeCommand(false),'harness.ping');
});
test('raw diagnostic dispatch coexists with both RuntimeSmoke entrypoints', () => {
  const api = require(cli);
  assert.equal(typeof api.runRuntimeSmokeSpec,'function');
  assert.equal(typeof api.runRuntimeSmokeSuiteSpec,'function');
});
