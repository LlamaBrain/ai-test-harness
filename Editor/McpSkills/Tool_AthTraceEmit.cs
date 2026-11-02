// LlamaBrainLabs.Ath.Editor — MCP emitter for the Captain SDLC cross-tool
// trace (Seam 1; envelope spec in the captain-sdlc repo's trace-schema.md). Appends one
// ath.smoke.completed event to <project>/.captain-sdlc/trace/YYYY-MM-DD.jsonl.
//
// This optional tool records a trace event after an external workflow has
// determined its verdict. It is diagnostic bookkeeping, not RuntimeSmoke
// execution or acceptance. The caller passes verdict and context; the tool
// stamps and serializes the trace record.

#nullable enable

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using LlamaBrainLabs.Ath.RuntimeSmokes;
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
            "after an external workflow when trace bookkeeping is desired. This is not RuntimeSmoke acceptance; the " +
            "pipeline can later locate the diagnostic trace context for a regression. " +
            "This legacy event is not a RuntimeSmoke result and cannot satisfy a " +
            "smoke or build gate. The tool mints the event_id, stamps the UTC " +
            "timestamp, and pins schema_version/tool/tool_version — pass only the " +
            "legacy classification and context. `result` must be exactly 'pass' or 'fail'. Does " +
            "not require PlayMode (emit can run after PlayMode exit); the project " +
            "slug falls back to the project folder name when no adapter is " +
            "registered. Status=ok on success; bad_result for a malformed verdict; " +
            "io_error:<type>:<msg> if the write fails.")]
        public Result Emit(
            [Description("Legacy trace context classification only ('pass' or 'fail'); not a RuntimeSmoke verdict or acceptance result.")]
            string result,
            [Description("Optional legacy workflow/skill label for trace context; not an acceptance authority.")]
            string skill = "legacy-smoke-context",
            [Description("Optional legacy workflow/skill version label for trace context.")]
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
            string taskId = "",
            [Description("Optional completed RuntimeSmoke correlation. Visual trace fields are derived from its registered transcript and reverified artifacts, never from caller verdicts.")]
            string correlationId = "")
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
                AthVisualVerificationTrace[]? visual = null;
                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    try
                    {
                        var snapshot = RuntimeSmokeRegistryService.Current?.GetStatus(correlationId);
                        if (snapshot == null || !snapshot.Found || (snapshot.State != RuntimeSmokeRegistryRunState.Passed && snapshot.State != RuntimeSmokeRegistryRunState.Failed && snapshot.State != RuntimeSmokeRegistryRunState.Cancelled))
                            throw new InvalidOperationException("A completed registered RuntimeSmoke is required.");
                        if (passed != (snapshot.State == RuntimeSmokeRegistryRunState.Passed)) throw new InvalidOperationException("Trace classification conflicts with RuntimeSmoke terminal outcome.");
                        visual = ProjectVisualEvidence(snapshot, commit);
                    }
                    catch (Exception ex) { res.Status = "bad_visual_evidence:" + ex.GetType().Name + ":" + ex.Message; return res; }
                }

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
                    Skill        = string.IsNullOrWhiteSpace(skill) ? "legacy-smoke-context" : skill.Trim(),
                    SkillVersion = string.IsNullOrWhiteSpace(skillVersion) ? AthRuntimeFlag.PackageVersion : skillVersion.Trim(),
                    Passed       = passed,
                    FailedStep   = passed ? null : NullIfBlank(failedStep),
                    Summary      = NullIfBlank(summary),
                    Artifacts    = artifactList,
                    VisualVerifications = visual,
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

        internal static AthVisualVerificationTrace[] ProjectVisualEvidence(RuntimeSmokeRegistrySnapshot snapshot, string buildIdentity)
        {
            var projected = new List<AthVisualVerificationTrace>();
            foreach (var record in snapshot.Transcript ?? Array.Empty<RuntimeSmokeTranscriptRecord>())
            {
                if (record == null || string.IsNullOrEmpty(record.visualEvidenceJson) || (record.type != "step_completed" && record.type != "assertion_failed")) continue;
                var evidence = RuntimeSmokeValueJsonParser.Parse(record.visualEvidenceJson).Map;
                if (evidence["schemaVersion"].Integer != 1 || evidence["correlationId"].String != snapshot.CorrelationId || evidence["workflowId"].String != snapshot.WorkflowId || evidence["stepId"].String != record.stepId)
                    throw new InvalidDataException("Visual correlation does not match registered run.");
                var capture = evidence["capture"].Map;
                var artifacts = new[] {
                    new AthVisualTraceArtifact { Path = capture["artifact"].String, Sha256 = capture["sha256"].String },
                    new AthVisualTraceArtifact { Path = evidence["resultArtifact"].String, Sha256 = evidence["resultSha256"].String },
                    new AthVisualTraceArtifact { Path = evidence["annotatedArtifact"].String, Sha256 = evidence["annotatedSha256"].String }
                };
                byte[]? resultBytes = null;
                for (int i = 0; i < artifacts.Length; i++)
                {
                    var bytes = ReadVerifiedArtifact(snapshot.ArtifactDirectory, artifacts[i]);
                    if (i == 1) resultBytes = bytes;
                }
                var result = RuntimeSmokeValueJsonParser.Parse(Encoding.UTF8.GetString(resultBytes!)).Map;
                if (result["schemaVersion"].Integer != 1 || result["outcome"].String != evidence["outcome"].String) throw new InvalidDataException("Visual result identity mismatch.");
                var policy = result["policy"].Map;
                var findings = result["findings"].Array;
                projected.Add(new AthVisualVerificationTrace {
                    CorrelationId = snapshot.CorrelationId, WorkflowId = snapshot.WorkflowId, StepId = record.stepId,
                    PolicyId = policy["id"].String, PolicyVersion = policy["version"].String, Outcome = result["outcome"].String,
                    BuildIdentity = string.IsNullOrWhiteSpace(buildIdentity) ? null : buildIdentity,
                    FindingsSummary = findings.Count.ToString(CultureInfo.InvariantCulture) + " findings; " +
                        findings.Count(f => f.Map["outcome"].String == "fail").ToString(CultureInfo.InvariantCulture) + " failed; " +
                        findings.Count(f => f.Map["outcome"].String == "inconclusive").ToString(CultureInfo.InvariantCulture) + " inconclusive",
                    Artifacts = artifacts
                });
            }
            if (projected.Count == 0) throw new InvalidDataException("Completed run contains no verified visual assertion evidence.");
            return projected.OrderBy(item => item.StepId, StringComparer.Ordinal).ToArray();
        }

        static byte[] ReadVerifiedArtifact(string directory, AthVisualTraceArtifact artifact)
        {
            if (!AthTraceWriter.SafeArtifact(artifact.Path)) throw new InvalidDataException("Unsafe visual artifact path.");
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, artifact.Path));
            if (!full.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Visual artifact escapes run.");
            for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Visual artifact crosses reparse point.");
            var info = new FileInfo(full);
            if (!info.Exists || info.Length < 1 || info.Length > RuntimeSmokeCaptureArtifactWriter.MaximumPngBytes) throw new InvalidDataException("Missing or oversized visual artifact.");
            var bytes = File.ReadAllBytes(full);
            using (var hash = SHA256.Create())
            {
                var actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (actual != artifact.Sha256) throw new InvalidDataException("Visual artifact digest mismatch.");
            }
            return bytes;
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
            if (csv == null || string.IsNullOrWhiteSpace(csv)) return null;
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
