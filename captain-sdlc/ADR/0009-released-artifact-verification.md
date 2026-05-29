# ADR-0009: Release-readiness verifies the published artifact, not just source-repo tests

**Date:** 2026-05-28

## Problem

In a single ~10-hour Captain SDLC working session, three separate publish-discipline failures landed in user environments — all with the same shape:

- **interrogate v0.1.3** — source committed `adr` / `roadmap` / `taskout` command markdowns and runtime modules; marketplace publish was version-bumped manually without re-running `prepare:distribution-repo`. Marketplace artifact shipped runtime code with no command surfaces. Users installed "0.1.3" and got nothing they could invoke. Fixed in v0.1.4.
- **interrogate v0.1.4** — *also* exposed that there was no automated check that distribution-repo matched the source. Fixed by extending `verify-release-readiness.mjs` to diff `runtime-dist/` against `distribution-repo/`.
- **claude-release v0.2.0 / v0.3.0** — source `plugin/lib/*.js` depends on `semver` and `conventional-commits-parser`. The `.gitignore` excludes `plugin/lib/node_modules/` ("installed via `npm install` in plugin/lib/" per the comment). Claude Code's plugin install mechanism doesn't run `npm install`. Every released version ships with broken runtime — `node lib/build-manifest.js` fails with `ERR_MODULE_NOT_FOUND: semver`. Source-repo tests pass; the published artifact doesn't run.

Each tool's own test suite passes against the source tree. The failure mode is at the *distribution boundary* — between "the source tested OK" and "what got published actually runs in a consumer environment."

## Solution

Adopt as a Captain SDLC discipline: **every tool's release process must include a release-readiness check that runs the published artifact end-to-end and confirms it works in the same environment a consumer would experience.** This is conceptually Seam 4 (cross-tool contract testing) applied to each tool's *distribution* boundary — the contract being "what we shipped is what we said we shipped."

Concrete shape (per tool):

- **For tools with separate source + distribution repos** (interrogate): `check:release` diffs `runtime-dist/` against `distribution-repo/`; the diff failing means someone hand-edited or bypassed the prepare step.
- **For tools with single source-as-distribution** (claude-release): the release check must execute each entry point from the *as-published* file layout, in a clean Node environment with no project-local `node_modules`. If it fails, the publish is broken. Bundling (esbuild → self-contained outputs) is the concrete mechanism.
- **For tools that ship runtime payloads to host applications** (ATH): the release check must invoke at least one MCP tool through the published payload path and confirm it returns.

The check belongs in each tool's own release pipeline. It is not a Captain SDLC layer concern — see ADR-0003. But the *discipline* of having it is a shared convention.

## Alternatives

- **Trust source-repo tests** — Demonstrably broken; led to three incidents. The source tree carries `node_modules` and project state that the distribution does not.
- **Manual smoke-testing the release before pushing** — Doesn't scale, doesn't survive context-switching, gets skipped under pressure. The whole point of Captain SDLC is to automate process work that doesn't deserve human attention.
- **Plugin systems that run `npm install` on install** — Claude Code's plugin install doesn't; we don't control that. Designing around hypothetical install-time package management is a non-fix.
- **Single global release-check tool that runs against all Captain SDLC tools** — Considered. Rejected: violates ADR-0001 (tools share conventions, not code). Each tool implements the discipline in its own pipeline.
