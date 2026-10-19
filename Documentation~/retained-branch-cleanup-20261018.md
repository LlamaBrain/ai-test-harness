# AITH retained-branch cleanup - 2026-10-18

Task: AITH-GIT-CLEANUP-20261018. Owner: codex-unified-game-actions-20261013.
Michael authorized sequential cleanup and Git retention, with main -> dev -> feature branches; dev is the collection point. No staging branch, release, push or deletion is included.

Target: canonical local dev, previously cce27986869ebb88611aa8d50415b52f0a160f7e; fetched origin/dev is the same commit. main remains unchanged. Source is the existing feat/runtime-smoke-platform-next checkout at E:/Personal/ai-test-harness/.worktrees/runtime-smoke-platform-next.

Retained histories:
- ed0855610217e4ff5b9863babbe37ba930b1aae1: runtime-smoke platform/continuations and guarded-nullable repairs.
- 2c93c78dbac381a1991e2cb645599b1c922827dc: footage capture, optional Recorder and raw console dispatch.
- aeaa43f30f34a3cb63e854e4e792424c0be6d5d8: API/documentation corrections.
- dabee3d8b95cbedcea1885c38e004809f1dc1103: historical documentation-audit appendix.

Cleanup checkpoints: aacb72628b4a03d5e92fd8e48952852185f4d717 (0.5.4 warning/docs union), 70b0118cf744ab1fa9f9451b75c1d9f5ff98a3f1 (0.6.0 capture/raw union), and the 0.6.1 merge containing this record. The operator's Captain note/queue closeout records the exact resulting 0.6.1 OID after integration.

Preserved modern RuntimeSmoke acceptance and suite CLI alongside raw diagnostics, nullable guards, media validation, optional Recorder defines and assembly friendship, test discovery and consumed Unity GUIDs. Retired procedural smoke documents remain retired; optional feature-demo guidance describes capture only. Incoming CRLF endings were normalized to LF and twelve empty metadata fields trimmed for a clean incoming whitespace check; no metadata identities were reminted.

Verification at actual revisions:
- Warning/docs checkpoint:73 Node client cases pass.
- Real-socket raw regression fails against aacb726's old client with the expected missing !raw prefix; combined client passes76/76, zero skips/failures, before the 0.6.0 commit. Logs: E:/Personal/dirigible2D/Temp/automation-scratch/retained-branch-cleanup-20261018/aith-raw-red.txt and aith-node-green.txt.
- Source compilation at 70b0118: Debug builds of runtime/Editor/test/Recorder/commands/remote-console assemblies using installed Unity/MCP references all exit0. Six package assemblies compile through four project builds. Scratch source/build definition and results are at E:/Personal/dirigible2D/Temp/automation-scratch/retained-branch-cleanup-20261018/package-compile. This is a standalone .NET8 compile with Unity references, not Unity import, EditMode execution or a development-player build.
- Recorder compilation reports two CS0618 obsolete API warnings. Scratch reference graphs also report MSB3243 log4net selection warnings. No warning-free claim.
- Version/package/runtime diagnostic/README agree at0.6.1; final version check and incoming whitespace/ancestry checks run before local dev integration. The documentation/version-only final merge retains earlier functional evidence at0.6.0.

Git integration and feature acceptance are separate. Original live capture/Recorder, clean-project import and consumer-specific Editor/player/HITL proof remain unfinished. Existing Dirigible proof pins ed08556 and is not relabeled as proof of this combined union. Dirigible manifest/dependency pins remain unchanged. This cleanup does not close an original frozen feature plan or install a new runtime.

Other registered source/verification worktrees, detached refs, existing dirty legacy overlays and stale REBASE_HEAD markers are preserved. The failure-contract candidate was compared with later histories and no extra useful missing fix was established; it is retained for provenance. No branch, stash or worktree is deleted.

Next owner: Michael selects any remaining Unity/Recorder feature acceptance or publication. Git consolidation is local dev only; no staging branch exists yet.
