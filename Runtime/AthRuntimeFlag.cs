#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
    }
}
#endif
