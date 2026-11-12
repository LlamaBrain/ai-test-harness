#!/usr/bin/env node
'use strict';

// Standalone MCP stdio adapter. Runtime operations share the exact CLI client.
// No engine SDK, HTTP listener, shell dispatch or dynamically loaded game code.
const path = require('node:path');
const { AthClient } = require('./client');
const { emitTrace } = require('./report');
const VERSION = require('./package.json').version;
const PROTOCOL_VERSION = '2025-03-26';
const objectSchema = (properties, required = []) => ({ type: 'object', properties, required, additionalProperties: false });
const string = { type: 'string' };
const timeout = { type: 'number', exclusiveMinimum: 0, maximum: 600000 };

function createMcpServer({ client = new AthClient(), projectRoot, prefix = '', maxInputBytes = 65536, maxConcurrent = 8 } = {}) {
  if (typeof prefix !== 'string' || !/^[A-Za-z0-9_-]*$/.test(prefix)) throw new Error('MCP prefix must contain only letters, numbers, underscores or hyphens');
  if (!Number.isInteger(maxInputBytes) || maxInputBytes < 1) throw new Error('maxInputBytes must be positive');
  if (!Number.isInteger(maxConcurrent) || maxConcurrent < 1 || maxConcurrent > 64) throw new Error('maxConcurrent must be between 1 and 64');
  const tools = [
    { name: 'ath-cmd', description: 'Execute a registered ATH runtime command; inspect the correlated structured result.',
      inputSchema: objectSchema({ command: string, raw: { type: 'boolean' }, timeoutMs: timeout }, ['command']) },
    { name: 'ath-state', description: 'Read a named, side-effect-free ATH runtime state.',
      inputSchema: objectSchema({ key: string, timeoutMs: timeout }, ['key']) },
    { name: 'ath-wait', description: 'Wait for an ATH predicate, sticky event, or asynchronous completion within a deadline.',
      inputSchema: objectSchema({ predicate: string, timeoutMs: timeout }, ['predicate']) },
    { name: 'ath-snap', description: 'Capture a rendered runtime PNG and wait for the completed file. Requires a local shared filesystem.',
      inputSchema: objectSchema({ label: string, timeoutMs: timeout }) },
    { name: 'ath-capabilities', description: 'Discover the engine, host, supported operations and registered states/commands.',
      inputSchema: objectSchema({ timeoutMs: timeout }) },
    { name: 'ath-trace-emit', description: 'Append an ath.smoke.completed Captain schema v1 event to the configured consuming project.',
      inputSchema: objectSchema({ result: { type: 'string', enum: ['pass', 'fail'] }, skill: string,
        summary: string, failedStep: string, artifacts: { type: 'array', items: string },
        engine: string, engineVersion: string, buildMode: string, elapsedMs: { type: 'number', minimum: 0 },
        commit: string, taskId: string, designDoc: string }, ['result', 'skill']) },
  ].map(tool => ({ ...tool, name: prefix + tool.name }));
  const byName = new Map(tools.map(tool => [tool.name, tool]));
  const pending = new Map();
  let initialized = false;
  let stopped = false;
  const rpcError = (id, code, message) => ({ jsonrpc: '2.0', id: id ?? null, error: { code, message } });
  const success = (id, result) => ({ jsonrpc: '2.0', id, result });

  function validate(args, schema) {
    if (!args || typeof args !== 'object' || Array.isArray(args)) throw new Error('tool arguments must be an object');
    for (const key of schema.required) if (!Object.hasOwn(args, key)) throw new Error(`missing argument: ${key}`);
    for (const [key, value] of Object.entries(args)) {
      const definition = schema.properties[key];
      if (!definition) throw new Error(`unknown argument: ${key}`);
      if (definition.type === 'array') {
        if (!Array.isArray(value) || value.some(item => typeof item !== definition.items.type)) throw new Error(`${key} must be a string array`);
      } else if (typeof value !== definition.type || definition.type === 'string' && !value.length) throw new Error(`${key} must be a nonempty ${definition.type}`);
      if (definition.enum && !definition.enum.includes(value)) throw new Error(`invalid ${key}`);
      if (definition.type === 'number' && (!Number.isFinite(value) || value < (definition.minimum ?? 0) ||
        definition.exclusiveMinimum != null && value <= definition.exclusiveMinimum || value > (definition.maximum ?? Number.MAX_VALUE))) throw new Error(`invalid ${key}`);
    }
  }

  async function call(name, args, signal) {
    const options = { timeoutMs: args.timeoutMs, signal };
    switch (name.slice(prefix.length)) {
      case 'ath-cmd': return client.command(args.command, { ...options, raw: args.raw === true });
      case 'ath-state': return client.state(args.key, options);
      case 'ath-wait': return client.wait(args.predicate, options);
      case 'ath-capabilities': return client.capabilities(options);
      case 'ath-snap': return client.screenshot(args.label || 'mcp', { ...options,
        outDir: projectRoot ? path.join(projectRoot, '.captain-sdlc', 'trace', 'media') : undefined });
      case 'ath-trace-emit':
        if (!projectRoot) throw new Error('ATH_PROJECT_ROOT or --project-root must select the consuming project before trace emission');
        return emitTrace(projectRoot, { ...args, name: args.skill }, args);
      default: throw new Error('unknown ATH operation');
    }
  }

  async function handle(message) {
    if (Array.isArray(message)) {
      if (!message.length || message.length > maxConcurrent) return rpcError(null, -32600, 'invalid or oversized JSON-RPC batch');
      const responses = [];
      for (const item of message) { const response = await handle(item); if (response) responses.push(response); }
      return responses.length ? responses : null;
    }
    if (!message || message.jsonrpc !== '2.0' || typeof message.method !== 'string' ||
      Object.hasOwn(message, 'id') && typeof message.id !== 'number' && typeof message.id !== 'string') return rpcError(message?.id, -32600, 'invalid JSON-RPC request');
    const { id, method, params = {} } = message;
    if (id == null) {
      if (method === 'notifications/cancelled') pending.get(params.requestId)?.abort();
      return null;
    }
    if (stopped) return rpcError(id, -32000, 'ATH MCP server is stopping');
    if (method === 'initialize') {
      if (typeof params?.protocolVersion !== 'string') return rpcError(id, -32602, 'protocolVersion is required');
      initialized = true;
      return success(id, { protocolVersion: PROTOCOL_VERSION, capabilities: { tools: { listChanged: false } },
        serverInfo: { name: 'ath', version: VERSION }, instructions: 'ATH runtime tools target one configured loopback endpoint. Use a unique --prefix when Unity MCP also registers ath tools.' });
    }
    if (method === 'ping') return success(id, {});
    if (!initialized) return rpcError(id, -32002, 'initialize first');
    if (method === 'tools/list') return success(id, { tools });
    if (method !== 'tools/call') return rpcError(id, -32601, 'method not found');
    const definition = byName.get(params?.name);
    if (!definition) return rpcError(id, -32602, 'unknown tool');
    if (pending.has(id)) return rpcError(id, -32600, 'duplicate in-flight request id');
    if (pending.size >= maxConcurrent) return rpcError(id, -32000, 'ATH MCP concurrency limit reached');
    const controller = new AbortController();
    pending.set(id, controller);
    try {
      const args = params.arguments ?? {};
      validate(args, definition.inputSchema);
      const value = await call(params.name, args, controller.signal);
      const isError = ['failed', 'error', 'unknown_key', 'timeout', 'unsupported'].includes(value?.status) || ['fail', 'timeout'].includes(value?.result);
      return success(id, { content: [{ type: 'text', text: JSON.stringify(value) }], isError });
    } catch (error) {
      return success(id, { content: [{ type: 'text', text: JSON.stringify({ status: 'error', error: error.message }) }], isError: true });
    } finally { pending.delete(id); }
  }

  function close() { stopped = true; for (const controller of pending.values()) controller.abort(); }

  /** Newline UTF-8 JSON-RPC, with bounded unfinished records and active calls.
   * Notifications are read while tools execute so cancellation can interrupt waits. */
  function listen(input = process.stdin, output = process.stdout) {
    return new Promise((resolve, reject) => {
      let buffer = '', discarding = false;
      const inflight = new Set();
      const write = message => { if (message) output.write(JSON.stringify(message) + '\n'); };
      const dispatch = line => {
        let message;
        try { message = JSON.parse(line); }
        catch { write(rpcError(null, -32700, 'invalid JSON')); return; }
        if (inflight.size >= maxConcurrent && message?.id != null) { write(rpcError(message.id, -32000, 'ATH MCP concurrency limit reached')); return; }
        const operation = handle(message).then(write).catch(error => write(rpcError(message?.id, -32603, error.message)));
        inflight.add(operation);
        operation.finally(() => inflight.delete(operation));
      };
      input.setEncoding('utf8');
      input.on('data', chunk => {
        buffer += chunk;
        let newline;
        while ((newline = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, newline);
          buffer = buffer.slice(newline + 1);
          if (!discarding && Buffer.byteLength(line, 'utf8') <= maxInputBytes) { if (line.trim()) dispatch(line); }
          else if (!discarding) write(rpcError(null, -32700, 'MCP input too large'));
          discarding = false;
        }
        if (Buffer.byteLength(buffer, 'utf8') > maxInputBytes) {
          if (!discarding) write(rpcError(null, -32700, 'MCP input too large'));
          buffer = '';
          discarding = true;
        }
      });
      input.on('end', () => {
        if (buffer.trim() && !discarding) write(rpcError(null, -32700, 'unterminated MCP record'));
        close();
        Promise.allSettled(inflight).then(resolve);
      });
      input.on('error', error => { close(); reject(error); });
      output.on('error', error => { close(); reject(error); });
    });
  }
  return { handle, listen, close };
}

if (require.main === module) {
  try {
    const { flags } = require('./ath-exe').parseArgv(process.argv.slice(2));
    createMcpServer({ client: new AthClient({ port: Number(flags.port || process.env.ATH_PORT || 8787) }),
      projectRoot: flags['project-root'] || process.env.ATH_PROJECT_ROOT,
      prefix: flags.prefix || process.env.ATH_MCP_PREFIX || '' }).listen().catch(error => {
      process.stderr.write(`ath-mcp: ${error.message}\n`); process.exitCode = 1;
    });
  } catch (error) { process.stderr.write(`ath-mcp: ${error.message}\n`); process.exitCode = 1; }
}

module.exports = { createMcpServer };
