#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
namespace LlamaBrainLabs.Ath
{
    /// <summary>Compatibility view; AthServices retains ownership of the original adapter.</summary>
    public sealed class LegacyAthHost : IAthHost
    {
        private readonly IAthHostAdapter _adapter;
        public LegacyAthHost(IAthHostAdapter adapter)
            => _adapter = adapter ?? throw new System.ArgumentNullException(nameof(adapter));
        public string HostName => _adapter.HostName;
        public bool IsValid => _adapter.IsValid;
        public bool GameReady => IsValid && _adapter.GameReady;
        public AthHostEvents Events => _adapter.Events;
        public bool TryGetState(string key, out string value) => _adapter.TryGetCustomState(key, out value);
        public AthHostCapabilities GetCapabilities() => new AthHostCapabilities
        {
            hostName = HostName,
            capabilities = new[] { "state", "commands", "events", "async", "reset" },
            commands = new[] { "harness.ping", "harness.state", "harness.reset", "harness.snap" }
        };
        public AthCommandResult Execute(string command, string argumentsJson, string correlationId)
            => AthCommandResult.Fail("unsupported");
        public AthCommandResult Reset(string scope) => scope == "edges"
            ? AthCommandResult.Ok() : AthCommandResult.Fail("unsupported_scope");
        public void Dispose() { }
    }
}
#endif
