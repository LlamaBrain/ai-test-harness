#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthHostEvents — event surface the host adapter raises into and AthBridge
// subscribes to. Plain C# events; no R3/UniRx/MessagePipe dependency.
//
// Event semantics (binding contract for adapter implementers — see also
// Documentation~/adapter-contract.md):
//   GameReady       — raised at most once per adapter lifetime; idempotent
//                     guard on the adapter side prevents double-fire.
//   PlayerSpawned   — raised every spawn occurrence (bridge promotes to edge).
//   PlayerRespawned — raised every respawn occurrence (bridge promotes).
//   PlayerRestarted — raised every restart occurrence (bridge promotes).
//   PlayerDied      — raised every death (bridge promotes).
//   GoalReached     — raised every goal touch (bridge promotes).
//   ItemConsumed    — raised every consume event (bridge promotes).
//   PauseChanged    — raised on every pause/unpause transition (not edge).

using System;

namespace LlamaBrainLabs.Ath
{
    /// <summary>
    /// Concrete event bag the host adapter exposes via
    /// <see cref="IAthHostAdapter.Events"/>. Adapters call the Raise*
    /// helpers from their own event handlers / lifecycle hooks; the
    /// bridge subscribes to the events at <c>AttachToAdapter</c> time.
    /// </summary>
    public sealed class AthHostEvents
    {
        public event Action<string, string> Observed = delegate { };
        public event Action       GameReady = delegate { };
        public event Action       PlayerSpawned = delegate { };
        public event Action       PlayerRespawned = delegate { };
        public event Action       PlayerRestarted = delegate { };
        public event Action       PlayerDied = delegate { };
        public event Action       GoalReached = delegate { };
        public event Action       ItemConsumed = delegate { };
        public event Action<bool> PauseChanged = delegate { };

        public void Raise(string name, string payload = "")
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || Array.Exists(name.ToCharArray(), char.IsWhiteSpace))
                throw new ArgumentException("Event names must be nonempty tokens of at most 128 characters.", nameof(name));
            Observed.Invoke(name, payload ?? "");
        }
        public void RaiseGameReady()       { GameReady.Invoke(); Raise("game_ready"); }
        public void RaisePlayerSpawned()   { PlayerSpawned.Invoke(); Raise("player_spawned"); }
        public void RaisePlayerRespawned() { PlayerRespawned.Invoke(); Raise("player_respawned"); }
        public void RaisePlayerRestarted() { PlayerRestarted.Invoke(); Raise("player_restarted"); }
        public void RaisePlayerDied()      { PlayerDied.Invoke(); Raise("player_died"); }
        public void RaiseGoalReached()    { GoalReached.Invoke(); Raise("goal_reached"); }
        public void RaiseItemConsumed()   { ItemConsumed.Invoke(); Raise("item_consumed"); }
        public void RaisePauseChanged(bool isPaused) { PauseChanged.Invoke(isPaused); Raise("pause_changed", isPaused.ToString().ToLowerInvariant()); }
    }
}
#endif
