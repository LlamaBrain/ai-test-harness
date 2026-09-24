#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Host-owned registration point shared by RuntimeSmoke command and MCP transports.</summary>
    public static class RuntimeSmokeRegistryService
    {
        private static readonly object Gate = new object();
        public static RuntimeSmokeRunRegistry Current { get; private set; }

        public static void Register(RuntimeSmokeRunRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            lock (Gate)
            {
                if (Current != null && !ReferenceEquals(Current, registry))
                    throw new InvalidOperationException("A RuntimeSmoke registry is already registered for this host session.");
                Current = registry;
            }
        }

        public static void Unregister(RuntimeSmokeRunRegistry registry)
        {
            lock (Gate)
            {
                if (ReferenceEquals(Current, registry)) Current = null;
            }
        }
    }
}
#endif
