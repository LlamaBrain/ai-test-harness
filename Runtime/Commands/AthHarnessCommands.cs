#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthHarnessCommands — meta-commands that operate on the harness itself
// rather than the host. Skill authors call harness.reset between sequenced
// waits to clear the bridge's edge-sticky flags; harness.ping is the
// round-trip smoke for log-capture wiring.

using System;
using IngameDebugConsole;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Commands
{
    public static class AthHarnessCommands
    {
        // ---- harness.reset ----
        [ConsoleMethod("harness.reset", "Clear bridge edge-sticky flags (player_died, player_spawned, etc.)")]
        public static void Reset() => Reset(NewId());

        [ConsoleMethod("harness.reset", "Reset; correlation-id form", "correlationId")]
        public static void Reset(string correlationId)
        {
            Debug.Log($"CMD:harness.reset id={correlationId} args=");
            var b = AthBridge.Instance;
            if (b == null) { Debug.Log($"ERR:harness.reset id={correlationId} reason=not_ready"); return; }
            b.ResetEdgeFlags();
            Debug.Log($"OK:harness.reset id={correlationId} cleared=edge_flags");
        }

        // ---- harness.ping ----
        [ConsoleMethod("harness.ping", "Round-trip smoke for log-capture wiring")]
        public static void Ping() => Ping(NewId());

        [ConsoleMethod("harness.ping", "Ping; correlation-id form", "correlationId")]
        public static void Ping(string correlationId)
        {
            Debug.Log($"CMD:harness.ping id={correlationId} args=");
            Debug.Log($"OK:harness.ping id={correlationId} pong=true");
        }

        // ---- harness.set_log_level ----
        [ConsoleMethod("harness.set_log_level", "Set AthRuntimeFlag.LogLevel (none|error|info|verbose)", "level")]
        public static void SetLogLevel(string level) => SetLogLevel(level, NewId());

        [ConsoleMethod("harness.set_log_level", "SetLogLevel; correlation-id form", "level", "correlationId")]
        public static void SetLogLevel(string level, string correlationId)
        {
            Debug.Log($"CMD:harness.set_log_level id={correlationId} args=\"{level}\"");
            if (!Enum.TryParse<AthLogLevel>(level, ignoreCase: true, out var parsed))
            {
                Debug.Log($"ERR:harness.set_log_level id={correlationId} reason=unknown_level given=\"{level}\" valid=\"none|error|info|verbose\"");
                return;
            }
            AthRuntimeFlag.LogLevel = parsed;
            Debug.Log($"OK:harness.set_log_level id={correlationId} level={parsed}");
        }

        // ---- harness.state ----
        // Resolves a state key through the shared AthStateDispatcher (same one
        // the editor ath-state tool uses) so the exe remote console can poll
        // game state over the socket. unknown_key is an ERR (fail-fast);
        // not_ready is a valid OK status for polling waits.
        [ConsoleMethod("harness.state", "Resolve a harness state key (player_alive, scene_name, async:<id>, ...)", "key")]
        public static void State(string key) => State(key, NewId());

        [ConsoleMethod("harness.state", "State; correlation-id form", "key", "correlationId")]
        public static void State(string key, string correlationId)
        {
            AthLog.Cmd("harness.state", correlationId, key);
            AthStateDispatcher.Resolve(
                key,
                out var status,
                out var value,
                out _,
                out _,
                out var customStateAttempted);

            if (status == "unknown_key")
            {
                AthLog.Err("harness.state", correlationId,
                    $"unknown_key custom_state_attempted={customStateAttempted.ToString().ToLowerInvariant()}");
                return;
            }

            AthLog.Ok("harness.state", correlationId,
                $"key={key} value=\"{AthLog.Esc(value)}\" status={status}");
        }

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
#endif
