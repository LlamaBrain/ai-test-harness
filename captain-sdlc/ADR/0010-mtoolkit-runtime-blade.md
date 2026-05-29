# ADR-0010: MToolKit is a runtime blade of the knife

**Date:** 2026-05-28

## Problem

MToolKit (`com.michael-tiller.mtoolkit`, an independently-versioned UPM package, currently v0.7.0) is a production-grade "sane C# Unity" runtime framework — DI / plugin architecture, forward-only save migration, Unity Localization integration, structured Serilog/JSONL logging, pluggable analytics. It is the developer's canonical foundation for substantial projects (Dirigible); it is opt-in and deliberately skipped for small ones (BeforeTheShade).

Several Captain SDLC roadmap milestones describe building generic infrastructure that MToolKit — or adjacent back-tech the developer already owns — has already canonized: save-migration testing (M14), localization key audit (M15), schema/contract testing (M13), perf instrumentation (M8), trace emission (M2), constitution enforcement (M11), headless build (M21, via the sibling `TemplateGameBuildScript` build template). Left unaddressed, the roadmap would re-invent the developer's own back-tech. The question is what MToolKit's relationship to the knife is, and what that does to the roadmap.

## Solution

Three decisions:

1. **MToolKit is a blade of the knife — a *runtime* blade, a distinct class from the *process* blades** (interrogate, ATH, claude-release, CICD). Process blades automate SDLC process around a project; the runtime blade canonizes the non-creative runtime-infrastructure decisions (save migration, localization, logging, DI wiring). This is consistent with the mission — "tools eat the boring repetitive work; the human eats the taste-bearing stuff." MToolKit eats runtime-plumbing decisions the way ATH eats QA. It does not muddy "paradigm, not toolset": the paradigm is canonizing every non-creative decision, applied one layer lower.

2. **Incorporate, don't extract.** Roadmap milestones declare a *dependency* on MToolKit's capabilities; they do not fork or re-implement MToolKit code inside the process tools. Building only the thin process layer (a gate, a wrapper, an audit, a templatized pattern) on top of MToolKit's existing primitive is strictly cheaper than extracting a generic version, and avoids forking a toolkit the developer actively maintains.

3. **Process blades detect-and-degrade.** Because MToolKit is opt-in, the roadmap must not *assume* it. Process blades probe for MToolKit in the consumer project and light up the cheap path when present (e.g. claude-release's save-migration gate wires to `ForwardMigrator<T>`), and degrade gracefully — a minimal fallback or an honest "not applicable" — when absent. This keeps the knife usable on small projects (BeforeTheShade) without coupling the process tools to MToolKit.

Consequence for the roadmap: the 26 open milestones re-disposition into Have / Thin / Build / Defer — see `roadmap.md` § MToolKit Re-Disposition. Net active novel work drops from "27 milestones" to ~8–9, concentrated in the cross-tool seams, which is exactly Captain SDLC's differentiated value.

## Relationship to prior ADRs

- **Reinforces ADR-0001 (tools-not-modules) and ADR-0003 (no Captain SDLC layer tool); does not contradict them.** MToolKit is adopted by the *games*, not imported by the *process tools*. The process blades interop with MToolKit's *artifacts* — save files plus schema hashes, asmdef topology, Slog JSONL, the package manifest — through shared conventions. Contract-based interop; code stays unshared. "Incorporate, not extract" is the tools-not-modules rule applied to the runtime blade.
- **Sharpens ADR-0006 (constitution vocabulary).** For MToolKit projects, the *content* of the constitution is largely "conform to MToolKit's patterns," and enforcement (Seam 6 / M11) collapses from a general invariant analyzer to a handful of asmdef-boundary + DI-registration + no-MonoBehaviour-business-logic checks the framework's conventions make well-defined. The vocabulary is unchanged; the cheap enforcement path is new.
- **Touches ADR-0004 (code-reading) and ADR-0005 (privacy split).** MToolKit's structural sanity makes Tier-1 code-reading more tractable; its consent plumbing is the concrete substrate beneath the privacy framework's classification taxonomy.

## Alternatives

- **Keep MToolKit purely a dependency, outside the knife.** Rejected: understates it. It is a product-level flagship toolkit and a genuine blade; naming it as one is what makes the "more of the knife is cheaper" story true rather than incidental.
- **Extract MToolKit's capabilities into generic Captain SDLC tools.** Rejected: forks an actively-maintained toolkit, creates a sync seam, and buys nothing the developer doesn't already own.
- **Assume MToolKit everywhere.** Rejected: it is opt-in; small projects don't use it. Detect-and-degrade preserves the knife's reach without coupling.

## Caveats carried into the roadmap

- The perf hooks (M7 / M8) need direct verification — exploration disagreed on whether a stored baseline/grade asset exists, or only live measurement.
- MToolKit's save-migration *framework* is production-grade, but Dirigible's golden-fixture *corpus* is currently single-version, so M14's testing deliverable is small but not zero.
