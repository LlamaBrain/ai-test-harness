#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    public enum RuntimeSmokeSuiteTerminalState { Passed, Failed, Cancelled, ReviewReady }

    public sealed class RuntimeSmokeSuiteEntryExecution
    {
        public string EntryId { get; internal set; }
        public bool Required { get; internal set; }
        public string CorrelationId { get; internal set; }
        public RuntimeSmokeExecutionResult Execution { get; internal set; }
    }

    public sealed class RuntimeSmokeSuiteExecutionResult
    {
        public string SuiteId { get; internal set; }
        public RuntimeSmokeSuiteTerminalState TerminalState { get; internal set; }
        public IReadOnlyList<RuntimeSmokeSuiteEntryExecution> Entries { get; internal set; }
    }

    /// <summary>Runs each validated suite workflow as an independent correlated RuntimeSmoke.</summary>
    public static class RuntimeSmokeSuiteExecutor
    {
        private const int DefaultMaximumSuiteSeconds = 3600;

        public static Task<RuntimeSmokeSuiteExecutionResult> RunAsync(
            RuntimeSmokeSuiteValidationResult suite,
            string suiteId,
            Func<RuntimeSmokeCompiledSuiteEntry, RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> executeStep,
            Func<RuntimeSmokeCompiledSuiteEntry, string> correlationFactory = null,
            Func<RuntimeSmokeCompiledSuiteEntry, IRuntimeSmokeTranscriptSink> transcriptFactory = null,
            Func<RuntimeSmokeCompiledSuiteEntry, IRuntimeSmokeCleanup> cleanupFactory = null,
            Func<bool> isCancellationRequested = null,
            int maximumSuiteSeconds = DefaultMaximumSuiteSeconds,
            Func<DateTime> utcNow = null)
        {
            return RunCoreAsync(suite, suiteId, executeStep, correlationFactory, transcriptFactory, cleanupFactory,
                isCancellationRequested, maximumSuiteSeconds, utcNow ?? (() => DateTime.UtcNow));
        }

        private static async Task<RuntimeSmokeSuiteExecutionResult> RunCoreAsync(
            RuntimeSmokeSuiteValidationResult suite,
            string suiteId,
            Func<RuntimeSmokeCompiledSuiteEntry, RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> executeStep,
            Func<RuntimeSmokeCompiledSuiteEntry, string> correlationFactory,
            Func<RuntimeSmokeCompiledSuiteEntry, IRuntimeSmokeTranscriptSink> transcriptFactory,
            Func<RuntimeSmokeCompiledSuiteEntry, IRuntimeSmokeCleanup> cleanupFactory,
            Func<bool> isCancellationRequested,
            int maximumSuiteSeconds,
            Func<DateTime> utcNow)
        {
            var outputs = new List<RuntimeSmokeSuiteEntryExecution>();
            var state = RuntimeSmokeSuiteTerminalState.Failed;
            if (suite == null || !suite.Success || suite.Entries == null || suite.Entries.Count == 0 ||
                executeStep == null || string.IsNullOrWhiteSpace(suiteId) || maximumSuiteSeconds <= 0)
                return new RuntimeSmokeSuiteExecutionResult { SuiteId = suiteId, TerminalState = state, Entries = outputs.AsReadOnly() };

            var deadline = utcNow().AddSeconds(maximumSuiteSeconds);
            state = RuntimeSmokeSuiteTerminalState.Passed;
            foreach (var entry in suite.Entries)
            {
                if (isCancellationRequested != null && isCancellationRequested())
                {
                    state = RuntimeSmokeSuiteTerminalState.Cancelled;
                    break;
                }
                if (utcNow() >= deadline)
                {
                    if (entry.Required) state = RuntimeSmokeSuiteTerminalState.Failed;
                    break;
                }

                var correlation = correlationFactory == null ? Guid.NewGuid().ToString("D") : correlationFactory(entry);
                var workflow = WithDeadline(entry.Workflow, Math.Max(1, Math.Min(entry.TimeoutSeconds, (int)Math.Ceiling((deadline - utcNow()).TotalSeconds))));
                var adapter = new EntryAdapter(entry, executeStep);
                var transcript = transcriptFactory == null ? (IRuntimeSmokeTranscriptSink)new NullTranscriptSink() : transcriptFactory(entry);
                var cleanup = cleanupFactory == null ? null : cleanupFactory(entry);
                var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, correlation, adapter, transcript, cleanup, isCancellationRequested, utcNow);
                outputs.Add(new RuntimeSmokeSuiteEntryExecution { EntryId = entry.Id, Required = entry.Required, CorrelationId = correlation, Execution = execution });
                if (execution.TerminalState == RuntimeSmokeRunTerminalState.Cancelled)
                {
                    state = RuntimeSmokeSuiteTerminalState.Cancelled;
                    break;
                }
                if (entry.Required && execution.TerminalState != RuntimeSmokeRunTerminalState.Passed)
                    state = RuntimeSmokeSuiteTerminalState.Failed;
            }
            return new RuntimeSmokeSuiteExecutionResult { SuiteId = suiteId, TerminalState = state, Entries = outputs.AsReadOnly() };
        }

        private static RuntimeSmokeCompiledWorkflow WithDeadline(RuntimeSmokeCompiledWorkflow workflow, int timeoutSeconds) => new RuntimeSmokeCompiledWorkflow
        {
            Id = workflow.Id,
            Proposition = workflow.Proposition,
            ExcludedBehavior = workflow.ExcludedBehavior,
            ParityContract = workflow.ParityContract,
            Target = workflow.Target,
            Mode = workflow.Mode,
            TimeoutSeconds = timeoutSeconds,
            CanonicalHash = workflow.CanonicalHash,
            RequiredCapabilities = workflow.RequiredCapabilities,
            Steps = workflow.Steps
        };

        private sealed class EntryAdapter : IRuntimeSmokeStepAdapter
        {
            private readonly RuntimeSmokeCompiledSuiteEntry _entry;
            private readonly Func<RuntimeSmokeCompiledSuiteEntry, RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> _execute;
            public EntryAdapter(RuntimeSmokeCompiledSuiteEntry entry,
                Func<RuntimeSmokeCompiledSuiteEntry, RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> execute)
            { _entry = entry; _execute = execute; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) => _execute(_entry, step, context);
        }

        private sealed class NullTranscriptSink : IRuntimeSmokeTranscriptSink
        {
            public void Append(RuntimeSmokeTranscriptRecord record) { }
        }
    }
}
#endif
