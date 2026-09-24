#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Optional transcript-sink contract for reporting the exact directory containing this run's artifacts.</summary>
    public interface IRuntimeSmokeArtifactLocation
    {
        string RunDirectory { get; }
    }

    public interface IRuntimeSmokeWorkflowCatalog
    {
        IReadOnlyList<RuntimeSmokeCompiledWorkflow> List(RuntimeSmokeTarget target, RuntimeSmokeMode mode);
        bool TryGet(string id, RuntimeSmokeTarget target, RuntimeSmokeMode mode, out RuntimeSmokeCompiledWorkflow workflow);
    }

    public enum RuntimeSmokeRegistryRunState { Unknown, Running, ReviewReady, Passed, Failed, Cancelled }

    public sealed class RuntimeSmokeWorkflowListItem
    {
        public string Id { get; internal set; }
        public string Proposition { get; internal set; }
        public string Target { get; internal set; }
        public string Mode { get; internal set; }
        public string Scenario { get; internal set; }
        public string Seed { get; internal set; }
        public string ScriptHash { get; internal set; }
        public string InvestigationSummary { get; internal set; }
        public int TimeoutSeconds { get; internal set; }
        public IReadOnlyList<string> RequiredCapabilities { get; internal set; }
    }

    public sealed class RuntimeSmokeRegistryCommandResult
    {
        public bool Accepted { get; internal set; }
        public string Status { get; internal set; }
        public string Diagnostic { get; internal set; }
        public string CorrelationId { get; internal set; }
        public string ValueDisplay { get; internal set; }
        public string ValueDigest { get; internal set; }
        public string ValueKind { get; internal set; }
        public bool ValueRedacted { get; internal set; }
    }

    public sealed class RuntimeSmokeInvestigationContext
    {
        public string CorrelationId { get; internal set; }
        public string WorkflowId { get; internal set; }
        public string Scenario { get; internal set; }
        public string Seed { get; internal set; }
        public string RunDirectory { get; internal set; }

        /// <summary>Creates a run-local capture writer from the registry-owned artifact directory.</summary>
        public RuntimeSmokeCaptureArtifactWriter CreateCaptureWriter()
        {
            if (string.IsNullOrWhiteSpace(RunDirectory))
                throw new InvalidOperationException("This RuntimeSmoke host did not expose a claimed run directory.");
            return new RuntimeSmokeCaptureArtifactWriter(RunDirectory);
        }
    }

    public sealed class RuntimeSmokeInvestigationResponse
    {
        public bool Succeeded { get; set; }
        public RuntimeSmokeValue Value { get; set; }
        public string Diagnostic { get; set; }
    }

    /// <summary>Host bridge for the closed set of actions validated in a workflow's investigation contract.</summary>
    public interface IRuntimeSmokeInvestigationAdapter
    {
        Task<RuntimeSmokeInvestigationResponse> ExecuteAsync(RuntimeSmokeInvestigationAction action, RuntimeSmokeInvestigationContext context);
    }

    public sealed class RuntimeSmokeRegistrySnapshot
    {
        public bool Found { get; internal set; }
        public string CorrelationId { get; internal set; }
        public string WorkflowId { get; internal set; }
        public string ScriptHash { get; internal set; }
        public RuntimeSmokeRegistryRunState State { get; internal set; }
        public RuntimeSmokeRunFailureCode FailureCode { get; internal set; }
        public string ActiveStepId { get; internal set; }
        public string ActiveCheckpointId { get; internal set; }
        public string Scenario { get; internal set; }
        public string Seed { get; internal set; }
        public string ArtifactDirectory { get; internal set; }
        public IReadOnlyList<RuntimeSmokeTranscriptRecord> Transcript { get; internal set; }
        public RuntimeSmokeExecutionResult Execution { get; internal set; }
        public RuntimeSmokeFailureContext FailureContext { get; internal set; }
        public IReadOnlyList<RuntimeSmokeInvestigationAction> InvestigationActions { get; internal set; }
        public IReadOnlyList<RuntimeSmokeInvestigationAction> CaptureActions { get; internal set; }
        public IReadOnlyList<RuntimeSmokeCaptureArtifactReference> CaptureArtifacts { get; internal set; }
    }

    /// <summary>Transport-neutral host registry for discoverable workflows and immutable, correlated runs.</summary>
    public sealed class RuntimeSmokeRunRegistry
    {
        private const int MaximumRememberedRuns = 10000;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly IRuntimeSmokeWorkflowCatalog _catalog;
        private readonly Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeStepAdapter> _adapterFactory;
        private readonly Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeTranscriptSink> _transcriptFactory;
        private readonly Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeCleanup> _cleanupFactory;
        private readonly Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeInvestigationAdapter> _investigationAdapterFactory;

        public RuntimeSmokeRunRegistry(IRuntimeSmokeWorkflowCatalog catalog,
            Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeStepAdapter> adapterFactory,
            Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeTranscriptSink> transcriptFactory,
            Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeCleanup> cleanupFactory = null,
            Func<RuntimeSmokeCompiledWorkflow, string, IRuntimeSmokeInvestigationAdapter> investigationAdapterFactory = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
            _transcriptFactory = transcriptFactory ?? throw new ArgumentNullException(nameof(transcriptFactory));
            _cleanupFactory = cleanupFactory;
            _investigationAdapterFactory = investigationAdapterFactory;
        }

        public IReadOnlyList<RuntimeSmokeWorkflowListItem> List(RuntimeSmokeTarget target, RuntimeSmokeMode mode)
        {
            if (!Enum.IsDefined(typeof(RuntimeSmokeTarget), target) || !Enum.IsDefined(typeof(RuntimeSmokeMode), mode))
                return Array.Empty<RuntimeSmokeWorkflowListItem>();
            var workflows = _catalog.List(target, mode) ?? Array.Empty<RuntimeSmokeCompiledWorkflow>();
            return Array.AsReadOnly(workflows.Where(workflow => workflow != null)
                .OrderBy(workflow => workflow.Id, StringComparer.Ordinal)
                .Select(workflow => new RuntimeSmokeWorkflowListItem
                {
                    Id = workflow.Id,
                    Proposition = workflow.Proposition,
                    Target = TargetName(workflow.Target),
                    Mode = ModeName(workflow.Mode),
                    Scenario = workflow.Scenario,
                    Seed = workflow.Seed,
                    ScriptHash = workflow.CanonicalHash,
                    InvestigationSummary = workflow.Investigation == null ? null : workflow.Investigation.summary,
                    TimeoutSeconds = workflow.TimeoutSeconds,
                    RequiredCapabilities = workflow.RequiredCapabilities ?? Array.Empty<string>()
                }).ToArray());
        }

        public RuntimeSmokeRegistryCommandResult Start(string workflowId, string correlationId, RuntimeSmokeTarget target, RuntimeSmokeMode mode,
            string scenario = null, string seed = null)
        {
            if (!IsCorrelationId(correlationId)) return Rejected(correlationId, "correlation_invalid", "Run correlation is not a safe immutable identifier.");
            if (!_catalog.TryGet(workflowId, target, mode, out var workflow) || workflow == null)
                return Rejected(correlationId, "workflow_unavailable", "No compiled workflow matches the requested ID, target, and mode.");

            scenario = string.IsNullOrWhiteSpace(scenario) ? workflow.Scenario : scenario;
            seed = string.IsNullOrWhiteSpace(seed) ? workflow.Seed : seed;
            if (!IsOptionalFixtureToken(scenario) || !IsOptionalFixtureToken(seed))
                return Rejected(correlationId, "fixture_identity_invalid", "Scenario and seed must be safe bounded fixture identifiers.");
            if ((!string.IsNullOrEmpty(workflow.Scenario) && !string.Equals(scenario, workflow.Scenario, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(workflow.Seed) && !string.Equals(seed, workflow.Seed, StringComparison.Ordinal)))
                return Rejected(correlationId, "authored_fixture_mismatch", "Caller-provided scenario or seed does not match the workflow-authored fixture.");

            Session session;
            lock (_gate)
            {
                if (_seen.Contains(correlationId)) return Rejected(correlationId, "correlation_reused", "A RuntimeSmoke correlation cannot be reused.");
                if (_seen.Count >= MaximumRememberedRuns) return Rejected(correlationId, "registry_capacity", "The process run registry reached its safe capacity.");
                session = new Session(correlationId, workflow, scenario, seed);
                _seen.Add(correlationId);
                _sessions.Add(correlationId, session);
            }

            try
            {
                var adapter = _adapterFactory(workflow, correlationId);
                var innerSink = _transcriptFactory(workflow, correlationId);
                session.ArtifactDirectory = (innerSink as IRuntimeSmokeArtifactLocation)?.RunDirectory;
                session.Sink = new SessionSink(session, innerSink);
                var cleanup = _cleanupFactory == null ? adapter as IRuntimeSmokeCleanup : _cleanupFactory(workflow, correlationId);
                if (adapter == null || innerSink == null) throw new InvalidOperationException("Host adapter or transcript sink factory returned null.");
                var executionTask = RuntimeSmokeCoordinator.RunAsync(workflow, correlationId, adapter,
                    session.Sink, cleanup, () => session.CancelRequested,
                    waitForCheckpointResumeAsync: session.WaitForCheckpointResumeAsync, scenario: scenario, seed: seed);
                lock (session.Gate) session.Task = executionTask;
                return new RuntimeSmokeRegistryCommandResult { Accepted = true, Status = "running", CorrelationId = correlationId };
            }
            catch (Exception exception)
            {
                lock (session.Gate) session.Execution = new RuntimeSmokeExecutionResult
                {
                    TerminalState = RuntimeSmokeRunTerminalState.Failed,
                    FailureCode = RuntimeSmokeRunFailureCode.ProtocolInvalid,
                    Diagnostic = "RuntimeSmoke start failed: " + SafeDiagnostic(exception.Message),
                    CorrelationId = correlationId,
                    WorkflowId = workflow.Id,
                    ScriptHash = workflow.CanonicalHash,
                    StartedUtc = DateTime.UtcNow,
                    EndedUtc = DateTime.UtcNow,
                    Transcript = Array.Empty<RuntimeSmokeTranscriptRecord>()
                };
                return new RuntimeSmokeRegistryCommandResult { Accepted = false, Status = "start_failed", Diagnostic = session.Execution.Diagnostic, CorrelationId = correlationId };
            }
        }

        public async Task<RuntimeSmokeRegistryCommandResult> ExecuteInvestigationActionAsync(string correlationId, string actionId)
        {
            Session session;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(correlationId ?? string.Empty, out session))
                    return Rejected(correlationId, "run_unknown", "No run exists for this correlation.");
            }

            RuntimeSmokeInvestigationAction action;
            lock (session.Gate)
            {
                if (session.Execution == null || session.Execution.TerminalState != RuntimeSmokeRunTerminalState.Failed)
                    return Rejected(correlationId, "run_not_failed", "Investigation actions are available only after a failed run.");
                action = (session.Workflow.Investigation?.actions ?? Array.Empty<RuntimeSmokeInvestigationAction>())
                    .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.id, actionId, StringComparison.Ordinal));
                if (action == null) return Rejected(correlationId, "action_not_declared", "The requested diagnostic action is not declared by this workflow.");
                if (!session.ExecutedInvestigationActions.Add(action.id))
                    return Rejected(correlationId, "action_already_executed", "A declared diagnostic action may be executed only once per failed run.");
            }

            var adapter = _investigationAdapterFactory?.Invoke(session.Workflow, correlationId);
            if (adapter == null)
            {
                lock (session.Gate) session.ExecutedInvestigationActions.Remove(action.id);
                return Rejected(correlationId, "investigation_unavailable", "The host has not registered a diagnostic bridge for this workflow.");
            }

            RuntimeSmokeInvestigationResponse response;
            try
            {
                response = await adapter.ExecuteAsync(action, new RuntimeSmokeInvestigationContext
                {
                    CorrelationId = correlationId,
                    WorkflowId = session.Workflow.Id,
                    Scenario = session.Scenario,
                    Seed = session.Seed,
                    RunDirectory = session.ArtifactDirectory
                });
            }
            catch
            {
                response = new RuntimeSmokeInvestigationResponse { Succeeded = false, Diagnostic = "The host diagnostic operation failed." };
            }

            if (response == null) response = new RuntimeSmokeInvestigationResponse { Succeeded = false, Diagnostic = "The host diagnostic operation returned no result." };
            var valueDisplay = response.Value == null ? null : response.Value.ToCanonicalJson();
            var valueDigest = response.Value?.Digest();
            var valueRedacted = valueDisplay != null && valueDisplay.Length > 2048;
            if (valueRedacted) valueDisplay = valueDisplay.Substring(0, 1024) + "… [redacted; sha256=" + valueDigest + "]";

            RuntimeSmokeTranscriptRecord record;
            string contextUpdateFailure = null;
            lock (session.Gate)
            {
                record = new RuntimeSmokeTranscriptRecord
                {
                    sequence = session.Records.Count,
                    type = "investigation_action",
                    timestampUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    workflowId = session.Workflow.Id,
                    scriptHash = session.Workflow.CanonicalHash,
                    correlationId = correlationId,
                    scenario = session.Scenario,
                    seed = session.Seed,
                    status = response.Succeeded ? "passed" : "failed",
                    diagnostic = SafeDiagnostic(response.Diagnostic),
                    investigationActionId = action.id,
                    investigationKind = action.kind,
                    investigationValueDisplay = valueDisplay,
                    investigationValueDigest = valueDigest,
                    investigationValueKind = response.Value == null ? null : response.Value.Kind.ToString().ToLowerInvariant(),
                    investigationValueRedacted = valueRedacted
                };
                try { session.Sink?.Append(record); }
                catch
                {
                    session.ExecutedInvestigationActions.Remove(action.id);
                    return Rejected(correlationId, "transcript_write_failed", "The diagnostic result could not be durably appended to the run transcript.");
                }
                try
                {
                    RuntimeSmokeFailureContextWriter.UpdateInvestigationEvidence(session.ArtifactDirectory, session.Workflow,
                        session.Execution, session.Records);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                    exception is ArgumentException || exception is NotSupportedException || exception is System.Security.SecurityException ||
                    exception is InvalidOperationException || exception is InvalidDataException)
                {
                    contextUpdateFailure = "The investigation result was recorded, but failure-context.json could not be refreshed.";
                }
            }
            return new RuntimeSmokeRegistryCommandResult
            {
                Accepted = response.Succeeded,
                Status = response.Succeeded ? "investigation_completed" : "investigation_failed",
                Diagnostic = SafeDiagnostic(contextUpdateFailure ?? response.Diagnostic),
                CorrelationId = correlationId,
                ValueDisplay = valueDisplay,
                ValueDigest = valueDigest,
                ValueKind = response.Value == null ? null : response.Value.Kind.ToString().ToLowerInvariant(),
                ValueRedacted = valueRedacted
            };
        }

        /// <summary>Executes one workflow-declared screenshot action while its run is still active.</summary>
        public async Task<RuntimeSmokeRegistryCommandResult> CaptureAsync(string correlationId, string actionId)
        {
            Session session;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(correlationId ?? string.Empty, out session))
                    return Rejected(correlationId, "run_unknown", "No run exists for this correlation.");
            }

            RuntimeSmokeInvestigationAction action;
            lock (session.Gate)
            {
                if (session.Execution != null || session.Task == null || session.Task.IsCompleted || session.CancelRequested)
                    return Rejected(correlationId, "run_not_active", "Capture is available only while a RuntimeSmoke run is active.");
                action = (session.Workflow.Investigation?.actions ?? Array.Empty<RuntimeSmokeInvestigationAction>())
                    .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.id, actionId, StringComparison.Ordinal));
                if (action == null) return Rejected(correlationId, "action_not_declared", "The requested capture action is not declared by this workflow.");
                if (!string.Equals(action.kind, "named_capture", StringComparison.Ordinal))
                    return Rejected(correlationId, "action_not_capture", "Only a declared named_capture action may be invoked through capture.");
                if (!session.ExecutedInvestigationActions.Add(action.id))
                    return Rejected(correlationId, "action_already_executed", "A declared capture action may be executed only once per run.");
            }

            var adapter = _investigationAdapterFactory?.Invoke(session.Workflow, correlationId);
            if (adapter == null)
            {
                lock (session.Gate) session.ExecutedInvestigationActions.Remove(action.id);
                return Rejected(correlationId, "capture_unavailable", "The host has not registered a capture bridge for this workflow.");
            }

            RuntimeSmokeInvestigationResponse response;
            try
            {
                response = await adapter.ExecuteAsync(action, new RuntimeSmokeInvestigationContext
                {
                    CorrelationId = correlationId,
                    WorkflowId = session.Workflow.Id,
                    Scenario = session.Scenario,
                    Seed = session.Seed,
                    RunDirectory = session.ArtifactDirectory
                });
            }
            catch
            {
                response = new RuntimeSmokeInvestigationResponse { Succeeded = false, Diagnostic = "The host capture operation failed." };
            }

            if (response == null) response = new RuntimeSmokeInvestigationResponse { Succeeded = false, Diagnostic = "The host capture operation returned no result." };
            var valueDisplay = response.Value == null ? null : response.Value.ToCanonicalJson();
            var valueDigest = response.Value?.Digest();
            var valueRedacted = valueDisplay != null && valueDisplay.Length > 2048;
            if (valueRedacted) valueDisplay = valueDisplay.Substring(0, 1024) + "… [redacted; sha256=" + valueDigest + "]";

            var record = new RuntimeSmokeTranscriptRecord
            {
                sequence = 0,
                type = "capture_action",
                timestampUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                workflowId = session.Workflow.Id,
                scriptHash = session.Workflow.CanonicalHash,
                correlationId = correlationId,
                scenario = session.Scenario,
                seed = session.Seed,
                status = response.Succeeded ? "passed" : "failed",
                diagnostic = SafeDiagnostic(response.Diagnostic),
                investigationActionId = action.id,
                investigationKind = action.kind,
                investigationValueDisplay = valueDisplay,
                investigationValueDigest = valueDigest,
                investigationValueKind = response.Value == null ? null : response.Value.Kind.ToString().ToLowerInvariant(),
                investigationValueRedacted = valueRedacted
            };
            lock (session.Gate)
            {
                record.sequence = session.Records.Count;
                try { session.Sink?.Append(record); }
                catch
                {
                    session.ExecutedInvestigationActions.Remove(action.id);
                    return Rejected(correlationId, "transcript_write_failed", "The capture result could not be durably appended to the run transcript.");
                }
            }
            return new RuntimeSmokeRegistryCommandResult
            {
                Accepted = response.Succeeded,
                Status = response.Succeeded ? "capture_completed" : "capture_failed",
                Diagnostic = SafeDiagnostic(response.Diagnostic),
                CorrelationId = correlationId,
                ValueDisplay = valueDisplay,
                ValueDigest = valueDigest,
                ValueKind = response.Value == null ? null : response.Value.Kind.ToString().ToLowerInvariant(),
                ValueRedacted = valueRedacted
            };
        }

        public RuntimeSmokeRegistrySnapshot GetStatus(string correlationId)
        {
            Session session;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(correlationId ?? string.Empty, out session))
                    return new RuntimeSmokeRegistrySnapshot { Found = false, CorrelationId = correlationId, State = RuntimeSmokeRegistryRunState.Unknown, Transcript = Array.Empty<RuntimeSmokeTranscriptRecord>() };
            }
            RefreshExecution(session);
            return Snapshot(session);
        }

        public RuntimeSmokeRegistryCommandResult Cancel(string correlationId)
        {
            Session session;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(correlationId ?? string.Empty, out session))
                    return Rejected(correlationId, "run_unknown", "No run exists for this correlation.");
                lock (session.Gate)
                {
                    if (session.Task == null || session.Task.IsCompleted || session.Execution != null)
                        return Rejected(correlationId, "run_not_active", "Only an active RuntimeSmoke run can be cancelled.");
                    session.CancelRequested = true;
                }
            }
            return new RuntimeSmokeRegistryCommandResult { Accepted = true, Status = "cancellation_requested", CorrelationId = correlationId };
        }

        public int CancelActiveRuns(RuntimeSmokeTarget target)
        {
            if (!Enum.IsDefined(typeof(RuntimeSmokeTarget), target)) return 0;
            Session[] sessions;
            lock (_gate) sessions = _sessions.Values.ToArray();

            var cancelled = 0;
            foreach (var session in sessions)
            {
                if (session.Workflow.Target != target) continue;
                lock (session.Gate)
                {
                    if (session.Task == null || session.Task.IsCompleted || session.Execution != null) continue;
                    session.CancelRequested = true;
                    cancelled++;
                }
            }
            return cancelled;
        }

        public RuntimeSmokeRegistryCommandResult Resume(string correlationId, string checkpointId)
        {
            Session session;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(correlationId ?? string.Empty, out session))
                    return Rejected(correlationId, "run_unknown", "No run exists for this correlation.");
            }
            lock (session.Gate)
            {
                if (session.Execution != null || session.Task == null || session.Task.IsCompleted)
                    return Rejected(correlationId, "run_not_active", "Only an active RuntimeSmoke checkpoint can be resumed.");
                if (session.CheckpointResume == null || string.IsNullOrEmpty(session.ActiveCheckpointId))
                    return Rejected(correlationId, "run_not_reviewing", "The run is not waiting at a review checkpoint.");
                if (!string.Equals(session.ActiveCheckpointId, checkpointId, StringComparison.Ordinal))
                    return Rejected(correlationId, "checkpoint_mismatch", "Checkpoint ID does not match the active review checkpoint.");
                if (session.CheckpointResume.Task.IsCompleted)
                    return Rejected(correlationId, "checkpoint_already_resumed", "The active checkpoint has already been resumed.");
                session.CheckpointResume.TrySetResult(true);
            }
            return new RuntimeSmokeRegistryCommandResult { Accepted = true, Status = "resumed", CorrelationId = correlationId };
        }

        public async Task<RuntimeSmokeRegistrySnapshot> WaitForCompletionAsync(string correlationId)
        {
            Session session;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(correlationId ?? string.Empty, out session)) return GetStatus(correlationId);
            }
            if (session.Task != null)
            {
                try
                {
                    var execution = await session.Task;
                    lock (session.Gate) session.Execution = execution;
                }
                catch (Exception exception)
                {
                    lock (session.Gate) session.Execution = new RuntimeSmokeExecutionResult
                    {
                        TerminalState = RuntimeSmokeRunTerminalState.Failed,
                        FailureCode = RuntimeSmokeRunFailureCode.ProtocolInvalid,
                        Diagnostic = "RuntimeSmoke task faulted: " + SafeDiagnostic(exception.Message),
                        CorrelationId = session.CorrelationId,
                        WorkflowId = session.Workflow.Id,
                        ScriptHash = session.Workflow.CanonicalHash,
                        StartedUtc = DateTime.UtcNow,
                        EndedUtc = DateTime.UtcNow,
                        Transcript = Array.AsReadOnly(session.Records.ToArray())
                    };
                }
            }
            return Snapshot(session);
        }

        private static void RefreshExecution(Session session)
        {
            lock (session.Gate)
            {
                if (session.Execution == null && session.Task != null && session.Task.IsCompletedSuccessfully)
                    session.Execution = session.Task.Result;
            }
        }

        private static RuntimeSmokeRegistrySnapshot Snapshot(Session session)
        {
            lock (session.Gate)
            {
            var execution = session.Execution;
            var state = execution == null ? (session.ActiveCheckpointId != null && session.CheckpointResume != null && !session.CheckpointResume.Task.IsCompleted
                    ? RuntimeSmokeRegistryRunState.ReviewReady : RuntimeSmokeRegistryRunState.Running) :
                execution.TerminalState == RuntimeSmokeRunTerminalState.Passed ? RuntimeSmokeRegistryRunState.Passed :
                execution.TerminalState == RuntimeSmokeRunTerminalState.Cancelled ? RuntimeSmokeRegistryRunState.Cancelled : RuntimeSmokeRegistryRunState.Failed;
            var lastStarted = session.Records.LastOrDefault(record => record.type == "step_started");
            var lastFinishedSequence = session.Records.Where(record => record.type == "step_completed" || record.type == "step_failed" || record.type == "assertion_failed")
                .Select(record => record.sequence).DefaultIfEmpty(-1).Max();
            var activeStep = lastStarted != null && lastStarted.sequence > lastFinishedSequence ? lastStarted.stepId : null;
            return new RuntimeSmokeRegistrySnapshot
            {
                Found = true,
                CorrelationId = session.CorrelationId,
                WorkflowId = session.Workflow.Id,
                ScriptHash = session.Workflow.CanonicalHash,
                State = state,
                FailureCode = execution == null ? RuntimeSmokeRunFailureCode.None : execution.FailureCode,
                CaptureArtifacts = RuntimeSmokeFailureContextWriter.ReadCaptureArtifacts(session.Workflow, session.Records),
                ActiveStepId = activeStep,
                ActiveCheckpointId = state == RuntimeSmokeRegistryRunState.ReviewReady ? session.ActiveCheckpointId : null,
                Scenario = session.Scenario,
                Seed = session.Seed,
                ArtifactDirectory = session.ArtifactDirectory,
                Transcript = Array.AsReadOnly(session.Records.ToArray()),
                Execution = execution,
                FailureContext = execution == null || execution.TerminalState == RuntimeSmokeRunTerminalState.Passed
                    ? null : RuntimeSmokeFailureContextWriter.Create(session.Workflow, execution, session.Records, session.ArtifactDirectory),
                InvestigationActions = execution == null || execution.TerminalState != RuntimeSmokeRunTerminalState.Failed
                    ? Array.Empty<RuntimeSmokeInvestigationAction>()
                    : Array.AsReadOnly((session.Workflow.Investigation?.actions ?? Array.Empty<RuntimeSmokeInvestigationAction>())
                        .Where(action => action != null).ToArray()),
                CaptureActions = execution != null || session.Workflow.Investigation?.actions == null
                    ? Array.Empty<RuntimeSmokeInvestigationAction>()
                    : Array.AsReadOnly(session.Workflow.Investigation.actions.Where(action => action != null && action.kind == "named_capture").ToArray())
            };
            }
        }

        private static RuntimeSmokeRegistryCommandResult Rejected(string correlation, string status, string diagnostic) =>
            new RuntimeSmokeRegistryCommandResult { Accepted = false, Status = status, Diagnostic = diagnostic, CorrelationId = correlation };

        private static string TargetName(RuntimeSmokeTarget target) => target == RuntimeSmokeTarget.Editor ? "editor" :
            target == RuntimeSmokeTarget.Development ? "development" : target == RuntimeSmokeTarget.SmokeHarness ? "smoke_harness" : "invalid";

        private static string ModeName(RuntimeSmokeMode mode) => mode == RuntimeSmokeMode.Fsd ? "fsd" :
            mode == RuntimeSmokeMode.AiObserved ? "ai_observed" : mode == RuntimeSmokeMode.Hitl ? "hitl" : "invalid";

        private static bool IsOptionalFixtureToken(string value) => string.IsNullOrWhiteSpace(value) ||
            value.Length <= 128 && value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static bool IsCorrelationId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static string SafeDiagnostic(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unspecified";
            var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
            return normalized.Length <= 256 ? normalized : normalized.Substring(0, 256) + "...";
        }

        private sealed class Session
        {
            public readonly object Gate = new object();
            public readonly List<RuntimeSmokeTranscriptRecord> Records = new List<RuntimeSmokeTranscriptRecord>();
            public readonly string CorrelationId;
            public readonly RuntimeSmokeCompiledWorkflow Workflow;
            public readonly string Scenario;
            public readonly string Seed;
            public volatile bool CancelRequested;
            public Task<RuntimeSmokeExecutionResult> Task;
            public RuntimeSmokeExecutionResult Execution;
            public string ActiveCheckpointId;
            public string ArtifactDirectory;
            public SessionSink Sink;
            public readonly HashSet<string> ExecutedInvestigationActions = new HashSet<string>(StringComparer.Ordinal);
            public TaskCompletionSource<bool> CheckpointResume;
            public Session(string correlationId, RuntimeSmokeCompiledWorkflow workflow, string scenario, string seed)
            {
                CorrelationId = correlationId;
                Workflow = workflow;
                Scenario = scenario;
                Seed = seed;
            }

            public Task<bool> WaitForCheckpointResumeAsync(string checkpointId)
            {
                lock (Gate)
                {
                    if (!string.Equals(ActiveCheckpointId, checkpointId, StringComparison.Ordinal) || CheckpointResume == null)
                        return System.Threading.Tasks.Task.FromResult(false);
                    return CheckpointResume.Task;
                }
            }
        }

        private sealed class SessionSink : IRuntimeSmokeTranscriptSink, IRuntimeSmokeResultSink
        {
            private readonly Session _session;
            private readonly IRuntimeSmokeTranscriptSink _inner;
            public SessionSink(Session session, IRuntimeSmokeTranscriptSink inner) { _session = session; _inner = inner; }
            public void Append(RuntimeSmokeTranscriptRecord record)
            {
                lock (_session.Gate)
                {
                    _session.Records.Add(record);
                    if (record.type == "review_ready")
                    {
                        _session.ActiveCheckpointId = record.stepId;
                        _session.CheckpointResume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    else if (record.type == "checkpoint_resumed")
                    {
                        _session.ActiveCheckpointId = null;
                        _session.CheckpointResume = null;
                    }
                }
                _inner.Append(record);
            }
            public void WriteResult(RuntimeSmokeExecutionResult result)
            {
                if (_inner is IRuntimeSmokeResultSink resultSink) resultSink.WriteResult(result);
            }
        }
    }
}
#endif
