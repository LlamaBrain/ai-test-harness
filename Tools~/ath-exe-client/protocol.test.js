'use strict';
// node:test fixtures for protocol.js. These mirror the exact strings the C#
// server/AthLog emit, so they double as the cross-language contract spec.
//   node --test

const test = require('node:test');
const assert = require('node:assert');
const {
  parseResponse,
  parseSentinel,
  parseFields,
  terminalSentinel,
  terminalFields,
  parsePredicate,
  readState,
  evalPredicate,
} = require('./protocol');

test('parseResponse: full envelope', () => {
  const r = parseResponse(
    '{"correlationId":"ab12cd34","status":"success","failReason":null,' +
    '"lines":["OK:harness.ping id=ab12cd34 pong=true"],"elapsedMs":12,"truncated":false}'
  );
  assert.equal(r.correlationId, 'ab12cd34');
  assert.equal(r.status, 'success');
  assert.equal(r.failReason, null);
  assert.deepEqual(r.lines, ['OK:harness.ping id=ab12cd34 pong=true']);
  assert.equal(r.elapsedMs, 12);
  assert.equal(r.truncated, false);
});

test('parseResponse: missing/odd fields default sanely', () => {
  const r = parseResponse('{"status":"failed","failReason":"busy"}');
  assert.equal(r.correlationId, null);
  assert.equal(r.status, 'failed');
  assert.equal(r.failReason, 'busy');
  assert.deepEqual(r.lines, []);
  assert.equal(r.elapsedMs, null);
  assert.equal(r.truncated, false);
});

test('parseResponse: truncated flag', () => {
  assert.equal(parseResponse('{"truncated":true}').truncated, true);
});

test('parseSentinel: CMD with empty args', () => {
  const s = parseSentinel('CMD:harness.ping id=ab12cd34 args=');
  assert.equal(s.kind, 'CMD');
  assert.equal(s.command, 'harness.ping');
  assert.equal(s.id, 'ab12cd34');
  assert.equal(s.fields.args, '');
});

test('parseSentinel: OK bare identifier values', () => {
  const s = parseSentinel('OK:harness.state id=xy key=player_alive value=true status=ok');
  assert.equal(s.kind, 'OK');
  assert.equal(s.command, 'harness.state');
  assert.equal(s.fields.key, 'player_alive');
  assert.equal(s.fields.value, 'true');
  assert.equal(s.fields.status, 'ok');
});

test('parseSentinel: ERR with reason + diagnostic', () => {
  const s = parseSentinel('ERR:harness.state id=xy reason=unknown_key custom_state_attempted=false');
  assert.equal(s.kind, 'ERR');
  assert.equal(s.fields.reason, 'unknown_key');
  assert.equal(s.fields.custom_state_attempted, 'false');
});

test('parseFields: quoted value with spaces', () => {
  const { fields } = parseFields('echo="hello world"');
  assert.equal(fields.echo, 'hello world');
});

test('parseFields: Windows drive path (\\\\ unescapes to \\)', () => {
  // C# escapes '\' -> '\\', so the wire shows doubled backslashes.
  const { fields } = parseFields('path="C:\\\\Users\\\\me\\\\snap.png"');
  assert.equal(fields.path, 'C:\\Users\\me\\snap.png');
});

test('parseFields: escaped quote inside value', () => {
  const { fields } = parseFields('msg="a \\"quoted\\" word"');
  assert.equal(fields.msg, 'a "quoted" word');
});

test('parseFields: \\n unescapes to newline (single-line wire, multiline value)', () => {
  const { fields } = parseFields('msg="line1\\nline2"');
  assert.equal(fields.msg, 'line1\nline2');
});

test('parseFields: [dispatched] marker', () => {
  const { fields, markers } = parseFields('id=xy [dispatched]');
  assert.equal(fields.id, 'xy');
  assert.deepEqual(markers, ['dispatched']);
});

test('parseFields: mixed bare + quoted + marker', () => {
  const { fields, markers } = parseFields(
    'id=xy path="C:\\\\a b\\\\c.png" capture_id=zz status=pending [dispatched]'
  );
  assert.equal(fields.id, 'xy');
  assert.equal(fields.path, 'C:\\a b\\c.png');
  assert.equal(fields.capture_id, 'zz');
  assert.equal(fields.status, 'pending');
  assert.deepEqual(markers, ['dispatched']);
});

test('terminalSentinel: picks OK for the right id, ignores other ids', () => {
  const lines = [
    'CMD:test.echo id=aaa args="x"',
    'OK:test.echo id=bbb echo="other"', // different id — ignore
    'OK:test.echo id=aaa echo="mine"',
  ];
  const s = terminalSentinel(lines, 'aaa');
  assert.equal(s.kind, 'OK');
  assert.equal(s.fields.echo, 'mine');
});

test('terminalSentinel: ERR wins over OK for the same id', () => {
  const lines = [
    'OK:cmd id=aaa note=ignored',
    'ERR:cmd id=aaa reason=command_error',
  ];
  assert.equal(terminalSentinel(lines, 'aaa').kind, 'ERR');
});

test('terminalSentinel: scans all lines (terminal after noise)', () => {
  const lines = Array.from({ length: 50 }, (_, k) => `LOG: noise line ${k}`);
  lines.push('OK:harness.ping id=zz pong=true');
  const s = terminalSentinel(lines, 'zz');
  assert.equal(s.fields.pong, 'true');
});

test('terminalSentinel: null when no terminal present', () => {
  assert.equal(terminalSentinel(['CMD:x id=zz args='], 'zz'), null);
});

test('terminalFields: from a parsed response', () => {
  const resp = parseResponse(
    '{"correlationId":"zz","status":"success","lines":' +
    '["CMD:harness.snap id=zz args=\\"shot\\"","OK:harness.snap id=zz path=\\"/tmp/a.png\\" status=pending [dispatched]"]}'
  );
  const f = terminalFields(resp);
  assert.equal(f.path, '/tmp/a.png');
  assert.equal(f.status, 'pending');
});

// ---- predicate vocabulary ----

test('parsePredicate: bare boolean key + alias', () => {
  assert.deepEqual(parsePredicate('game_ready'), { key: 'game_ready', op: 'truthy' });
  assert.deepEqual(parsePredicate('player_died'), { key: 'player_died_since_reset', op: 'truthy' });
});

test('parsePredicate: special forms', () => {
  assert.deepEqual(parsePredicate('spawn_attempts_at_least:3'), { key: 'spawn_attempts', op: 'gte', expected: 3 });
  assert.deepEqual(parsePredicate('scene_loaded:Game'), { key: 'scene_name', op: 'eq', expected: 'Game' });
  assert.deepEqual(parsePredicate('async_done:ab12'), { key: 'async:ab12', op: 'async_done' });
  assert.deepEqual(parsePredicate('state_equals:is_paused=true'), { key: 'is_paused', op: 'eq', expected: 'true' });
  assert.deepEqual(parsePredicate('player_alive=true'), { key: 'player_alive', op: 'eq', expected: 'true' });
});

test('parsePredicate: empty throws', () => {
  assert.throws(() => parsePredicate(''));
});

test('readState: OK ok/not_ready and ERR unknown_key', () => {
  const ok = parseResponse('{"correlationId":"q","status":"success","lines":["OK:harness.state id=q key=player_alive value=true status=ok"]}');
  assert.deepEqual(readState(ok), { status: 'ok', value: 'true' });

  const nr = parseResponse('{"correlationId":"q","status":"success","lines":["OK:harness.state id=q key=game_ready value= status=not_ready"]}');
  assert.deepEqual(readState(nr), { status: 'not_ready', value: '' });

  const err = parseResponse('{"correlationId":"q","status":"failed","failReason":"command_error","lines":["ERR:harness.state id=q reason=unknown_key custom_state_attempted=false"]}');
  assert.deepEqual(readState(err), { status: 'unknown_key', value: '' });
});

test('evalPredicate: truthy / eq / gte / async_done / fail-fast / pending', () => {
  assert.equal(evalPredicate({ op: 'truthy' }, { status: 'ok', value: 'true' }), 'satisfied');
  assert.equal(evalPredicate({ op: 'truthy' }, { status: 'ok', value: 'false' }), 'pending');
  assert.equal(evalPredicate({ op: 'eq', expected: 'Game' }, { status: 'ok', value: 'Game' }), 'satisfied');
  assert.equal(evalPredicate({ op: 'gte', expected: 3 }, { status: 'ok', value: '5' }), 'satisfied');
  assert.equal(evalPredicate({ op: 'gte', expected: 3 }, { status: 'ok', value: '2' }), 'pending');
  assert.equal(evalPredicate({ op: 'async_done' }, { status: 'ok', value: 'done' }), 'satisfied');
  assert.equal(evalPredicate({ op: 'truthy' }, { status: 'not_ready', value: '' }), 'pending');
  assert.equal(evalPredicate({ op: 'truthy' }, { status: 'unknown_key', value: '' }), 'fail');
});
