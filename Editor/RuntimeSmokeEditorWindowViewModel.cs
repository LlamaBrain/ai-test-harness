#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using LlamaBrainLabs.Ath.RuntimeSmokes;

namespace LlamaBrainLabs.Ath.Editor.RuntimeSmokes
{
    public sealed class RuntimeSmokeEditorWindowInput
    {
        public bool Registered;
        public IReadOnlyList<RuntimeSmokeWorkflowListItem> Workflows = Array.Empty<RuntimeSmokeWorkflowListItem>();
        public string Filter;
        public string SelectedWorkflowId;
        public RuntimeSmokeRegistrySnapshot Snapshot;
    }

    public sealed class RuntimeSmokeEditorWindowControls { public bool CanStart; public bool CanCancel; public bool CanResume; public bool CanCapture; }
    public sealed class RuntimeSmokeEditorProgress { public bool IsIndeterminate; public int CompletedSteps; public string ActiveStepId; public int Percent; }
    public sealed class RuntimeSmokeEditorAssertionFailure { public string AssertionId; public string Name; public string Classification; public string Expected; public string Actual; public string Diagnostic; }

    public sealed class RuntimeSmokeEditorWindowState
    {
        public IReadOnlyList<RuntimeSmokeWorkflowListItem> VisibleWorkflows = Array.Empty<RuntimeSmokeWorkflowListItem>();
        public RuntimeSmokeEditorWindowControls Controls = new RuntimeSmokeEditorWindowControls();
        public RuntimeSmokeEditorProgress Progress = new RuntimeSmokeEditorProgress();
        public RuntimeSmokeWorkflowListItem SelectedWorkflow;
        public RuntimeSmokeEditorAssertionFailure AssertionFailure;
        public RuntimeSmokeFailureContext FailureContext;
        public IReadOnlyList<RuntimeSmokeTranscriptRecord> Transcript = Array.Empty<RuntimeSmokeTranscriptRecord>();
        public IReadOnlyList<RuntimeSmokeInvestigationAction> InvestigationActions = Array.Empty<RuntimeSmokeInvestigationAction>();
        public IReadOnlyList<RuntimeSmokeInvestigationAction> CaptureActions = Array.Empty<RuntimeSmokeInvestigationAction>();
        public IReadOnlyList<string> Artifacts = Array.Empty<string>();
        public string ArtifactDirectory;
        public RuntimeSmokeRegistrySnapshot Snapshot;
        public string CorrelationId;
        public string WorkflowId;
        public string Scenario;
        public string Seed;
        public string ResumeCheckpointId;
        public RuntimeSmokeRegistryRunState RunState = RuntimeSmokeRegistryRunState.Unknown;
    }

    /// <summary>Pure projection of the host registry state for the Unity window and focused tests.</summary>
    public static class RuntimeSmokeEditorWindowReducer
    {
        public static RuntimeSmokeEditorWindowState Reduce(RuntimeSmokeEditorWindowInput input)
        {
            input = input ?? new RuntimeSmokeEditorWindowInput();
            var workflows = (input.Workflows ?? Array.Empty<RuntimeSmokeWorkflowListItem>())
                .Where(item => item != null && Matches(item, input.Filter)).ToArray();
            var snapshot = input.Snapshot;
            var state = snapshot == null ? RuntimeSmokeRegistryRunState.Unknown : snapshot.State;
            var active = state == RuntimeSmokeRegistryRunState.Running || state == RuntimeSmokeRegistryRunState.ReviewReady;
            var records = snapshot == null || snapshot.Transcript == null
                ? Array.Empty<RuntimeSmokeTranscriptRecord>() : snapshot.Transcript.Where(record => record != null).ToArray();
            var completed = records.Where(record => record.type == "step_completed")
                .Select(record => record.stepId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).Count();
            var assertion = records.LastOrDefault(record => record.type == "assertion_failed");
            var context = snapshot == null ? null : snapshot.FailureContext;
            var hasAssertion = assertion != null || context != null && context.assertionId != null;
            var failure = !hasAssertion ? null : assertion == null
                ? new RuntimeSmokeEditorAssertionFailure
                {
                    AssertionId = context.assertionId, Name = context.assertionName,
                    Classification = context.assertionClassification,
                    Expected = context.expectedRedacted ? "<redacted>" : context.expectedDisplay,
                    Actual = context.actualRedacted ? "<redacted>" : context.actualDisplay, Diagnostic = context.diagnostic
                }
                : new RuntimeSmokeEditorAssertionFailure
                {
                    AssertionId = assertion.assertionId, Name = assertion.assertionName,
                    Classification = assertion.assertionClassification,
                    Expected = assertion.expectedRedacted ? "<redacted>" : assertion.expectedDisplay,
                    Actual = assertion.actualRedacted ? "<redacted>" : assertion.actualDisplay, Diagnostic = assertion.diagnostic
                };
            var selectedVisible = workflows.Any(item => string.Equals(item.Id, input.SelectedWorkflowId, StringComparison.Ordinal));
            var selectedWorkflow = workflows.FirstOrDefault(item => string.Equals(item.Id, input.SelectedWorkflowId, StringComparison.Ordinal));
            var terminal = state == RuntimeSmokeRegistryRunState.Passed || state == RuntimeSmokeRegistryRunState.Failed || state == RuntimeSmokeRegistryRunState.Cancelled;
            return new RuntimeSmokeEditorWindowState
            {
                VisibleWorkflows = Array.AsReadOnly(workflows),
                Controls = new RuntimeSmokeEditorWindowControls
                {
                    CanStart = input.Registered && !active && selectedVisible,
                    CanCancel = input.Registered && active,
                    CanResume = input.Registered && state == RuntimeSmokeRegistryRunState.ReviewReady && !string.IsNullOrEmpty(snapshot.ActiveCheckpointId),
                    CanCapture = input.Registered && active && snapshot?.CaptureActions != null && snapshot.CaptureActions.Any(action => action != null),
                },
                Progress = new RuntimeSmokeEditorProgress
                {
                    IsIndeterminate = active, CompletedSteps = completed,
                    ActiveStepId = snapshot == null ? null : snapshot.ActiveStepId, Percent = terminal ? 100 : 0
                },
                SelectedWorkflow = selectedWorkflow,
                AssertionFailure = failure,
                FailureContext = context,
                Transcript = Array.AsReadOnly(records),
                InvestigationActions = state == RuntimeSmokeRegistryRunState.Failed && snapshot?.InvestigationActions != null
                    ? Array.AsReadOnly(snapshot.InvestigationActions.Where(action => action != null).ToArray())
                    : Array.Empty<RuntimeSmokeInvestigationAction>(),
                CaptureActions = active && snapshot?.CaptureActions != null
                    ? Array.AsReadOnly(snapshot.CaptureActions.Where(action => action != null).ToArray())
                    : Array.Empty<RuntimeSmokeInvestigationAction>(),
                Artifacts = snapshot == null || !snapshot.Found ? Array.Empty<string>() : Array.AsReadOnly(
                    (state == RuntimeSmokeRegistryRunState.Failed || state == RuntimeSmokeRegistryRunState.Cancelled
                        ? new[] { "transcript.jsonl", "result.json", "failure-context.json" }
                        : new[] { "transcript.jsonl", "result.json" })
                    .Concat(context?.captureArtifacts == null ? Array.Empty<string>() : context.captureArtifacts
                        .Where(artifact => artifact != null && !string.IsNullOrEmpty(artifact.path)).Select(artifact => artifact.path))
                    .Concat(snapshot.CaptureArtifacts == null ? Array.Empty<string>() : snapshot.CaptureArtifacts
                        .Where(artifact => artifact != null && !string.IsNullOrEmpty(artifact.path)).Select(artifact => artifact.path))
                    .Distinct(StringComparer.Ordinal).ToArray()),
                ArtifactDirectory = snapshot == null ? null : snapshot.ArtifactDirectory,
                Snapshot = snapshot,
                CorrelationId = snapshot == null ? null : snapshot.CorrelationId,
                WorkflowId = snapshot == null ? null : snapshot.WorkflowId,
                Scenario = snapshot == null ? null : snapshot.Scenario,
                Seed = snapshot == null ? null : snapshot.Seed,
                ResumeCheckpointId = state == RuntimeSmokeRegistryRunState.ReviewReady ? snapshot.ActiveCheckpointId : null,
                RunState = state
            };
        }

        private static bool Matches(RuntimeSmokeWorkflowListItem item, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return Contains(item.Id, filter) || Contains(item.Proposition, filter) ||
                Contains(item.Scenario, filter) || Contains(item.Seed, filter) || Contains(item.InvestigationSummary, filter) ||
                (item.RequiredCapabilities ?? Array.Empty<string>()).Any(value => Contains(value, filter));
        }

        private static bool Contains(string value, string filter) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
#endif
