# AI Test Harness skills

These generic workflows exercise ATH through the shared CLI/MCP or existing Unity
Editor tools. They do not require a particular game's player, items or replay
mechanics.

| Skill | Scope |
|---|---|
| `ath-unreal-smoke` | Runnable ATH fixture/world/evidence workflow in any instrumented Unreal project. |
| `ath-exe-smoke` | Built Unity player template; fill in the host's scenario. |
| `ath-feature-demo` | Unity Editor/Recorder capture template; fill in feature commands. |

BeforeTheShade's original `ath-smoke-fullloop` is preserved under
[`HostWorkflows~/BeforeTheShade/`](../HostWorkflows~/BeforeTheShade/ath-smoke-fullloop/SKILL.md).
Its old generic skill directory contains a relocation pointer. Existing installed
copies keep working; new generic installations should not copy this gameplay
workflow. Project-specific smokes belong in the consuming project or a host bridge
plugin.

## Install

Skills are not auto-discovered by UPM. Copy or link only the selected directory
into `<project>/.claude/skills/<name>/` (or the agent client's skill directory).

```sh
cp -r <ath>/Skills/ath-unreal-smoke <project>/.claude/skills/
# Linux/macOS alternative:
ln -s <ath>/Skills/ath-unreal-smoke <project>/.claude/skills/ath-unreal-smoke
```

```powershell
# Windows development link:
New-Item -ItemType Junction -Path '<project>/.claude/skills/ath-unreal-smoke' -Target '<ath>/Skills/ath-unreal-smoke'
```

Inspect an existing destination before changing a link. Skills carry a `version`
frontmatter value; compare the live `package_version` state or runtime
`ath version --port <port>` with `0.3.0` before running. Recopy stale skills after
upgrading the package.

## Author

Declare the host capability requirements and expected values. Use registered
commands for mutations and cheap state queries for observations. Reset sticky
flags before the triggering action; never reset between that action and its
wait. Track asynchronous operations by the returned correlation ID and inspect
their success separately from completion.

Define explicit pass criteria, timeouts and first-failure diagnostics. Emit
`ath.smoke.completed` on both pass and fail and keep evidence beneath the
consuming project's `.captain-sdlc/trace/`. Shared trace emission requires neither
engine nor Unity Editor. See [shared tooling](../Documentation~/shared-tooling.md)
and [trace events](../Documentation~/trace-events.md).