#!/usr/bin/env node
'use strict';
// ath / ath-exe — shared developer CLI for Unity and Unreal ATH integrations.
// The historical path and ath-exe command remain compatibility entrypoints.
// All runtime operations delegate to the engine-independent client.js.
//
// Subcommands:
//   cmd    <command...>          fire one console command, print the result
//                                (--raw: no corrId appended, no capture)
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
const { AthClient, sendCommand, buildLaunchArgs, commandSucceeded } = require('./client');
const { runSmoke, emitTrace } = require('./report');

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
  const booleanFlags = new Set(['raw', 'detach', 'keep-open', 'trace', 'null-rhi', 'rendered', 'help']);
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const eq = a.indexOf('=');
      if (eq !== -1) { flags[a.slice(2, eq)] = a.slice(eq + 1); }
      else if (!booleanFlags.has(a.slice(2)) && i + 1 < argv.length && !argv[i + 1].startsWith('--')) { flags[a.slice(2)] = argv[++i]; }
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
    : [path.join(process.cwd(), 'ProjectSettings', 'AthRemote.json'), path.join(process.cwd(), 'Config', 'AthRemote.json')];
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

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const num = (v, dflt) => (v != null ? Number(v) : dflt);
const clientFor = flags => new AthClient({ port: resolvePort(flags), timeoutMs: num(flags['timeout-ms'], DEFAULT_CMD_TIMEOUT_MS) });

// ---- subcommands ----

async function cmdCmd(positionals, flags) {
  const command = positionals.join(' ').trim();
  if (!command) return fail('cmd requires a command string');
  // --raw = the editor tool's tagId:false: the server dispatches verbatim and appends no
  // correlation id, for commands that define only the bare overload (the console matches on
  // exact arity, so a tagged call would fail to dispatch). No sentinels come back — assert
  // through `state` instead.
  const resp = await clientFor(flags).command(command, { raw: !!flags.raw });
  print(resp);
  return commandSucceeded(resp, !!flags.raw) ? 0 : 1;
}

async function cmdState(positionals, flags) {
  const key = positionals[0];
  if (!key) return fail('state requires a key');
  if (/[\s\0]/.test(key)) return fail('state key must be a token without whitespace');
  const resp = await clientFor(flags).command(`harness.state ${key}`);
  const st = proto.readState(resp);
  print({ key, ...st, envelope: resp.status });
  return st.status === 'ok' || st.status === 'not_ready' ? 0 : 1;
}

async function cmdWait(positionals, flags) {
  const result = await clientFor(flags).wait(positionals[0] || '', { timeoutMs: num(flags.timeout ?? flags['timeout-ms'], DEFAULT_WAIT_TIMEOUT_MS) });
  print(result);
  return result.result === 'satisfied' ? 0 : 1;
}

async function cmdSnap(positionals, flags) {
  const result = await clientFor(flags).screenshot(positionals[0] || 'snap', {
    timeoutMs: num(flags.timeout ?? flags['timeout-ms'], DEFAULT_SNAP_TIMEOUT_MS), outDir: flags.out,
  });
  print(result);
  return result.status === 'ok' ? 0 : 1;
}

async function cmdAttach(positionals, flags) {
  const port = positionals[0] != null ? Number(positionals[0]) : resolvePort(flags);
  try {
    const resp = await sendCommand(port, 'harness.ping', { timeoutMs: num(flags['timeout-ms'], 2000) });
    const ok = commandSucceeded(resp);
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
  const args = buildLaunchArgs({ engine: flags.engine || 'unity', port, project: flags.project, map: flags.map,
    mediaDir: flags['media-dir'], timeoutMs: flags['timeout-ms'] == null ? undefined : Number(flags['timeout-ms']), nullRhi: !!flags['null-rhi'] });

  const child = spawn(exe, args, { detached: !!flags.detach, stdio: 'ignore', windowsHide: true, shell: false });
  let launchError = null;
  child.on('error', (error) => { launchError = error.message; });
  let exited = false;
  child.on('exit', () => { exited = true; });

  const readyMs = num(flags['ready-ms'], DEFAULT_LAUNCH_READY_MS);
  const deadline = Date.now() + readyMs;
  let ready = false;
  while (Date.now() < deadline) {
    if (launchError || exited) break;
    try {
      const resp = await sendCommand(port, 'harness.ping', { timeoutMs: 1000 });
      if (commandSucceeded(resp)) { ready = true; break; }
    } catch { /* not up yet */ }
    await sleep(POLL_INTERVAL_MS);
  }

  if (ready) {
    print({ status: 'ready', pid: child.pid, port });
    if (flags.detach) child.unref(); // owned only when not detached
    return 0;
  }

  // failed readiness
  print({ status: 'not_ready', pid: child.pid, port, ...(launchError ? { error: launchError } : {}) });
  if (!flags['keep-open'] && !flags.detach) { try { child.kill(); } catch { /* already gone */ } }
  return 1;
}

async function cmdCapabilities(positionals, flags) {
  print(await clientFor(flags).capabilities());
  return 0;
}

async function cmdExecute(positionals, flags) {
  if (!positionals[0]) return fail('execute requires a command name and optional JSON arguments');
  const response = await clientFor(flags).execute(positionals[0], JSON.parse(positionals.slice(1).join(' ') || '{}'));
  print(response);
  return commandSucceeded(response) ? 0 : 1;
}

async function cmdReset(positionals, flags) {
  const response = await clientFor(flags).reset(positionals[0]);
  print(response);
  return commandSucceeded(response) ? 0 : 1;
}

const metadataFor = flags => ({ project: flags['project-name'], commit: flags.commit,
  taskId: flags['task-id'], designDoc: flags['design-doc'], skillVersion: flags['skill-version'],
  engine: flags.engine, engineVersion: flags['engine-version'], buildMode: flags['build-mode'] });

async function cmdSmoke(positionals, flags) {
  if (!positionals[0]) return fail('smoke requires a JSON specification file');
  const specification = JSON.parse(fs.readFileSync(positionals[0], 'utf8'));
  if (flags.rendered) {
    specification.requires = [...new Set([...(specification.requires || []), 'screenshot'])];
    specification.steps.push({ name: 'Rendered viewport evidence', type: 'screenshot', label: specification.name, timeoutMs: num(flags['snap-timeout-ms'], 30000) });
  }
  const result = await runSmoke(clientFor(flags), specification, { projectRoot: flags['project-root'],
    trace: !!flags.trace, metadata: metadataFor(flags) });
  if (flags.out) {
    fs.mkdirSync(path.dirname(path.resolve(flags.out)), { recursive: true });
    fs.writeFileSync(flags.out, JSON.stringify(result, null, 2) + '\n', 'utf8');
  }
  print(result);
  return result.result === 'pass' ? 0 : 1;
}

async function cmdTrace(positionals, flags) {
  if (!positionals[0] || !flags['project-root']) return fail('trace requires a smoke result file and --project-root');
  const result = JSON.parse(fs.readFileSync(positionals[0], 'utf8'));
  print(emitTrace(flags['project-root'], result, metadataFor(flags)));
  return 0;
}

async function cmdVersion(positionals, flags) {
  const local = { athVersion: require('./package.json').version, protocolVersion: 1 };
  // Local version remains available with neither engine running. Supplying an
  // endpoint explicitly also interrogates the connected runtime implementation.
  if (flags.port != null || flags.config != null) local.runtime = await clientFor(flags).version();
  print(local);
  return 0;
}

// ---- output / errors ----
function print(obj) { process.stdout.write(JSON.stringify(obj, null, 2) + '\n'); }
function fail(msg) { process.stderr.write(`ath-exe: ${msg}\n`); return 2; }

const COMMANDS = { cmd: cmdCmd, state: cmdState, wait: cmdWait, snap: cmdSnap, launch: cmdLaunch, attach: cmdAttach,
  capabilities: cmdCapabilities, version: cmdVersion, execute: cmdExecute, reset: cmdReset, smoke: cmdSmoke, trace: cmdTrace };

async function main(argv = process.argv.slice(2)) {
  const [sub, ...rest] = argv;
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

module.exports = { parseArgv, resolvePort, sendCommand, getFreePort, main, AthClient, buildLaunchArgs };
