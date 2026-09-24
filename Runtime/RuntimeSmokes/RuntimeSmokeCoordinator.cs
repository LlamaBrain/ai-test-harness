#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    public enum RuntimeSmokeRunTerminalState { Passed, Failed, Cancelled }
    public enum RuntimeSmokeRunFailureCode { None, RunCancelled, RunTimeout, ProtocolInvalid, PlayerCrashed, WatchdogTimeout }
    public enum RuntimeSmokeStepResponseStatus { Passed, Failed, TimedOut, Pending }

    public sealed class RuntimeSmokeStepContext
    {
        public string CorrelationId { get; internal set; }
        public string WorkflowId { get; internal set; }
        public string Scenario { get; internal set; }
        public string Seed { get; internal set; }
        public string StepId { get; internal set; }
        public DateTime DeadlineUtc { get; internal set; }
        public int StepIndex { get; internal set; }
    }

    public sealed class RuntimeSmokeStepResponse
    {
        public string CorrelationId { get; set; }
        public RuntimeSmokeStepResponseStatus Status { get; set; }
        public RuntimeSmokeValue Actual { get; set; }
        public string Diagnostic { get; set; }
    }

    public interface IRuntimeSmokeStepAdapter
    {
        Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context);
    }

    /// <summary>Optional read-only host snapshot captured at failure before cleanup restores session state.</summary>
    public interface IRuntimeSmokeFailureStateProvider
    {
        RuntimeSmokeValue CaptureFailureState();
    }

    public interface IRuntimeSmokeCleanup
    {
        void RestoreSessionOverrides();
    }

    /// <summary>Host-owned simulation control used only at a declared HITL review checkpoint.</summary>
    public interface IRuntimeSmokeReviewCheckpointAdapter
    {
        void PauseForReview(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context);
        void ResumeFromReview(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context);
    }

    public interface IRuntimeSmokeTranscriptSink
    {
        void Append(RuntimeSmokeTranscriptRecord record);
    }

    public interface IRuntimeSmokeResultSink
    {
        void WriteResult(RuntimeSmokeExecutionResult result);
    }

    [Serializable]
    public sealed class RuntimeSmokeTranscriptRecord
    {
        public int schemaVersion = 1;
        public long sequence;
        public string type;
        public string timestampUtc;
        public string workflowId;
        public string scriptHash;
        public string correlationId;
        public string scenario;
        public string seed;
        public string proposition;
        public string stepId;
        public string status;
        public string failureCode;
        public string diagnostic;
        public int assertionSchemaVersion;
        public string assertionId;
        public string assertionName;
        public string assertionComparison;
        public string assertionPredicate;
        public string assertionClassification;
        public string assertionTolerance;
        public bool hasAssertionTolerance;
        public long assertionElapsedMilliseconds;
        public string expectedDigest;
        public string actualDigest;
        public string expectedDisplay;
        public string actualDisplay;
        public bool expectedRedacted;
        public bool actualRedacted;
        public string investigationActionId;
        public string investigationKind;
        public string investigationValueDisplay;
        public string investigationValueDigest;
        public string investigationValueKind;
        public bool investigationValueRedacted;
    }

    public sealed class RuntimeSmokeExecutionResult
    {
        public int SchemaVersion { get; internal set; } = 1;
        public RuntimeSmokeRunTerminalState TerminalState { get; internal set; }
        public RuntimeSmokeRunFailureCode FailureCode { get; internal set; }
        public string Diagnostic { get; internal set; }
        public string CorrelationId { get; internal set; }
        public string WorkflowId { get; internal set; }
        public string ScriptHash { get; internal set; }
        public string Scenario { get; internal set; }
        public string Seed { get; internal set; }
        public string ReproductionCorrelationId { get; internal set; } = Guid.NewGuid().ToString("D");
        public DateTime StartedUtc { get; internal set; }
        public DateTime EndedUtc { get; internal set; }
        public RuntimeSmokeAssertionResult AssertionResult { get; internal set; }
        public RuntimeSmokeValue HostState { get; internal set; }
        public IReadOnlyList<RuntimeSmokeTranscriptRecord> Transcript { get; internal set; }
    }

    [Serializable]
    public sealed class RuntimeSmokeResultDocument
    {
        public int schemaVersion = 1;
        public string correlationId;
        public string workflowId;
        public string proposition;
        public string scriptHash;
        public string scenario;
        public string seed;
        public string terminalState;
        public string failureCode;
        public string diagnostic;
        public string startedUtc;
        public string endedUtc;
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
        public string assertionFailureCode;
        public string expectedDigest;
        public string actualDigest;
        public string expectedDisplay;
        public string actualDisplay;
        public bool expectedRedacted;
        public bool actualRedacted;
    }

    /// <summary>Transport-neutral synchronous coordinator. Adapters own bounded predicate polling; the coordinator owns correlation, deadlines, assertions, cleanup, and terminal authority.</summary>
    public static class RuntimeSmokeCoordinator
    {
        private const int MaximumRememberedCorrelations = 10000;
        private static readonly object RegistryLock = new object();
        private static readonly HashSet<string> SeenCorrelations = new HashSet<string>(StringComparer.Ordinal);
        private static bool _active;

        public static async Task<RuntimeSmokeExecutionResult> RunAsync(RuntimeSmokeCompiledWorkflow workflow, string correlationId,
            IRuntimeSmokeStepAdapter adapter, IRuntimeSmokeTranscriptSink sink, IRuntimeSmokeCleanup cleanup = null,
            Func<bool> isCancellationRequested = null, Func<DateTime> utcNow = null,
            Func<string, Task<bool>> waitForCheckpointResumeAsync = null, string scenario = null, string seed = null)
        {
            var now = utcNow ?? (() => DateTime.UtcNow);
            var fixtureMismatch = HasFixtureOverride(workflow?.Scenario, scenario) || HasFixtureOverride(workflow?.Seed, seed);
            scenario = fixtureMismatch || string.IsNullOrWhiteSpace(scenario) ? workflow?.Scenario : scenario;
            seed = fixtureMismatch || string.IsNullOrWhiteSpace(seed) ? workflow?.Seed : seed;
            var started = now();
            var records = new List<RuntimeSmokeTranscriptRecord>();
            var result = new RuntimeSmokeExecutionResult
            {
                TerminalState = RuntimeSmokeRunTerminalState.Failed,
                SchemaVersion = 1,
                FailureCode = RuntimeSmokeRunFailureCode.ProtocolInvalid,
                Diagnostic = fixtureMismatch ? "Run scenario and seed must match the authored workflow fixture." : null,
                CorrelationId = correlationId,
                WorkflowId = workflow == null ? null : workflow.Id,
                ScriptHash = workflow == null ? null : workflow.CanonicalHash,
                Scenario = scenario,
                Seed = seed,
                StartedUtc = started,
                Transcript = records.AsReadOnly()
            };
            if (fixtureMismatch || workflow == null || adapter == null || sink == null || !IsCorrelationId(correlationId) || workflow.Steps == null || workflow.Steps.Count < 2)
                return result;

            lock (RegistryLock)
            {
                if (_active || SeenCorrelations.Contains(correlationId) || SeenCorrelations.Count >= MaximumRememberedCorrelations)
                    return result;
                _active = true;
                SeenCorrelations.Add(correlationId);
            }

            var deadline = started.AddSeconds(workflow.TimeoutSeconds);
            var state = RuntimeSmokeRunTerminalState.Failed;
            var failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
            var diagnostic = (string)null;
            RuntimeSmokeAssertionResult assertionResult = null;
            long sequence = 0;
            var sinkFailed = false;
            long assertionStepStartedTimestamp = 0;
            Action<string, RuntimeSmokeCompiledStep, string, string, string, RuntimeSmokeAssertionResult> emit = (type, step, status, failureCode, detail, assertion) =>
            {
                if (assertion != null && assertion.CorrelationId == null)
                    BindAssertionContext(assertion, workflow, step, correlationId, assertionStepStartedTimestamp);
                var record = new RuntimeSmokeTranscriptRecord
                {
                    sequence = sequence++,
                    type = type,
                    timestampUtc = now().ToString("o", CultureInfo.InvariantCulture),
                    workflowId = workflow.Id,
                    scriptHash = workflow.CanonicalHash,
                    correlationId = correlationId,
                    scenario = scenario,
                    seed = seed,
                    proposition = workflow.Proposition,
                    stepId = step == null ? null : step.Id,
                    status = status,
                    failureCode = failureCode,
                    diagnostic = detail,
                    assertionSchemaVersion = assertion == null ? 0 : assertion.AssertionSchemaVersion,
                    assertionId = assertion == null ? null : assertion.AssertionId,
                    assertionName = assertion == null ? null : assertion.AssertionName,
                    assertionComparison = assertion == null ? null : RuntimeSmokeAssertionSchema.ComparisonLabel(assertion.Comparison),
                    assertionPredicate = assertion == null ? null : assertion.Predicate,
                    assertionClassification = assertion == null ? null : assertion.Classification,
                    assertionTolerance = assertion == null || !assertion.Tolerance.HasValue ? null : assertion.Tolerance.Value.ToString(CultureInfo.InvariantCulture),
                    hasAssertionTolerance = assertion != null && assertion.Tolerance.HasValue,
                    assertionElapsedMilliseconds = assertion == null ? 0 : assertion.ElapsedMilliseconds,
                    expectedDigest = assertion == null ? null : assertion.ExpectedDigest,
                    actualDigest = assertion == null ? null : assertion.ActualDigest,
                    expectedDisplay = assertion == null ? null : assertion.ExpectedDisplay,
                    actualDisplay = assertion == null ? null : assertion.ActualDisplay,
                    expectedRedacted = assertion != null && assertion.ExpectedRedacted,
                    actualRedacted = assertion != null && assertion.ActualRedacted
                };
                records.Add(record);
                try { sink.Append(record); }
                catch (Exception exception) { sinkFailed = true; diagnostic = "Transcript sink failed: " + SafeDiagnostic(exception.Message); }
            };

            try
            {
                emit("run_started", null, "running", null, null, null);
                if (sinkFailed) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; goto Finished; }
                for (var index = 0; index < workflow.Steps.Count; index++)
                {
                    assertionStepStartedTimestamp = Stopwatch.GetTimestamp();
                    var step = workflow.Steps[index];
                    if (step == null) { diagnostic = "Compiled workflow contains a null step."; goto Finished; }
                    if (isCancellationRequested != null && isCancellationRequested())
                    {
                        state = RuntimeSmokeRunTerminalState.Cancelled;
                        failure = RuntimeSmokeRunFailureCode.RunCancelled;
                        diagnostic = "Run cancellation was requested.";
                        goto Finished;
                    }
                    if (now() >= deadline)
                    {
                        failure = RuntimeSmokeRunFailureCode.RunTimeout;
                        diagnostic = "Workflow deadline expired before step execution.";
                        goto Finished;
                    }
                    if (step.Type == "terminal")
                    {
                        if (index != workflow.Steps.Count - 1)
                        {
                            diagnostic = "Terminal step is not the final compiled step.";
                            goto Finished;
                        }
                        state = RuntimeSmokeRunTerminalState.Passed;
                        failure = RuntimeSmokeRunFailureCode.None;
                        goto Finished;
                    }

                    if (step.Type == "sleep")
                    {
                        emit("step_started", step, "running", null, null, null);
                        var sleepDeadline = now().AddSeconds(step.Seconds);
                        while (now() < sleepDeadline)
                        {
                            if (isCancellationRequested != null && isCancellationRequested())
                            {
                                state = RuntimeSmokeRunTerminalState.Cancelled;
                                failure = RuntimeSmokeRunFailureCode.RunCancelled;
                                diagnostic = "Run cancellation was requested during bounded pacing.";
                                goto Finished;
                            }
                            if (now() >= deadline)
                            {
                                failure = RuntimeSmokeRunFailureCode.RunTimeout;
                                diagnostic = "Bounded pacing exceeded the workflow deadline.";
                                goto Finished;
                            }
                            await Task.Delay(25);
                        }
                        emit("step_completed", step, "passed", null, null, null);
                        if (sinkFailed) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; goto Finished; }
                        continue;
                    }

                    if (step.Type == "checkpoint")
                    {
                        var checkpointContext = new RuntimeSmokeStepContext
                        {
                            CorrelationId = correlationId,
                            WorkflowId = workflow.Id,
                            Scenario = scenario,
                            Seed = seed,
                            StepId = step.Id,
                            DeadlineUtc = deadline,
                            StepIndex = index
                        };
                        if (!(adapter is IRuntimeSmokeReviewCheckpointAdapter checkpointAdapter) || waitForCheckpointResumeAsync == null)
                        {
                            failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                            diagnostic = "Review checkpoint requires a host pause adapter and an active run registry.";
                            goto Finished;
                        }
                        emit("step_started", step, "running", null, null, null);
                        var simulationPaused = false;
                        try
                        {
                            checkpointAdapter.PauseForReview(step, checkpointContext);
                            simulationPaused = true;
                            emit("review_ready", step, "review_ready", null, null, null);
                            if (sinkFailed) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; goto Finished; }
                            var resume = waitForCheckpointResumeAsync(step.Id);
                            if (resume == null)
                            {
                                failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                                diagnostic = "Review checkpoint registry did not provide a resume signal.";
                                goto Finished;
                            }
                            while (!resume.IsCompleted)
                            {
                                if (isCancellationRequested != null && isCancellationRequested())
                                {
                                    state = RuntimeSmokeRunTerminalState.Cancelled;
                                    failure = RuntimeSmokeRunFailureCode.RunCancelled;
                                    diagnostic = "Run cancellation was requested at a review checkpoint.";
                                    goto Finished;
                                }
                                if (now() >= deadline)
                                {
                                    failure = RuntimeSmokeRunFailureCode.RunTimeout;
                                    diagnostic = "Review checkpoint exceeded the workflow deadline.";
                                    goto Finished;
                                }
                                await Task.WhenAny(resume, Task.Delay(25));
                            }
                            if (!await resume)
                            {
                                failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                                diagnostic = "Review checkpoint resume signal was rejected.";
                                goto Finished;
                            }
                            simulationPaused = false;
                            checkpointAdapter.ResumeFromReview(step, checkpointContext);
                            emit("checkpoint_resumed", step, "resumed", null, null, null);
                            emit("step_completed", step, "passed", null, null, null);
                            if (sinkFailed) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; goto Finished; }
                            continue;
                        }
                        catch (Exception exception)
                        {
                            failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                            diagnostic = "Review checkpoint failed: " + SafeDiagnostic(exception.Message);
                            goto Finished;
                        }
                        finally
                        {
                            if (simulationPaused) checkpointAdapter.ResumeFromReview(step, checkpointContext);
                        }
                    }

                    emit("step_started", step, "running", null, null, null);
                    if (sinkFailed) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; goto Finished; }
                    RuntimeSmokeStepResponse response;
                    try
                    {
                        var context = new RuntimeSmokeStepContext
                        {
                            CorrelationId = correlationId,
                            WorkflowId = workflow.Id,
                            Scenario = scenario,
                            Seed = seed,
                            StepId = step.Id,
                            DeadlineUtc = deadline,
                            StepIndex = index
                        };
                        do
                        {
                            var awaited = await AwaitAdapterAsync(adapter, step, context, deadline, isCancellationRequested, now);
                            if (awaited.Cancelled)
                            {
                                state = RuntimeSmokeRunTerminalState.Cancelled;
                                failure = RuntimeSmokeRunFailureCode.RunCancelled;
                                diagnostic = "Run cancellation was requested while an adapter operation was in flight.";
                                goto Finished;
                            }
                            if (awaited.TimedOut)
                            {
                                failure = RuntimeSmokeRunFailureCode.RunTimeout;
                                if (step.Type == "assert")
                                {
                                    assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.PredicateTimeout, "Adapter operation exceeded the workflow deadline.");
                                    emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                                }
                                diagnostic = "Adapter operation exceeded the workflow deadline.";
                                goto Finished;
                            }
                            response = awaited.Response;
                            if (response != null && !string.Equals(response.CorrelationId, correlationId, StringComparison.Ordinal))
                            {
                                if (step.Type == "assert")
                                {
                                    assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid,
                                        "Adapter returned a response with the wrong correlation.");
                                    emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                                }
                                failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                                diagnostic = "Step adapter response has the wrong correlation.";
                                goto Finished;
                            }
                            if (response != null && response.Status == RuntimeSmokeStepResponseStatus.Pending)
                            {
                                if (isCancellationRequested != null && isCancellationRequested())
                                {
                                    state = RuntimeSmokeRunTerminalState.Cancelled;
                                    failure = RuntimeSmokeRunFailureCode.RunCancelled;
                                    diagnostic = "Run cancellation was requested while waiting for a predicate.";
                                    goto Finished;
                                }
                                if (now() >= deadline)
                                {
                                    failure = RuntimeSmokeRunFailureCode.RunTimeout;
                                    if (step.Type == "assert")
                                    {
                                        assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.PredicateTimeout, "Predicate deadline expired.");
                                        emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                                    }
                                    diagnostic = "Predicate remained pending until the workflow deadline.";
                                    goto Finished;
                                }
                                await Task.Delay(50);
                            }
                        }
                        while (response != null && response.Status == RuntimeSmokeStepResponseStatus.Pending);
                    }
                    catch (Exception exception)
                    {
                        if (step.Type == "assert")
                        {
                            assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.PredicateError, exception.Message);
                            emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                        }
                        failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                        diagnostic = "Step adapter threw: " + SafeDiagnostic(exception.Message);
                        goto Finished;
                    }
                    if (response == null || !string.Equals(response.CorrelationId, correlationId, StringComparison.Ordinal))
                    {
                        if (step.Type == "assert")
                        {
                            assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid,
                                response == null ? "Adapter returned no response." : "Adapter returned a response with the wrong correlation.");
                            emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                        }
                        failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                        diagnostic = "Step adapter response is missing or has the wrong correlation.";
                        goto Finished;
                    }
                    if (response.Status == RuntimeSmokeStepResponseStatus.TimedOut)
                    {
                        if (step.Type == "assert")
                        {
                            assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.PredicateTimeout, response.Diagnostic);
                            emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                        }
                        failure = RuntimeSmokeRunFailureCode.RunTimeout;
                        diagnostic = "Step adapter reported timeout.";
                        goto Finished;
                    }
                    if (response.Status != RuntimeSmokeStepResponseStatus.Passed)
                    {
                        if (step.Type == "assert")
                        {
                            assertionResult = RuntimeSmokeAssertionEvaluator.EvaluateError(step.Assertion, RuntimeSmokeAssertionFailureCode.PredicateError, response.Diagnostic);
                            emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                        }
                        failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                        diagnostic = "Step adapter did not acknowledge success: " + SafeDiagnostic(response.Diagnostic);
                        goto Finished;
                    }
                    if (step.Type == "assert")
                    {
                        assertionResult = RuntimeSmokeAssertionEvaluator.Evaluate(step.Assertion, response.Actual);
                        if (!assertionResult.Passed)
                        {
                            failure = RuntimeSmokeRunFailureCode.None;
                            diagnostic = "Assertion failed: " + assertionResult.FailureCode;
                            emit("assertion_failed", step, "failed", AssertionFailureLabel(assertionResult.FailureCode), assertionResult.Diagnostic, assertionResult);
                            goto Finished;
                        }
                    }
                    emit("step_completed", step, "passed", null, null, assertionResult != null && step.Type == "assert" ? assertionResult : null);
                    if (sinkFailed) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; goto Finished; }
                }
                diagnostic = "Workflow ended without a terminal step.";
            }
            catch (Exception exception)
            {
                failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                diagnostic = "Coordinator protocol error: " + SafeDiagnostic(exception.Message);
            }
            finally
            {
                if (state != RuntimeSmokeRunTerminalState.Passed && adapter is IRuntimeSmokeFailureStateProvider stateProvider)
                {
                    try { result.HostState = stateProvider.CaptureFailureState(); }
                    catch { result.HostState = null; }
                }
                try { cleanup?.RestoreSessionOverrides(); }
                catch (Exception exception)
                {
                    state = RuntimeSmokeRunTerminalState.Failed;
                    failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                    diagnostic = "Session cleanup failed: " + SafeDiagnostic(exception.Message);
                }
            }

        Finished:
            // The finally block above is bypassed by goto from inside try only if the
            // language's structured exit rules change; keep cleanup explicit here too.
            if (records.Count == 0)
            {
                try { cleanup?.RestoreSessionOverrides(); }
                catch (Exception exception) { failure = RuntimeSmokeRunFailureCode.ProtocolInvalid; diagnostic = "Session cleanup failed: " + SafeDiagnostic(exception.Message); }
            }
            if (sinkFailed)
            {
                state = RuntimeSmokeRunTerminalState.Failed;
                failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
            }
            result.TerminalState = state;
            result.FailureCode = failure;
            result.Diagnostic = diagnostic;
            result.EndedUtc = now();
            result.AssertionResult = assertionResult;
            if (sink is IRuntimeSmokeResultSink resultSink)
            {
                try { resultSink.WriteResult(result); }
                catch (Exception exception)
                {
                    state = RuntimeSmokeRunTerminalState.Failed;
                    failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                    diagnostic = "Result artifact write failed: " + SafeDiagnostic(exception.Message);
                    result.TerminalState = state;
                    result.FailureCode = failure;
                    result.Diagnostic = diagnostic;
                }
            }
            emit("run_terminal", null, state.ToString().ToLowerInvariant(), failure == RuntimeSmokeRunFailureCode.None ? null : RunFailureLabel(failure), diagnostic, assertionResult);
            if (sinkFailed)
            {
                state = RuntimeSmokeRunTerminalState.Failed;
                failure = RuntimeSmokeRunFailureCode.ProtocolInvalid;
                result.TerminalState = state;
                result.FailureCode = failure;
            }
            result.Transcript = records.AsReadOnly();
            lock (RegistryLock) _active = false;
            return result;
        }

        private static async Task<AdapterAwaitResult> AwaitAdapterAsync(IRuntimeSmokeStepAdapter adapter, RuntimeSmokeCompiledStep step,
            RuntimeSmokeStepContext context, DateTime deadline, Func<bool> isCancellationRequested, Func<DateTime> now)
        {
            var operation = adapter.ExecuteAsync(step, context);
            if (operation == null) return new AdapterAwaitResult { Response = null };
            while (!operation.IsCompleted)
            {
                if (isCancellationRequested != null && isCancellationRequested())
                {
                    ObserveIfFaulted(operation);
                    return new AdapterAwaitResult { Cancelled = true };
                }
                if (now() >= deadline)
                {
                    ObserveIfFaulted(operation);
                    return new AdapterAwaitResult { TimedOut = true };
                }
                await Task.WhenAny(operation, Task.Delay(25));
            }
            return new AdapterAwaitResult { Response = await operation };
        }

        private static void ObserveIfFaulted(Task operation)
        {
            operation.ContinueWith(completed => { var ignored = completed.Exception; },
                System.Threading.CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private sealed class AdapterAwaitResult
        {
            public RuntimeSmokeStepResponse Response;
            public bool Cancelled;
            public bool TimedOut;
        }

        private static bool IsCorrelationId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 160 &&
                   value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');
        }

        private static void BindAssertionContext(RuntimeSmokeAssertionResult assertion, RuntimeSmokeCompiledWorkflow workflow,
            RuntimeSmokeCompiledStep step, string correlationId, long startedTimestamp)
        {
            assertion.CorrelationId = correlationId;
            assertion.WorkflowId = workflow == null ? null : workflow.Id;
            assertion.Proposition = workflow == null ? null : workflow.Proposition;
            assertion.StepId = step == null ? null : step.Id;
            assertion.ElapsedMilliseconds = startedTimestamp <= 0 ? 0 : Math.Max(0L,
                (long)((Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / Stopwatch.Frequency));
        }

        private static bool HasFixtureOverride(string authoredValue, string requestedValue) =>
            !string.IsNullOrWhiteSpace(authoredValue) && !string.IsNullOrWhiteSpace(requestedValue) &&
            !string.Equals(authoredValue, requestedValue, StringComparison.Ordinal);

        private static string SafeDiagnostic(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unspecified";
            var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
            return normalized.Length <= 256 ? normalized : normalized.Substring(0, 256) + "...";
        }

        internal static string RunFailureLabel(RuntimeSmokeRunFailureCode code)
        {
            switch (code)
            {
                case RuntimeSmokeRunFailureCode.RunCancelled: return "run_cancelled";
                case RuntimeSmokeRunFailureCode.RunTimeout: return "run_timeout";
                case RuntimeSmokeRunFailureCode.ProtocolInvalid: return "protocol_invalid";
                case RuntimeSmokeRunFailureCode.PlayerCrashed: return "player_crashed";
                case RuntimeSmokeRunFailureCode.WatchdogTimeout: return "watchdog_timeout";
                default: return null;
            }
        }

        internal static string AssertionFailureLabel(RuntimeSmokeAssertionFailureCode code)
        {
            switch (code)
            {
                case RuntimeSmokeAssertionFailureCode.AssertionMismatch: return "assertion_mismatch";
                case RuntimeSmokeAssertionFailureCode.PredicateMissing: return "predicate_missing";
                case RuntimeSmokeAssertionFailureCode.PredicateError: return "predicate_error";
                case RuntimeSmokeAssertionFailureCode.PredicateTimeout: return "predicate_timeout";
                case RuntimeSmokeAssertionFailureCode.AssertionSchemaInvalid: return "assertion_schema_invalid";
                case RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid: return "assertion_adapter_protocol_invalid";
                default: return null;
            }
        }
    }

    /// <summary>Append-only UTF-8 JSONL output in a newly claimed run directory; it never removes authored inputs.</summary>
    public sealed class RuntimeSmokeJsonlTranscriptSink : IRuntimeSmokeTranscriptSink, IRuntimeSmokeResultSink, IRuntimeSmokeArtifactLocation
    {
        private readonly string _transcriptPath;
        private readonly string _resultPath;
        private readonly string _runDirectory;
        private readonly RuntimeSmokeCompiledWorkflow _workflow;
        private readonly long _sequenceOffset;

        public string RunDirectory => _runDirectory;

        public RuntimeSmokeJsonlTranscriptSink(string hostRoot, string outputRoot, string authoredRoot, string correlationId,
            RuntimeSmokeCompiledWorkflow workflow = null, RuntimeSmokePlayerLaunchOptions playerLaunchOptions = null)
        {
            if (string.IsNullOrWhiteSpace(hostRoot)) throw new ArgumentException("Host root is required.", nameof(hostRoot));
            if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("Output root is required.", nameof(outputRoot));
            if (string.IsNullOrWhiteSpace(authoredRoot)) throw new ArgumentException("Authored input root is required.", nameof(authoredRoot));
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '_' && character != '-'))
                throw new ArgumentException("Correlation id is not a safe run-directory name.", nameof(correlationId));
            playerLaunchOptions = playerLaunchOptions ?? RuntimeSmokePlayerLaunchOptions.FromEnvironment();
            if (playerLaunchOptions != null)
            {
                if (string.IsNullOrWhiteSpace(playerLaunchOptions.HostRoot) || string.IsNullOrWhiteSpace(playerLaunchOptions.OutputRoot) ||
                    string.IsNullOrWhiteSpace(playerLaunchOptions.AuthoredRoot))
                    throw new InvalidOperationException("RuntimeSmoke player launch host paths have not been durably claimed.");
                hostRoot = playerLaunchOptions.HostRoot;
                outputRoot = playerLaunchOptions.OutputRoot;
                authoredRoot = playerLaunchOptions.AuthoredRoot;
            }
            var host = Path.GetFullPath(hostRoot);
            if (!Directory.Exists(host)) throw new DirectoryNotFoundException("Selected host/worktree root must already exist.");
            var root = ResolveAgainstHost(host, outputRoot);
            var authored = ResolveAgainstHost(host, authoredRoot);
            if (!IsWithin(root, host) || PathEquals(root, host)) throw new InvalidOperationException("RuntimeSmoke output must be inside the selected host/worktree.");
            if (!IsWithin(authored, host) || PathEquals(authored, host)) throw new InvalidOperationException("Authored RuntimeSmoke inputs must be inside the selected host/worktree.");
            if (PathsOverlap(root, authored)) throw new InvalidOperationException("RuntimeSmoke output must not overlap authored script inputs.");
            EnsureNoReparsePoints(host);
            EnsureNoReparsePoints(authored);
            EnsureNoReparsePoints(root);
            Directory.CreateDirectory(root);
            EnsureNoReparsePoints(root);
            string directory;
            if (playerLaunchOptions != null)
            {
                if (workflow == null || !string.Equals(workflow.Id, playerLaunchOptions.WorkflowId, StringComparison.Ordinal) ||
                    !string.Equals(correlationId, playerLaunchOptions.CorrelationId, StringComparison.Ordinal))
                    throw new InvalidOperationException("RuntimeSmoke player launch does not match the requested host workflow and correlation.");
                directory = Path.GetFullPath(playerLaunchOptions.ArtifactRoot);
                var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!directory.StartsWith(rootPrefix, PathComparison) || !Directory.Exists(directory))
                    throw new InvalidOperationException("RuntimeSmoke player run directory is outside the configured output root or missing.");
                EnsureNoReparsePoints(directory);
                var armedPath = Path.Combine(directory, "runtime-smoke-armed.json");
                if (!File.Exists(armedPath) || File.Exists(Path.Combine(directory, "result.json")))
                    throw new IOException("RuntimeSmoke player run is not armed or already contains a result.");
                RuntimeSmokeTranscriptRecord armedRecord;
                using (var stream = new FileStream(Path.Combine(directory, "transcript.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var firstLine = reader.ReadLine();
                    armedRecord = string.IsNullOrWhiteSpace(firstLine) ? null : JsonUtility.FromJson<RuntimeSmokeTranscriptRecord>(firstLine);
                }
                if (armedRecord == null || armedRecord.sequence != 0 || armedRecord.type != "armed" ||
                    armedRecord.correlationId != correlationId || armedRecord.workflowId != workflow.Id ||
                    armedRecord.scriptHash != workflow.CanonicalHash)
                    throw new IOException("RuntimeSmoke player transcript does not begin with its matching armed record.");
                _sequenceOffset = 1;
            }
            else
            {
                directory = Path.GetFullPath(Path.Combine(root, correlationId));
                var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!directory.StartsWith(prefix, PathComparison)) throw new InvalidOperationException("Run output escaped its configured root.");
                if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("RuntimeSmoke output directory is already claimed.");
                Directory.CreateDirectory(directory);
                EnsureNoReparsePoints(directory);
            }
            var captureDirectory = Path.Combine(directory, "captures");
            if (playerLaunchOptions == null)
            {
                Directory.CreateDirectory(captureDirectory);
            }
            else if (!Directory.Exists(captureDirectory))
            {
                throw new DirectoryNotFoundException("RuntimeSmoke player run is missing its runner-owned captures directory.");
            }
            EnsureNoReparsePoints(captureDirectory);
            _runDirectory = directory;
            _workflow = workflow;
            _transcriptPath = Path.Combine(directory, "transcript.jsonl");
            if (_sequenceOffset == 0)
                using (new FileStream(_transcriptPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            _resultPath = Path.Combine(directory, "result.json");
        }

        private static string ResolveAgainstHost(string host, string path) =>
            Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(host, path));

        private static bool PathsOverlap(string left, string right) => IsWithin(left, right) || IsWithin(right, left);
        private static bool PathEquals(string left, string right) => string.Equals(TrimEnd(left), TrimEnd(right), PathComparison);

        private static bool IsWithin(string path, string root)
        {
            var normalizedPath = TrimEnd(path);
            var normalizedRoot = TrimEnd(root);
            if (string.Equals(normalizedPath, normalizedRoot, PathComparison)) return true;
            return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison) ||
                   (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar && normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, PathComparison));
        }

        private static string TrimEnd(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        internal static void EnsureNoReparsePoints(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            var current = root;
            if (string.IsNullOrEmpty(current)) throw new InvalidOperationException("RuntimeSmoke path has no filesystem root.");
            Check(current);
            var segments = full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                Check(current);
            }

            void Check(string candidate)
            {
                if (!Directory.Exists(candidate) && !File.Exists(candidate)) return;
                var attributes = File.GetAttributes(candidate);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("RuntimeSmoke paths cannot traverse symbolic links, junctions, or other reparse points.");
            }
        }

        public void Append(RuntimeSmokeTranscriptRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            RuntimeSmokeTranscriptRecord durableRecord = record;
            if (_sequenceOffset != 0)
            {
                durableRecord = new RuntimeSmokeTranscriptRecord
                {
                    schemaVersion = record.schemaVersion,
                    sequence = record.sequence + _sequenceOffset,
                    type = record.type,
                    timestampUtc = record.timestampUtc,
                    workflowId = record.workflowId,
                    proposition = record.proposition,
                    scriptHash = record.scriptHash,
                    correlationId = record.correlationId,
                    scenario = record.scenario,
                    seed = record.seed,
                    stepId = record.stepId,
                    status = record.status,
                    failureCode = record.failureCode,
                    diagnostic = record.diagnostic,
                    assertionSchemaVersion = record.assertionSchemaVersion,
                    assertionId = record.assertionId,
                    assertionName = record.assertionName,
                    assertionComparison = record.assertionComparison,
                    assertionPredicate = record.assertionPredicate,
                    assertionClassification = record.assertionClassification,
                    assertionTolerance = record.assertionTolerance,
                    hasAssertionTolerance = record.hasAssertionTolerance,
                    assertionElapsedMilliseconds = record.assertionElapsedMilliseconds,
                    expectedDigest = record.expectedDigest,
                    actualDigest = record.actualDigest,
                    expectedDisplay = record.expectedDisplay,
                    actualDisplay = record.actualDisplay,
                    expectedRedacted = record.expectedRedacted,
                    actualRedacted = record.actualRedacted,
                    investigationActionId = record.investigationActionId,
                    investigationKind = record.investigationKind,
                    investigationValueDisplay = record.investigationValueDisplay,
                    investigationValueDigest = record.investigationValueDigest,
                    investigationValueKind = record.investigationValueKind,
                    investigationValueRedacted = record.investigationValueRedacted
                };
            }
            var line = JsonUtility.ToJson(durableRecord) + "\n";
            var bytes = new UTF8Encoding(false).GetBytes(line);
            using (var stream = new FileStream(_transcriptPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        public void WriteResult(RuntimeSmokeExecutionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.TerminalState != RuntimeSmokeRunTerminalState.Passed && _workflow != null)
                new RuntimeSmokeFailureContextWriter(_runDirectory, _workflow).Write(result);
            var assertion = result.AssertionResult;
            var document = new RuntimeSmokeResultDocument
            {
                schemaVersion = result.SchemaVersion,
                correlationId = result.CorrelationId,
                workflowId = result.WorkflowId,
                proposition = _workflow == null ? null : _workflow.Proposition,
                scriptHash = result.ScriptHash,
                scenario = result.Scenario,
                seed = result.Seed,
                terminalState = result.TerminalState.ToString().ToLowerInvariant(),
                failureCode = result.FailureCode == RuntimeSmokeRunFailureCode.None ? null : RuntimeSmokeCoordinator.RunFailureLabel(result.FailureCode),
                diagnostic = result.Diagnostic,
                startedUtc = result.StartedUtc.ToString("o", CultureInfo.InvariantCulture),
                endedUtc = result.EndedUtc.ToString("o", CultureInfo.InvariantCulture),
                assertionSchemaVersion = assertion == null ? 0 : assertion.AssertionSchemaVersion,
                assertionId = assertion == null ? null : assertion.AssertionId,
                assertionName = assertion == null ? null : assertion.AssertionName,
                assertionComparison = assertion == null ? null : RuntimeSmokeAssertionSchema.ComparisonLabel(assertion.Comparison),
                assertionPredicate = assertion == null ? null : assertion.Predicate,
                assertionClassification = assertion == null ? null : assertion.Classification,
                assertionStepId = assertion == null ? null : assertion.StepId,
                assertionTolerance = assertion == null || !assertion.Tolerance.HasValue ? null : assertion.Tolerance.Value.ToString(CultureInfo.InvariantCulture),
                hasAssertionTolerance = assertion != null && assertion.Tolerance.HasValue,
                assertionElapsedMilliseconds = assertion == null ? 0 : assertion.ElapsedMilliseconds,
                assertionFailureCode = assertion == null ? null : RuntimeSmokeCoordinator.AssertionFailureLabel(assertion.FailureCode),
                expectedDigest = assertion == null ? null : assertion.ExpectedDigest,
                actualDigest = assertion == null ? null : assertion.ActualDigest,
                expectedDisplay = assertion == null ? null : assertion.ExpectedDisplay,
                actualDisplay = assertion == null ? null : assertion.ActualDisplay,
                expectedRedacted = assertion != null && assertion.ExpectedRedacted,
                actualRedacted = assertion != null && assertion.ActualRedacted
            };
            var bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(document) + "\n");
            using (var stream = new FileStream(_resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
    }
}
#endif
