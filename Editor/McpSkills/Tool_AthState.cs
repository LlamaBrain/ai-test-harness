// LlamaBrainLabs.Ath.Editor — typed state queries against AthBridge + the
// registered host adapter. Delegates the dispatch to AthStateDispatcher so
// future tests can target the dispatcher without an MCP attachment.

#nullable enable

using System.ComponentModel;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthState
    {
        public sealed class Result
        {
            public string Key                  = "";
            public string Status               = "ok";   // ok | not_ready | not_in_playmode | unknown_key
            public string Value                = "";     // string-encoded; structured slices serialized inline
            public bool   AdapterPresent;
            public bool   BridgePresent;
            public bool   CustomStateAttempted;          // true when status==unknown_key and adapter was queried via TryGetCustomState
        }

        [McpPluginTool(
            "ath-state",
            Title = "AI Test Harness / State",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description(
            "Query a named slice of harness state. Built-in keys: " +
            "in_gameplay, scene_name, host_name, bridge_ready, adapter_ready, game_ready, " +
            "player_alive, player_holding_seed, player_position (x,y,z), player_velocity, " +
            "spawn_attempts, is_paused, " +
            "ghost_active, ghost_seed_active, last_run_recording_frames, last_seed_recording_frames, " +
            "player_died_since_reset, player_spawned_since_reset, goal_reached_since_reset, seed_consumed_since_reset, " +
            "last_async. " +
            "Unknown keys fall through to the registered adapter's TryGetCustomState — " +
            "Status=unknown_key with CustomStateAttempted=true distinguishes 'adapter doesn't know this key' " +
            "from 'no adapter registered.'")]
        public Result Get(
            [Description("State key. See description for the built-in set.")]
            string key)
        {
            var result = new Result { Key = key ?? "" };

            return MainThread.Instance.Run(() =>
            {
                if (!UnityEditor.EditorApplication.isPlaying)
                {
                    result.Status         = "not_in_playmode";
                    result.AdapterPresent = AthServices.Adapter != null;
                    result.BridgePresent  = AthBridge.Instance != null;
                    return result;
                }

                AthStateDispatcher.Resolve(
                    result.Key,
                    out var status,
                    out var value,
                    out var adapterPresent,
                    out var bridgePresent,
                    out var customStateAttempted);

                result.Status               = status;
                result.Value                = value;
                result.AdapterPresent       = adapterPresent;
                result.BridgePresent        = bridgePresent;
                result.CustomStateAttempted = customStateAttempted;
                return result;
            });
        }
    }
}
