#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
        public event Action       GameReady;
        public event Action       PlayerSpawned;
        public event Action       PlayerRespawned;
        public event Action       PlayerRestarted;
        public event Action       PlayerDied;
        public event Action       GoalReached;
        public event Action       ItemConsumed;
        public event Action<bool> PauseChanged;

        public void RaiseGameReady()        => GameReady?.Invoke();
        public void RaisePlayerSpawned()    => PlayerSpawned?.Invoke();
        public void RaisePlayerRespawned()  => PlayerRespawned?.Invoke();
        public void RaisePlayerRestarted()  => PlayerRestarted?.Invoke();
        public void RaisePlayerDied()       => PlayerDied?.Invoke();
        public void RaiseGoalReached()      => GoalReached?.Invoke();
        public void RaiseItemConsumed()     => ItemConsumed?.Invoke();
        public void RaisePauseChanged(bool isPaused) => PauseChanged?.Invoke(isPaused);
    }
}
#endif
