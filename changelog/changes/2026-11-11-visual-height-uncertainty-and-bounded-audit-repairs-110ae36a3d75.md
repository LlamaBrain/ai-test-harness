# Visual height uncertainty and bounded audit repairs

Developer-facing changes for `ai-test-harness`.

Task: `pr3-independent-review`
Source: `captain/intent-4c88889a-a094-4701-97c0-33f1ca7047c6` at `b439ac29a8e7595a97440d81de65775634a90b97`
Target: `98b967a29c67975bc4788e77148cc69a07741d19`
Merge base: `98b967a29c67975bc4788e77148cc69a07741d19`

### Fixed

- **visual:** Keep centered mixed-size glyph cohorts inconclusive while preserving supported single-font roles. Bound audit groups to eight items and 64 MiB, enforce independent screen deadlines, verify disk-result digests, isolate invalid screenshots and stop later workers when termination is unconfirmed. Add real 100/256/1,024-screen verification to Windows/Linux CI. Rebuild the native analyzer alongside the upgraded client for the internal batch transport; public result and RuntimeSmoke contracts remain unchanged. (b439ac29a8e7595a97440d81de65775634a90b97)

<!-- captain-changes:v1 -->
```json
{
  "schema_version": 1,
  "audience": "developer",
  "project": "ai-test-harness",
  "id": "pr3-independent-review",
  "subject": "Visual height uncertainty and bounded audit repairs",
  "source_branch": "captain/intent-4c88889a-a094-4701-97c0-33f1ca7047c6",
  "source_head": "b439ac29a8e7595a97440d81de65775634a90b97",
  "target_head": "98b967a29c67975bc4788e77148cc69a07741d19",
  "merge_base": "98b967a29c67975bc4788e77148cc69a07741d19",
  "entries": [
    {
      "oid": "b439ac29a8e7595a97440d81de65775634a90b97",
      "message": "fix(visual): harden height uncertainty and batch audits (v0.10.4)\n\nImplements: pr3-independent-review",
      "type": "fix",
      "scope": "visual",
      "description": "harden height uncertainty and batch audits (v0.10.4)",
      "breaking": false,
      "migration": null,
      "category": "Fixed",
      "summary": "Keep centered mixed-size glyph cohorts inconclusive while preserving supported single-font roles. Bound audit groups to eight items and 64 MiB, enforce independent screen deadlines, verify disk-result digests, isolate invalid screenshots and stop later workers when termination is unconfirmed. Add real 100/256/1,024-screen verification to Windows/Linux CI. Rebuild the native analyzer alongside the upgraded client for the internal batch transport; public result and RuntimeSmoke contracts remain unchanged.",
      "excluded": false
    }
  ],
  "omitted": []
}
```
