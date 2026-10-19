#nullable enable

using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using LlamaBrainLabs.Ath.RuntimeSmokes;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthRuntimeSmoke
    {
        public sealed class ListResult
        {
            public string Status = "ok";
            public string Diagnostic = "";
            public RuntimeSmokeWorkflowListItem[] Workflows = Array.Empty<RuntimeSmokeWorkflowListItem>();
        }

        public sealed class CommandResult
        {
            public bool Accepted;
            public string Status = "";
            public string CorrelationId = "";
            public string Diagnostic = "";
            public string ValueDisplay = "";
            public string ValueDigest = "";
            public string ValueKind = "";
            public bool ValueRedacted;
        }

        public sealed class StatusResult
        {
            public bool Found;
            public string CorrelationId = "";
            public string WorkflowId = "";
            public string ScriptHash = "";
            public string State = "unknown";
            public string FailureCode = "";
            public string ActiveStepId = "";
            public string ActiveCheckpointId = "";
            public string Scenario = "";
            public string Seed = "";
            public string ArtifactDirectory = "";
            public string[] ArtifactNames = Array.Empty<string>();
            public StatusProgress Progress = new StatusProgress();
            public RuntimeSmokeInvestigationAction[] InvestigationActions = Array.Empty<RuntimeSmokeInvestigationAction>();
            public RuntimeSmokeInvestigationAction[] CaptureActions = Array.Empty<RuntimeSmokeInvestigationAction>();
            public RuntimeSmokeTranscriptRecord[] Transcript = Array.Empty<RuntimeSmokeTranscriptRecord>();
            public RuntimeSmokeFailureContext? FailureContext;
        }

        public sealed class StatusProgress
        {
            public bool IsIndeterminate;
            public int CompletedSteps;
            public string ActiveStepId = "";
            public long CompletedWeight;
            public long TotalWeight;
            public int Percent;
        }

        [McpPluginTool("ath-runtime-smoke-list", Title = "AI Test Harness / RuntimeSmoke List", ReadOnlyHint = true, IdempotentHint = true)]
        [Description("List checked-in RuntimeSmoke workflows available for a declared host target and execution mode.")]
        public ListResult List(
            [Description("Target lane: editor, development, or smoke_harness.")] string target,
            [Description("Execution mode: fsd, ai_observed, or hitl.")] string mode)
        {
            if (!TryTarget(target, out var parsedTarget) || !TryMode(mode, out var parsedMode))
                return new ListResult { Status = "invalid_request", Diagnostic = "Target or mode is not a declared RuntimeSmoke value." };
            return MainThread.Instance.Run(() =>
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null) return new ListResult { Status = "not_registered", Diagnostic = "This host has not registered a RuntimeSmoke workflow catalog." };
                return new ListResult { Workflows = new System.Collections.Generic.List<RuntimeSmokeWorkflowListItem>(registry.List(parsedTarget, parsedMode)).ToArray() };
            });
        }

        [McpPluginTool("ath-runtime-smoke-run", Title = "AI Test Harness / RuntimeSmoke Run")]
        [Description("Start one declared RuntimeSmoke workflow with a fresh immutable correlation. Returns immediately with the run ID; poll ath-runtime-smoke-status for terminal evidence.")]
        public CommandResult Run(
            [Description("Stable top-level workflow ID.")] string workflowId,
            [Description("Target lane: editor, development, or smoke_harness.")] string target,
            [Description("Execution mode: fsd, ai_observed, or hitl.")] string mode,
            [Description("Optional unique run correlation. A UUID is generated when omitted.")] string correlationId = "")
        {
            var correlation = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("D") : correlationId;
            if (!TryTarget(target, out var parsedTarget) || !TryMode(mode, out var parsedMode))
                return new CommandResult { Status = "invalid_request", CorrelationId = correlation, Diagnostic = "Target or mode is not a declared RuntimeSmoke value." };
            return MainThread.Instance.Run(() =>
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null) return new CommandResult { Status = "not_registered", CorrelationId = correlation, Diagnostic = "This host has not registered a RuntimeSmoke workflow catalog." };
                var result = registry.Start(workflowId, correlation, parsedTarget, parsedMode);
                return new CommandResult { Accepted = result.Accepted, Status = result.Status, CorrelationId = correlation, Diagnostic = result.Diagnostic ?? "" };
            });
        }

        [McpPluginTool("ath-runtime-smoke-status", Title = "AI Test Harness / RuntimeSmoke Status", ReadOnlyHint = true, IdempotentHint = true)]
        [Description("Read the current transcript tail and terminal status for one immutable RuntimeSmoke correlation.")]
        public StatusResult Status([Description("RuntimeSmoke run correlation returned by ath-runtime-smoke-run.")] string correlationId)
        {
            return MainThread.Instance.Run(() =>
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null) return new StatusResult { CorrelationId = correlationId ?? "", State = "not_registered" };
                var snapshot = registry.GetStatus(correlationId);
                return ProjectStatus(snapshot);
            });
        }

        private static StatusResult ProjectStatus(RuntimeSmokeRegistrySnapshot snapshot)
        {
            if (snapshot == null) return new StatusResult();
            var records = snapshot.Transcript == null
                ? Array.Empty<RuntimeSmokeTranscriptRecord>()
                : new System.Collections.Generic.List<RuntimeSmokeTranscriptRecord>(snapshot.Transcript).ToArray();
            var active = snapshot.State == RuntimeSmokeRegistryRunState.Running || snapshot.State == RuntimeSmokeRegistryRunState.ReviewReady;
            var terminal = snapshot.State == RuntimeSmokeRegistryRunState.Passed || snapshot.State == RuntimeSmokeRegistryRunState.Failed || snapshot.State == RuntimeSmokeRegistryRunState.Cancelled;
            var completed = records.Where(record => record != null && record.type == "step_completed")
                .Select(record => record.stepId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).Count();
            var failed = snapshot.State == RuntimeSmokeRegistryRunState.Failed || snapshot.State == RuntimeSmokeRegistryRunState.Cancelled;
            var failureCode = snapshot.FailureContext?.failureCode;
            return new StatusResult
            {
                Found = snapshot.Found,
                CorrelationId = snapshot.CorrelationId ?? "",
                WorkflowId = snapshot.WorkflowId ?? "",
                ScriptHash = snapshot.ScriptHash ?? "",
                State = StateLabel(snapshot.State),
                FailureCode = failureCode != null && failureCode.Length > 0
                    ? failureCode
                    : RunFailureLabel(snapshot.FailureCode) ?? "",
                ActiveStepId = snapshot.ActiveStepId ?? "",
                ActiveCheckpointId = snapshot.ActiveCheckpointId ?? "",
                Scenario = snapshot.Scenario ?? "",
                Seed = snapshot.Seed ?? "",
                ArtifactDirectory = snapshot.ArtifactDirectory ?? "",
                ArtifactNames = (failed
                    ? new[] { "transcript.jsonl", "result.json", "failure-context.json" }
                    : new[] { "transcript.jsonl", "result.json" })
                    .Concat(snapshot.CaptureArtifacts == null ? Array.Empty<string>() : snapshot.CaptureArtifacts
                        .Where(artifact => artifact != null && !string.IsNullOrEmpty(artifact.path)).Select(artifact => artifact.path))
                    .Concat(snapshot.FailureContext?.captureArtifacts == null ? Array.Empty<string>() : snapshot.FailureContext.captureArtifacts
                        .Where(artifact => artifact != null && !string.IsNullOrEmpty(artifact.path)).Select(artifact => artifact.path))
                    .Distinct(StringComparer.Ordinal).ToArray(),
                Progress = new StatusProgress
                {
                    IsIndeterminate = active,
                    CompletedSteps = completed,
                    ActiveStepId = snapshot.ActiveStepId ?? "",
                    CompletedWeight = snapshot.CompletedProgressWeight,
                    TotalWeight = snapshot.TotalProgressWeight,
                    Percent = snapshot.ProgressPercent
                },
                InvestigationActions = snapshot.State == RuntimeSmokeRegistryRunState.Failed && snapshot.InvestigationActions != null
                    ? new System.Collections.Generic.List<RuntimeSmokeInvestigationAction>(snapshot.InvestigationActions).ToArray()
                    : Array.Empty<RuntimeSmokeInvestigationAction>(),
                CaptureActions = snapshot.State == RuntimeSmokeRegistryRunState.Running || snapshot.State == RuntimeSmokeRegistryRunState.ReviewReady
                    ? new System.Collections.Generic.List<RuntimeSmokeInvestigationAction>(snapshot.CaptureActions ?? Array.Empty<RuntimeSmokeInvestigationAction>()).ToArray()
                    : Array.Empty<RuntimeSmokeInvestigationAction>(),
                Transcript = records,
                FailureContext = snapshot.FailureContext
            };
        }

        private static string StateLabel(RuntimeSmokeRegistryRunState state)
        {
            switch (state)
            {
                case RuntimeSmokeRegistryRunState.Running: return "running";
                case RuntimeSmokeRegistryRunState.ReviewReady: return "review_ready";
                case RuntimeSmokeRegistryRunState.Passed: return "passed";
                case RuntimeSmokeRegistryRunState.Failed: return "failed";
                case RuntimeSmokeRegistryRunState.Cancelled: return "cancelled";
                default: return "unknown";
            }
        }

        private static string RunFailureLabel(RuntimeSmokeRunFailureCode failureCode)
        {
            switch (failureCode)
            {
                case RuntimeSmokeRunFailureCode.RunCancelled: return "run_cancelled";
                case RuntimeSmokeRunFailureCode.RunTimeout: return "run_timeout";
                case RuntimeSmokeRunFailureCode.ProtocolInvalid: return "protocol_invalid";
                case RuntimeSmokeRunFailureCode.PlayerCrashed: return "player_crashed";
                case RuntimeSmokeRunFailureCode.WatchdogTimeout: return "watchdog_timeout";
                default: return "";
            }
        }

        [McpPluginTool("ath-runtime-smoke-resume", Title = "AI Test Harness / RuntimeSmoke Resume")]
        [Description("Resume exactly the named active HITL review checkpoint. Duplicate, stale, and mismatched checkpoint IDs are rejected.")]
        public CommandResult Resume(
            [Description("RuntimeSmoke run correlation.")] string correlationId,
            [Description("Active checkpoint ID shown by status.")] string checkpointId)
        {
            return MainThread.Instance.Run(() =>
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null) return new CommandResult { Status = "not_registered", CorrelationId = correlationId ?? "" };
                var result = registry.Resume(correlationId, checkpointId);
                return new CommandResult { Accepted = result.Accepted, Status = result.Status, CorrelationId = correlationId ?? "", Diagnostic = result.Diagnostic ?? "" };
            });
        }

        [McpPluginTool("ath-runtime-smoke-export-context", Title = "AI Test Harness / RuntimeSmoke Failure Context", ReadOnlyHint = true, IdempotentHint = true)]
        [Description("Export the structured failure-context packet for a terminal failed RuntimeSmoke run. This is diagnosis-only and never retries or mutates the run.")]
        public RuntimeSmokeFailureContext? ExportContext([Description("Failed RuntimeSmoke run correlation.")] string correlationId)
        {
            return MainThread.Instance.Run(() =>
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null) return null;
                var snapshot = registry.GetStatus(correlationId);
                return snapshot.FailureContext;
            });
        }

        [McpPluginTool("ath-runtime-smoke-investigate", Title = "AI Test Harness / RuntimeSmoke Investigate")]
        [Description("Execute exactly one declared, bounded diagnostic action for a terminal failed RuntimeSmoke run. This never retries or changes the run outcome.")]
        public async Task<CommandResult> Investigate(
            [Description("Failed RuntimeSmoke run correlation.")] string correlationId,
            [Description("Stable investigation action ID declared by that workflow.")] string actionId)
        {
            var registry = MainThread.Instance.Run(() => RuntimeSmokeRegistryService.Current);
            if (registry == null) return new CommandResult { Status = "not_registered", CorrelationId = correlationId ?? "" };
            var operation = MainThread.Instance.Run(() => registry.ExecuteInvestigationActionAsync(correlationId, actionId));
            var result = await operation;
            return new CommandResult
            {
                Accepted = result.Accepted,
                Status = result.Status,
                CorrelationId = result.CorrelationId ?? "",
                Diagnostic = result.Diagnostic ?? "",
                ValueDisplay = result.ValueDisplay ?? "",
                ValueDigest = result.ValueDigest ?? "",
                ValueKind = result.ValueKind ?? "",
                ValueRedacted = result.ValueRedacted
            };
        }

        [McpPluginTool("ath-runtime-smoke-capture", Title = "AI Test Harness / RuntimeSmoke Capture")]
        [Description("Execute exactly one declared named capture for an active RuntimeSmoke run or HITL review checkpoint. This does not resume or alter the run outcome.")]
        public async Task<CommandResult> Capture(
            [Description("Active RuntimeSmoke run correlation.")] string correlationId,
            [Description("Stable named_capture action ID declared by that workflow.")] string actionId)
        {
            var registry = MainThread.Instance.Run(() => RuntimeSmokeRegistryService.Current);
            if (registry == null) return new CommandResult { Status = "not_registered", CorrelationId = correlationId ?? "" };
            var operation = MainThread.Instance.Run(() => registry.CaptureAsync(correlationId, actionId));
            var result = await operation;
            return new CommandResult
            {
                Accepted = result.Accepted,
                Status = result.Status,
                CorrelationId = result.CorrelationId ?? "",
                Diagnostic = result.Diagnostic ?? "",
                ValueDisplay = result.ValueDisplay ?? "",
                ValueDigest = result.ValueDigest ?? "",
                ValueKind = result.ValueKind ?? "",
                ValueRedacted = result.ValueRedacted
            };
        }
        [McpPluginTool("ath-runtime-smoke-cancel", Title = "AI Test Harness / RuntimeSmoke Cancel")]
        [Description("Request cancellation of an active RuntimeSmoke. Cancellation is terminal and can never be reported as PASS.")]
        public CommandResult Cancel([Description("RuntimeSmoke run correlation.")] string correlationId)
        {
            return MainThread.Instance.Run(() =>
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null) return new CommandResult { Status = "not_registered", CorrelationId = correlationId ?? "" };
                var result = registry.Cancel(correlationId);
                return new CommandResult { Accepted = result.Accepted, Status = result.Status, CorrelationId = correlationId ?? "", Diagnostic = result.Diagnostic ?? "" };
            });
        }

        private static bool TryTarget(string value, out RuntimeSmokeTarget target)
        {
            switch (value)
            {
                case "editor": target = RuntimeSmokeTarget.Editor; return true;
                case "development": target = RuntimeSmokeTarget.Development; return true;
                case "smoke_harness": target = RuntimeSmokeTarget.SmokeHarness; return true;
                default: target = RuntimeSmokeTarget.Editor; return false;
            }
        }

        private static bool TryMode(string value, out RuntimeSmokeMode mode)
        {
            switch (value)
            {
                case "fsd": mode = RuntimeSmokeMode.Fsd; return true;
                case "ai_observed": mode = RuntimeSmokeMode.AiObserved; return true;
                case "hitl": mode = RuntimeSmokeMode.Hitl; return true;
                default: mode = RuntimeSmokeMode.Fsd; return false;
            }
        }
    }
}
