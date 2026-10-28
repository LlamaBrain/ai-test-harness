#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
#nullable enable
using System;

namespace LlamaBrainLabs.Ath
{
    /// <summary>Engine-thread-owned registration. Only Observations supports worker-thread access.</summary>
    public sealed class AthHostRegistration : IDisposable
    {
        public IAthHost? Host { get; private set; }
        public AthObservationStore Observations { get; } = new AthObservationStore();

        public void Register(IAthHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (ReferenceEquals(Host, host)) return;
            Unregister();
            Host = host;
            if (host.Events != null) host.Events.Observed += Observations.Observe;
        }

        public void Unregister()
        {
            var old = Host;
            Host = null;
            Observations.BeginSession();
            if (old == null) return;
            if (old.Events != null) old.Events.Observed -= Observations.Observe;
            try { old.Dispose(); } catch { /* Teardown must remain idempotent. */ }
        }

        public void Dispose() => Unregister();
    }
}
#endif
