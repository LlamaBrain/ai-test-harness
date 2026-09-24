using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeCaptureTests
    {
        [Test]
        public async Task DeclaredCaptureRunsAtReviewWithoutEnablingFailureOnlyInvestigation()
        {
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var workflow = CompileHitlCapture();
                var host = new CaptureAdapter();
                var registry = new RuntimeSmokeRunRegistry(new Catalog(workflow), (_, _) => new CheckpointAdapter(),
                    (_, correlation) => new Transcript(Path.Combine(root, correlation)),
                    investigationAdapterFactory: (_, _) => host);
                Assert.That(registry.Start(workflow.Id, "capture-review", RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Hitl).Accepted, Is.True);

                RuntimeSmokeRegistrySnapshot snapshot = null;
                for (var index = 0; index < 100; index++)
                {
                    snapshot = registry.GetStatus("capture-review");
                    if (snapshot.State == RuntimeSmokeRegistryRunState.ReviewReady) break;
                    await Task.Delay(10);
                }
                Assert.That(snapshot.State, Is.EqualTo(RuntimeSmokeRegistryRunState.ReviewReady));

                var captureMethod = typeof(RuntimeSmokeRunRegistry).GetMethod("CaptureAsync", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(captureMethod, Is.Not.Null, "The registry must expose a declared active-run capture operation.");
                var result = await (Task<RuntimeSmokeRegistryCommandResult>)captureMethod.Invoke(registry,
                    new object[] { "capture-review", "review-capture" });
                Assert.That(result.Accepted, Is.True, result.Diagnostic);
                Assert.That(host.LastAction.kind, Is.EqualTo("named_capture"));
                Assert.That(host.LastContext.CorrelationId, Is.EqualTo("capture-review"));
                Assert.That(host.LastContext.CreateCaptureWriter(), Is.Not.Null);
                Assert.That(registry.GetStatus("capture-review").State, Is.EqualTo(RuntimeSmokeRegistryRunState.ReviewReady));
                Assert.That((await registry.ExecuteInvestigationActionAsync("capture-review", "review-capture")).Status,
                    Is.EqualTo("run_not_failed"), "Diagnostic investigation remains failure-only.");
                var captureArtifactsProperty = typeof(RuntimeSmokeRegistrySnapshot).GetProperty("CaptureArtifacts", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(captureArtifactsProperty, Is.Not.Null, "Active and terminal snapshots should expose only verified run-local capture receipts.");
                var captureArtifacts = ((IEnumerable<RuntimeSmokeCaptureArtifactReference>)captureArtifactsProperty.GetValue(registry.GetStatus("capture-review"))).ToArray();
                Assert.That(captureArtifacts.Select(artifact => artifact.path), Is.EqualTo(new[] { "captures/review-checkpoint.png" }));
                Assert.That(captureArtifacts[0].sha256, Is.EqualTo(new string('a', 64)));

                var repeated = await (Task<RuntimeSmokeRegistryCommandResult>)captureMethod.Invoke(registry,
                    new object[] { "capture-review", "review-capture" });
                Assert.That(repeated.Status, Is.EqualTo("action_already_executed"));
                var undeclared = await (Task<RuntimeSmokeRegistryCommandResult>)captureMethod.Invoke(registry,
                    new object[] { "capture-review", "arbitrary-operation" });
                Assert.That(undeclared.Status, Is.EqualTo("action_not_declared"));

                Assert.That(registry.Resume("capture-review", "review").Accepted, Is.True);
                Assert.That((await registry.WaitForCompletionAsync("capture-review")).State, Is.EqualTo(RuntimeSmokeRegistryRunState.Passed));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static RuntimeSmokeCompiledWorkflow CompileHitlCapture()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/capture-hitl\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"hitl\",\"proposition\":\"declared capture is safe during review\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.command\",\"investigation.capture\"],\"timeoutSeconds\":10,\"investigation\":{\"summary\":\"Capture the declared review state\",\"invariant\":\"Capture is limited to a declared action\",\"fixtureSemantics\":\"A deterministic review fixture\",\"setupRationale\":\"Exercise active capture independently\",\"expectedProgress\":\"Begin, review, finish\",\"expectedEvidence\":[\"result.json\",\"transcript.jsonl\"],\"benignShutdownNoise\":\"none\",\"failureDisposition\":\"diagnosis_only\",\"excludedSystems\":[\"save_load\"],\"classifications\":[{\"failureCode\":\"assertion_mismatch\",\"likelyOwner\":\"fixture\",\"rationale\":\"The value differs\"}],\"actions\":[{\"id\":\"review-capture\",\"kind\":\"named_capture\",\"capability\":\"investigation.capture\",\"captureLabel\":\"review-checkpoint\"}]},\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"review\",\"type\":\"checkpoint\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflowJson = json.Replace("\"excludedBehavior\":\"save/load\"", "\"excludedBehavior\":\"save/load\",\"scenario\":\"capture-fixture\",\"seed\":\"capture-seed\"");
            var compilation = RuntimeSmokeDocumentCompiler.Compile(workflowJson, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Hitl,
                new RuntimeSmokeCompileOptions { RequireInvestigationContract = true, AvailableCapabilities = new[] { "test.command", "investigation.capture" } });
            Assert.That(compilation.Success, Is.True, string.Join("; ", compilation.Issues.Select(issue => issue.Code + ":" + issue.Message)));
            return compilation.Workflow;
        }

        private sealed class Catalog : IRuntimeSmokeWorkflowCatalog
        {
            private readonly RuntimeSmokeCompiledWorkflow _workflow;
            public Catalog(RuntimeSmokeCompiledWorkflow workflow) { _workflow = workflow; }
            public IReadOnlyList<RuntimeSmokeCompiledWorkflow> List(RuntimeSmokeTarget target, RuntimeSmokeMode mode) =>
                _workflow.Target == target && _workflow.Mode == mode ? new[] { _workflow } : Array.Empty<RuntimeSmokeCompiledWorkflow>();
            public bool TryGet(string id, RuntimeSmokeTarget target, RuntimeSmokeMode mode, out RuntimeSmokeCompiledWorkflow workflow)
            {
                workflow = _workflow;
                return _workflow.Id == id && _workflow.Target == target && _workflow.Mode == mode;
            }
        }

        private sealed class CheckpointAdapter : IRuntimeSmokeStepAdapter, IRuntimeSmokeReviewCheckpointAdapter
        {
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Passed });
            public void PauseForReview(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) { }
            public void ResumeFromReview(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) { }
        }

        private sealed class Transcript : IRuntimeSmokeTranscriptSink, IRuntimeSmokeArtifactLocation
        {
            public Transcript(string runDirectory)
            {
                RunDirectory = runDirectory;
                Directory.CreateDirectory(Path.Combine(runDirectory, "captures"));
            }
            public string RunDirectory { get; }
            public void Append(RuntimeSmokeTranscriptRecord record) { }
        }

        private sealed class CaptureAdapter : IRuntimeSmokeInvestigationAdapter
        {
            public RuntimeSmokeInvestigationAction LastAction { get; private set; }
            public RuntimeSmokeInvestigationContext LastContext { get; private set; }
            public Task<RuntimeSmokeInvestigationResponse> ExecuteAsync(RuntimeSmokeInvestigationAction action, RuntimeSmokeInvestigationContext context)
            {
                LastAction = action;
                LastContext = context;
                return Task.FromResult(new RuntimeSmokeInvestigationResponse { Succeeded = true, Value = RuntimeSmokeValue.FromMap(new[]
                {
                    new KeyValuePair<string, RuntimeSmokeValue>("path", RuntimeSmokeValue.From("captures/review-checkpoint.png")),
                    new KeyValuePair<string, RuntimeSmokeValue>("bytes", RuntimeSmokeValue.From(123L)),
                    new KeyValuePair<string, RuntimeSmokeValue>("sha256", RuntimeSmokeValue.From(new string('a', 64)))
                }) });
            }
        }
    }
}
