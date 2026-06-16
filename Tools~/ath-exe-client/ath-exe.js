#!/usr/bin/env node
'use strict';
// ath-exe — internal, developer-only CLI that drives a built ATH_REMOTE Unity
// player over a 127.0.0.1 loopback socket. Not shipped. Pairs with the
// in-player AthRemoteConsoleServer. The pure wire/predicate logic lives in
// protocol.js (unit-tested); this file is the thin socket/process shell.
//
// Subcommands:
//   cmd    <command...>          fire one console command, print the result
//   state  <key>                 query harness.state <key>
//   wait   <predicate>           poll harness.state until satisfied/timeout
//   snap   <label>               harness.snap, then poll the PNG path
//   launch <exe>                 spawn the player opted-in, wait until ready
//   attach <port>                connectivity check (harness.ping) on a port
//
// Common flags: --port N  --timeout-ms N  --config <AthRemote.json>
// Port resolves: --port -> config file -> 8787 (default).  `launch` with no
// --port picks a free random port and passes it to the player.

const net = require('net');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const proto = require('./protocol');

const DEFAULT_PORT = 8787;
const DEFAULT_CMD_TIMEOUT_MS = 5000;
const DEFAULT_WAIT_TIMEOUT_MS = 30000;
const DEFAULT_SNAP_TIMEOUT_MS = 10000;
const DEFAULT_LAUNCH_READY_MS = 30000;
const POLL_INTERVAL_MS = 250;

// ---- tiny arg parser: positionals + --flag value / --flag=value / --bool ----
function parseArgv(argv) {
  const positionals = [];
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const eq = a.indexOf('=');
      if (eq !== -1) { flags[a.slice(2, eq)] = a.slice(eq + 1); }
      else if (i + 1 < argv.length && !argv[i + 1].startsWith('--')) { flags[a.slice(2)] = argv[++i]; }
      else { flags[a.slice(2)] = true; }
    } else {
      positionals.push(a);
    }
  }
  return { positionals, flags };
}

function readConfig(flags) {
  const explicit = flags.config;
  const candidates = explicit
    ? [explicit]
    : [path.join(process.cwd(), 'ProjectSettings', 'AthRemote.json')];
  for (const c of candidates) {
    try { return JSON.parse(fs.readFileSync(c, 'utf8')); } catch { /* absent/unreadable */ }
  }
  return {};
}

function resolvePort(flags) {
  if (flags.port != null) return Number(flags.port);
  const cfg = readConfig(flags);
  if (cfg && cfg.port != null) return Number(cfg.port);
  return DEFAULT_PORT;
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const srv = net.createServer();
    srv.once('error', reject);
    srv.listen(0, '127.0.0.1', () => {
      const port = srv.address().port;
      srv.close(() => resolve(port));
    });
  });
}

// One command per connection: connect, write `<command>\n`, read one JSON line.
function sendCommand(port, command, { timeoutMs = DEFAULT_CMD_TIMEOUT_MS } = {}) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host: '127.0.0.1', port });
    let buf = '';
    let settled = false;
    const finish = (fn, arg) => { if (!settled) { settled = true; socket.destroy(); fn(arg); } };
    socket.setTimeout(timeoutMs);
    socket.on('connect', () => socket.write(command + '\n'));
    socket.on('data', (d) => {
      buf += d.toString('utf8');
      const nl = buf.indexOf('\n');
      if (nl !== -1) {
        try { finish(resolve, proto.parseResponse(buf.slice(0, nl))); }
        catch (e) { finish(reject, e); }
      }
    });
    socket.on('timeout', () => finish(reject, new Error(`timeout after ${timeoutMs}ms`)));
    socket.on('error', (e) => finish(reject, e));
    socket.on('close', () => finish(reject, new Error('connection closed before response')));
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const num = (v, dflt) => (v != null ? Number(v) : dflt);

// ---- subcommands ----

async function cmdCmd(positionals, flags) {
  const command = positionals.join(' ').trim();
  if (!command) return fail('cmd requires a command string');
  const port = resolvePort(flags);
  const resp = await sendCommand(port, command, { timeoutMs: num(flags['timeout-ms'], DEFAULT_CMD_TIMEOUT_MS) });
  print({ ...resp, fields: proto.terminalFields(resp) });
  return resp.status === 'failed' ? 1 : 0;
}

async function cmdState(positionals, flags) {
  const key = positionals[0];
  if (!key) return fail('state requires a key');
  const port = resolvePort(flags);
  const resp = await sendCommand(port, `harness.state ${key}`, { timeoutMs: num(flags['timeout-ms'], DEFAULT_CMD_TIMEOUT_MS) });
  const st = proto.readState(resp);
  print({ key, ...st, envelope: resp.status });
  return st.status === 'unknown_key' || st.status === 'error' ? 1 : 0;
}

async function cmdWait(positionals, flags) {
  const desc = proto.parsePredicate(positionals[0] || '');
  const port = resolvePort(flags);
  const timeoutMs = num(flags.timeout, DEFAULT_WAIT_TIMEOUT_MS);
  const deadline = Date.now() + timeoutMs;
  let last = null;
  while (Date.now() < deadline) {
    const resp = await sendCommand(port, `harness.state ${desc.key}`);
    last = proto.readState(resp);
    const verdict = proto.evalPredicate(desc, last);
    if (verdict === 'satisfied') { print({ predicate: positionals[0], result: 'satisfied', state: last }); return 0; }
    if (verdict === 'fail') { print({ predicate: positionals[0], result: 'fail', state: last }); return 1; }
    await sleep(POLL_INTERVAL_MS);
  }
  print({ predicate: positionals[0], result: 'timeout', state: last });
  return 1;
}

async function cmdSnap(positionals, flags) {
  const label = positionals[0] || 'snap';
  const port = resolvePort(flags);
  const resp = await sendCommand(port, `harness.snap ${label}`);
  const f = proto.terminalFields(resp);
  if (resp.status === 'failed' || !f.path) { print({ status: resp.status, failReason: resp.failReason, fields: f }); return 1; }

  // Two-phase: poll the player-written PNG (same machine/filesystem) until stable.
  const timeoutMs = num(flags.timeout, DEFAULT_SNAP_TIMEOUT_MS);
  const deadline = Date.now() + timeoutMs;
  let lastSize = -1;
  while (Date.now() < deadline) {
    try {
      const sz = fs.statSync(f.path).size;
      if (sz > 0 && sz === lastSize) {
        let outPath = f.path;
        if (flags.out) { outPath = path.join(flags.out, path.basename(f.path)); fs.copyFileSync(f.path, outPath); }
        print({ status: 'ok', path: outPath, bytes: sz });
        return 0;
      }
      lastSize = sz;
    } catch { /* not written yet */ }
    await sleep(POLL_INTERVAL_MS);
  }
  print({ status: 'timeout', path: f.path });
  return 1;
}

async function cmdAttach(positionals, flags) {
  const port = positionals[0] != null ? Number(positionals[0]) : resolvePort(flags);
  try {
    const resp = await sendCommand(port, 'harness.ping', { timeoutMs: num(flags['timeout-ms'], 2000) });
    const ok = resp.status === 'success';
    print({ port, reachable: ok, status: resp.status });
    return ok ? 0 : 1;
  } catch (e) {
    print({ port, reachable: false, error: e.message });
    return 1;
  }
}

async function cmdLaunch(positionals, flags) {
  const exe = positionals[0];
  if (!exe) return fail('launch requires an exe path');
  if (flags.detach && flags['keep-open']) return fail('--detach and --keep-open are mutually exclusive');

  const port = flags.port != null ? Number(flags.port) : await getFreePort();
  const args = ['-ath-remote-console', '-ath-port', String(port)];
  if (flags['media-dir']) args.push('-ath-media-dir', flags['media-dir']);
  if (flags['timeout-ms']) args.push('-ath-timeout-ms', String(flags['timeout-ms']));

  const child = spawn(exe, args, { detached: !!flags.detach, stdio: 'ignore' });
  child.on('error', (e) => { console.error(`spawn failed: ${e.message}`); });

  const readyMs = num(flags['ready-ms'], DEFAULT_LAUNCH_READY_MS);
  const deadline = Date.now() + readyMs;
  let ready = false;
  while (Date.now() < deadline) {
    try {
      const resp = await sendCommand(port, 'harness.ping', { timeoutMs: 1000 });
      if (resp.status === 'success') { ready = true; break; }
    } catch { /* not up yet */ }
    await sleep(POLL_INTERVAL_MS);
  }

  if (ready) {
    print({ status: 'ready', pid: child.pid, port });
    if (flags.detach) child.unref(); // owned only when not detached
    return 0;
  }

  // failed readiness
  print({ status: 'not_ready', pid: child.pid, port });
  if (!flags['keep-open'] && !flags.detach) { try { child.kill(); } catch { /* already gone */ } }
  return 1;
}

// ---- output / errors ----
function print(obj) { process.stdout.write(JSON.stringify(obj, null, 2) + '\n'); }
function fail(msg) { process.stderr.write(`ath-exe: ${msg}\n`); return 2; }

const COMMANDS = { cmd: cmdCmd, state: cmdState, wait: cmdWait, snap: cmdSnap, launch: cmdLaunch, attach: cmdAttach };

async function main() {
  const [sub, ...rest] = process.argv.slice(2);
  const handler = COMMANDS[sub];
  if (!handler) {
    process.stderr.write(`usage: ath-exe <${Object.keys(COMMANDS).join('|')}> [...]\n`);
    return 2;
  }
  const { positionals, flags } = parseArgv(rest);
  return handler(positionals, flags);
}

if (require.main === module) {
  main()
    .then((code) => process.exit(code || 0))
    .catch((e) => { process.stderr.write(`ath-exe: ${e.message}\n`); process.exit(1); });
}

module.exports = { parseArgv, resolvePort, sendCommand, getFreePort }; // exported for tests
