# Captain SDLC — Tools

Prototype tooling for the cross-tool layer. The nerve-center repo owns these
until that layer graduates into its own tool (see `../trace-schema.md` open
question #1).

## captain-trace

The minimal Seam-1 trace **consumer** (the schema's "minimal first cut"). Reads a
project's append-only `.captain-sdlc/trace/*.jsonl` and answers the envelope's
priority questions:

```
node captain-trace.mjs walk <event_id>    # causal chain: event → parents → …
node captain-trace.mjs by-commit <sha>     # every event recorded against a commit
node captain-trace.mjs ls [--limit <n>]    # recent events, newest last
```

`--dir <path>` points at the directory holding the `.jsonl` files (default
`./.captain-sdlc/trace`). Read-only, zero dependencies. Lines with an unknown
`schema_version` or malformed JSON are warned-and-skipped to stderr, never
guessed at — the consumer-side `trace.schema.unknown` posture.

Pairs with ATH's `ath-trace-emit` (the first emitter, milestone M2). `walk`'s
`parents` chains light up automatically once an upstream emitter (e.g.
claude-release `code.commit.created`) populates them; today's
`ath.smoke.completed` events have no parents yet, so `walk` shows just the one
event.
