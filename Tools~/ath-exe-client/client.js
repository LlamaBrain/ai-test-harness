'use strict';

// Shared ATH client. The protocol, waits, and evidence capture have no engine
// dependency. Only buildLaunchArgs knows engine-specific process arguments.
const net = require('node:net');
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const { performance } = require('node:perf_hooks');
const proto = require('./protocol');

const DEFAULT_PORT = 8787;
const DEFAULT_TIMEOUT_MS = 5000;
const DEFAULT_POLL_MS = 250;
const MAX_REQUEST_BYTES = 8 * 1024;
const MAX_RESPONSE_BYTES = 1024 * 1024;
const MAX_SCREENSHOT_BYTES = 32 * 1024 * 1024;
const MAX_DECODED_BYTES = 128 * 1024 * 1024;

function positive(value, name) {
  if (!Number.isFinite(value) || value <= 0) throw new TypeError(`${name} must be positive and finite`);
  return value;
}
function validPort(port) {
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new TypeError('port must be an integer from 1 to 65535');
  return port;
}
function token(value, name) {
  if (typeof value !== 'string' || !value || /[\s\0]/.test(value)) throw new TypeError(`${name} must be a nonempty token without whitespace`);
  return value;
}
function abortError() { return Object.assign(new Error('operation aborted'), { code: 'ABORT_ERR' }); }
function checkAbort(signal) { if (signal?.aborted) throw abortError(); }
function delay(ms, signal) {
  checkAbort(signal);
  return new Promise((resolve, reject) => {
    const finish = (error) => { clearTimeout(timer); signal?.removeEventListener('abort', aborted); error ? reject(error) : resolve(); };
    const aborted = () => finish(abortError());
    const timer = setTimeout(finish, Math.max(1, ms));
    signal?.addEventListener('abort', aborted, { once: true });
  });
}

/** One request and one JSON response line on a loopback connection. The timer
 * bounds the complete exchange, including a peer that trickles partial bytes. */
async function sendCommand(port, command, { timeoutMs = DEFAULT_TIMEOUT_MS, signal,
  maxResponseBytes = MAX_RESPONSE_BYTES } = {}) {
  validPort(port);
  positive(timeoutMs, 'timeoutMs');
  positive(maxResponseBytes, 'maxResponseBytes');
  checkAbort(signal);
  if (typeof command !== 'string' || !command.trim() || /[\r\n\0]/.test(command))
    throw new TypeError('command must be a nonempty single-line string');
  if (Buffer.byteLength(command, 'utf8') + 1 > MAX_REQUEST_BYTES) throw new Error('request too large (maximum 8 KiB)');

  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host: '127.0.0.1', port });
    let buffer = '';
    let bytes = 0;
    let settled = false;
    const finish = (error, response) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      signal?.removeEventListener('abort', aborted);
      socket.destroy();
      error ? reject(error) : resolve(response);
    };
    const aborted = () => finish(abortError());
    const timer = setTimeout(() => finish(Object.assign(new Error(`timeout after ${timeoutMs}ms`), { code: 'ETIMEDOUT' })), timeoutMs);
    signal?.addEventListener('abort', aborted, { once: true });
    socket.setEncoding('utf8'); // StringDecoder retains split multibyte sequences.
    socket.on('connect', () => socket.write(command + '\n'));
    socket.on('data', chunk => {
      bytes += Buffer.byteLength(chunk, 'utf8');
      if (bytes > maxResponseBytes) { finish(new Error('response too large')); return; }
      buffer += chunk;
      const newline = buffer.indexOf('\n');
      if (newline < 0) return;
      try { finish(null, proto.parseResponse(buffer.slice(0, newline))); }
      catch (error) { finish(error); }
    });
    socket.on('error', error => finish(error));
    socket.on('close', () => finish(new Error('connection closed before response')));
  });
}

function commandSucceeded(response, raw = false, expectedCommand) {
  if (!['success', 'dispatched'].includes(response.status)) return false;
  if (!raw && (typeof response.correlationId !== 'string' || !response.correlationId.length)) return false;
  const terminal = proto.terminalSentinel(response.lines, response.correlationId);
  return raw || terminal?.kind === 'OK' && (!expectedCommand || terminal.command === expectedCommand);
}

async function pngInfo(bytes, deadline, signal) {
  const signature = Buffer.from('89504e470d0a1a0a', 'hex');
  if (bytes.length < 57 || !bytes.subarray(0, 8).equals(signature)) return null;
  let offset = 8, width, height, depth, color, interlace, seenHeader = false;
  let seenData = false, dataEnded = false, palette = 0, processed = 0;
  const data = [];
  while (offset + 12 <= bytes.length) {
    const length = bytes.readUInt32BE(offset);
    if (length > bytes.length - offset - 12) return null;
    const type = bytes.toString('ascii', offset + 4, offset + 8);
    const chunk = bytes.subarray(offset + 8, offset + 8 + length);
    let crc = 0xffffffff;
    for (const b of bytes.subarray(offset + 4, offset + 8 + length)) {
      crc ^= b;
      for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
      if (++processed % 65536 === 0) {
        await new Promise(resolve => setImmediate(resolve));
        checkAbort(signal);
        if (performance.now() >= deadline) return null;
      }
    }
    if (((crc ^ 0xffffffff) >>> 0) !== bytes.readUInt32BE(offset + 8 + length)) return null;
    offset += length + 12;
    if (!seenHeader) {
      if (type !== 'IHDR' || length !== 13) return null;
      width = chunk.readUInt32BE(0); height = chunk.readUInt32BE(4);
      depth = chunk[8]; color = chunk[9]; interlace = chunk[12];
      const depths = { 0: [1, 2, 4, 8, 16], 2: [8, 16], 3: [1, 2, 4, 8], 4: [8, 16], 6: [8, 16] };
      if (!width || !height || !depths[color]?.includes(depth) || chunk[10] || chunk[11] || interlace > 1) return null;
      seenHeader = true;
    } else if (type === 'IDAT') {
      if (dataEnded || (color === 3 && !palette)) return null;
      seenData = true; data.push(chunk);
    } else if (type === 'IEND') {
      if (length || !seenData || offset !== bytes.length) return null;
      const channels = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[color];
      const passes = interlace ? [[0,0,8,8],[4,0,8,8],[0,4,4,8],[2,0,4,4],[0,2,2,4],[1,0,2,2],[0,1,1,2]] : [[0,0,1,1]];
      let expected = 0;
      const rows = [];
      for (const [x, y, dx, dy] of passes) {
        const w = Math.max(0, Math.ceil((width - x) / dx));
        const h = Math.max(0, Math.ceil((height - y) / dy));
        if (!w || !h) continue;
        const row = Math.ceil(w * channels * depth / 8) + 1;
        expected += row * h;
        if (!Number.isSafeInteger(expected) || expected > MAX_DECODED_BYTES) return null;
        rows.push([row, h, w]);
      }
      try {
        const compressed = Buffer.concat(data);
        const result = await new Promise((resolve, reject) => {
          let settled = false;
          let outputSize = 0;
          const output = Buffer.allocUnsafe(expected);
          const inflator = zlib.createInflate();
          const finish = (error, value) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            signal?.removeEventListener('abort', aborted);
            inflator.destroy();
            error ? reject(error) : resolve(value);
          };
          const aborted = () => finish(abortError());
          const timer = setTimeout(() => finish(null, null), Math.max(1, deadline - performance.now()));
          signal?.addEventListener('abort', aborted, { once: true });
          inflator.on('data', chunk => {
            if (outputSize + chunk.length > expected) return finish(null, null);
            chunk.copy(output, outputSize);
            outputSize += chunk.length;
          });
          inflator.on('error', error => finish(error));
          inflator.on('end', () => finish(null, { buffer: output.subarray(0, outputSize), bytesRead: inflator.bytesWritten }));
          inflator.end(compressed);
        });
        checkAbort(signal);
        if (!result || performance.now() >= deadline || result.bytesRead !== compressed.length) return null;
        const decoded = result.buffer;
        if (decoded.length !== expected) return null;
        let pos = 0, checked = 0;
        for (const [row, count, pixels] of rows) {
          let previous = color === 3 ? Buffer.alloc(row - 1) : null;
          for (let i = 0; i < count; i++, pos += row) {
            const filter = decoded[pos];
            if (filter > 4) return null;
            if (color === 3) {
              const current = Buffer.allocUnsafe(row - 1);
              for (let j = 0; j < current.length; j++) {
                const left = j ? current[j - 1] : 0, up = previous[j], upperLeft = j ? previous[j - 1] : 0;
                let predictor = 0;
                if (filter === 1) predictor = left;
                else if (filter === 2) predictor = up;
                else if (filter === 3) predictor = Math.floor((left + up) / 2);
                else if (filter === 4) {
                  const p = left + up - upperLeft, a = Math.abs(p - left), b = Math.abs(p - up), c = Math.abs(p - upperLeft);
                  predictor = a <= b && a <= c ? left : b <= c ? up : upperLeft;
                }
                current[j] = (decoded[pos + 1 + j] + predictor) & 255;
                if (++checked % 65536 === 0) {
                  await new Promise(resolve => setImmediate(resolve));
                  checkAbort(signal);
                  if (performance.now() >= deadline) return null;
                }
              }
              for (let pixel = 0; pixel < pixels; pixel++) {
                const bit = pixel * depth;
                if (((current[bit >>> 3] >>> (8 - depth - (bit & 7))) & ((1 << depth) - 1)) >= palette) return null;
                if (++checked % 65536 === 0) {
                  await new Promise(resolve => setImmediate(resolve));
                  checkAbort(signal);
                  if (performance.now() >= deadline) return null;
                }
              }
              previous = current;
            }
            if (++checked % 65536 === 0) {
              await new Promise(resolve => setImmediate(resolve));
              checkAbort(signal);
              if (performance.now() >= deadline) return null;
            }
          }
        }
      } catch (error) { if (error.code === 'ABORT_ERR') throw error; return null; }
      return { width, height };
    } else {
      if (seenData) dataEnded = true;
      if (type === 'PLTE') {
        if (palette || seenData || !length || length > 768 || length % 3 || color === 0 || color === 4) return null;
        palette = length / 3;
        if (color === 3 && palette > (1 << depth)) return null;
      } else if (type === 'IHDR' || !/^[A-Za-z]{4}$/.test(type) || type[0] === type[0].toUpperCase()) return null;
    }
  }
  return null;
}

class AthClient {
  constructor({ port = DEFAULT_PORT, timeoutMs = DEFAULT_TIMEOUT_MS, pollIntervalMs = DEFAULT_POLL_MS } = {}) {
    this.port = validPort(port);
    this.timeoutMs = positive(timeoutMs, 'timeoutMs');
    this.pollIntervalMs = positive(pollIntervalMs, 'pollIntervalMs');
  }

  async command(command, { raw = false, timeoutMs = this.timeoutMs, signal } = {}) {
    const response = await sendCommand(this.port, raw ? '!raw ' + command : command, { timeoutMs, signal });
    if (!commandSucceeded(response, raw, command.trim().split(/\s+/, 1)[0]) && response.status !== 'failed') {
      response.status = 'failed';
      response.failReason = 'no_response';
    }
    return { ...response, fields: proto.terminalFields(response) };
  }

  async state(key, options = {}) {
    token(key, 'state key');
    return proto.readState(await this.command(`harness.state ${key}`, options));
  }

  async capabilities(options = {}) {
    const response = await this.command('harness.capabilities', options);
    if (!commandSucceeded(response)) throw new Error(response.fields.reason || response.failReason || 'capabilities unavailable');
    const value = JSON.parse(response.fields.value || 'null');
    if (!value || value.protocolVersion !== 1) throw new Error('unsupported ATH protocol version');
    if (typeof value.engine !== 'string' || typeof value.hostName !== 'string' ||
      !['capabilities', 'stateKeys', 'commands'].every(key => Array.isArray(value[key]) && value[key].every(item => typeof item === 'string')))
      throw new Error('malformed capabilities metadata');
    return value;
  }

  async version(options = {}) {
    const response = await this.command('harness.version', options);
    if (!commandSucceeded(response)) throw new Error(response.failReason || 'version unavailable');
    return response.fields.value ? JSON.parse(response.fields.value) : response.fields;
  }

  async execute(command, args = {}, options = {}) {
    token(command, 'command name');
    if (!args || typeof args !== 'object' || Array.isArray(args)) throw new TypeError('arguments must be a JSON object');
    const encoded = Buffer.from(JSON.stringify(args), 'utf8').toString('base64');
    return this.command(`harness.execute ${command} ${encoded}`, options);
  }

  async reset(scope, options = {}) {
    if (scope != null) token(scope, 'reset scope');
    return this.command(scope ? `harness.reset ${scope}` : 'harness.reset', options);
  }

  async wait(predicate, { timeoutMs = 30000, signal } = {}) {
    positive(timeoutMs, 'timeoutMs');
    const descriptor = proto.parsePredicate(predicate);
    token(descriptor.key, 'state key');
    const start = performance.now(), deadline = start + timeoutMs;
    let last = null;
    while (performance.now() < deadline) {
      checkAbort(signal);
      try {
        last = await this.state(descriptor.key, { timeoutMs: Math.max(1, Math.min(this.timeoutMs, deadline - performance.now())), signal });
      } catch (error) {
        if (error.code === 'ABORT_ERR') throw error;
        if (error.code === 'ETIMEDOUT' && performance.now() >= deadline - 2) break;
        return { predicate, result: 'fail', state: last, error: error.message, elapsedMs: Math.round(performance.now() - start) };
      }
      const verdict = proto.evalPredicate(descriptor, last);
      if (verdict === 'satisfied' || verdict === 'fail')
        return { predicate, result: verdict, state: last, elapsedMs: Math.round(performance.now() - start) };
      const remaining = deadline - performance.now();
      if (remaining > 0) await delay(Math.min(this.pollIntervalMs, remaining), signal);
    }
    return { predicate, result: 'timeout', state: last, elapsedMs: Math.round(performance.now() - start) };
  }

  /** Captures a runtime-produced PNG on the same filesystem. A complete PNG and
   * stable file size across polls are required before copying the evidence. */
  async screenshot(label = 'snap', { timeoutMs = 10000, outDir, signal } = {}) {
    positive(timeoutMs, 'timeoutMs');
    token(label, 'screenshot label');
    const deadline = performance.now() + timeoutMs;
    const response = await this.command(`harness.snap ${label}`, { timeoutMs: Math.min(this.timeoutMs, timeoutMs), signal });
    const source = response.fields.path;
    if (!commandSucceeded(response) || !source) return { status: 'failed', failReason: response.fields.reason || response.failReason || 'missing_path' };
    let lastSize = -1;
    while (performance.now() < deadline) {
      checkAbort(signal);
      try {
        const stat = fs.statSync(source);
        if (stat.isFile() && stat.size > 0 && stat.size <= MAX_SCREENSHOT_BYTES && stat.size === lastSize) {
          const fd = fs.openSync(source, 'r');
          let content;
          try {
            const buffer = Buffer.allocUnsafe(stat.size + 1);
            let count = 0, n;
            while (count < buffer.length && (n = fs.readSync(fd, buffer, count, buffer.length - count, null)) > 0) count += n;
            content = buffer.subarray(0, count);
          } finally { fs.closeSync(fd); }
          const info = content.length === stat.size ? await pngInfo(content, deadline, signal) : null;
          if (info) {
            checkAbort(signal);
            if (performance.now() >= deadline) break;
            let output = path.resolve(source);
            if (outDir) {
              fs.mkdirSync(outDir, { recursive: true });
              output = path.resolve(outDir, path.basename(source));
              if (output !== path.resolve(source)) fs.writeFileSync(output, content);
            }
            checkAbort(signal);
            if (performance.now() >= deadline) break;
            return { status: 'ok', path: output, bytes: content.length, ...info };
          }
        }
        lastSize = stat.size;
      } catch (error) {
        if (error.code !== 'ENOENT' && error.code !== 'EBUSY' && error.code !== 'EACCES') throw error;
      }
      const remaining = deadline - performance.now();
      if (remaining > 0) await delay(Math.min(this.pollIntervalMs, remaining), signal);
    }
    return { status: 'timeout', path: source };
  }
}

/** The shared runtime wire stays identical; only process startup differs. */
function buildLaunchArgs({ engine = 'unity', port, project, map, mediaDir, timeoutMs, nullRhi = false } = {}) {
  validPort(port);
  if (!['unity', 'unreal'].includes(engine)) throw new Error('engine must be unity or unreal');
  const args = [];
  if (engine === 'unreal') {
    if (project) args.push(project);
    if (map) args.push(map);
    if (project) args.push('-game');
    args.push('-unattended', '-nosplash');
    if (nullRhi) args.push('-nullrhi');
    args.push('-ath-remote-console', `-ath-port=${port}`);
    if (mediaDir) args.push(`-ath-media-dir=${mediaDir}`);
    if (timeoutMs != null) args.push(`-ath-timeout-ms=${positive(timeoutMs, 'timeoutMs')}`);
  } else {
    args.push('-ath-remote-console', '-ath-port', String(port));
    if (mediaDir) args.push('-ath-media-dir', mediaDir);
    if (timeoutMs != null) args.push('-ath-timeout-ms', String(positive(timeoutMs, 'timeoutMs')));
  }
  return args;
}

module.exports = { AthClient, sendCommand, buildLaunchArgs, commandSucceeded, pngInfo };
