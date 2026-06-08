// Tool_AthRecord — STUB. Present in the base editor assembly only when
// com.unity.recorder is NOT installed (the ATH_RECORDER define is unset), so the
// `ath-record` tool always exists and degrades cleanly to recorder_unavailable.
//
// The real implementation lives in the optional
// LlamaBrainLabs.Ath.Editor.Recorder assembly, which compiles ONLY when
// ATH_RECORDER is defined (its defineConstraints). Exactly one of stub/real is
// ever compiled, so there is never a duplicate tool id. The stub mirrors the
// real tool's parameter set so a SKILL's `ath-record { ... }` call validates
// against the same schema either way.

#if !ATH_RECORDER
#nullable enable

using System.ComponentModel;
using com.IvanMurzak.McpPlugin;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthRecord
    {
        public sealed class Result
        {
            public string Status = "recorder_unavailable";
            public string Action = "";
        }

        [McpPluginTool(
            "ath-record",
            Title = "AI Test Harness / Record"
        )]
        [Description(
            "Tier-2 motion capture (Unity Recorder mp4) for HITL-validation footage — OPT-IN. " +
            "Install the 'com.unity.recorder' package (>= 4.0.0) to enable it. Until then this stub " +
            "returns Status=recorder_unavailable for every action; a SKILL should fall back to " +
            "ath-snap stills. When enabled, actions are start | stop | query.")]
        public Result Record(
            [Description("'start' | 'stop' | 'query' (no-op here; install com.unity.recorder to enable).")]
            string action = "",
            [Description("Optional clip label. Ignored while Recorder is uninstalled.")]
            string label = "",
            [Description("Capture width. Ignored while Recorder is uninstalled.")]
            int width = 0,
            [Description("Capture height. Ignored while Recorder is uninstalled.")]
            int height = 0,
            [Description("Capture frame rate. Ignored while Recorder is uninstalled.")]
            int frameRate = 30,
            [Description("Trace-relative clip path. Ignored while Recorder is uninstalled.")]
            string path = "")
        {
            return new Result { Action = (action ?? "").Trim().ToLowerInvariant() };
        }
    }
}
#endif
