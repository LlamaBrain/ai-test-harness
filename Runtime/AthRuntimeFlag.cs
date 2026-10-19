#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE || SMOKE_HARNESS
// AthRuntimeFlag — process-wide knobs for the AI Test Harness runtime.
// Mutable from harness commands (harness.set_log_level) but not persisted.

namespace LlamaBrainLabs.Ath
{
    public enum AthLogLevel
    {
        None,
        Error,
        Info,
        Verbose,
    }

    public static class AthRuntimeFlag
    {
        public static AthLogLevel LogLevel = AthLogLevel.Info;

        // Bumped in lockstep with package.json's version field. Exposed through
        // package-version diagnostics and legacy trace metadata.
        public const string PackageVersion = "0.6.1";
    }
}
#endif
