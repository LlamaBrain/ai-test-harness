# cross-engine ATH and Unreal support

Developer-facing changes for `ai-test-harness`.

Task: `cross-engine-unreal`
Source: `captain/cross-engine-unreal` at `41ecbe263c66a3f753a08e5beb89312beef4cf83`
Target: `2c93c78dbac381a1991e2cb645599b1c922827dc`
Merge base: `2c93c78dbac381a1991e2cb645599b1c922827dc`

### Added

- **ath:** add engine-neutral Node CLI, MCP, protocol and Captain trace tooling plus an opt-in native Unreal plugin, while retaining the Unity adapter and legacy protocol (41ecbe263c66a3f753a08e5beb89312beef4cf83)

<!-- captain-changes:v1 -->
```json
{
  "schema_version": 1,
  "audience": "developer",
  "project": "ai-test-harness",
  "id": "cross-engine-unreal",
  "subject": "cross-engine ATH and Unreal support",
  "source_branch": "captain/cross-engine-unreal",
  "source_head": "41ecbe263c66a3f753a08e5beb89312beef4cf83",
  "target_head": "2c93c78dbac381a1991e2cb645599b1c922827dc",
  "merge_base": "2c93c78dbac381a1991e2cb645599b1c922827dc",
  "entries": [
    {
      "oid": "41ecbe263c66a3f753a08e5beb89312beef4cf83",
      "message": "feat(ath): add cross-engine tooling and Unreal support\n\nAdd a shared protocol client and MCP server alongside a reusable Unreal plugin, while preserving the Unity host and legacy remote-console interfaces.",
      "type": "feat",
      "scope": "ath",
      "description": "add cross-engine tooling and Unreal support",
      "breaking": false,
      "migration": null,
      "category": "Added",
      "summary": "add engine-neutral Node CLI, MCP, protocol and Captain trace tooling plus an opt-in native Unreal plugin, while retaining the Unity adapter and legacy protocol",
      "excluded": false
    }
  ],
  "omitted": []
}
```
