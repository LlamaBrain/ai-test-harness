#if UNITY_EDITOR || DEVELOPMENT_BUILD
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

            if (Adapter != null)
            {
                Bridge?.DetachFromAdapter();
                try { Adapter.Dispose(); } catch { /* swallow — partial teardown is fine */ }
            }

            Adapter = adapter;
            Bridge?.AttachToAdapter(adapter);
        }

        /// <summary>
        /// Detach the current adapter from the bridge, dispose it, and null
        /// the slot. Safe to call multiple times; safe to call when nothing
        /// is registered.
        /// </summary>
        public static void Unregister()
        {
            Bridge?.DetachFromAdapter();
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
        public static void ClearAdapter() => Adapter = null;
        public static void ClearBridge()  => Bridge  = null;
    }
}
#endif
