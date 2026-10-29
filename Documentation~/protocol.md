# Shared remote protocol

Protocol version 1 preserves the existing ATH remote-console format. Engines may
add commands and state providers without changing transport framing.

## Requests and responses

Send one UTF-8 command followed by `\n` to loopback TCP port 8787 by default.
Each exchange opens a connection; reconnecting does not create a new runtime
session or clear sticky state. Command strings cannot contain embedded CR/LF/NUL.

```text
harness.ping\n
```

```json
{"correlationId":"abc123","status":"success","failReason":"","lines":["CMD:harness.ping id=abc123","OK:harness.ping id=abc123 pong=true"],"elapsedMs":1,"truncated":false}
```

`status` is `success`, `dispatched` or `failed`; `failReason` is a stable
diagnostic such as `busy`, `bad_request`, `command_error` or `no_response`.
`lines` contains raw sentinels, not arbitrary engine logs. `elapsedMs` measures
the response exchange; it does not imply an asynchronous task has completed.
`truncated` reports bounded capture. Clients must inspect command terminals and
must not turn a missing terminal into a successful empty state value.

The server supplies the correlation ID for each incoming legacy command. The
client selects the matching `OK:` or `ERR:` terminal; ERR wins when both exist.
Sentinel values are either bare tokens or quoted strings. Inside quotes,
backslash, quote and newline escape as `\\`, `\"` and `\n`; CR is removed.
JSON escaping is a separate outer layer. Reuse `protocol.js` to parse both layers.

## Additive discovery and execution

| Command | Behavior |
|---|---|
| `harness.ping` | `pong=true` round trip. |
| `harness.version` | Version/protocol discovery. |
| `harness.capabilities` | `value="<escaped JSON>"` metadata. |
| `harness.state <key>` | Read-only named state; `status=ok\|not_ready`, `value="..."`. |
| `harness.execute <registered-name> <base64-json>` | Execute a registered host command with a UTF-8 JSON object. |
| `harness.reset` | Clear edge-sticky events. |
| `harness.snap <label>` | Request PNG capture; return `status=pending` and a path. |

Capability JSON contains `protocolVersion:1`, `engine`, `hostName`, and string
arrays `capabilities`, `stateKeys`, `commands`. Additional fields are optional.
The shared client rejects unknown protocol versions and malformed required fields.
Old Unity runtimes without discovery remain usable for legacy cmd/state/wait
operations; lack of discovery does not imply support for new capabilities.

`harness.execute` carries a JSON object encoded as base64 to avoid engine-specific
console quote/token rules. It does not execute arbitrary source code. Engines
validate the registered name, argument object and request bounds.

## State and predicates

Unknown keys fail immediately. Temporarily unavailable state returns `not_ready`.
Other unsupported/error states cannot satisfy equality with an empty string.

| Predicate | Meaning |
|---|---|
| `<key>` | Wait for literal string `true`. |
| `state_equals:<key>=<value>` or `<key>=<value>` | Exact invariant-text equality. |
| `event:<name>` | Sticky named event, exposed as a boolean state. |
| `async_done:<correlation-id>` | Poll `async:<id>` until `done`. |
| `scene_loaded:<name>` | Legacy `scene_name` equality. |
| `spawn_attempts_at_least:<n>` | Preserved legacy numeric predicate. |

Legacy aliases such as `player_died` remain supported. Generic tests should use
registered state/event names. An asynchronous completion may have failed:
`async_done` means completion, not success. Inspect completion diagnostics and
`async_success:<id>` where advertised. The bounded history may evict old IDs.

Waits execute in the shared client, which repeatedly performs read-only state
queries. A wait deadline bounds both polling and each in-flight request. Socket
failure, unknown keys, timeout and cancellation remain distinct results.

Screenshot completion is two-phase: successful dispatch supplies a local path;
the client waits for a stable, complete PNG before copying evidence. Rendering
must be available. NullRHI/headless tests must not report a screenshot pass.
