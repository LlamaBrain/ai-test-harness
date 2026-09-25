#if UNITY_EDITOR || DEVELOPMENT_BUILD
// MinimalAthAdapter — no-op IAthHostAdapter for a fresh-project boundary check.
//
// This sample exists to prove that the LlamaBrainLabs.Ath.Runtime asmdef is
// genuinely independent: it compiles against ONLY the Runtime asmdef
// (no Commands, no Editor, no IngameDebugConsole, no MCP). The planned Phase 8
// clean-project check imports this sample and confirms its adapter compiles
// against Runtime alone. That Unity check has not yet been recorded as passed;
// host_name and sample_marker are diagnostic state, not smoke acceptance.
//
// Every method here is the simplest legal implementation. Real hosts
// (BtsAthAdapter, future-host adapters) should crib structure from this
// file and fill in the bodies with host-specific calls.

using LlamaBrainLabs.Ath;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath.Samples
{
    public sealed class MinimalAthAdapter : IAthHostAdapter
    {
        private readonly AthHostEvents _events = new AthHostEvents();
        private bool _disposed;

        public string HostName        => "MinimalHostAdapter";
        public string ActiveSceneName => SceneManager.GetActiveScene().name;
        public bool   InGameplayScene => true;   // sample is always "in gameplay"
        public bool   GameReady       => true;   // sample is ready from frame 0

        public bool IsValid => !_disposed;

        public bool    PlayerAlive       => false;
        public bool    PlayerHoldingItem => false;
        public Vector3 PlayerPosition    => Vector3.zero;
        public Vector3 PlayerVelocity    => Vector3.zero;

        public int  SpawnAttempts => 0;
        public bool IsPaused      => false;

        public bool GhostActive                  => false;
        public bool GhostItemActive              => false;
        public int  LastRunRecordingFrameCount   => 0;
        public int  LastItemRecordingFrameCount  => 0;

        public bool TryGetCustomState(string key, out string value)
        {
            // The minimal adapter recognizes one custom key for adapter-wiring
            // diagnostics: "sample_marker" → "minimal-ok".
            if (key == "sample_marker") { value = "minimal-ok"; return true; }
            value = "";
            return false;
        }

        // ---- Request* methods: no-ops. ----
        public void RequestRestart() { }
        public void RequestPause(bool paused) { }
        public void RequestPlayerKill() { }
        public void RequestPlayerRespawn() { }
        public void RequestPlayerTeleport(float x, float y) { }
        public void RequestPlayerSetVelocity(float vx, float vy) { }
        public void RequestItemConsume() { }
        public void RequestItemTeleport(float x, float y) { }

        public AthHostEvents Events => _events;

        public void Dispose()
        {
            // Idempotent; nothing to unsubscribe from in the minimal case.
            _disposed = true;
        }
    }

    /// <summary>
    /// One-line bootstrap that registers the minimal adapter at scene-load
    /// time. Drop the sample into a fresh project, enter PlayMode, and the
    /// harness should report this adapter as ready.
    /// </summary>
    internal static class MinimalAthAdapterBootstrap
    {
        private static MinimalAthAdapter _adapter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            if (_adapter != null) return;
            _adapter = new MinimalAthAdapter();
            AthServices.Register(_adapter);
        }
    }
}
#endif
