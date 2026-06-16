#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthBootstrap — instantiates the AthBridge GameObject before any scene
// loads. No prefab, no host wiring, no manual scene drop — package self-boots
// via RuntimeInitializeOnLoadMethod.
//
// Replaces dirigible's TestBridgePlugin (which used dirigible's plugin system
// and GlobalPluginConfig). Plain RuntimeInitialize is enough for hosts without
// a plugin lifecycle of their own.

using UnityEngine;

namespace LlamaBrainLabs.Ath
{
    internal static class AthBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateBridge()
        {
            if (AthBridge.Instance != null) return;

            var go = new GameObject("[AthBridge]");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<AthBridge>();
            // AthBridge.Awake does DontDestroyOnLoad + AthServices.Bridge = this.
        }
    }
}
#endif
