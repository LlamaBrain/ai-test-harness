// AthTraceWriter — pure, dependency-free serializer for Captain SDLC
// cross-tool trace events (Seam 1; envelope spec in
// the captain-sdlc repo's trace-schema.md). Builds one JSON-Lines record for the
// ath.smoke.completed event kind. No Unity, no Newtonsoft, no IO — struct
// in, string out — so the envelope shape can be unit-tested without an MCP
// attachment or a running editor, mirroring the AthStateDispatcher split.
//
// Newtonsoft is intentionally avoided: the Editor asmdef pins
// overrideReferences with only ReflectorNet/McpPlugin DLLs, so a JSON lib
// is not guaranteed in the graph. The envelope is small and fixed, so a
// correct hand-built encoder is the cheaper dependency.
//
// This file owns the ENVELOPE assembly and the ath.smoke.completed PAYLOAD
// shape in code. The canonical envelope spec is trace-schema.md in the captain-sdlc repo;
// the human-readable payload spec is Documentation~/trace-events.md. Keep all
// three in lockstep — a payload change bumps the package (tool_version); an
// envelope change bumps SchemaVersion.

#nullable enable

using System.Globalization;
using System.Text;
using System;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    /// <summary>
    /// Inputs for one <c>ath.smoke.completed</c> trace event. The caller
    /// supplies <see cref="EventId"/> and <see cref="TimestampIso"/> so this
    /// type stays pure and deterministic — no Guid.NewGuid / DateTime.UtcNow
    /// here; those live in the tool, keeping serialization testable.
    /// </summary>
    internal struct AthSmokeCompletedEvent
    {
        public string  EventId;
        public string  TimestampIso;   // ISO-8601 UTC, e.g. 2026-05-28T17:45:12.234Z
        public string  ToolVersion;    // AthRuntimeFlag.PackageVersion

        // refs
        public string  Project;        // required slug
        public string? Commit;
        public string? DesignDoc;
        public string? TaskId;

        // payload
        public string   Skill;
        public string   SkillVersion;
        public bool     Passed;
        public string?  FailedStep;    // null/empty when Passed
        public string?  Summary;
        public string[]? Artifacts;
        public AthVisualVerificationTrace[]? VisualVerifications;
    }

    internal sealed class AthVisualTraceArtifact
    {
        public string Path = "";
        public string Sha256 = "";
    }

    /// <summary>Ancillary projection of verified RuntimeSmoke evidence, never an acceptance authority.</summary>
    internal sealed class AthVisualVerificationTrace
    {
        public string CorrelationId = "", WorkflowId = "", StepId = "";
        public string PolicyId = "", PolicyVersion = "", Outcome = "", FindingsSummary = "";
        public string? BuildIdentity, BaselineIdentity;
        public AthVisualTraceArtifact[] Artifacts = Array.Empty<AthVisualTraceArtifact>();
    }

    internal static class AthTraceWriter
    {
        public const int    SchemaVersion = 1;
        public const string Tool          = "ath";
        public const string Kind          = "ath.smoke.completed";

        /// <summary>Serialize one event to a single JSON line (no trailing newline).</summary>
        public static string BuildLine(in AthSmokeCompletedEvent ev)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');

            sb.Append("\"schema_version\":").Append(SchemaVersion);
            sb.Append(",\"event_id\":").Append(Str(ev.EventId));
            sb.Append(",\"timestamp\":").Append(Str(ev.TimestampIso));
            sb.Append(",\"tool\":").Append(Str(Tool));
            sb.Append(",\"tool_version\":").Append(Str(ev.ToolVersion));
            sb.Append(",\"kind\":").Append(Str(Kind));

            // refs — canonical references that situate the event. release is
            // always null for a smoke (no release context); design_doc/task_id
            // are emitted when the caller supplies them.
            sb.Append(",\"refs\":{");
            sb.Append("\"project\":").Append(Str(ev.Project));
            sb.Append(",\"commit\":").Append(NullableStr(ev.Commit));
            sb.Append(",\"release\":null");
            sb.Append(",\"design_doc\":").Append(NullableStr(ev.DesignDoc));
            sb.Append(",\"task_id\":").Append(NullableStr(ev.TaskId));
            sb.Append('}');

            // payload — owned by Documentation~/trace-events.md. The perf
            // "envelope summary" the schema mentions is deferred to M7
            // (baseline regression envelope); the minimal first cut records
            // the verdict and enough context to walk back to it.
            sb.Append(",\"payload\":{");
            sb.Append("\"skill\":").Append(Str(ev.Skill));
            sb.Append(",\"skill_version\":").Append(Str(ev.SkillVersion));
            sb.Append(",\"result\":").Append(Str(ev.Passed ? "pass" : "fail"));
            sb.Append(",\"failed_step\":").Append(ev.Passed ? "null" : NullableStr(ev.FailedStep));
            sb.Append(",\"summary\":").Append(NullableStr(ev.Summary));
            sb.Append(",\"artifacts\":").Append(Arr(ev.Artifacts));
            if (ev.VisualVerifications != null && ev.VisualVerifications.Length > 0)
            {
                sb.Append(",\"visual_verification\":[");
                for (int i = 0; i < ev.VisualVerifications.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendVisual(sb, ev.VisualVerifications[i]);
                }
                sb.Append(']');
            }
            sb.Append('}');

            sb.Append('}');
            return sb.ToString();
        }

        // --- JSON helpers (RFC 8259) ---

        static void AppendVisual(StringBuilder sb, AthVisualVerificationTrace visual)
        {
            if (visual == null || string.IsNullOrWhiteSpace(visual.CorrelationId) || string.IsNullOrWhiteSpace(visual.WorkflowId) ||
                string.IsNullOrWhiteSpace(visual.StepId) || string.IsNullOrWhiteSpace(visual.PolicyId) || string.IsNullOrWhiteSpace(visual.PolicyVersion) ||
                (visual.Outcome != "pass" && visual.Outcome != "fail" && visual.Outcome != "inconclusive") || visual.Artifacts == null || visual.Artifacts.Length == 0)
                throw new ArgumentException("Visual trace requires verified identity, policy, outcome and artifact references.");
            sb.Append("{\"correlation_id\":").Append(Str(visual.CorrelationId));
            sb.Append(",\"workflow_id\":").Append(Str(visual.WorkflowId));
            sb.Append(",\"step_id\":").Append(Str(visual.StepId));
            sb.Append(",\"policy_id\":").Append(Str(visual.PolicyId));
            sb.Append(",\"policy_version\":").Append(Str(visual.PolicyVersion));
            sb.Append(",\"outcome\":").Append(Str(visual.Outcome));
            sb.Append(",\"findings_summary\":").Append(Str(visual.FindingsSummary));
            sb.Append(",\"build_identity\":").Append(NullableStr(visual.BuildIdentity));
            sb.Append(",\"baseline_identity\":").Append(NullableStr(visual.BaselineIdentity));
            sb.Append(",\"artifact_base\":\"runtime_smoke_run\",\"artifacts\":[");
            for (int i = 0; i < visual.Artifacts.Length; i++)
            {
                var artifact = visual.Artifacts[i];
                if (artifact == null || !SafeArtifact(artifact.Path) || artifact.Sha256 == null || artifact.Sha256.Length != 64)
                    throw new ArgumentException("Invalid visual artifact reference.");
                foreach (char c in artifact.Sha256) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) throw new ArgumentException("Invalid visual artifact digest.");
                if (i > 0) sb.Append(',');
                sb.Append("{\"path\":").Append(Str(artifact.Path)).Append(",\"sha256\":").Append(Str(artifact.Sha256)).Append('}');
            }
            sb.Append("]}");
        }

        internal static bool SafeArtifact(string value)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith("/", StringComparison.Ordinal) || value.Contains("\\") || value.Contains(":")) return false;
            foreach (var part in value.Split('/')) if (part.Length == 0 || part == "." || part == "..") return false;
            foreach (var c in value) if (char.IsControl(c)) return false;
            return true;
        }

        /// <summary>A required string → quoted+escaped (never the bare null literal).</summary>
        static string Str(string? s) => Quote(s ?? "");

        /// <summary>An optional string → quoted+escaped, or the bare null literal when null/empty.</summary>
        static string NullableStr(string? s) =>
            string.IsNullOrEmpty(s) ? "null" : Quote(s!);

        static string Arr(string[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(items[i] ?? ""));
            }
            sb.Append(']');
            return sb.ToString();
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
