# ADR-0011: Shared test-support lives in the template until a second consumer

**Date:** 2026-05-28

## Problem

Shared test-support code — the first instance being `MigrationGoldenTestBase`, the golden-corpus harness lifted out of 13 duplicated per-domain copies — can live in two places: the vendored TemplateGame's `MToolKit.Tests.Support` (copied per project, propagated by template-sync) or the MToolKit UPM package (shipped to every consumer via the package, the "canonical framework" home). MToolKit is the canonical runtime blade (ADR-0010) and its migration *canon* already lives in the package's `Persistence/README.md`, so there's a real pull to put the test harness in the package too. Where should shared test-support land, and when?

## Solution

**Shared test-support starts in the vendored template; it is promoted into the MToolKit package only when a second consumer needs it.**

- MToolKit currently has exactly one consumer (dirigible2D), which already carries the TemplateGame — so the template home already gives full reach at zero extra cost.
- Promoting to the package is *not* "just move a file." It stands up the package's first test assembly, requires a `"testables": ["com.michael-tiller.mtoolkit"]` entry in every consumer's `manifest.json` (UPM won't compile a package's test assemblies otherwise), and forces the test-support assembly to reference ES3 across the package→Assets boundary (ES3 lives in the consumer's Assets, not the package). That is real project-config coupling plus boundary risk for zero benefit while there is a single consumer.
- The trigger to promote is a **second MToolKit consumer**. At that point the package home earns its keep — one canonical copy reaching both — and the promotion can be verified against two projects at once. The code is identical either way; only the asmdef location, the `testables` entries, and the consumers' asmdef references change, so deferring costs nothing later.

This is the test-infrastructure instance of the broader scope-realism rule: don't build speculative framework infrastructure ahead of a consumer that needs it.

## Alternatives

- **Promote to the MToolKit package now** — rejected for now. Canonically "correct," but zero reach benefit while dirigible2D is the only consumer (and it already has the harness via the template), and it adds a `testables` coupling and a package→Assets ES3 reference for no payoff. Revisit at the second consumer.
- **Keep shared test-support permanently in the template** — rejected as a permanent rule. Once multiple projects consume MToolKit, per-project template copies drift and the package becomes the right canonical home. This ADR defers promotion; it does not refuse it.

## References

- ADR-0010: MToolKit is a runtime blade of the knife.
