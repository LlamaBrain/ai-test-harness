---
name: ath-smoke-fullloop
description: Retired procedural smoke reference. Do not use for smoke acceptance.
version: 0.3.0
---

# Retired: procedural full-loop smoke

The historical BTS-specific command/wait procedure is retired as an acceptance path. This notice preserves its context; the previous procedure remains in Git history and no longer defines setup, assertions, pass criteria, or a supported current workflow.

The AITH package does not prescribe a replacement BTS workflow or establish its validation status. Consult BTS-owned workflow documentation and run evidence for current consumer coverage. Discover registered host workflows through that host's RuntimeSmoke Editor window or `ath-runtime-smoke-list`; do not use this retired procedure as acceptance.

See [RuntimeSmoke usage and authoring](../../../Documentation~/runtime-smoke-usage.md). A sequence of `ath-cmd`, `ath-state`, `ath-wait`, or `ath-trace-emit` calls is diagnostic only and cannot establish smoke PASS.
