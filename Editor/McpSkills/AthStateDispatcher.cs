// AthStateDispatcher — the switch that maps ath-state keys to adapter +
// bridge accessors. Kept separate from Tool_AthState so unit tests can
// target the dispatcher without an MCP attachment.

#nullable enable

using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    internal static class AthStateDispatcher
    {
        /// <summary>
        /// Resolve a state key against the registered adapter + bridge.
        /// Writes status + value via out params; also reports diagnostic
        /// flags so the MCP tool can populate its richer result shape.
        /// </summary>
        public static void Resolve(
            string key,
            out string status,
            out string value,
            out bool   adapterPresent,
            out bool   bridgePresent,
            out bool   customStateAttempted)
        {
            status               = "ok";
            value                = "";
            customStateAttempted = false;

            var bridge  = AthBridge.Instance;
            var adapter = AthServices.Adapter;
            adapterPresent = adapter != null;
            bridgePresent  = bridge  != null;

            switch (key)
            {
                // ---- identity / readiness ----
                case "in_gameplay":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.InGameplayScene.ToString().ToLowerInvariant();
                    return;

                case "scene_name":
                    value = SceneManager.GetActiveScene().name;
                    return;

                case "package_version":
                    value = AthRuntimeFlag.PackageVersion;
                    return;

                case "host_name":
                    value = adapter?.HostName ?? "<none>";
                    return;

                case "bridge_ready":
                    value = (bridge != null).ToString().ToLowerInvariant();
                    return;

                case "adapter_ready":
                    value = (adapter != null).ToString().ToLowerInvariant();
                    return;

                case "game_ready":
                    if (bridge == null) { status = "not_ready"; return; }
                    value = bridge.GameReady.ToString().ToLowerInvariant();
                    return;

                // ---- player slice ----
                case "player_alive":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.PlayerAlive.ToString().ToLowerInvariant();
                    return;

                case "player_holding_seed":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.PlayerHoldingItem.ToString().ToLowerInvariant();
                    return;

                case "player_position":
                {
                    if (adapter == null) { status = "not_ready"; return; }
                    var p = adapter.PlayerPosition;
                    value = $"{p.x:F4},{p.y:F4},{p.z:F4}";
                    return;
                }

                case "player_velocity":
                {
                    if (adapter == null) { status = "not_ready"; return; }
                    var v = adapter.PlayerVelocity;
                    value = $"{v.x:F4},{v.y:F4},{v.z:F4}";
                    return;
                }

                // ---- world slice ----
                case "spawn_attempts":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.SpawnAttempts.ToString();
                    return;

                case "is_paused":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.IsPaused.ToString().ToLowerInvariant();
                    return;

                // ---- ghost slice ----
                case "ghost_active":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.GhostActive.ToString().ToLowerInvariant();
                    return;

                case "ghost_seed_active":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.GhostItemActive.ToString().ToLowerInvariant();
                    return;

                case "last_run_recording_frames":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.LastRunRecordingFrameCount.ToString();
                    return;

                case "last_seed_recording_frames":
                    if (adapter == null) { status = "not_ready"; return; }
                    value = adapter.LastItemRecordingFrameCount.ToString();
                    return;

                // ---- edge flags (bridge-owned) ----
                case "player_died_since_reset":
                    if (bridge == null) { status = "not_ready"; return; }
                    value = bridge.PlayerDiedSinceLastReset.ToString().ToLowerInvariant();
                    return;

                case "player_spawned_since_reset":
                    if (bridge == null) { status = "not_ready"; return; }
                    value = bridge.PlayerSpawnedSinceLastReset.ToString().ToLowerInvariant();
                    return;

                case "goal_reached_since_reset":
                    if (bridge == null) { status = "not_ready"; return; }
                    value = bridge.GoalReachedSinceLastReset.ToString().ToLowerInvariant();
                    return;

                case "seed_consumed_since_reset":
                    if (bridge == null) { status = "not_ready"; return; }
                    value = bridge.ItemConsumedSinceLastReset.ToString().ToLowerInvariant();
                    return;

                // ---- async ring buffer ----
                case "last_async":
                {
                    if (bridge == null) { status = "not_ready"; return; }
                    var rec = bridge.LastAsync();
                    if (rec == null) { value = "empty"; return; }
                    var v = rec.Value;
                    value = $"name={v.Name};id={v.CorrelationId};success={v.Success.ToString().ToLowerInvariant()};error={v.Error ?? ""}";
                    return;
                }

                // ---- fall through to adapter's custom-state escape hatch ----
                default:
                    if (adapter != null)
                    {
                        customStateAttempted = true;
                        if (adapter.TryGetCustomState(key, out var custom))
                        {
                            value = custom ?? "";
                            return;
                        }
                    }
                    status = "unknown_key";
                    return;
            }
        }
    }
}
