#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthTestDebugCommands — smoke + introspection commands that any host can
// rely on regardless of project. Mirrors dirigible's TestDebugCommands.cs.
//
// Output convention (load-bearing for Tool_AthCmd's log-capture filter):
//   - CMD:<name> id=<id> args=...    on entry
//   - OK:<name> id=<id> ...          on success
//   - ERR:<name> id=<id> reason=...  on failure
// Every command has a dual overload: a user-typed form that generates a
// fresh correlation id and forwards, plus a correlation-id-bearing form
// that the MCP tool invokes directly.

using System;
using IngameDebugConsole;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath.Commands
{
    public static class AthTestDebugCommands
    {
        // ---- test.echo ----
        [ConsoleMethod("test.echo", "Echo a message back through the harness pipeline", "message")]
        public static void Echo(string message) => Echo(message, NewId());

        [ConsoleMethod("test.echo", "Echo; correlation-id form for MCP", "message", "correlationId")]
        public static void Echo(string message, string correlationId)
        {
            Debug.Log($"CMD:test.echo id={correlationId} args=\"{message}\"");
            Debug.Log($"OK:test.echo id={correlationId} echo=\"{message}\"");
        }

        // ---- test.scene ----
        [ConsoleMethod("test.scene", "Report the active scene name")]
        public static void Scene() => Scene(NewId());

        [ConsoleMethod("test.scene", "Active scene; correlation-id form", "correlationId")]
        public static void Scene(string correlationId)
        {
            Debug.Log($"CMD:test.scene id={correlationId} args=");
            Debug.Log($"OK:test.scene id={correlationId} scene=\"{SceneManager.GetActiveScene().name}\"");
        }

        // ---- test.bridge_ready ----
        [ConsoleMethod("test.bridge_ready", "Report whether AthBridge.Instance is non-null")]
        public static void BridgeReady() => BridgeReady(NewId());

        [ConsoleMethod("test.bridge_ready", "BridgeReady; correlation-id form", "correlationId")]
        public static void BridgeReady(string correlationId)
        {
            Debug.Log($"CMD:test.bridge_ready id={correlationId} args=");
            var ready = AthBridge.Instance != null;
            Debug.Log($"OK:test.bridge_ready id={correlationId} ready={ready.ToString().ToLowerInvariant()}");
        }

        // ---- test.game_ready ----
        [ConsoleMethod("test.game_ready", "Report bridge.GameReady (event-fired OR adapter-validity-guarded)")]
        public static void GameReady() => GameReady(NewId());

        [ConsoleMethod("test.game_ready", "GameReady; correlation-id form", "correlationId")]
        public static void GameReady(string correlationId)
        {
            Debug.Log($"CMD:test.game_ready id={correlationId} args=");
            var b = AthBridge.Instance;
            if (b == null) { Debug.Log($"ERR:test.game_ready id={correlationId} reason=not_ready"); return; }
            Debug.Log($"OK:test.game_ready id={correlationId} ready={b.GameReady.ToString().ToLowerInvariant()}");
        }

        // ---- test.adapter_name ----
        [ConsoleMethod("test.adapter_name", "Report the registered adapter's HostName or <none>")]
        public static void AdapterName() => AdapterName(NewId());

        [ConsoleMethod("test.adapter_name", "AdapterName; correlation-id form", "correlationId")]
        public static void AdapterName(string correlationId)
        {
            Debug.Log($"CMD:test.adapter_name id={correlationId} args=");
            var name = AthServices.Adapter?.HostName ?? "<none>";
            Debug.Log($"OK:test.adapter_name id={correlationId} host=\"{name}\"");
        }

        // ---- test.last_async ----
        [ConsoleMethod("test.last_async", "Report most-recent async-op record")]
        public static void LastAsync() => LastAsync(NewId());

        [ConsoleMethod("test.last_async", "LastAsync; correlation-id form", "correlationId")]
        public static void LastAsync(string correlationId)
        {
            Debug.Log($"CMD:test.last_async id={correlationId} args=");
            var b = AthBridge.Instance;
            if (b == null) { Debug.Log($"ERR:test.last_async id={correlationId} reason=not_ready"); return; }
            var rec = b.LastAsync();
            if (rec == null) { Debug.Log($"OK:test.last_async id={correlationId} empty=true"); return; }
            var v = rec.Value;
            Debug.Log($"OK:test.last_async id={correlationId} name={v.Name} ref={v.CorrelationId} success={v.Success.ToString().ToLowerInvariant()} error=\"{v.Error ?? ""}\"");
        }

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
#endif
