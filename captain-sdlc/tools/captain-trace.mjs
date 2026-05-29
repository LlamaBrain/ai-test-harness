#!/usr/bin/env node
// captain-trace — the minimal Captain SDLC cross-tool trace consumer (Seam 1).
//
// Proves the trace envelope (../trace-schema.md) answers its priority goals:
//   captain-trace walk <event_id>   — print the causal chain (event → parents → …)
//   captain-trace by-commit <sha>   — every event recorded against a commit
//   captain-trace ls                — recent events (newest last)
//
// Reads append-only JSON Lines from a project's .captain-sdlc/trace/*.jsonl.
// Default trace dir is ./.captain-sdlc/trace; override with --dir <path>
// (pointing at the directory that holds the .jsonl files).
//
// Zero dependencies, read-only. Faithful to the schema's forward-compat rules:
// lines with an unrecognized schema_version are warned-and-skipped rather than
// guessed at (the consumer-side `trace.schema.unknown` posture); malformed
// lines are warned-and-skipped so one bad write never blinds the whole log.
//
// No emitter populates `parents` yet (that waits on an upstream event such as
// claude-release's code.commit.created), so `walk` on a current
// ath.smoke.completed shows just that event. The walker is general and lights
// up automatically once parents are emitted.

import { readdirSync, readFileSync, existsSync, statSync } from 'node:fs';
import { join } from 'node:path';

const KNOWN_SCHEMA_VERSION = 1;

function usage(code = 2) {
  process.stderr.write(
    `captain-trace — Captain SDLC cross-tool trace reader\n\n` +
    `Usage:\n` +
    `  captain-trace walk <event_id> [--dir <trace-dir>]\n` +
    `  captain-trace by-commit <sha>  [--dir <trace-dir>]\n` +
    `  captain-trace ls               [--dir <trace-dir>] [--limit <n>]\n\n` +
    `Default --dir: ./.captain-sdlc/trace\n`,
  );
  process.exit(code);
}

// --- arg parsing (tiny; no deps) ---
function parseArgs(argv) {
  const positional = [];
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--dir') flags.dir = argv[++i];
    else if (a === '--limit') flags.limit = Number(argv[++i]);
    else if (a.startsWith('--')) usage();
    else positional.push(a);
  }
  return { positional, flags };
}

// --- load + validate every event in the trace dir ---
function loadEvents(dir) {
  if (!existsSync(dir) || !statSync(dir).isDirectory()) {
    process.stderr.write(`error: trace dir not found: ${dir}\n`);
    process.exit(1);
  }
  const files = readdirSync(dir).filter((f) => f.endsWith('.jsonl')).sort();
  const events = [];
  let skipped = 0;
  for (const file of files) {
    const full = join(dir, file);
    const lines = readFileSync(full, 'utf8').split('\n');
    lines.forEach((line, idx) => {
      const trimmed = line.trim();
      if (!trimmed) return;
      let ev;
      try {
        ev = JSON.parse(trimmed);
      } catch {
        process.stderr.write(`warn: ${file}:${idx + 1} malformed JSON — skipped\n`);
        skipped++;
        return;
      }
      if (ev.schema_version !== KNOWN_SCHEMA_VERSION) {
        process.stderr.write(
          `warn: ${file}:${idx + 1} unknown schema_version=${ev.schema_version} — skipped (trace.schema.unknown)\n`,
        );
        skipped++;
        return;
      }
      ev._file = file;
      events.push(ev);
    });
  }
  return { events, skipped };
}

function indexById(events) {
  const byId = new Map();
  for (const ev of events) if (ev.event_id) byId.set(ev.event_id, ev);
  return byId;
}

// Resolve an exact id, else a unique prefix; null if none, throws-as-message if ambiguous.
function resolveId(byId, idArg) {
  if (byId.has(idArg)) return idArg;
  const matches = [...byId.keys()].filter((k) => k.startsWith(idArg));
  if (matches.length === 1) return matches[0];
  if (matches.length > 1) {
    process.stderr.write(`error: id prefix "${idArg}" is ambiguous (${matches.length} matches)\n`);
    process.exit(1);
  }
  return null;
}

function fmt(ev) {
  const id = (ev.event_id || '????????').slice(0, 8);
  const refs = ev.refs || {};
  const where = [refs.project, refs.commit].filter(Boolean).join('@');
  const verdict =
    ev.payload && typeof ev.payload.result === 'string' ? ` result=${ev.payload.result}` : '';
  const failed =
    ev.payload && ev.payload.failed_step ? ` failed=${ev.payload.failed_step}` : '';
  return `${ev.timestamp || '<no-ts>'}  ${id}  ${ev.kind || '<no-kind>'}` +
    `${where ? `  [${where}]` : ''}${verdict}${failed}`;
}

// --- commands ---
function cmdWalk(byId, idArg) {
  const start = resolveId(byId, idArg);
  if (!start) {
    process.stderr.write(`error: event_id not found: ${idArg}\n`);
    process.exit(1);
  }
  const seen = new Set();
  const printChain = (id, depth) => {
    if (seen.has(id)) {
      process.stdout.write(`${'  '.repeat(depth)}↑ ${id.slice(0, 8)} (cycle — stop)\n`);
      return;
    }
    seen.add(id);
    const ev = byId.get(id);
    const indent = '  '.repeat(depth);
    if (!ev) {
      process.stdout.write(`${indent}↑ ${id.slice(0, 8)} (referenced parent not in log)\n`);
      return;
    }
    process.stdout.write(`${indent}${depth === 0 ? '● ' : '↑ '}${fmt(ev)}\n`);
    for (const parent of ev.parents || []) printChain(parent, depth + 1);
  };
  printChain(start, 0);
}

function cmdByCommit(events, sha) {
  const matches = events.filter((ev) => (ev.refs?.commit || '').startsWith(sha));
  if (matches.length === 0) {
    process.stdout.write(`(no events for commit ${sha})\n`);
    return;
  }
  matches
    .sort((a, b) => String(a.timestamp).localeCompare(String(b.timestamp)))
    .forEach((ev) => process.stdout.write(`${fmt(ev)}\n`));
}

function cmdLs(events, limit) {
  const sorted = events
    .slice()
    .sort((a, b) => String(a.timestamp).localeCompare(String(b.timestamp)));
  const shown = Number.isFinite(limit) && limit > 0 ? sorted.slice(-limit) : sorted;
  shown.forEach((ev) => process.stdout.write(`${fmt(ev)}\n`));
}

// --- main ---
const { positional, flags } = parseArgs(process.argv.slice(2));
const command = positional[0];
const dir = flags.dir || join('.captain-sdlc', 'trace');

if (!command) usage();

const { events } = loadEvents(dir);

switch (command) {
  case 'walk': {
    if (!positional[1]) usage();
    cmdWalk(indexById(events), positional[1]);
    break;
  }
  case 'by-commit': {
    if (!positional[1]) usage();
    cmdByCommit(events, positional[1]);
    break;
  }
  case 'ls': {
    cmdLs(events, flags.limit);
    break;
  }
  default:
    process.stderr.write(`error: unknown command "${command}"\n`);
    usage();
}
