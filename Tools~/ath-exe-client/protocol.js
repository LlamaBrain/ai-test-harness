'use strict';
// protocol.js — PURE parsing for the ATH remote-console wire protocol.
// No sockets, no I/O, no Unity: fed strings, returns structured data.
// This is the contract; the in-player C# server must produce exactly what
// this parses. See the plan's "Locked protocol/lifecycle invariants".
//
// Wire shape:
//   Request  = one newline-delimited command string.
//   Response = one newline-delimited JSON object:
//              { correlationId, status, failReason, lines, elapsedMs, truncated }
//   `lines`  = the raw CMD:/OK:/ERR: sentinel strings the command logged.
//
// Sentinel field grammar (owned by AthLog on the C# side):
//   identifier values are bare:            status=ok  pong=true  level=Info
//   non-identifier values are quoted:      path="C:\\Users\\me\\snap.png"
//   quoted escapes:  \"->"   \\->\   \n->newline   (values are single-line)
//   standalone markers:                    [dispatched]

/** Parse the JSON response envelope. Throws on malformed JSON. */
function parseResponse(jsonLine) {
  const obj = JSON.parse(jsonLine);
  return {
    correlationId: obj.correlationId ?? null,
    status: obj.status ?? null,
    failReason: obj.failReason ?? null,
    lines: Array.isArray(obj.lines) ? obj.lines : [],
    elapsedMs: typeof obj.elapsedMs === 'number' ? obj.elapsedMs : null,
    truncated: obj.truncated === true,
  };
}

/**
 * Parse one CMD:/OK:/ERR: sentinel line.
 * @returns {{kind:string, command:string, id:(string|null), fields:Object, markers:string[]}|null}
 *          null if the line is not a sentinel.
 */
function parseSentinel(line) {
  if (typeof line !== 'string') return null;
  const m = /^(CMD|OK|ERR):(\S+)\s*(.*)$/s.exec(line);
  if (!m) return null;
  const { fields, markers } = parseFields(m[3]);
  return {
    kind: m[1],
    command: m[2],
    id: Object.prototype.hasOwnProperty.call(fields, 'id') ? fields.id : null,
    fields,
    markers,
  };
}

/**
 * Tokenize a sentinel payload (`key=val key="quoted val" [marker]`) honoring
 * quotes and escapes. Bare `[x]` and bare non key=value tokens become markers.
 */
function parseFields(s) {
  const fields = {};
  const markers = [];
  let i = 0;
  const n = s.length;
  while (i < n) {
    if (s[i] === ' ') { i++; continue; }

    // [marker]
    if (s[i] === '[') {
      const close = s.indexOf(']', i);
      const end = close === -1 ? n : close;
      const marker = s.slice(i + 1, end).trim();
      if (marker) markers.push(marker);
      i = close === -1 ? n : close + 1;
      continue;
    }

    // key=...  (find the '=' that separates this token's key)
    let j = i;
    while (j < n && s[j] !== ' ' && s[j] !== '=') j++;
    if (j >= n || s[j] !== '=') {
      // bare token, no '=' — treat as a marker
      markers.push(s.slice(i, j));
      i = j;
      continue;
    }
    const key = s.slice(i, j);
    i = j + 1; // past '='

    let value;
    if (i < n && s[i] === '"') {
      i++; // opening quote
      let buf = '';
      while (i < n) {
        const c = s[i];
        if (c === '\\' && i + 1 < n) {
          const nx = s[i + 1];
          buf += nx === 'n' ? '\n' : nx; // \" -> "  \\ -> \  \n -> newline
          i += 2;
          continue;
        }
        if (c === '"') { i++; break; }
        buf += c;
        i++;
      }
      value = buf;
    } else {
      let k = i;
      while (k < n && s[k] !== ' ') k++;
      value = s.slice(i, k);
      i = k;
    }
    fields[key] = value;
  }
  return { fields, markers };
}

/**
 * The terminal OK/ERR sentinel for a correlation id, scanning ALL lines (so a
 * verbose/truncated line stream never hides the terminal). ERR wins over OK if
 * both are present for the id. Returns null if neither is found.
 */
function terminalSentinel(lines, correlationId) {
  let ok = null;
  let err = null;
  for (const line of lines) {
    const s = parseSentinel(line);
    if (!s) continue;
    if (correlationId != null && s.id !== correlationId) continue;
    if (s.kind === 'OK') ok = s;
    else if (s.kind === 'ERR') err = s;
  }
  return err || ok;
}

/** Convenience: the payload fields of the terminal sentinel for a response. */
function terminalFields(response) {
  const term = terminalSentinel(response.lines, response.correlationId);
  return term ? term.fields : {};
}

// ---- wait predicate vocabulary (mirrors ath-smoke-fullloop, state-based) ----

// Friendly bare predicate -> canonical harness.state key.
const TRUTHY_ALIASES = {
  player_spawned: 'player_spawned_since_reset',
  player_died: 'player_died_since_reset',
  goal_reached: 'goal_reached_since_reset',
  seed_consumed: 'seed_consumed_since_reset',
};

/**
 * Parse a wait predicate into { key, op, expected? }. The client polls
 * `harness.state <key>` and applies the op. Throws on an empty predicate.
 */
function parsePredicate(pred) {
  if (typeof pred !== 'string' || pred.length === 0) throw new Error('empty predicate');
  let m;
  if ((m = /^state_equals:([^=]+)=(.*)$/.exec(pred))) return { key: m[1], op: 'eq', expected: m[2] };
  if ((m = /^spawn_attempts_at_least:(\d+)$/.exec(pred))) return { key: 'spawn_attempts', op: 'gte', expected: Number(m[1]) };
  if ((m = /^async_done:(.+)$/.exec(pred))) return { key: 'async:' + m[1], op: 'async_done' };
  if ((m = /^scene_loaded:(.+)$/.exec(pred))) return { key: 'scene_name', op: 'eq', expected: m[1] };
  if (pred.includes('=')) { const i = pred.indexOf('='); return { key: pred.slice(0, i), op: 'eq', expected: pred.slice(i + 1) }; }
  return { key: TRUTHY_ALIASES[pred] || pred, op: 'truthy' }; // bare boolean key
}

/**
 * Normalize a `harness.state` response into { status, value }. The OK line
 * carries status=ok|not_ready + value; an ERR terminal (e.g. unknown_key)
 * surfaces its reason as the status.
 */
function readState(response) {
  const term = terminalSentinel(response.lines, response.correlationId);
  if (term && term.kind === 'ERR') return { status: term.fields.reason || 'error', value: '' };
  const f = term ? term.fields : {};
  return { status: f.status || 'ok', value: f.value != null ? f.value : '' };
}

/**
 * Evaluate a parsed predicate against a normalized state.
 * @returns {'satisfied'|'pending'|'fail'} — 'fail' = fail-fast (unknown_key /
 *          unresolvable); 'pending' = keep polling until the caller's timeout.
 */
function evalPredicate(desc, state) {
  if (!state) return 'pending';
  if (state.status === 'unknown_key' || state.status === 'error') return 'fail';
  if (state.status === 'not_ready') return 'pending';
  const v = state.value;
  switch (desc.op) {
    case 'truthy': return v === 'true' ? 'satisfied' : 'pending';
    case 'eq': return v === String(desc.expected) ? 'satisfied' : 'pending';
    case 'gte': { const n = Number(v); return !Number.isNaN(n) && n >= desc.expected ? 'satisfied' : 'pending'; }
    case 'async_done': return v === 'done' ? 'satisfied' : 'pending';
    default: return 'pending';
  }
}

module.exports = {
  parseResponse,
  parseSentinel,
  parseFields,
  terminalSentinel,
  terminalFields,
  parsePredicate,
  readState,
  evalPredicate,
};
