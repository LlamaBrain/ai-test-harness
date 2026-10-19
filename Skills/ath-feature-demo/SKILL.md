---
name: ath-feature-demo
description: Reference for collecting optional visual review artifacts alongside a host-owned RuntimeSmoke workflow; it does not define smoke acceptance.
version: 0.6.0
---

# Optional visual review artifacts

Use the consuming host's checked-in RuntimeSmoke workflow and declared human review checkpoint. This reference has no independent PASS criteria and does not replace RuntimeSmoke results or human acceptance.

At that checkpoint, optional Editor tools can collect review media:

- ath-snap capture requests a Game-view PNG; query its CaptureId until the receipt is ready.
- ath-record start/stop/query brackets a demonstration when Unity Recorder is installed. Query until the output is finalized. Without Recorder, the tool reports recorder_unavailable; retain that limitation.
- Both return trace-relative media paths. Preserve the real capture/source association with the host's evidence. Legacy ath-trace-emit can append diagnostic context, but its caller-supplied result cannot establish smoke or build acceptance.

Retain PlayMode and the staged review state for the human's real inputs. A recording is evidence for review; only actual human feedback accepts a declared human checkpoint.

See ../../Documentation~/runtime-smoke-usage.md and ../../Documentation~/trace-events.md.
