#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
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

        // Bumped in lockstep with package.json's version field. The smoke
        // skills compare this against their frontmatter version constant to
        // detect stale .claude/skills/ copies that did not get re-copied
        // after a package update.
        public const string PackageVersion = "0.3.0";
    }
}
#endif
