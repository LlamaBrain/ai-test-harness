#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// IAthHostAdapter — the portability seam. The package knows nothing about
// any host's domain types; everything the harness needs from the host
// flows through this interface. Hosts register their implementation via
// AthServices.Register(adapter).
//
// Contract summary (full text in Documentation~/adapter-contract.md):
//   - Property getters MUST be cheap and side-effect-free. No event raises,
//     no flag mutations, no Debug.Log, no coroutine starts from getters.
//   - Only Request* methods may mutate gameplay state.
//   - Adapter ownership transfers to ATH at Register; ATH may call Dispose
//     during Unregister or replacement Register.
//   - Dispose is idempotent and must be called from the Unity main thread.
//   - Empty Dispose() {} is valid for trivial hosts.

using System;
using UnityEngine;

namespace LlamaBrainLabs.Ath
{
    public interface IAthHostAdapter : IDisposable
    {
        // ---- identity / readiness ----
        string HostName            { get; }
        string ActiveSceneName     { get; }
        bool   InGameplayScene     { get; }
        bool   GameReady           { get; }

        // ---- validity (bootstrap consults on scene reload) ----
        bool IsValid { get; }

        // ---- player slice ----
        bool    PlayerAlive        { get; }
        bool    PlayerHoldingItem  { get; }
        Vector3 PlayerPosition     { get; }
        Vector3 PlayerVelocity     { get; }

        // ---- world slice ----
        int  SpawnAttempts { get; }
        bool IsPaused      { get; }

        // ---- ghost / replay slice ----
        bool GhostActive               { get; }
        bool GhostItemActive           { get; }
        int  LastRunRecordingFrameCount { get; }
        int  LastItemRecordingFrameCount { get; }

        // ---- host-specific state escape hatch ----
        // Returns false if the key is unknown to this adapter; otherwise
        // writes a stable text representation into <paramref name="value"/>.
        bool TryGetCustomState(string key, out string value);

        // ---- action requests (only mutations) ----
        void RequestRestart();
        void RequestPause(bool paused);
        void RequestPlayerKill();
        void RequestPlayerRespawn();
        void RequestPlayerTeleport(float x, float y);
        void RequestPlayerSetVelocity(float vx, float vy);
        void RequestItemConsume();
        void RequestItemTeleport(float x, float y);

        // ---- event surface the bridge subscribes to ----
        AthHostEvents Events { get; }
    }
}
#endif
