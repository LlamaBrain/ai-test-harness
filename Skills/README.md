# AI Test Harness — Claude skills

This folder contains the smoke-test `SKILL.md` workflows the harness ships
with. Each skill orchestrates `/ath-cmd`, `/ath-state`, and `/ath-wait`
through Unity MCP to drive a multi-step regression scenario end-to-end.

## Installing skills into a host project

Claude Code reads skills from `<project>/.claude/skills/<name>/SKILL.md`.
Unity Package Manager does **not** auto-discover skills shipped inside a
package's `Skills/` folder. Each skill you want available in a host
project must be copied or symlinked into that project's `.claude/skills/`
directory.

### Copy (cross-platform)

```bash
cp -r <ath-package-path>/Skills/ath-smoke-fullloop  <host-project>/.claude/skills/
```

### Symlink (Linux/macOS — keeps the skill live-updating against the package)

```bash
ln -s <ath-package-path>/Skills/ath-smoke-fullloop  <host-project>/.claude/skills/ath-smoke-fullloop
```

### Symlink (Windows, in an elevated shell)

```powershell
New-Item -ItemType SymbolicLink `
  -Path "<host-project>\.claude\skills\ath-smoke-fullloop" `
  -Target "<ath-package-path>\Skills\ath-smoke-fullloop"
```

## Verifying the copied skill is current

Every shipped skill includes a `version:` frontmatter field that names the
package version it was authored against. The skill's first step (Step 0)
queries `/ath-state { key: "package_version" }` and aborts with a
"re-copy from `<source>`" message if the runtime version doesn't match.

Concretely:

```yaml
---
name: ath-smoke-fullloop
version: 0.1.0-preview.1
---
```

```jsonc
let live = ath-state { "key": "package_version" }
if (live.Value != "0.1.0-preview.1") ABORT
```

When you bump the AI Test Harness package version, re-copy or re-sync
all installed skills.

## Available skills

| Skill | Description |
|---|---|
| `ath-smoke-fullloop` | BeforeTheShade death → ghost replay → goal → clean-restart smoke. Locks in the v0.1 BTS gameplay loop end-to-end. |

(More smoke skills will land as additional host scenarios are wired.)

## Authoring new skills

Skills live in subdirectories under `Skills/<skill-name>/SKILL.md`. The
preferred structure:

- YAML frontmatter with `name`, `description`, `version`.
- Numbered steps; each step is a small block of `/ath-cmd` / `/ath-state`
  / `/ath-wait` calls plus the expected outcome.
- Explicit `harness.reset` between sequenced waits to clear edge-sticky
  flags — and a note explaining when *not* to issue it (e.g. between a
  command that fires the spawn and the wait that observes it).
- A "PASS criteria" block enumerating every assertion that must hold.
- A "Failure handling" block describing the screenshots + state dumps to
  capture before triage.

See `ath-smoke-fullloop/SKILL.md` for a worked example.

## Future improvement

An editor menu item `Tools > AI Test Harness > Sync Skills` would mirror
`<package>/Skills/*` into `<project>/.claude/skills/` on demand, removing
the manual copy step. Tracked for a v0.1.x polish release.
