#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthServices — static locator with two slots: Adapter (host-supplied,
// registered explicitly) and Bridge (instantiated by AthBootstrap at
// runtime). Ownership policy: once the host calls Register, ATH owns
// adapter teardown until Unregister. Mirrors dirigible's DirigibleTestServices
// pattern, minus the DI-resolved global slots (BTS has no DI).

using System;

namespace LlamaBrainLabs.Ath
{
    public static class AthServices
    {
        public static IAthHostAdapter Adapter { get; private set; }
        public static AthBridge       Bridge  { get; internal set; }
        private static readonly AthHostRegistration Registration = new AthHostRegistration();
        public static IAthHost Host => Registration.Host;
        public static AthObservationStore Observations => Registration.Observations;

        /// <summary>Register a game-independent host on the Unity main thread; ownership transfers to ATH.</summary>
        public static void RegisterHost(IAthHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (ReferenceEquals(Host, host)) return;
            Unregister();
            Registration.Register(host);
        }

        /// <summary>
        /// Register a host adapter. If a prior adapter exists, it is detached
        /// from the bridge and disposed before the replacement is attached.
        /// Throws <see cref="ArgumentNullException"/> on null — use
        /// <see cref="Unregister"/> for explicit teardown.
        /// </summary>
        public static void Register(IAthHostAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (ReferenceEquals(Adapter, adapter)) return;
            Unregister();

            Adapter = adapter;
            Registration.Register(new LegacyAthHost(adapter));
            Bridge?.AttachToAdapter(adapter);
        }

        /// <summary>
        /// Detach the current adapter from the bridge, dispose it, and null
        /// the slot. Safe to call multiple times; safe to call when nothing
        /// is registered.
        /// </summary>
        public static void Unregister()
        {
            Bridge?.ResetSession();
            Bridge?.DetachFromAdapter();
            Registration.Unregister();
            var a = Adapter;
            Adapter = null;
            if (a != null)
            {
                try { a.Dispose(); } catch { /* swallow */ }
            }
        }

        /// <summary>
        /// Per-field clear used by harness teardown paths. Does NOT dispose
        /// — call <see cref="Unregister"/> for full teardown.
        /// </summary>
        public static void ClearAdapter()
        {
            if (Host is LegacyAthHost) Registration.Unregister();
            Adapter = null;
        }
        public static void ClearBridge()  => Bridge  = null;
    }
}
#endif
