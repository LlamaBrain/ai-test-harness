#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthRemoteConsoleBootstrap — creates the loopback listener AFTER scene load,
// but ONLY when opted in at runtime (-ath-remote-console / ATH_REMOTE_CONSOLE).
// A harness-bearing build with no flag stays completely silent: no GameObject,
// no thread, no bound port. This is the runtime half of the two-lock design
// (the compile-time half is the ATH_REMOTE define).

using UnityEngine;

namespace LlamaBrainLabs.Ath.RemoteConsole
{
    internal static class AthRemoteConsoleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var opts = AthRemoteOptions.FromEnvironment();
            if (!opts.Enabled) return;
            if (AthRemoteConsoleServer.Active != null) return;

            var go = new GameObject("[AthRemoteConsole]") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            var server = go.AddComponent<AthRemoteConsoleServer>();
            server.Configure(opts);
        }
    }
}
#endif
