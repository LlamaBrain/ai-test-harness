// LlamaBrainLabs.Ath.Editor — synchronous predicate-poll for harness convergence.
// Polls every 100ms; predicate bodies run on the Unity main thread via
// MainThread.Run. SYNC return is mandatory — the MCP framework's serializer
// marks async-tool results as "Pending" before the await completes (see the
// leading comment on dirigible's Tool_DirigibleWait.cs — do NOT modernize
// this to async).

#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthWait
    {
        public sealed class Result
        {
            public string Predicate          = "";
            public string Arg                = "";
            public bool   Satisfied;
            public string Status             = "ok";   // ok | timeout | not_in_playmode | unknown_predicate
            public long   ElapsedMs;
            public string LastEvaluatedValue = "";     // most recent value the predicate compared against
            public bool   PlaymodeOnExit;
            public bool   BridgeReadyOnExit;
            public bool   AdapterReadyOnExit;
        }

        [McpPluginTool(
            "ath-wait",
            Title = "AI Test Harness / Wait",
            ReadOnlyHint = true
        )]
        [Description(
            "Poll a named predicate until satisfied or timeout. Predicates: " +
            "playmode, scene_loaded:<name>, in_gameplay, game_ready, adapter_ready, " +
            "player_died, player_spawned, player_alive, goal_reached, seed_consumed, " +
            "paused_equals:<bool>, ghost_active, async_done:<id>, " +
            "state_equals:<key>=<value>, log_match:<regex>, " +
            "spawn_attempts_at_least:<int>. " +
            "Returns rich diagnostics on timeout (LastEvaluatedValue + on-exit playmode/bridge/adapter " +
            "flags) so a missed convergence localizes quickly. " +
            "On Status=timeout — or whenever the bridge appears stuck — fetch /console-get-logs " +
            "before retrying: Editor-side exceptions (NullRef in a predicate path, missing scene " +
            "actor, mid-playmode compile error) surface in the Unity console but not in this " +
            "result, and retrying without reading them just burns turns.")]
        public Result Wait(
            [Description("Predicate name. Supports arg via 'predicate:arg' colon syntax.")]
            string predicate,
            [Description("Timeout in milliseconds. Default 30000.")]
            int timeout_ms = 30000)
        {
            var result = new Result { Predicate = predicate ?? "" };

            string predName = predicate ?? "";
            string predArg  = "";
            var colon = predName.IndexOf(':');
            if (colon >= 0)
            {
                predArg  = predName.Substring(colon + 1);
                predName = predName.Substring(0, colon);
                result.Arg = predArg;
            }

            // Main-thread playmode early-out for everything but `playmode`.
            if (predName != "playmode")
            {
                var inPlay = MainThread.Instance.Run(() => UnityEditor.EditorApplication.isPlaying);
                if (!inPlay)
                {
                    result.Status = "not_in_playmode";
                    FillOnExit(result);
                    return result;
                }
            }

            // log_match: subscribe a log handler for the lifetime of the wait.
            ConcurrentQueue<string>? logBuffer = null;
            Regex?                   logRegex  = null;
            Application.LogCallback? logHandler = null;
            if (predName == "log_match")
            {
                if (string.IsNullOrEmpty(predArg))
                {
                    result.Status             = "unknown_predicate";
                    result.LastEvaluatedValue = "missing_regex";
                    FillOnExit(result);
                    return result;
                }
                try { logRegex = new Regex(predArg, RegexOptions.Compiled); }
                catch (ArgumentException ex)
                {
                    result.Status             = "unknown_predicate";
                    result.LastEvaluatedValue = "invalid_regex:" + ex.Message;
                    FillOnExit(result);
                    return result;
                }
                logBuffer = new ConcurrentQueue<string>();
                var buf = logBuffer;
                logHandler = (msg, _, _) => { if (msg != null) buf.Enqueue(msg); };
                MainThread.Instance.Run(() => { Application.logMessageReceived += logHandler; });
            }

            var sw = Stopwatch.StartNew();
            try
            {
                while (sw.ElapsedMilliseconds < timeout_ms)
                {
                    bool ok;
                    if (predName == "log_match")
                    {
                        string? matched = null;
                        while (logBuffer!.TryDequeue(out var line))
                        {
                            if (logRegex!.IsMatch(line)) { matched = line; break; }
                        }
                        ok = matched != null;
                        result.LastEvaluatedValue = ok ? $"matched=\"{matched}\"" : "no_match_yet";
                    }
                    else
                    {
                        var predNameLocal = predName;
                        var predArgLocal  = predArg;
                        ok = MainThread.Instance.Run(() => CheckPredicate(predNameLocal, predArgLocal, result));
                    }

                    if (ok)
                    {
                        result.Satisfied = true;
                        break;
                    }
                    Thread.Sleep(100);
                }
            }
            finally
            {
                if (logHandler != null)
                    MainThread.Instance.Run(() => { Application.logMessageReceived -= logHandler; });
            }

            result.ElapsedMs = sw.ElapsedMilliseconds;
            if (!result.Satisfied && result.Status == "ok") result.Status = "timeout";
            FillOnExit(result);
            return result;
        }

        private static void FillOnExit(Result result)
        {
            // Main-thread query — these flags help triage timeouts.
            MainThread.Instance.Run(() =>
            {
                result.PlaymodeOnExit     = UnityEditor.EditorApplication.isPlaying;
                result.BridgeReadyOnExit  = AthBridge.Instance != null;
                result.AdapterReadyOnExit = AthServices.Adapter != null;
                return 0;
            });
        }

        private static bool CheckPredicate(string predName, string predArg, Result result)
        {
            var bridge  = AthBridge.Instance;
            var adapter = AthServices.Adapter;

            switch (predName)
            {
                case "playmode":
                {
                    var v = UnityEditor.EditorApplication.isPlaying;
                    result.LastEvaluatedValue = v.ToString().ToLowerInvariant();
                    return v;
                }

                case "scene_loaded":
                {
                    var name = SceneManager.GetActiveScene().name;
                    result.LastEvaluatedValue = $"scene={name}";
                    return name == predArg;
                }

                case "in_gameplay":
                    if (adapter == null) { result.LastEvaluatedValue = "no_adapter"; return false; }
                    result.LastEvaluatedValue = adapter.InGameplayScene.ToString().ToLowerInvariant();
                    return adapter.InGameplayScene;

                case "game_ready":
                    if (bridge == null) { result.LastEvaluatedValue = "no_bridge"; return false; }
                    result.LastEvaluatedValue = bridge.GameReady.ToString().ToLowerInvariant();
                    return bridge.GameReady;

                case "adapter_ready":
                    result.LastEvaluatedValue = (adapter != null).ToString().ToLowerInvariant();
                    return adapter != null;

                case "player_died":
                    if (bridge == null) { result.LastEvaluatedValue = "no_bridge"; return false; }
                    result.LastEvaluatedValue = bridge.PlayerDiedSinceLastReset.ToString().ToLowerInvariant();
                    return bridge.PlayerDiedSinceLastReset;

                case "player_spawned":
                    if (bridge == null) { result.LastEvaluatedValue = "no_bridge"; return false; }
                    result.LastEvaluatedValue = bridge.PlayerSpawnedSinceLastReset.ToString().ToLowerInvariant();
                    return bridge.PlayerSpawnedSinceLastReset;

                case "player_alive":
                    if (adapter == null) { result.LastEvaluatedValue = "no_adapter"; return false; }
                    result.LastEvaluatedValue = adapter.PlayerAlive.ToString().ToLowerInvariant();
                    return adapter.PlayerAlive;

                case "goal_reached":
                    if (bridge == null) { result.LastEvaluatedValue = "no_bridge"; return false; }
                    result.LastEvaluatedValue = bridge.GoalReachedSinceLastReset.ToString().ToLowerInvariant();
                    return bridge.GoalReachedSinceLastReset;

                case "seed_consumed":
                    if (bridge == null) { result.LastEvaluatedValue = "no_bridge"; return false; }
                    result.LastEvaluatedValue = bridge.ItemConsumedSinceLastReset.ToString().ToLowerInvariant();
                    return bridge.ItemConsumedSinceLastReset;

                case "paused_equals":
                {
                    if (adapter == null) { result.LastEvaluatedValue = "no_adapter"; return false; }
                    if (!bool.TryParse(predArg, out var want))
                    {
                        result.Status             = "unknown_predicate";
                        result.LastEvaluatedValue = $"bad_bool_arg=\"{predArg}\"";
                        return false;
                    }
                    var p = adapter.IsPaused;
                    result.LastEvaluatedValue = p.ToString().ToLowerInvariant();
                    return p == want;
                }

                case "ghost_active":
                    if (adapter == null) { result.LastEvaluatedValue = "no_adapter"; return false; }
                    result.LastEvaluatedValue = adapter.GhostActive.ToString().ToLowerInvariant();
                    return adapter.GhostActive;

                case "async_done":
                {
                    if (bridge == null) { result.LastEvaluatedValue = "no_bridge"; return false; }
                    var rec = bridge.FindAsync(predArg);
                    if (rec == null) { result.LastEvaluatedValue = $"no_record_for={predArg}"; return false; }
                    var v = rec.Value;
                    result.LastEvaluatedValue = $"name={v.Name};success={v.Success};error={v.Error ?? ""}";
                    return true;
                }

                case "state_equals":
                {
                    // arg is "<key>=<value>"
                    var eq = predArg.IndexOf('=');
                    if (eq < 0)
                    {
                        result.Status             = "unknown_predicate";
                        result.LastEvaluatedValue = $"bad_state_equals_arg=\"{predArg}\"";
                        return false;
                    }
                    var k = predArg.Substring(0, eq);
                    var want = predArg.Substring(eq + 1);
                    AthStateDispatcher.Resolve(k, out var status, out var got, out _, out _, out _);
                    result.LastEvaluatedValue = $"key={k};want=\"{want}\";got=\"{got}\";status={status}";
                    return status == "ok" && got == want;
                }

                case "spawn_attempts_at_least":
                {
                    if (adapter == null) { result.LastEvaluatedValue = "no_adapter"; return false; }
                    if (!int.TryParse(predArg, out var want))
                    {
                        result.Status             = "unknown_predicate";
                        result.LastEvaluatedValue = $"bad_int_arg=\"{predArg}\"";
                        return false;
                    }
                    var n = adapter.SpawnAttempts;
                    result.LastEvaluatedValue = $"attempts={n};want>={want}";
                    return n >= want;
                }

                default:
                    result.Status             = "unknown_predicate";
                    result.LastEvaluatedValue = predName;
                    return false;
            }
        }
    }
}
