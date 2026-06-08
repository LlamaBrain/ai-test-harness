// LlamaBrainLabs.Ath.Editor — MCP emitter for the Captain SDLC cross-tool
// trace (Seam 1; envelope spec in the captain-sdlc repo's trace-schema.md). Appends one
// ath.smoke.completed event to <project>/.captain-sdlc/trace/YYYY-MM-DD.jsonl.
//
// An ATH smoke is agent-orchestrated (a SKILL drives the editor through
// ath-cmd/ath-state/ath-wait and the agent computes the verdict), so there is
// no single host process to hook. This tool is the emitter: the SKILL passes
// the verdict + context, and the tool owns CORRECTNESS of the record — it
// mints the event_id, stamps the UTC timestamp, pins
// schema_version/tool/tool_version, resolves the consuming-project trace
// path, and serializes via the pure AthTraceWriter. Mirrors the Tool_AthState
// shape; delegates serialization to AthTraceWriter and IO to AthTraceEmitter
// so both halves can be unit-tested without an MCP attachment.

#nullable enable

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthTraceEmit
    {
        public sealed class Result
        {
            public string Status  = "ok";   // ok | bad_result | io_error:<type>:<msg>
            public string EventId = "";
            public string Kind    = AthTraceWriter.Kind;
            public string Path    = "";      // absolute path of the day's jsonl file
            public string Project = "";      // slug written to refs.project
            public string Verdict = "";      // pass | fail (echoed back)
        }

        [McpPluginTool(
            "ath-trace-emit",
            Title = "AI Test Harness / Trace Emit"
        )]
        [Description(
            "Append one Captain SDLC `ath.smoke.completed` trace event to the " +
            "consuming project's .captain-sdlc/trace/YYYY-MM-DD.jsonl (append-only " +
            "JSON Lines; envelope spec in the captain-sdlc repo's trace-schema.md). Call this " +
            "as the final step of an ATH smoke skill, on BOTH pass and fail, so the " +
            "pipeline can later walk backward from a regression to the smoke that " +
            "should have caught it. The tool mints the event_id, stamps the UTC " +
            "timestamp, and pins schema_version/tool/tool_version — pass only the " +
            "verdict and context. `result` must be exactly 'pass' or 'fail'. Does " +
            "not require PlayMode (emit can run after PlayMode exit); the project " +
            "slug falls back to the project folder name when no adapter is " +
            "registered. Status=ok on success; bad_result for a malformed verdict; " +
            "io_error:<type>:<msg> if the write fails.")]
        public Result Emit(
            [Description("Smoke verdict. Must be exactly 'pass' or 'fail'.")]
            string result,
            [Description("Skill that produced this verdict. Default 'ath-smoke-fullloop'.")]
            string skill = "ath-smoke-fullloop",
            [Description("Skill frontmatter version (e.g. '0.1.0'). Defaults to the package version.")]
            string skillVersion = "",
            [Description("On fail: the step that failed, e.g. 'Step 5'. Ignored on pass.")]
            string failedStep = "",
            [Description("One-line human summary of the run.")]
            string summary = "",
            [Description("Short commit SHA the smoke ran against (e.g. `git rev-parse --short HEAD`). Optional.")]
            string commit = "",
            [Description("Comma-separated trace-relative artifact paths — normally 'media/<file>' produced by ath-snap/ath-record (safe bare filenames also accepted). Absolute/rooted paths, '..' segments, and backslashes are rejected with Status=bad_artifact and no event is written. Optional.")]
            string artifacts = "",
            [Description("Override the project slug in refs. Defaults to the adapter HostName slug, then the project folder name.")]
            string project = "",
            [Description("Design doc this smoke verifies (refs.design_doc). Optional.")]
            string designDoc = "",
            [Description("Task id this smoke verifies (refs.task_id). Optional.")]
            string taskId = "")
        {
            var res = new Result();

            var normalized = (result ?? "").Trim().ToLowerInvariant();
            if (normalized != "pass" && normalized != "fail")
            {
                res.Status = "bad_result";
                return res;
            }
            var passed = normalized == "pass";

            // Validate artifact paths up front (pure string work, no main thread
            // needed) — reject anything that isn't a safe trace-relative path so
            // a malformed entry can never be written into the record or point
            // outside the trace dir. Errors, never silently drops.
            var artifactList = SplitCsv(artifacts);
            if (artifactList != null)
            {
                foreach (var a in artifactList)
                {
                    if (!AthMediaUtil.IsSafeTraceRelativeArtifact(a))
                    {
                        res.Status = "bad_artifact:" + ShortEntry(a);
                        return res;
                    }
                }
            }

            return MainThread.Instance.Run(() =>
            {
                var now = DateTime.UtcNow;

                var slug = !string.IsNullOrWhiteSpace(project)
                    ? Slugify(project)
                    : ResolveProjectSlug();

                var ev = new AthSmokeCompletedEvent
                {
                    EventId      = Guid.NewGuid().ToString(),
                    TimestampIso = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    ToolVersion  = AthRuntimeFlag.PackageVersion,
                    Project      = slug,
                    Commit       = NullIfBlank(commit),
                    DesignDoc    = NullIfBlank(designDoc),
                    TaskId       = NullIfBlank(taskId),
                    Skill        = string.IsNullOrWhiteSpace(skill) ? "ath-smoke-fullloop" : skill.Trim(),
                    SkillVersion = string.IsNullOrWhiteSpace(skillVersion) ? AthRuntimeFlag.PackageVersion : skillVersion.Trim(),
                    Passed       = passed,
                    FailedStep   = passed ? null : NullIfBlank(failedStep),
                    Summary      = NullIfBlank(summary),
                    Artifacts    = artifactList,
                };

                var line = AthTraceWriter.BuildLine(in ev);

                try
                {
                    var root     = AthTraceEmitter.ResolveProjectRoot(Application.dataPath);
                    var fileName = AthTraceEmitter.DailyFileName(now);
                    res.Path = AthTraceEmitter.AppendLine(root, fileName, line);
                }
                catch (Exception ex)
                {
                    res.Status = "io_error:" + ex.GetType().Name + ":" + ex.Message;
                    Debug.LogWarning($"[AthTrace] failed to write {AthTraceWriter.Kind}: {ex.GetType().Name}: {ex.Message}");
                    return res;
                }

                res.EventId = ev.EventId;
                res.Project = slug;
                res.Verdict = normalized;
                Debug.Log($"[AthTrace] {AthTraceWriter.Kind} result={normalized} project={slug} -> {res.Path}");
                return res;
            });
        }

        /// <summary>Adapter HostName slug when an adapter is registered (PlayMode);
        /// otherwise the project folder name, which is always available.</summary>
        static string ResolveProjectSlug()
        {
            var adapter = AthServices.Adapter;
            if (adapter != null && !string.IsNullOrWhiteSpace(adapter.HostName))
                return Slugify(adapter.HostName);

            var root = AthTraceEmitter.ResolveProjectRoot(Application.dataPath);
            return Slugify(new DirectoryInfo(root).Name);
        }

        static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

        static string[]? SplitCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            var parts = csv.Split(',')
                           .Select(x => x.Trim())
                           .Where(x => x.Length > 0)
                           .ToArray();
            return parts.Length > 0 ? parts : null;
        }

        /// <summary>Trim, strip newlines, and cap a rejected artifact entry so it
        /// stays readable inside the bad_artifact status string.</summary>
        static string ShortEntry(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = s!.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return t.Length > 60 ? t.Substring(0, 60) + "…" : t;
        }

        /// <summary>
        /// Slugify a display name into refs.project. camelCase / PascalCase
        /// word boundaries become hyphens ("BeforeTheShade" → "before-the-shade");
        /// runs of acronym caps and digit→cap transitions stay joined ("BTS" →
        /// "bts", "dirigible2D" → "dirigible2d"); non-alphanumerics collapse to a
        /// single hyphen. An explicit `project` arg is the recommended source
        /// (see trace-schema open question on project namespacing); this is the
        /// fallback.
        /// </summary>
        static string Slugify(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            char prev = '\0';
            foreach (var ch in s.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    if (char.IsUpper(ch) && sb.Length > 0 && char.IsLower(prev))
                        sb.Append('-');
                    sb.Append(char.ToLowerInvariant(ch));
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                {
                    sb.Append('-');
                }
                prev = ch;
            }
            var outp = sb.ToString().Trim('-');
            return outp.Length > 0 ? outp : "project";
        }
    }
}
