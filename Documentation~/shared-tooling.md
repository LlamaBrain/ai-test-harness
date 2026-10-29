# Shared CLI, smoke runner and MCP

The Node tooling runs without Unity or Unreal installed. Node 18 or newer is
required.

The tool directory is self-contained: its CLI/MCP/report versions resolve from
its own `package.json`, so distributing that directory does not require the
parent Unity UPM package descriptor.

Install it independently of either engine integration:

```sh
cd <ath>/Tools~/ath-exe-client
npm link
ath version
npm test
npm run check
```

`ath` and `ath-exe` use the same entrypoint. Direct invocation remains
`node <ath>/Tools~/ath-exe-client/ath-exe.js <subcommand> ...`; existing scripts
using that path continue to work. No engine is required for client tests or trace
emission. A live runtime is required for commands, state, waits and screenshots.

## Runtime operations

```sh
ath attach 8787
ath cmd "harness.ping" --port 8787
ath capabilities --port 8787
ath state ath.fixture.ready --port 8787
ath cmd "ath.fixture.increment" --port 8787
ath wait "state_equals:ath.fixture.counter=1" --port 8787 --timeout 5000
ath wait "event:ath.fixture.changed" --port 8787 --timeout 5000
ath snap fixture --port 8787 --out <project>/.captain-sdlc/trace/media
```

Command exit status is nonzero for a failed response. Waits distinguish
`satisfied`, `timeout` and `fail`; a failed unknown key returns promptly rather
than consuming the timeout. The JSON output retains diagnostic state. Use
`--timeout-ms` for command exchange bounds and `--timeout` for wait/snap bounds.

Explicit host mutations use `ath execute <registered-name> <json-object>`.
For legacy Unity commands with only a bare console overload,
`ath cmd --raw "<command>"` preserves existing raw dispatch. Such dispatch
provides no captured terminal; read state to establish the result.

`--port` takes precedence over local `ProjectSettings/AthRemote.json` (Unity) or
`Config/AthRemote.json` (Unreal), then defaults to 8787. Use distinct explicit
ports when multiple engine sessions run at once.

## Launch

```sh
ath launch <UnityPlayer.exe> --engine unity --port 8787 --detach
ath launch <UnrealEditor.exe> --engine unreal --project <Game.uproject> --map /Game/System/FrontEnd/Maps/L_LyraFrontEnd --port 8788 --detach
ath launch <PackagedGame.exe> --engine unreal --port 8789 --detach
```

The Lyra map above is an acceptance-host example; other projects select their own
map or use their default. Unreal Editor plus `--project` starts standalone
`-game`. Start normal Editor/PIE separately when testing PIE lifecycle.
`--null-rhi` selects headless nonvisual Unreal execution. A launch without an
explicit port chooses a free port and reports it; pass the reported port to
subsequent commands. `attach` performs a connectivity check; it does not persist
a global selected session. `--detach` leaves a successfully launched process
running. See [unreal.md](unreal.md) for installation and execution modes.

## Smoke specifications

```json
{
  "name": "counter-smoke",
  "engine": "unreal",
  "requires": ["state", "commands", "events"],
  "steps": [
    {"type":"command","command":"ath.fixture.reset"},
    {"type":"command","command":"ath.fixture.increment"},
    {"type":"wait","predicate":"state_equals:ath.fixture.counter=1","timeoutMs":5000},
    {"type":"wait","predicate":"event:ath.fixture.changed","timeoutMs":5000},
    {"type":"assert","key":"ath.fixture.active","equals":"true"}
  ]
}
```

Supported steps are `command`, `execute`, `assert`, `wait`, and `screenshot`.
Every step may have an explanatory `name`; the first failed step stops execution
and remains in the result. Dispatch alone does not establish asynchronous success:
follow it with a correlated completion wait and success assertion.

```sh
ath smoke <ath>/Tools~/ath-exe-client/fixture-smoke.json --port 8787 --project-root <consuming-project> --trace --rendered
```

The runner emits on pass and fail when `--trace` is set, retaining a detailed
JSON run report and any screenshots under the consuming project's
`.captain-sdlc/trace/`. `--out <path>` additionally writes the result at a requested
location. It is not a replacement for the trace/report artifacts. Use explicit
`--project-root`; never point evidence at the ATH package directory.

An existing result can be emitted without an engine or Editor:

```sh
ath trace <result.json> --project-root <consuming-project> --project-name <project-id> --commit <revision>
```

See [trace-events.md](trace-events.md) for the stable Captain envelope and artifact
path restrictions. The Kinecade acceptance runner in `Tools~/unreal-acceptance/`
is a separate consumer-specific workflow; the shared package has no dependency
on it.

The fixture specification has 18 nonvisual steps. `--rendered` additionally
requires screenshot capability and a valid PNG. Omit it only for a separately
identified nonvisual run. A step with `id:"operation"` makes its result available
to later steps as `${operation.correlationId}`, for example
`async_done:${operation.correlationId}` followed by an
`async_success:${operation.correlationId}` state assertion.

## MCP

The standalone Node MCP server uses the same client for `ath-cmd`, `ath-state`,
`ath-wait`, `ath-snap`, and `ath-trace-emit`. It runs over stdio and writes only
JSON-RPC traffic to stdout. It requires no Unreal-specific MCP implementation.
Configure its endpoint port and consuming-project evidence root explicitly; see
the server's startup options and [unreal.md](unreal.md).

```json
{
  "mcpServers": {
    "ath-unreal": {
      "command": "node",
      "args": ["<ath>/Tools~/ath-exe-client/mcp-server.js", "--port", "8787", "--project-root", "<consuming-project>", "--prefix", "unreal-"]
    }
  }
}
```

The equivalent environment settings are `ATH_PORT`, `ATH_PROJECT_ROOT` and
`ATH_MCP_PREFIX`. The default prefix is empty; the example registers
`unreal-ath-cmd`, `unreal-ath-state`, `unreal-ath-wait`, `unreal-ath-snap`,
`unreal-ath-capabilities` and `unreal-ath-trace-emit`. Startup does not require an
engine session; runtime tool calls require the endpoint. Trace emission uses the
configured consuming-project root. The stdio server implements MCP protocol
`2025-03-26`; its transport/lifecycle follows the official
[MCP transport](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports)
and [lifecycle](https://modelcontextprotocol.io/specification/2025-03-26/basic/lifecycle)
specifications.

Keep an existing Unity MCP connection when you need Unity Editor controls or
Recorder. To avoid ambiguous tool registration, register either the shared ATH
tool names or the Unity MCP ATH tools for a given endpoint. When operating both,
use the shared server's tool-name prefix option to distinguish the endpoint.
Tool inputs and results remain engine-independent; capability discovery tells
the agent which optional runtime operations are available.
