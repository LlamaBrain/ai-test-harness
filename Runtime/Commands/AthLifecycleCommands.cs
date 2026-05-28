#if UNITY_EDITOR || DEVELOPMENT_BUILD
// AthLifecycleCommands — adapter-driven readiness probes. Both commands
// fall back to safe defaults if no adapter is registered, distinguishing
// "harness up but no host attached" from "everything's wired."

using System;
using IngameDebugConsole;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath.Commands
{
    public static class AthLifecycleCommands
    {
        // ---- lifecycle.in_gameplay ----
        [ConsoleMethod("lifecycle.in_gameplay", "Adapter.InGameplayScene (false if no adapter)")]
        public static void InGameplay() => InGameplay(NewId());

        [ConsoleMethod("lifecycle.in_gameplay", "InGameplay; correlation-id form", "correlationId")]
        public static void InGameplay(string correlationId)
        {
            Debug.Log($"CMD:lifecycle.in_gameplay id={correlationId} args=");
            var a = AthServices.Adapter;
            var inGameplay = a?.InGameplayScene ?? false;
            var adapterPresent = a != null;
            Debug.Log($"OK:lifecycle.in_gameplay id={correlationId} in_gameplay={inGameplay.ToString().ToLowerInvariant()} adapter_present={adapterPresent.ToString().ToLowerInvariant()}");
        }

        // ---- lifecycle.scene ----
        [ConsoleMethod("lifecycle.scene", "Adapter.ActiveSceneName, fallback to SceneManager")]
        public static void Scene() => Scene(NewId());

        [ConsoleMethod("lifecycle.scene", "Scene; correlation-id form", "correlationId")]
        public static void Scene(string correlationId)
        {
            Debug.Log($"CMD:lifecycle.scene id={correlationId} args=");
            var fromAdapter = AthServices.Adapter?.ActiveSceneName;
            var sceneName = !string.IsNullOrEmpty(fromAdapter)
                ? fromAdapter
                : SceneManager.GetActiveScene().name;
            var source = !string.IsNullOrEmpty(fromAdapter) ? "adapter" : "scene_manager";
            Debug.Log($"OK:lifecycle.scene id={correlationId} scene=\"{sceneName}\" source={source}");
        }

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
#endif
