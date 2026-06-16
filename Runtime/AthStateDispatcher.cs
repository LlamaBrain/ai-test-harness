#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthStateDispatcher — the switch that maps state keys to adapter + bridge
// accessors. Relocated from the Editor assembly into Runtime so BOTH the
// editor ath-state tool AND the in-player harness.state command resolve keys
// through one implementation. Touches only Runtime types (no UnityEditor), so
// it ships in the player under the same gate. Public for cross-assembly access.

#nullable enable

using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath
{
    public static class AthStateDispatcher
    {
        /// <summary>
        /// Resolve a state key against the registered adapter + bridge.
        /// Writes status + value via out params; also reports diagnostic
        /// flags so callers can populate a richer result shape.
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

                // ---- fall through: async:<id> completion lookup, then custom state ----
                default:
                    // async:<id> — structured completion lookup against the ring
                    // (mirrors the editor async_done:<id> predicate). Presence in
                    // the ring means the op completed; absence means "not observed"
                    // (not-yet / never / aged out of the 16-slot ring) — pollable.
                    if (key != null && key.StartsWith("async:", System.StringComparison.Ordinal))
                    {
                        if (bridge == null) { status = "not_ready"; return; }
                        var id  = key.Substring("async:".Length);
                        var rec = bridge.FindAsync(id);
                        if (rec == null) { status = "not_ready"; value = "missing"; return; }
                        value = "done";   // completion; success/error detail via last_async
                        return;
                    }

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
#endif
