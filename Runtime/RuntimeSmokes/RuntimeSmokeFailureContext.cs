#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    [Serializable]
    public sealed class RuntimeSmokeFailureContext
    {
        public int schemaVersion = 1;
        public string correlationId;
        public string workflowId;
        public string scriptHash;
        public string sourceScriptPath;
        public string investigationHash;
        public RuntimeSmokeInvestigationDefinition investigation;
        public string scenario;
        public string seed;
        public string proposition;
        public string target;
        public string mode;
        public string suiteId;
        public string buildRevision;
        public string buildVersion;
        public string profileHash;
        public string contentHash;
        public string backend;
        public string stripping;
        public int playerProcessId;
        public string playerProcessStartUtc;
        public string playerExecutableSha256;
        public string playerLogArtifact;
        public string terminalState;
        public string failureCode;
        public string diagnostic;
        public int assertionSchemaVersion;
        public string assertionId;
        public string assertionName;
        public string assertionComparison;
        public string assertionPredicate;
        public string assertionClassification;
        public string assertionStepId;
        public string assertionTolerance;
        public bool hasAssertionTolerance;
        public long assertionElapsedMilliseconds;
        public string classification;
        public string expectedDigest;
        public string actualDigest;
        public string expectedDisplay;
        public string actualDisplay;
        public bool expectedRedacted;
        public bool actualRedacted;
        public string hostStateKind;
        public string hostStateDigest;
        public string hostStateDisplay;
        public bool hostStateRedacted;
        public RuntimeSmokeCaptureArtifactReference[] captureArtifacts = Array.Empty<RuntimeSmokeCaptureArtifactReference>();
        public string startedUtc;
        public string endedUtc;
        public string reproduction;
        public string reproductionTransport;
        public string reproductionCommand;
        public RuntimeSmokeTranscriptRecord[] transcriptTail = Array.Empty<RuntimeSmokeTranscriptRecord>();
    }

    [Serializable]
    public sealed class RuntimeSmokeCaptureArtifactReference
    {
        public string path;
        public long bytes;
        public string sha256;
    }

    /// <summary>Writes bounded diagnosis-only evidence inside the already claimed run directory.</summary>
    public sealed class RuntimeSmokeFailureContextWriter
    {
        private const int TranscriptTailLimit = 40;
        private readonly string _path;
        private readonly RuntimeSmokeCompiledWorkflow _workflow;

        public RuntimeSmokeFailureContextWriter(string runDirectory, RuntimeSmokeCompiledWorkflow workflow)
        {
            if (string.IsNullOrWhiteSpace(runDirectory)) throw new ArgumentException("Run directory is required.", nameof(runDirectory));
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));
            _path = Path.Combine(Path.GetFullPath(runDirectory), "failure-context.json");
            _workflow = workflow;
        }

        public void Write(RuntimeSmokeExecutionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.TerminalState == RuntimeSmokeRunTerminalState.Passed) return;
            var bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(Create(_workflow, result, runDirectory: Path.GetDirectoryName(_path))) + "\n");
            using (var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        public static void UpdateInvestigationEvidence(string runDirectory, RuntimeSmokeCompiledWorkflow workflow,
            RuntimeSmokeExecutionResult result, System.Collections.Generic.IReadOnlyList<RuntimeSmokeTranscriptRecord> transcript)
        {
            if (string.IsNullOrWhiteSpace(runDirectory)) throw new ArgumentException("Run directory is required.", nameof(runDirectory));
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.TerminalState == RuntimeSmokeRunTerminalState.Passed) return;

            var root = Path.GetFullPath(runDirectory);
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(root);
            var destination = Path.Combine(root, "failure-context.json");
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(destination);
            if (!File.Exists(destination)) throw new FileNotFoundException("The failed run context must exist before investigation updates.", destination);
            var existing = JsonUtility.FromJson<RuntimeSmokeFailureContext>(ReadBoundedFile(destination, 1024 * 1024));
            if (existing == null || existing.correlationId != result.CorrelationId || existing.workflowId != workflow.Id || existing.scriptHash != result.ScriptHash)
                throw new InvalidDataException("The existing failure context does not match the failed run being updated.");

            var bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(Create(workflow, result, transcript, root)) + "\n");
            var temporary = Path.Combine(root, ".failure-context-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(temporary);
                RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(destination);
                File.Replace(temporary, destination, null);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static RuntimeSmokeFailureContext Create(RuntimeSmokeCompiledWorkflow workflow, RuntimeSmokeExecutionResult result,
            System.Collections.Generic.IReadOnlyList<RuntimeSmokeTranscriptRecord> transcriptOverride = null, string runDirectory = null)
        {
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.TerminalState == RuntimeSmokeRunTerminalState.Passed) return null;
            var assertion = result.AssertionResult;
            var transcript = (transcriptOverride ?? result.Transcript ?? Array.Empty<RuntimeSmokeTranscriptRecord>()).Where(record => record != null).ToArray();
            var tail = transcript.Skip(Math.Max(0, transcript.Length - TranscriptTailLimit)).ToArray();
            var context = new RuntimeSmokeFailureContext
            {
                correlationId = result.CorrelationId,
                workflowId = result.WorkflowId,
                scriptHash = result.ScriptHash,
                sourceScriptPath = workflow.SourceScriptPath,
                investigationHash = workflow.InvestigationHash,
                investigation = workflow.Investigation,
                scenario = result.Scenario,
                seed = result.Seed,
                proposition = workflow.Proposition,
                terminalState = result.TerminalState.ToString().ToLowerInvariant(),
                failureCode = result.FailureCode == RuntimeSmokeRunFailureCode.None ? null : RuntimeSmokeCoordinator.RunFailureLabel(result.FailureCode),
                diagnostic = SafeText(result.Diagnostic, 512),
                assertionSchemaVersion = assertion == null ? 0 : assertion.AssertionSchemaVersion,
                assertionId = assertion == null ? null : assertion.AssertionId,
                assertionName = assertion == null ? null : assertion.AssertionName,
                assertionComparison = assertion == null ? null : RuntimeSmokeAssertionSchema.ComparisonLabel(assertion.Comparison),
                assertionPredicate = assertion == null ? null : assertion.Predicate,
                assertionClassification = assertion == null ? null : assertion.Classification,
                assertionStepId = assertion == null ? null : assertion.StepId,
                assertionTolerance = assertion == null || !assertion.Tolerance.HasValue ? null : assertion.Tolerance.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hasAssertionTolerance = assertion != null && assertion.Tolerance.HasValue,
                assertionElapsedMilliseconds = assertion == null ? 0 : assertion.ElapsedMilliseconds,
                classification = assertion == null ? RuntimeSmokeCoordinator.RunFailureLabel(result.FailureCode) : assertion.Classification,
                expectedDigest = assertion == null ? null : assertion.ExpectedDigest,
                actualDigest = assertion == null ? null : assertion.ActualDigest,
                expectedDisplay = assertion == null ? null : assertion.ExpectedDisplay,
                actualDisplay = assertion == null ? null : assertion.ActualDisplay,
                expectedRedacted = assertion != null && assertion.ExpectedRedacted,
                actualRedacted = assertion != null && assertion.ActualRedacted,
                hostStateKind = result.HostState == null ? null : result.HostState.Kind.ToString().ToLowerInvariant(),
                hostStateDigest = result.HostState?.Digest(),
                hostStateDisplay = RenderHostState(result.HostState, out var hostStateRedacted),
                hostStateRedacted = hostStateRedacted,
                captureArtifacts = ReadCaptureArtifacts(workflow, transcript),
                target = TargetName(workflow.Target),
                mode = ModeName(workflow.Mode),
                startedUtc = result.StartedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                endedUtc = result.EndedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                reproductionTransport = ReproductionTransport(workflow),
                reproductionCommand = BuildReproductionCommand(workflow, result.ReproductionCorrelationId),
                reproduction = "Reproduce workflow '" + SafeText(workflow.Id, 160) + "' with the fresh correlation in reproductionCommand; do not reuse '" + SafeText(result.CorrelationId, 160) + "'.",
                transcriptTail = tail
            };
            ReadLaunchEvidence(context, runDirectory, result);
            return context;
        }

        private static string RenderHostState(RuntimeSmokeValue value, out bool redacted)
        {
            redacted = false;
            if (value == null) return null;
            var display = value.ToCanonicalJson();
            if (display.Length <= 2048) return display;
            redacted = true;
            return display.Substring(0, 1024) + "… [redacted; sha256=" + value.Digest() + "]";
        }

        private static string BuildReproductionCommand(RuntimeSmokeCompiledWorkflow workflow, string correlation)
        {
            if (workflow == null || string.IsNullOrWhiteSpace(correlation)) return null;
            var id = SafeText(workflow.Id, 160);
            var runId = SafeText(correlation, 40);
            if (workflow.Mode == RuntimeSmokeMode.Fsd && workflow.Target != RuntimeSmokeTarget.SmokeHarness)
                return "runtime_smoke.run \"" + id + "\" \"" + runId + "\"";
            return "ath-runtime-smoke-run workflowId=\"" + id + "\" target=\"" + TargetName(workflow.Target) +
                "\" mode=\"" + ModeName(workflow.Mode) + "\" correlationId=\"" + runId + "\"";
        }

        private static string ReproductionTransport(RuntimeSmokeCompiledWorkflow workflow) => workflow.Mode == RuntimeSmokeMode.Fsd &&
            workflow.Target != RuntimeSmokeTarget.SmokeHarness ? "development-console" : "editor-mcp";

        private static string TargetName(RuntimeSmokeTarget target) => target == RuntimeSmokeTarget.Editor ? "editor" :
            target == RuntimeSmokeTarget.Development ? "development" : target == RuntimeSmokeTarget.SmokeHarness ? "smoke_harness" : "invalid";

        private static string ModeName(RuntimeSmokeMode mode) => mode == RuntimeSmokeMode.Fsd ? "fsd" :
            mode == RuntimeSmokeMode.AiObserved ? "ai_observed" : mode == RuntimeSmokeMode.Hitl ? "hitl" : "invalid";

        public static RuntimeSmokeCaptureArtifactReference[] ReadCaptureArtifacts(RuntimeSmokeCompiledWorkflow workflow,
            System.Collections.Generic.IReadOnlyList<RuntimeSmokeTranscriptRecord> transcript)
        {
            var actions = workflow.Investigation?.actions ?? Array.Empty<RuntimeSmokeInvestigationAction>();
            var receipts = new System.Collections.Generic.List<RuntimeSmokeCaptureArtifactReference>();
            foreach (var record in transcript ?? Array.Empty<RuntimeSmokeTranscriptRecord>())
            {
                if ((record.type != "investigation_action" && record.type != "capture_action") || record.status != "passed" || record.investigationKind != "named_capture") continue;
                var action = actions.FirstOrDefault(candidate => candidate != null && candidate.id == record.investigationActionId && candidate.kind == "named_capture");
                if (action == null || !IsSafeCaptureLabel(action.captureLabel)) continue;
                try
                {
                    var value = RuntimeSmokeValueJsonParser.Parse(record.investigationValueDisplay ?? string.Empty);
                    RuntimeSmokeValue path = null;
                    var hasPath = value.Kind == RuntimeSmokeValueKind.Map &&
                        (value.Map.TryGetValue("path", out path) || value.Map.TryGetValue("artifact", out path));
                    if (value.Kind != RuntimeSmokeValueKind.Map ||
                        !hasPath || path.Kind != RuntimeSmokeValueKind.String ||
                        path.String != "captures/" + action.captureLabel + ".png" ||
                        !value.Map.TryGetValue("bytes", out var bytes) || bytes.Kind != RuntimeSmokeValueKind.Integer || bytes.Integer <= 0 || bytes.Integer > 32L * 1024 * 1024 ||
                        !value.Map.TryGetValue("sha256", out var hash) || hash.Kind != RuntimeSmokeValueKind.String || !IsSha256(hash.String)) continue;
                    receipts.Add(new RuntimeSmokeCaptureArtifactReference { path = path.String, bytes = bytes.Integer, sha256 = hash.String });
                }
                catch (FormatException) { }
            }
            return receipts.GroupBy(receipt => receipt.path, StringComparer.Ordinal).Select(group => group.Last()).ToArray();
        }

        private static bool IsSafeCaptureLabel(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 &&
            value != "." && value != ".." && value[0] != '.' && value[value.Length - 1] != '.' && !value.Contains("..") &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static bool IsSha256(string value) => value != null && value.Length == 64 &&
            value.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f');

        private static void ReadLaunchEvidence(RuntimeSmokeFailureContext context, string runDirectory, RuntimeSmokeExecutionResult result)
        {
            if (string.IsNullOrWhiteSpace(runDirectory)) return;
            try
            {
                var root = Path.GetFullPath(runDirectory);
                var launchPath = Path.Combine(root, "runtime-smoke-launch.json");
                var armedPath = Path.Combine(root, "runtime-smoke-armed.json");
                if (!File.Exists(launchPath) || !File.Exists(armedPath)) return;
                RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(root);
                RuntimeSmokeLaunchDocument launch;
                RuntimeSmokeArmedDocument armed;
                var launchJson = ReadBoundedFile(launchPath, 65536);
                var armedJson = ReadBoundedFile(armedPath, 65536);
                if (launchJson == null || armedJson == null) return;
                launch = JsonUtility.FromJson<RuntimeSmokeLaunchDocument>(launchJson);
                armed = JsonUtility.FromJson<RuntimeSmokeArmedDocument>(armedJson);
                if (launch == null || armed == null || launch.correlationId != result.CorrelationId || armed.correlationId != result.CorrelationId ||
                    launch.scriptHash != result.ScriptHash || armed.scriptHash != result.ScriptHash || launch.workflowId != result.WorkflowId ||
                    armed.workflowId != result.WorkflowId || armed.processId <= 0 || string.IsNullOrWhiteSpace(armed.processStartUtc) ||
                    armed.executableSha256 != launch.executableSha256 || context.target != launch.target || context.mode != launch.mode) return;
                context.suiteId = SafeText(launch.suiteId, 160);
                context.buildRevision = SafeText(launch.buildRevision, 128);
                context.buildVersion = SafeText(launch.buildVersion, 64);
                context.profileHash = SafeText(launch.profileHash, 128);
                context.contentHash = SafeText(launch.contentHash, 128);
                context.backend = SafeText(launch.backend, 32);
                context.stripping = SafeText(launch.stripping, 32);
                context.playerProcessId = armed.processId;
                context.playerProcessStartUtc = SafeText(armed.processStartUtc, 40);
                context.playerExecutableSha256 = SafeText(armed.executableSha256, 64);

                var expectedLog = Path.GetFullPath(Path.Combine(root, "logs", "player.log"));
                var suppliedLog = string.IsNullOrWhiteSpace(launch.logPath) ? null : Path.GetFullPath(launch.logPath);
                if (suppliedLog != null && PathEquals(suppliedLog, expectedLog) && File.Exists(expectedLog))
                {
                    RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(expectedLog);
                    context.playerLogArtifact = "logs/player.log";
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException || exception is System.Security.SecurityException || exception is InvalidOperationException)
            {
                // Invalid or stale external provenance is omitted rather than trusted or surfaced as a new run failure.
            }
        }

        private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static string ReadBoundedFile(string path, long maximumBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 0 || stream.Length > maximumBytes) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
            }
        }

        private static string SafeText(string value, int limit)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
            return normalized.Length <= limit ? normalized : normalized.Substring(0, limit) + "...";
        }
    }
}
#endif
