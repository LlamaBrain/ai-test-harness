# AITH skill references

RuntimeSmoke is the supported smoke orchestration and acceptance practice. A host authors checked-in RuntimeSmoke workflow JSON and runs it through the host bridge using the Editor window, typed MCP tools, or the structured `ath-exe runtime-smoke` runner. See [RuntimeSmoke usage and authoring](../Documentation~/runtime-smoke-usage.md) and the [RuntimeSmoke contract](../Documentation~/runtime-smokes.md).

The procedural smoke documents in this directory predate RuntimeSmoke and are retained only as historical implementation context. They are not active test instructions, must not be copied into a host as acceptance workflows, and cannot establish PASS through command output, state polling, screenshot presence, or trace emission. Host repositories should remove or replace copied versions as part of their own documented migration; that consumer work is not performed by this package.

Low-level command usage may still be useful for a narrowly scoped diagnostic. Label such a procedure diagnostic-only, link the relevant host RuntimeSmoke workflow, and keep its outcome distinct from the workflow's structured result. Do not add new procedural smoke skills or templates.

## Retired package documents

- `ath-smoke-fullloop/SKILL.md`: historical BTS-specific command-driven procedure, retained only as investigation context. It does not establish current BTS workflow coverage or acceptance; consult BTS-owned workflow docs and run evidence for that status.
- `ath-exe-smoke/SKILL.md`: historical manual EXE command/wait template, retained only as investigation context. Built-player acceptance uses the canonical `ath-exe runtime-smoke` or `runtime-smoke-suite` runner with a declared host workflow and exact-artifact evidence; the retired file is not its authoring guide.

Optional capture reference: [ath-feature-demo](ath-feature-demo/SKILL.md) describes media collection alongside a host-owned checkpoint; it defines no independent smoke verdict.
