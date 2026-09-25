---
name: ath-exe-smoke
description: Retired manual EXE smoke template. Use a declared RuntimeSmoke FSD workflow for acceptance.
version: 0.3.0
---

# Retired: manual EXE smoke template

This template's manual `ath-exe launch/cmd/state/wait/snap` sequence is not a smoke acceptance path. Keep the EXE remote console for focused diagnostics only.

For built-player acceptance, author a host-owned RuntimeSmoke FSD workflow and launch that workflow with the structured AITH runner:

```powershell
node Tools~/ath-exe-client/ath-exe.js runtime-smoke path/to/launch-spec.json
```

The launch spec must bind the workflow ID and `canonicalScriptHash` from the host catalog/compiler (the compiled expanded workflow identity, not a raw authored-file hash) to the exact development or `smoke_harness` artifact and its build-evidence record, identify a fresh run and correlation, declare the ordered required step IDs and deadline, and keep output under a new run-local directory. The runner accepts only matching launch/armed/process/transcript/result evidence and a clean process exit. See [RuntimeSmoke usage and authoring](../../Documentation~/runtime-smoke-usage.md), [the complete transport contract](../../Documentation~/runtime-smokes.md), and [EXE transport details](../../Documentation~/exe-harness.md).

The consuming project's verifier/build gate must bind the passing suite result to the exact artifact before approving it for human testing. The AITH package alone does not prove consumer integration or build-gate completion.
