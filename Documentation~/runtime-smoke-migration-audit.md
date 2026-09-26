# AITH RuntimeSmoke documentation migration audit

**Audit date:** 2026-09-25
**Audited source revision:** `e7188c30c6a799fbab065a46ade2d52ab0c7e061` (`feat/runtime-smoke-platform-next`) plus the Phase 7 package-version/reference corrections in this commit.
**Scope:** AITH package README, `ROADMAP.md`, `Documentation~/`, `Skills/`, the Minimal Host Adapter sample and its asmdef, relevant package metadata and source comments, and `Tools~/ath-exe-client/` command help/source. BTS and Dirigible files, dependency manifests/lockfiles, Unity projects, and runtime behavior are outside this documentation audit.

RuntimeSmoke is the sole supported smoke orchestration and acceptance practice in AITH guidance. The structured run result and transcript establish the workflow outcome. Low-level command transports and legacy trace payloads remain available for bounded diagnostics/history; they cannot establish, override, or approve a smoke result. This AITH package audit does not complete the cross-repository Phase 7 migration or prove a consumer integration.

## Follow-up verification — 2026-09-26

The maintained AITH documentation, API references, generated-skill retirement notices, command help, and minimal-host examples were re-audited at commit 3ff124842e6388ab85aa76c6c9f98b30280802d3 (AITH 0.5.1). No new AITH-scope conflict was found. The audit above remains the original 2026-09-25 change record at 7188c30c6a799fbab065a46ade2d52ab0c7e061; this follow-up binds the current package state without changing that historical provenance. BTS and Dirigible documentation/example reproduction remain separate consumer gates.

## Affected AITH sources and disposition

| Source | Finding before migration | Disposition in this AITH change |
|---|---|---|
| `README.md` | Described command tools and Claude smoke skills as the harness's smoke pipeline; its status language could imply consumer dogfood proved the current RuntimeSmoke implementation. | RuntimeSmoke is the supported acceptance contract. The package now describes its compiler, assertions, coordinator, Editor window, MCP operations, and EXE runner as implemented; consumer registration and Editor/player/review/build-gate status remain delegated to evidence owned by each host. Older transports and skills are diagnostic/retired. |
| `Documentation~/using-ath.md` | Described per-step command/wait authoring as a canonical smoke skill and instructed authors to create new smoke skills with manual PASS criteria. | Added an acceptance boundary; reclassified the command loop as diagnostic; replaced procedural-smoke authoring instructions with checked-in host RuntimeSmoke workflow guidance. |
| `Documentation~/exe-harness.md` | Direct `ath-exe launch/cmd/state/wait/snap` sequence could be read as built-player smoke acceptance. | Added a prominent boundary and link to `ath-exe runtime-smoke`; direct EXE operations are bounded diagnostics only. Existing transport and build setup details remain. |
| `Documentation~/trace-events.md` | Said skills compute smoke verdicts and emit one `ath.smoke.completed` pass/fail at the end. | Reclassified `ath-trace-emit` as an ancillary trace utility; its legacy `result` is caller context, not acceptance evidence. The old sample is explicitly historical. |
| `Documentation~/runtime-smokes.md` | API reference existed without a clear operator/authoring entrypoint. It already recorded pending consumer and human-review gates. | Linked the canonical usage guide and retained the pending integration caveats. |
| `Documentation~/runtime-smoke-usage.md` | No authoritative end-to-end AITH usage guide existed. | Added host-neutral discovery, Editor window/MCP, workflow/fragment, assertion, mode/checkpoint, suite/build-player, containment, evidence, failure investigation, and fresh-correlation guidance. It identifies host-owned work and explicitly marks unproved integration gates. |
| `Skills/README.md` | Advertised all procedural skills as smoke tests orchestrating command/state/wait tools. | Replaced with the RuntimeSmoke policy and an explicit retired-reference inventory. |
| `Skills/ath-smoke-fullloop/SKILL.md` | Detailed command/wait sequence contained independent setup, assertion, and PASS guidance for a BTS smoke. | Replaced with a non-runnable retirement notice linking to the host-owned RuntimeSmoke workflow and noting that AITH does not claim consumer replacement proof. |
| `Skills/ath-exe-smoke/SKILL.md` | Manual direct-EXE sequence and locally computed PASS criteria bypassed the structured artifact runner. | Replaced with a non-runnable retirement notice directing acceptance to the declared FSD workflow and exact-artifact RuntimeSmoke runner. |
| `Tools~/ath-exe-client/ath-exe.js` | CLI subcommand comments listed direct commands alongside `runtime-smoke` without explaining their authority difference. | Source help comment now identifies `runtime-smoke` as the sole acceptance operation and other subcommands as diagnostic-only. |
| `Documentation~/adapter-contract.md` | Described adapter mutations as mappings to console commands and attributed edge-flag race resistance to skill authors using `ath-wait`. | Clarified that `Request*` is an adapter mutation surface, not a workflow/acceptance definition; scoped command polling to diagnostics and documented host RuntimeSmoke predicate reads separately. |
| `Samples~/MinimalHostAdapter/README.md`, `MinimalAthAdapter.cs` | Referred to a Phase 4b dry-run and described `sample_marker` as a smoke signal; did not distinguish sample asmdef isolation from whole-package dependency requirements. | Reconciled with planned Phase 8, marked clean-project compilation/registration as unverified, labeled sample state diagnostic-only, and documented package peer-dependency setup. |
| `Runtime/AthRuntimeFlag.cs`, `Runtime/AthAsyncOpRecord.cs` | Source comments described retired skill version pinning and skill-authored command/wait flows. | Reworded as package-version metadata and low-level diagnostics; explicitly excluded those calls from RuntimeSmoke acceptance. |
| `package.json` | Package and sample descriptions implied skills were active authoring tools and that the fresh-project dry-run had been used. | Descriptions now identify skill files as historical, identify the Phase 8 boundary check as planned, and state that its clean-project Unity proof is not yet verified. |
| `Skills/README.md` | Its retired EXE entry did not name the public suite runner and could be mistaken for current runner authoring guidance. | Identifies both public EXE runner commands and states the retired skill file is not their authoring guide. |

## Reference audit

Searched the AITH `README.md`, all Markdown under `Documentation~/` and `Skills/`, and Markdown/JavaScript under `Tools~/` for: `agent-orchestrated`, `skill drives`, `computes the verdict`, `Writing a new smoke skill`, `PASS criteria`, smoke-skill advertising, direct EXE command sequences, and RuntimeSmoke acceptance language.

The following direct command examples remain intentionally scoped diagnostics: the command troubleshooting/cheat-sheet examples in `using-ath.md` and direct EXE examples in `exe-harness.md`. Both documents now have explicit boundaries stating those sequences cannot establish smoke PASS. The historical `ath.smoke.completed` schema/sample remains for trace-history compatibility and is labeled non-acceptance. The two package skill paths remain as retirement notices so old copies have an explicit supersession pointer; neither contains a runnable smoke procedure or PASS checklist.

Maintained host-specific plans, ADRs, project instructions, copied consumer skills, and actual AITH-host examples are outside this AITH-only change and remain required audit work in BTS and Dirigible. In particular, this package edit does not validate a live Editor MCP workflow, checkpoint/resume behavior in a consuming game, failure-context export there, or an exact-artifact required-suite gate.


### Roadmap follow-up — 2026-09-24

A targeted scan found that `ROADMAP.md` still presented a traversal smoke skill, a cross-host skill template, and skill-centric cross-cutting notes as future/current guidance. The roadmap now identifies itself as a historical snapshot and points operators to the RuntimeSmoke usage guide and API contract. The earlier extraction proposal is marked superseded; Phase 8 is described as an adapter boundary check and explicitly not acceptance evidence. Host-specific traversal coverage, if still wanted, belongs in that host’s declared RuntimeSmoke workflow. This edit does not claim the clean-project Phase 8 boundary check or complete every old roadmap item.

### README, skill, and BTS-status follow-up — 2026-09-24

The README now describes the AITH RuntimeSmoke surfaces as implemented while keeping consumer integration and acceptance status host-owned. The retired BTS skill explicitly directs readers to BTS-owned workflow documentation and run evidence; this package does not duplicate a consumer result or claim broader BTS acceptance. The retained skill index identifies the procedural files as historical references, not current host coverage or PASS instructions.

### Adapter contract, sample boundary, and package metadata follow-up — 2026-09-24

The adapter contract and sample/source comments now distinguish diagnostic polling from a host's typed RuntimeSmoke workflow. The sample's asmdef references only `LlamaBrainLabs.Ath.Runtime`, but no clean-project Phase 8 Unity compile/registration result is recorded in this AITH worktree. The package manifest declares Unity MCP and the README identifies `IngameDebugConsole.Runtime` as a required peer dependency; both must be available for whole-package compilation, while the sample asmdef itself remains Runtime-only. No clean-project proof or consuming-host status is inferred from that static boundary.
## Checks and evidence boundary

- `node --test Tools~/ath-exe-client/*.test.js` — **53 passed, 0 failed, 0 skipped** (2026-09-24).
- Internal Markdown target scan over 12 package/Documentation~/Skills Markdown files — **no missing local targets** (2026-09-24). External URLs and heading-anchor existence were not network-validated.
- Follow-up local Markdown link scan over `README.md`, `ROADMAP.md`, `Skills/README.md`, both retired skills, this audit, adapter contract, and sample README — **all local targets found** (2026-09-24). External URLs and heading anchors were not network-validated.
- `package.json` JSON parse — **valid** (2026-09-24).
- `git diff --check` — **passed** (2026-09-24); Git emitted only line-ending normalization warnings for edited Markdown/JS sources.
- Package version contract test — **1 passed**; full `node --test Tools~/ath-exe-client/*.test.js` — **68 passed, 0 failed, 0 skipped** (2026-09-25).

This AITH audit does not complete the cross-repository Phase 7 migration or prove a consuming-host integration. Unity compilation, the Phase 8 clean-project boundary check, consumer Editor/player/review evidence, and the final exact-artifact suite remain owned by their respective gates.

### Phase 7 package API/reference follow-up — 2026-09-25

The release metadata audit found that `VERSION` and `AthRuntimeFlag.PackageVersion` still reported 0.3.0 while `package.json` was 0.4.0. This commit bumps the package to 0.4.1 and synchronizes those public version surfaces, the README status/install tag, and the changelog. A focused Node contract test now guards the three version sources. Version strings in retired skill frontmatter and historical trace examples remain historical caller/sample data; they are not current package release surfaces.
