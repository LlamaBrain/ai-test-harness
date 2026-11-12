# Unreal development plugin installer

Node 18 or newer is sufficient; no Unreal process is started. The source defaults
to this checkout's `Unreal~/Ath`, and the consuming project is always explicit:

```sh
node <ath>/Tools~/unreal-install/install.js --project <Game.uproject> --dry-run
node <ath>/Tools~/unreal-install/install.js --project <Game.uproject>
node <ath>/Tools~/unreal-install/install.js --project <Game.uproject> --verify-only
```

`--project` may instead select a directory containing exactly one `.uproject`.
Use `--source <plugin-directory>` for another maintained ATH checkout. Paths with
spaces must be quoted. `--dry-run` validates and reports without creating even
the Plugins directory. `--verify-only` requires an existing matching link and
never creates one. The modes are mutually exclusive.

Installation creates `<project>/Plugins/Ath` as a Windows directory junction or
a directory symlink on other platforms. It never copies the implementation,
changes the project descriptor, removes a link, overwrites a plugin or launches
the engine. An existing matching link verifies idempotently. An ordinary existing
plugin directory, foreign/dangling link, or redirected Plugins parent is a conflict
and returns a nonzero exit status with its exact path. Inspect and resolve that
configuration through the consuming project's normal workflow.

Build the host's existing Editor target after installation and enable Ath in its
plugin configuration if the project explicitly disables it. Source installation
does not establish compile/load/runtime success. See
[Unreal acceptance](../../Documentation~/unreal.md).

```sh
node --test <ath>/Tools~/unreal-install/install.test.js
node --check <ath>/Tools~/unreal-install/install.js
```

Tests use temporary project/plugin directories, exercising real link creation,
verification, read-only dry-runs and conflict preservation. They do not alter a
real Unreal project.
