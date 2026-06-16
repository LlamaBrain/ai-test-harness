#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthBridge — DontDestroyOnLoad singleton holding live runtime state for
// the MCP state queries. Subscribes to the registered adapter's events,
// promotes them to edge-sticky flags so a wait can converge even if the
// event fired before the wait was issued. Resets edge flags on scene
// unload so a stale prior-session signal cannot satisfy a fresh wait.
//
// Mirrors dirigible's DirigibleTestBridge — DI scope walking removed
// (the host adapter is registered explicitly), R3/UniTask dependencies
// dropped (plain events + System.Threading.Tasks.Task).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamaBrainLabs.Ath
{
    public sealed class AthBridge : MonoBehaviour
    {
        public static AthBridge Instance { get; private set; }

        // ---- edge-sticky flags (reset by harness.reset + scene unload) ----
        public bool PlayerDiedSinceLastReset    { get; private set; }
        public bool PlayerSpawnedSinceLastReset { get; private set; }
        public bool PlayerRespawnedSinceLastReset { get; private set; }
        public bool PlayerRestartedSinceLastReset { get; private set; }
        public bool GoalReachedSinceLastReset   { get; private set; }
        public bool ItemConsumedSinceLastReset  { get; private set; }
        public bool LastPauseValue              { get; private set; }
        public int  PauseChangeCount            { get; private set; }

        // ---- game-ready (event-fired OR adapter-getter-true, validity-guarded) ----
        private bool _gameReadyEventFired;
        public bool GameReady =>
            _gameReadyEventFired
            || (AthServices.Adapter is { IsValid: true } a && a.GameReady);

        // ---- async-op ring buffer (mirror DirigibleTestBridge.cs:57-68) ----
        private const int RING_CAPACITY = 16;
        private readonly LinkedList<AthAsyncOpRecord> _asyncOps = new();

        public AthAsyncOpRecord? FindAsync(string correlationId)
        {
            foreach (var rec in _asyncOps)
                if (rec.CorrelationId == correlationId) return rec;
            return null;
        }

        public AthAsyncOpRecord? LastAsync()
            => _asyncOps.Count == 0 ? (AthAsyncOpRecord?)null : _asyncOps.Last.Value;

        public void TrackAsync(string name, string correlationId, Task task)
        {
            // Fire-and-forget tracker — registers a record in the ring buffer
            // when the task completes.
            _ = TrackAsyncInner(name, correlationId, task);
        }

        private async Task TrackAsyncInner(string name, string correlationId, Task task)
        {
            bool ok = true;
            string err = null;
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { ok = false; err = ex.Message; }
            PushAsync(new AthAsyncOpRecord(name, correlationId, ok, err));
        }

        private void PushAsync(AthAsyncOpRecord rec)
        {
            _asyncOps.AddLast(rec);
            while (_asyncOps.Count > RING_CAPACITY) _asyncOps.RemoveFirst();
        }

        // ---- attached adapter (cached so detach matches what was attached) ----
        private IAthHostAdapter _attachedAdapter;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded   += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            AthServices.Bridge = this;

            // Late-binding handshake: if Register was called before our Awake,
            // AthServices.Adapter is set — attach now.
            if (AthServices.Adapter != null)
                AttachToAdapter(AthServices.Adapter);
        }

        internal void AttachToAdapter(IAthHostAdapter adapter)
        {
            DetachFromAdapter();
            if (adapter == null) return;

            _attachedAdapter = adapter;
            var ev = adapter.Events;
            if (ev == null) return;

            ev.GameReady       += OnGameReady;
            ev.PlayerSpawned   += OnPlayerSpawned;
            ev.PlayerRespawned += OnPlayerRespawned;
            ev.PlayerRestarted += OnPlayerRestarted;
            ev.PlayerDied      += OnPlayerDied;
            ev.GoalReached     += OnGoalReached;
            ev.ItemConsumed    += OnItemConsumed;
            ev.PauseChanged    += OnPauseChanged;
        }

        internal void DetachFromAdapter()
        {
            if (_attachedAdapter == null) return;
            var ev = _attachedAdapter.Events;
            if (ev != null)
            {
                ev.GameReady       -= OnGameReady;
                ev.PlayerSpawned   -= OnPlayerSpawned;
                ev.PlayerRespawned -= OnPlayerRespawned;
                ev.PlayerRestarted -= OnPlayerRestarted;
                ev.PlayerDied      -= OnPlayerDied;
                ev.GoalReached     -= OnGoalReached;
                ev.ItemConsumed    -= OnItemConsumed;
                ev.PauseChanged    -= OnPauseChanged;
            }
            _attachedAdapter = null;
        }

        // ---- event handlers (edge promotion) ----
        private void OnGameReady()       => _gameReadyEventFired = true;
        private void OnPlayerSpawned()   => PlayerSpawnedSinceLastReset = true;
        private void OnPlayerRespawned() => PlayerRespawnedSinceLastReset = true;
        private void OnPlayerRestarted() => PlayerRestartedSinceLastReset = true;
        private void OnPlayerDied()      => PlayerDiedSinceLastReset = true;
        private void OnGoalReached()     => GoalReachedSinceLastReset = true;
        private void OnItemConsumed()    => ItemConsumedSinceLastReset = true;
        private void OnPauseChanged(bool isPaused)
        {
            LastPauseValue = isPaused;
            PauseChangeCount++;
        }

        // ---- harness.reset support ----
        public void ResetEdgeFlags()
        {
            PlayerDiedSinceLastReset = false;
            PlayerSpawnedSinceLastReset = false;
            PlayerRespawnedSinceLastReset = false;
            PlayerRestartedSinceLastReset = false;
            GoalReachedSinceLastReset = false;
            ItemConsumedSinceLastReset = false;
            // _gameReadyEventFired intentionally NOT reset here — game readiness
            // is a once-per-session signal, not an edge to re-arm. Scene unload
            // handles the cross-session reset.
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Defensive re-attach: if the adapter changed identity since last
            // load (e.g. host bootstrap recreated it because IsValid went
            // false), refresh our subscription.
            var current = AthServices.Adapter;
            if (current != _attachedAdapter)
                AttachToAdapter(current);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            // Reset edge flags so stale prior-session signals cannot satisfy
            // a fresh wait. Mirrors DirigibleTestBridge.cs:228.
            ResetEdgeFlags();
            _gameReadyEventFired = false;

            // Defense: if the adapter has become invalid (its underlying host
            // singleton was destroyed during unload), detach now so its
            // residual events cannot fire into us.
            if (_attachedAdapter != null && !_attachedAdapter.IsValid)
                DetachFromAdapter();
        }

        private void OnDestroy()
        {
            DetachFromAdapter();
            SceneManager.sceneLoaded   -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (Instance == this) Instance = null;
            if (AthServices.Bridge == this) AthServices.ClearBridge();
        }
    }
}
#endif
