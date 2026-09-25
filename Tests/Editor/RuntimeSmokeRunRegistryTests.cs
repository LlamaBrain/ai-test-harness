using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeRunRegistryTests
    {
        [Test]
        public async Task ListStartAndStatusUseTheSameRegisteredCompiledWorkflow()
        {
            var workflow = Compile();
            var expectedProgressWeight = workflow.Steps
                .Where(step => step.Type != "terminal")
                .Sum(step => Math.Max(1, step.Seconds));
            var registry = Registry(workflow, (step, context) => Task.Delay(250).ContinueWith(_ => new RuntimeSmokeStepResponse
            {
                CorrelationId = context.CorrelationId,
                Status = RuntimeSmokeStepResponseStatus.Passed
            }));
            var listed = registry.List(RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd);
            Assert.That(listed.Select(item => item.Id), Is.EqualTo(new[] { workflow.Id }));

            var started = registry.Start(workflow.Id, "registry-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd);
            Assert.That(started.Accepted, Is.True, started.Diagnostic);
            var active = registry.GetStatus("registry-run");
            Assert.That(active.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Running));
            Assert.That(active.ActiveStepId, Is.EqualTo("begin"));
            Assert.That(active.TotalProgressWeight, Is.EqualTo(expectedProgressWeight));
            Assert.That(active.ArtifactDirectory, Is.Null, "Ordinary transcript sinks keep working without claiming a path.");
            var snapshot = await registry.WaitForCompletionAsync("registry-run");
            Assert.That(snapshot.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Passed));
            Assert.That(snapshot.WorkflowId, Is.EqualTo(workflow.Id));
            Assert.That(snapshot.CorrelationId, Is.EqualTo("registry-run"));
            Assert.That(snapshot.Transcript.Count(record => record.type == "run_terminal"), Is.EqualTo(1));
            Assert.That(snapshot.CompletedProgressWeight, Is.EqualTo(expectedProgressWeight));
            Assert.That(snapshot.TotalProgressWeight, Is.EqualTo(expectedProgressWeight));
            Assert.That(snapshot.ProgressPercent, Is.EqualTo(100));
        }

        [Test]
        public async Task AuthoredScenarioAndSeedFlowThroughListStartTranscriptAndResult()
        {
            var workflow = CompileAuthored();
            var registry = Registry(workflow, (step, context) => Task.FromResult(new RuntimeSmokeStepResponse
            {
                CorrelationId = context.CorrelationId,
                Status = RuntimeSmokeStepResponseStatus.Passed
            }));

            var listed = registry.List(RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Single();
            Assert.That(listed.GetType().GetProperty("Scenario"), Is.Not.Null);
            Assert.That(listed.GetType().GetProperty("Seed"), Is.Not.Null);
            Assert.That(listed.GetType().GetProperty("Scenario").GetValue(listed, null), Is.EqualTo("scenario-authored"));
            Assert.That(listed.GetType().GetProperty("Seed").GetValue(listed, null), Is.EqualTo("9001"));
            Assert.That(listed.GetType().GetProperty("Target").GetValue(listed, null), Is.EqualTo("editor"));
            Assert.That(listed.GetType().GetProperty("Mode").GetValue(listed, null), Is.EqualTo("fsd"));
            Assert.That(listed.InvestigationSummary, Is.EqualTo("Inspect the authored fixture"));

            var unsafeFixture = registry.Start(workflow.Id, "unsafe-fixture-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, "../outside", "9001");
            Assert.That(unsafeFixture.Accepted, Is.False);
            Assert.That(unsafeFixture.Status, Is.EqualTo("fixture_identity_invalid"));
            var mismatchedFixture = registry.Start(workflow.Id, "authored-seed-mismatch", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, "other-scenario", "9001");
            Assert.That(mismatchedFixture.Accepted, Is.False);
            Assert.That(mismatchedFixture.Status, Is.EqualTo("authored_fixture_mismatch"));
            Assert.That(registry.Start(workflow.Id, "authored-seed-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
            var completed = await registry.WaitForCompletionAsync("authored-seed-run");
            Assert.That(completed.Scenario, Is.EqualTo("scenario-authored"));
            Assert.That(completed.Seed, Is.EqualTo("9001"));
            Assert.That(completed.Transcript.First().scenario, Is.EqualTo("scenario-authored"));
            Assert.That(completed.Transcript.First().seed, Is.EqualTo("9001"));
            Assert.That(completed.Execution.GetType().GetProperty("Scenario"), Is.Not.Null);
            Assert.That(completed.Execution.GetType().GetProperty("Seed"), Is.Not.Null);
            Assert.That(completed.Execution.GetType().GetProperty("Scenario").GetValue(completed.Execution, null), Is.EqualTo("scenario-authored"));
            Assert.That(completed.Execution.GetType().GetProperty("Seed").GetValue(completed.Execution, null), Is.EqualTo("9001"));
        }

        [Test]
        public void SnapshotExposesExactRunDirectoryFromOptionalTranscriptSinkContract()
        {
            const string runDirectory = "C:/host-specific/output/runtime-smoke-run";
            var workflow = Compile();
            var registry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new DelegatingAdapter((step, context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Passed })),
                (_, _) => new PathAwareTranscript(runDirectory));

            Assert.That(registry.Start(workflow.Id, "artifact-location-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
            Assert.That(registry.GetStatus("artifact-location-run").ArtifactDirectory, Is.EqualTo(runDirectory));
        }

        [Test]
        public async Task CancelSignalsInFlightRunAndUnknownOrReusedCorrelationsAreRejected()
        {
            var workflow = Compile();
            var registry = Registry(workflow, (step, context) => Task.Delay(1000).ContinueWith(_ => new RuntimeSmokeStepResponse
            {
                CorrelationId = context.CorrelationId,
                Status = RuntimeSmokeStepResponseStatus.Passed
            }));
            Assert.That(registry.GetStatus("missing").Found, Is.False);
            Assert.That(registry.Cancel("missing").Accepted, Is.False);

            var started = registry.Start(workflow.Id, "cancel-registry-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd);
            Assert.That(started.Accepted, Is.True, started.Diagnostic);
            Assert.That(registry.Cancel("cancel-registry-run").Accepted, Is.True);
            var cancelled = await registry.WaitForCompletionAsync("cancel-registry-run");
            Assert.That(cancelled.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Cancelled));
            Assert.That(registry.Start(workflow.Id, "cancel-registry-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.False);
            Assert.That(registry.Start("workflows/unknown", "other-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.False);
        }

        [Test]
        public async Task FailedAssertionStatusCarriesAnExportableFailureContext()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/registry-failure\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready is true\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Is ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflow = RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready" } }).Workflow;
            var registry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new DelegatingAdapter((step, context) => Task.FromResult(new RuntimeSmokeStepResponse
            {
                CorrelationId = context.CorrelationId,
                Status = RuntimeSmokeStepResponseStatus.Passed,
                Actual = RuntimeSmokeValue.From(false)
            })), (_, _) => new MemoryTranscript());

            Assert.That(registry.Start(workflow.Id, "registry-failure-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
            var snapshot = await registry.WaitForCompletionAsync("registry-failure-run");
            Assert.That(snapshot.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Failed));
            Assert.That(snapshot.FailureContext.assertionId, Is.EqualTo("is-ready"));
            Assert.That(snapshot.FailureContext.assertionSchemaVersion, Is.EqualTo(1));
            Assert.That(snapshot.FailureContext.failureCode, Is.EqualTo("assertion_mismatch"));
            Assert.That(snapshot.FailureContext.expectedDisplay, Is.EqualTo("true"));
            Assert.That(snapshot.FailureContext.actualDisplay, Is.EqualTo("false"));
        }

        [Test]
        public async Task InvestigationActionsAreDeclaredFailureOnlyAndAppendTypedResultsToTranscript()
        {
            var workflow = CompileAuthored();
            var investigation = new RecordingInvestigationAdapter();
            var registry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new DelegatingAdapter((step, context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Passed })),
                (_, _) => new MemoryTranscript(), investigationAdapterFactory: (_, _) => investigation);

            Assert.That(registry.Start(workflow.Id, "investigation-pass", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
            Assert.That((await registry.WaitForCompletionAsync("investigation-pass")).State, Is.EqualTo(RuntimeSmokeRegistryRunState.Passed));
            Assert.That((await registry.ExecuteInvestigationActionAsync("investigation-pass", "status")).Status, Is.EqualTo("run_not_failed"));

            var cancellingRegistry = Registry(workflow, (step, context) => Task.Delay(250).ContinueWith(_ => new RuntimeSmokeStepResponse
            {
                CorrelationId = context.CorrelationId,
                Status = RuntimeSmokeStepResponseStatus.Passed
            }));
            Assert.That(cancellingRegistry.Start(workflow.Id, "investigation-cancelled", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
            Assert.That(cancellingRegistry.Cancel("investigation-cancelled").Accepted, Is.True);
            Assert.That((await cancellingRegistry.WaitForCompletionAsync("investigation-cancelled")).State, Is.EqualTo(RuntimeSmokeRegistryRunState.Cancelled));
            Assert.That((await cancellingRegistry.ExecuteInvestigationActionAsync("investigation-cancelled", "status")).Status, Is.EqualTo("run_not_failed"));

            // Turn this into a failed run without allowing an investigation action to alter its result.
            var failingRegistry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new DelegatingAdapter((step, context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Failed })),
                (_, _) => new MemoryTranscript(), investigationAdapterFactory: (_, _) => investigation);
            Assert.That(failingRegistry.Start(workflow.Id, "investigation-failed", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
            var failed = await failingRegistry.WaitForCompletionAsync("investigation-failed");
            Assert.That(failed.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Failed));
            Assert.That((await failingRegistry.ExecuteInvestigationActionAsync("investigation-failed", "not-declared")).Status, Is.EqualTo("action_not_declared"));

            var action = await failingRegistry.ExecuteInvestigationActionAsync("investigation-failed", "status");
            Assert.That(action.Accepted, Is.True, action.Diagnostic);
            Assert.That(action.ValueDisplay, Is.EqualTo("{\"alive\":true}"));
            Assert.That(investigation.LastAction.kind, Is.EqualTo("status_query"));
            var snapshot = failingRegistry.GetStatus("investigation-failed");
            Assert.That(snapshot.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Failed), "Diagnosis must never rewrite terminal run state.");
            Assert.That(snapshot.Transcript.Last().type, Is.EqualTo("investigation_action"));
            Assert.That(snapshot.Transcript.Last().investigationActionId, Is.EqualTo("status"));
            Assert.That(snapshot.Transcript.Last().investigationValueDisplay, Is.EqualTo("{\"alive\":true}"));
            Assert.That(snapshot.Transcript.Last().investigationValueDigest, Is.EqualTo(action.ValueDigest));
            Assert.That(snapshot.FailureContext.transcriptTail.Last().type, Is.EqualTo("investigation_action"));
            Assert.That(snapshot.FailureContext.transcriptTail.Last().correlationId, Is.EqualTo("investigation-failed"));
        }

        [Test]
        public async Task InvestigationActionIsDurablyAppendedToTheClaimedRunTranscript()
        {
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-investigation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Authored"));
            Directory.CreateDirectory(Path.Combine(root, "Artifacts"));
            try
            {
                var workflow = CompileAuthored();
                var registry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new DelegatingAdapter((step, context) =>
                    Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Failed })),
                    (_, correlation) => new RuntimeSmokeJsonlTranscriptSink(root, "Artifacts/RuntimeSmokes", "Authored", correlation, workflow),
                    investigationAdapterFactory: (_, _) => new RecordingInvestigationAdapter());
                Assert.That(registry.Start(workflow.Id, "investigation-durable", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd).Accepted, Is.True);
                Assert.That((await registry.WaitForCompletionAsync("investigation-durable")).State, Is.EqualTo(RuntimeSmokeRegistryRunState.Failed));
                Assert.That(Directory.Exists(Path.Combine(root, "Artifacts", "RuntimeSmokes", "investigation-durable", "captures")), Is.True,
                    "The Editor run owner must provision its run-local capture directory before declared investigation actions execute.");
                Assert.That((await registry.ExecuteInvestigationActionAsync("investigation-durable", "status")).Accepted, Is.True);
                var lines = File.ReadAllLines(Path.Combine(root, "Artifacts", "RuntimeSmokes", "investigation-durable", "transcript.jsonl"));
                var records = lines.Select(line => UnityEngine.JsonUtility.FromJson<RuntimeSmokeTranscriptRecord>(line)).ToArray();
                Assert.That(records.Last().type, Is.EqualTo("investigation_action"));
                Assert.That(records.Last().investigationActionId, Is.EqualTo("status"));
                Assert.That(records.Last().investigationKind, Is.EqualTo("status_query"));
                Assert.That(records.Last().investigationValueKind, Is.EqualTo("map"));
                Assert.That(records.Select(record => record.sequence), Is.Ordered);
                var context = UnityEngine.JsonUtility.FromJson<RuntimeSmokeFailureContext>(File.ReadAllText(
                    Path.Combine(root, "Artifacts", "RuntimeSmokes", "investigation-durable", "failure-context.json")));
                Assert.That(context.transcriptTail.Any(record => record.type == "investigation_action" && record.investigationActionId == "status"), Is.True);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task CheckpointPublishesReviewReadyAndRequiresMatchingSingleResume()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/registry-checkpoint\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"hitl\",\"proposition\":\"review is explicit\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"before-review\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"review-walls\",\"type\":\"checkpoint\"},{\"id\":\"after-review\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflow = RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Hitl,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command" } }).Workflow;
            Assert.That(workflow, Is.Not.Null, "A named review checkpoint does not need a wait-predicate field.");
            var checkpointAdapter = new CheckpointAdapter();
            var registry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => checkpointAdapter,
                (_, _) => new MemoryTranscript());

            Assert.That(registry.Start(workflow.Id, "checkpoint-registry-run", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Hitl).Accepted, Is.True);
            RuntimeSmokeRegistrySnapshot snapshot = null;
            for (var index = 0; index < 100; index++)
            {
                snapshot = registry.GetStatus("checkpoint-registry-run");
                if (snapshot.State == RuntimeSmokeRegistryRunState.ReviewReady) break;
                await Task.Delay(10);
            }
            Assert.That(snapshot.State, Is.EqualTo(RuntimeSmokeRegistryRunState.ReviewReady));
            Assert.That(snapshot.ActiveCheckpointId, Is.EqualTo("review-walls"));
            Assert.That(registry.Resume("checkpoint-registry-run", "wrong-checkpoint").Accepted, Is.False);
            Assert.That(registry.Resume("checkpoint-registry-run", "review-walls").Accepted, Is.True);
            Assert.That(registry.Resume("checkpoint-registry-run", "review-walls").Accepted, Is.False);
            var completed = await registry.WaitForCompletionAsync("checkpoint-registry-run");
            Assert.That(completed.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Passed));
            Assert.That(checkpointAdapter.PauseCount, Is.EqualTo(1));
            Assert.That(checkpointAdapter.ResumeCount, Is.EqualTo(1));
            Assert.That(completed.Transcript.Count(record => record.type == "review_ready"), Is.EqualTo(1));
            Assert.That(completed.Transcript.Count(record => record.type == "checkpoint_resumed"), Is.EqualTo(1));
        }

        [Test]
        public async Task EditorTargetRunCanBeCancelledWhenPlayModeExitsAtReviewCheckpoint()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/registry-exit-checkpoint\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"hitl\",\"proposition\":\"a review checkpoint remains tied to its live Editor session\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"review\",\"type\":\"checkpoint\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflow = RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Hitl,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command" } }).Workflow;
            var registry = new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new CheckpointAdapter(),
                (_, _) => new MemoryTranscript());

            Assert.That(registry.Start(workflow.Id, "checkpoint-editor-exit", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Hitl).Accepted, Is.True);
            RuntimeSmokeRegistrySnapshot snapshot = null;
            for (var index = 0; index < 100; index++)
            {
                snapshot = registry.GetStatus("checkpoint-editor-exit");
                if (snapshot.State == RuntimeSmokeRegistryRunState.ReviewReady) break;
                await Task.Delay(10);
            }
            Assert.That(snapshot.State, Is.EqualTo(RuntimeSmokeRegistryRunState.ReviewReady));

            Assert.That(registry.CancelActiveRuns(RuntimeSmokeTarget.Editor), Is.EqualTo(1));
            var completed = await registry.WaitForCompletionAsync("checkpoint-editor-exit");

            Assert.That(completed.State, Is.EqualTo(RuntimeSmokeRegistryRunState.Cancelled));
            Assert.That(completed.ActiveCheckpointId, Is.Null);
            Assert.That(completed.Transcript.Count(record => record.type == "checkpoint_resumed"), Is.Zero);
            Assert.That(completed.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.RunCancelled));
            Assert.That(completed.Transcript.Last().type, Is.EqualTo("run_terminal"));
        }

        private static RuntimeSmokeRunRegistry Registry(RuntimeSmokeCompiledWorkflow workflow,
            System.Func<RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> execute) =>
            new RuntimeSmokeRunRegistry(new FixedCatalog(workflow), (_, _) => new DelegatingAdapter(execute),
                (_, _) => new MemoryTranscript());

        private static RuntimeSmokeCompiledWorkflow Compile()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/registry\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"registered run completes\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"do\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            return RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command" } }).Workflow;
        }

        private static RuntimeSmokeCompiledWorkflow CompileAuthored()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/authored\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"authored run completes\",\"excludedBehavior\":\"save/load\",\"scenario\":\"scenario-authored\",\"seed\":\"9001\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"investigation\":{\"summary\":\"Inspect the authored fixture\",\"invariant\":\"The authored run completes\",\"fixtureSemantics\":\"A deterministic registry fixture\",\"setupRationale\":\"Keep registry coverage isolated\",\"expectedProgress\":\"Start then terminal\",\"expectedEvidence\":[\"result.json\"],\"benignShutdownNoise\":\"none\",\"failureDisposition\":\"diagnosis_only\",\"excludedSystems\":[\"save_load\"],\"classifications\":[{\"failureCode\":\"assertion_mismatch\",\"likelyOwner\":\"fixture\",\"rationale\":\"The typed value differs\"}],\"actions\":[{\"id\":\"status\",\"kind\":\"status_query\",\"capability\":\"test.command\"}]},\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"do\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            return RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command" } }).Workflow;
        }

        private sealed class FixedCatalog : IRuntimeSmokeWorkflowCatalog
        {
            private readonly RuntimeSmokeCompiledWorkflow _workflow;
            public FixedCatalog(RuntimeSmokeCompiledWorkflow workflow) { _workflow = workflow; }
            public IReadOnlyList<RuntimeSmokeCompiledWorkflow> List(RuntimeSmokeTarget target, RuntimeSmokeMode mode) =>
                _workflow.Target == target && _workflow.Mode == mode ? new[] { _workflow } : new RuntimeSmokeCompiledWorkflow[0];
            public bool TryGet(string id, RuntimeSmokeTarget target, RuntimeSmokeMode mode, out RuntimeSmokeCompiledWorkflow workflow)
            {
                workflow = _workflow;
                return _workflow.Id == id && _workflow.Target == target && _workflow.Mode == mode;
            }
        }

        private sealed class DelegatingAdapter : IRuntimeSmokeStepAdapter
        {
            private readonly System.Func<RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> _execute;
            public DelegatingAdapter(System.Func<RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, Task<RuntimeSmokeStepResponse>> execute) { _execute = execute; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) => _execute(step, context);
        }

        private sealed class MemoryTranscript : IRuntimeSmokeTranscriptSink
        {
            public void Append(RuntimeSmokeTranscriptRecord record) { }
        }

        private sealed class PathAwareTranscript : IRuntimeSmokeTranscriptSink, IRuntimeSmokeArtifactLocation
        {
            public PathAwareTranscript(string runDirectory) { RunDirectory = runDirectory; }
            public string RunDirectory { get; }
            public void Append(RuntimeSmokeTranscriptRecord record) { }
        }

        private sealed class RecordingInvestigationAdapter : IRuntimeSmokeInvestigationAdapter
        {
            public RuntimeSmokeInvestigationAction LastAction { get; private set; }
            public Task<RuntimeSmokeInvestigationResponse> ExecuteAsync(RuntimeSmokeInvestigationAction action, RuntimeSmokeInvestigationContext context)
            {
                LastAction = action;
                return Task.FromResult(new RuntimeSmokeInvestigationResponse { Succeeded = true, Value = RuntimeSmokeValue.FromMap(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, RuntimeSmokeValue>("alive", RuntimeSmokeValue.From(true))
                }) });
            }
        }

        private sealed class CheckpointAdapter : IRuntimeSmokeStepAdapter, IRuntimeSmokeReviewCheckpointAdapter
        {
            public int PauseCount { get; private set; }
            public int ResumeCount { get; private set; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) => Task.FromResult(new RuntimeSmokeStepResponse
            {
                CorrelationId = context.CorrelationId,
                Status = RuntimeSmokeStepResponseStatus.Passed
            });
            public void PauseForReview(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) => PauseCount++;
            public void ResumeFromReview(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) => ResumeCount++;
        }
    }
}
