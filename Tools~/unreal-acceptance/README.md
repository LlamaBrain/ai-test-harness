# KinecadeSandbox ATH acceptance

This is consuming-project acceptance glue, separate from the reusable client and
native plugin. KinecadeSandbox is an existing Lyra project; the suite tests ATH,
not Lyra combat or future Kinecade mechanics.

```sh
node <ath>/Tools~/unreal-acceptance/kinecade.js --project-root <KinecadeSandbox> --dry-run
node <ath>/Tools~/unreal-acceptance/kinecade.js --project-root <KinecadeSandbox> --port 8787 --rendered
```

Without `--project-root`, the consuming root is the current working directory.
A real run requires `KinecadeSandbox.uproject` there before it connects or writes
evidence. Use an explicit root when launching from ATH. `--dry-run` prints the
specification without an engine session or evidence writes.

The default suite validates Unreal/project identity, runtime readiness and real
world validity, then executes all shared fixture steps: state mutation, predicates,
sticky event observation/reset, JSON command execution, correlated asynchronous
completion and success, and cleanup. `--rendered` requires screenshot capability
and a complete PNG. Omitting it is a nonvisual test and cannot pass a visual gate.

`--scene <name>` adds a selected-map assertion using `scene_name`. In PIE that
name may include Unreal's instance prefix; read the actual state before selecting
the expected name. `--scenario <file.json>` adds an array of custom steps directly
after the baseline reset and before the portable fixture suite. Custom steps do
not replace the ATH responsibility checks. `--out <report.json>` writes an
additional result copy; `--commit <revision>` binds trace reference metadata.

All runs emit Captain-compatible pass/fail evidence in the consuming project's
`.captain-sdlc/trace/`. No project assets, gameplay modules or engine processes are
created by this runner. Compile/load, PIE restart, standalone and packaged runs
remain separate gates in [verification.md](../../Documentation~/verification.md).

```sh
node --test <ath>/Tools~/unreal-acceptance/kinecade.test.js
```
