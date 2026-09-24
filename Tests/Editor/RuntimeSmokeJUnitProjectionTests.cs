using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeJUnitProjectionTests
    {
        [Test]
        public async Task JsonlSinkWritesFailureContextBoundToTheSameAssertionAndRun()
        {
            var workflow = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/failure-context\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready is true\",\"excludedBehavior\":\"save/load\",\"scenario\":\"fixture\",\"seed\":\"42\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Is ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"end\",\"type\":\"terminal\"}]}",
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready" } }).Workflow;
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-failure-context-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var sink = new RuntimeSmokeJsonlTranscriptSink(root, "Artifacts/RuntimeSmokes", "Assets/RuntimeSmokes", "failure-context-run", workflow);
                var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, "failure-context-run",
                    new HostStateMismatchAdapter("failure-context-run"), sink);
                Assert.That(execution.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
                var path = Path.Combine(root, "Artifacts", "RuntimeSmokes", "failure-context-run", "failure-context.json");
                Assert.That(File.Exists(path), Is.True);
                var context = UnityEngine.JsonUtility.FromJson<RuntimeSmokeFailureContext>(File.ReadAllText(path));
                Assert.That(context.correlationId, Is.EqualTo("failure-context-run"));
                Assert.That(context.workflowId, Is.EqualTo("workflows/failure-context"));
                Assert.That(context.scenario, Is.EqualTo("fixture"));
                Assert.That(context.seed, Is.EqualTo("42"));
                Assert.That(context.assertionId, Is.EqualTo("is-ready"));
                Assert.That(context.assertionSchemaVersion, Is.EqualTo(1));
                Assert.That(context.expectedDisplay, Is.EqualTo("true"));
                Assert.That(context.actualDisplay, Is.EqualTo("false"));
                Assert.That(context.hostStateDisplay, Is.EqualTo("{\"cleanupRestored\":false,\"gameReady\":false,\"hostValid\":true}"));
                Assert.That(context.reproductionCommand, Does.Contain("runtime_smoke.run"));
                Assert.That(context.transcriptTail.Length, Is.GreaterThan(0));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public async Task FailureContextLinksOnlyVerifiedRunLocalCaptureReceipts()
        {
            var compiled = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/capture-context\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready is true\",\"excludedBehavior\":\"save/load\",\"scenario\":\"fixture\",\"seed\":\"42\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\",\"investigation.capture\"],\"timeoutSeconds\":10,\"investigation\":{\"summary\":\"Inspect a failed run\",\"invariant\":\"Runtime remains ready\",\"fixtureSemantics\":\"No game mutation\",\"setupRationale\":\"Test capture projection\",\"expectedProgress\":\"Failure then capture\",\"expectedEvidence\":[\"result.json\",\"captures/game-view.png\"],\"benignShutdownNoise\":\"None\",\"failureDisposition\":\"diagnosis_only\",\"excludedSystems\":[\"save_load\"],\"classifications\":[{\"failureCode\":\"assertion_mismatch\",\"likelyOwner\":\"runtime_smoke\",\"rationale\":\"The declared predicate differs from the fixture state\"}],\"actions\":[{\"id\":\"capture\",\"kind\":\"named_capture\",\"capability\":\"investigation.capture\",\"captureLabel\":\"game-view\"}]},\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Is ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"end\",\"type\":\"terminal\"}]}" ,
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready", "investigation.capture" } });
            Assert.That(compiled.Success, Is.True, string.Join("; ", compiled.Issues.Select(issue => issue.Code + ": " + issue.Message)));
            var workflow = compiled.Workflow;
            var result = await RuntimeSmokeCoordinator.RunAsync(workflow, "capture-context-run",
                new PassingStepAdapter("capture-context-run"), new MemoryTranscript());
            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            var unsafeRecord = new RuntimeSmokeTranscriptRecord
            {
                type = "investigation_action", status = "passed", investigationActionId = "capture", investigationKind = "named_capture",
                investigationValueDisplay = "{\"bytes\":42,\"path\":\"captures/../outside.png\",\"sha256\":\"" + new string('b', 64) + "\"}"
            };
            var safeRecord = new RuntimeSmokeTranscriptRecord
            {
                type = "investigation_action", status = "passed", investigationActionId = "capture", investigationKind = "named_capture",
                investigationValueDisplay = "{\"artifact\":\"captures/game-view.png\",\"bytes\":42,\"sha256\":\"" + new string('c', 64) + "\"}"
            };

            var unsafeContext = RuntimeSmokeFailureContextWriter.Create(workflow, result, new[] { unsafeRecord });
            Assert.That(unsafeContext.captureArtifacts, Is.Empty);

            var context = RuntimeSmokeFailureContextWriter.Create(workflow, result, new[] { safeRecord });
            Assert.That(context.captureArtifacts, Has.Length.EqualTo(1));
            var liveCaptureRecord = new RuntimeSmokeTranscriptRecord
            {
                type = "capture_action", status = "passed", investigationActionId = "capture", investigationKind = "named_capture",
                investigationValueDisplay = "{\"artifact\":\"captures/game-view.png\",\"bytes\":42,\"sha256\":\"" + new string('d', 64) + "\"}"
            };
            var liveContext = RuntimeSmokeFailureContextWriter.Create(workflow, result, new[] { liveCaptureRecord });
            Assert.That(liveContext.captureArtifacts.Select(artifact => artifact.path), Is.EqualTo(new[] { "captures/game-view.png" }));
            Assert.That(context.captureArtifacts[0].path, Is.EqualTo("captures/game-view.png"));
            Assert.That(context.captureArtifacts[0].bytes, Is.EqualTo(42));
            Assert.That(context.captureArtifacts[0].sha256, Is.EqualTo(new string('c', 64)));
        }

        [Test]
        public async Task FailureContextCapturesTypedHostStateAndFreshSafeReproductionCommand()
        {
            const string workflowId = "workflows/state-context";
            var workflow = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/state-context\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"runtime is ready\",\"excludedBehavior\":\"save/load\",\"scenario\":\"fixture\",\"seed\":\"42\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"end\",\"type\":\"terminal\"}]}" ,
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready" } }).Workflow;
            var adapter = new HostStateMismatchAdapter("state-context-run");
            var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, "state-context-run",
                adapter, new MemoryTranscript(), adapter);

            Assert.That(execution.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(adapter.CleanupRestored, Is.True);
            var context = RuntimeSmokeFailureContextWriter.Create(workflow, execution);
            Assert.That(context.hostStateKind, Is.EqualTo("map"));
            Assert.That(context.hostStateDisplay, Is.EqualTo("{\"cleanupRestored\":false,\"gameReady\":false,\"hostValid\":true}"));
            Assert.That(context.hostStateDigest, Has.Length.EqualTo(64));
            Assert.That(context.target, Is.EqualTo("editor"));
            Assert.That(context.mode, Is.EqualTo("fsd"));
            Assert.That(context.reproductionCommand, Does.Match("^runtime_smoke\\.run \\\"" + workflowId + "\\\" \\\"[a-f0-9-]{36}\\\"$"));
            Assert.That(context.reproductionCommand, Does.Not.Contain(execution.CorrelationId));
        }

        [Test]
        public async Task AuthoredScenarioAndSeedArePersistedInTranscriptAndResult()
        {
            var workflow = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/authored-result\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"authored run passes\",\"excludedBehavior\":\"save/load\",\"scenario\":\"scenario-authored\",\"seed\":\"9001\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"end\",\"type\":\"terminal\"}]}",
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready" } }).Workflow;
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-authored-result-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var sink = new RuntimeSmokeJsonlTranscriptSink(root, "Artifacts/RuntimeSmokes", "Assets/RuntimeSmokes", "authored-result-run", workflow);
                var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, "authored-result-run",
                    new PassingStepAdapter("authored-result-run"), sink);
                Assert.That(execution.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Passed));
                Assert.That(execution.Transcript.First().scenario, Is.EqualTo("scenario-authored"));
                Assert.That(execution.Transcript.First().seed, Is.EqualTo("9001"));
                var resultPath = Path.Combine(root, "Artifacts", "RuntimeSmokes", "authored-result-run", "result.json");
                var result = UnityEngine.JsonUtility.FromJson<RuntimeSmokeResultDocument>(File.ReadAllText(resultPath));
                Assert.That(result.scenario, Is.EqualTo("scenario-authored"));
                Assert.That(result.seed, Is.EqualTo("9001"));
                var junit = XDocument.Parse(RuntimeSmokeJUnitProjection.Serialize(new[] { execution }));
                var properties = junit.Descendants("property").ToDictionary(property => (string)property.Attribute("name"), property => (string)property.Attribute("value"));
                Assert.That(properties["scenario"], Is.EqualTo("scenario-authored"));
                Assert.That(properties["seed"], Is.EqualTo("9001"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public async Task AssertionFailureIsProjectedWithStableSchemaAndExpectedActual()
        {
            var workflow = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/junit\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready is true\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Is ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"end\",\"type\":\"terminal\"}]}",
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready" } }).Workflow;
            var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, "junit-run",
                new PassingStepAdapter("junit-run"), new MemoryTranscript());

            var xml = XDocument.Parse(RuntimeSmokeJUnitProjection.Serialize(new[] { execution }));
            var testcase = xml.Descendants("testcase").Single();
            var failure = testcase.Element("failure");
            Assert.That(testcase.Attribute("name").Value, Is.EqualTo("workflows/junit"));
            Assert.That(failure.Attribute("type").Value, Is.EqualTo("assertion_mismatch"));
            Assert.That(failure.Value, Does.Contain("is-ready").And.Contain("expected=true").And.Contain("actual=false"));
            Assert.That(testcase.Elements("properties").Single().Elements("property").Any(property =>
                property.Attribute("name").Value == "assertionSchemaVersion" && property.Attribute("value").Value == "1"), Is.True);
        }

        [Test]
        public async Task AssertionComparatorAndExecutionContextAgreeAcrossFailureArtifactsAndJUnit()
        {
            var workflow = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/assertion-metadata\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"metric stays within tolerance\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.metric\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"metric-check\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"metric-close\",\"name\":\"Metric is within tolerance\",\"predicate\":\"test.metric\",\"comparison\":\"within_absolute\",\"expected\":10,\"tolerance\":0.5,\"classification\":\"construction_noop\"}},{\"id\":\"finish\",\"type\":\"terminal\"}]}",
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.metric" } }).Workflow;
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-assertion-metadata-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                const string correlation = "assertion-metadata-run";
                var sink = new RuntimeSmokeJsonlTranscriptSink(root, "Artifacts/RuntimeSmokes", "Assets/RuntimeSmokes", correlation, workflow);
                var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, correlation,
                    new NumericMismatchAdapter(correlation), sink);
                Assert.That(execution.AssertionResult, Is.Not.Null);
                foreach (var propertyName in new[] { "CorrelationId", "WorkflowId", "Proposition", "StepId", "Predicate", "ElapsedMilliseconds", "Tolerance", "Classification" })
                    Assert.That(execution.AssertionResult.GetType().GetProperty(propertyName), Is.Not.Null, propertyName);
                Assert.That(execution.AssertionResult.GetType().GetProperty("Tolerance").GetValue(execution.AssertionResult, null), Is.EqualTo(0.5m));
                Assert.That(execution.AssertionResult.GetType().GetProperty("StepId").GetValue(execution.AssertionResult, null), Is.EqualTo("metric-check"));
                Assert.That(execution.AssertionResult.GetType().GetProperty("Classification").GetValue(execution.AssertionResult, null), Is.EqualTo("construction_noop"));

                var runDirectory = Path.Combine(root, "Artifacts", "RuntimeSmokes", correlation);
                var transcriptLine = File.ReadAllLines(Path.Combine(runDirectory, "transcript.jsonl")).Single(line => line.Contains("assertion_failed"));
                var resultJson = File.ReadAllText(Path.Combine(runDirectory, "result.json"));
                var contextJson = File.ReadAllText(Path.Combine(runDirectory, "failure-context.json"));
                foreach (var artifact in new[] { transcriptLine, resultJson, contextJson })
                {
                    Assert.That(artifact, Does.Contain("within_absolute"));
                    Assert.That(artifact, Does.Contain("test.metric"));
                    Assert.That(artifact, Does.Contain("metric-check"));
                    Assert.That(artifact, Does.Contain("assertion_mismatch"));
                    Assert.That(artifact, Does.Contain("construction_noop"));
                }
                Assert.That(contextJson, Does.Contain("metric stays within tolerance"));
                Assert.That(contextJson, Does.Contain("0.5"));

                var xml = XDocument.Parse(RuntimeSmokeJUnitProjection.Serialize(new[] { execution }));
                var properties = xml.Descendants("property").ToDictionary(property => (string)property.Attribute("name"), property => (string)property.Attribute("value"));
                Assert.That(properties["assertionComparison"], Is.EqualTo("within_absolute"));
                Assert.That(properties["assertionTolerance"], Is.EqualTo("0.5"));
                Assert.That(properties["assertionPredicate"], Is.EqualTo("test.metric"));
                Assert.That(properties["assertionStepId"], Is.EqualTo("metric-check"));
                Assert.That(properties["proposition"], Is.EqualTo("metric stays within tolerance"));
                Assert.That(properties["assertionClassification"], Is.EqualTo("construction_noop"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public async Task FailureContextBindsRepositoryRelativeSourceAndDeclaredInvestigationActions()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/investigation\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"the authored predicate holds\",\"excludedBehavior\":\"save/load\",\"scenario\":\"ready-fixture\",\"seed\":\"501\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\",\"investigation.status\"],\"timeoutSeconds\":10,\"investigation\":{\"summary\":\"Inspect ready-state failure\",\"invariant\":\"The ready predicate is true\",\"fixtureSemantics\":\"A clean deterministic session\",\"setupRationale\":\"Avoid unrelated persistence\",\"expectedProgress\":\"Session starts then readiness resolves\",\"expectedEvidence\":[\"result.json\",\"player.log\"],\"benignShutdownNoise\":\"none\",\"failureDisposition\":\"auto_retry_forbidden\",\"excludedSystems\":[\"save_load\"],\"classifications\":[{\"failureCode\":\"assertion_mismatch\",\"likelyOwner\":\"readiness\",\"rationale\":\"The observed state differs from the expected predicate\"}],\"actions\":[{\"id\":\"status\",\"kind\":\"status_query\",\"capability\":\"investigation.status\"}]},\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Ready state\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflowResult = RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { RequireInvestigationContract = true, AvailableCapabilities = new[] { "test.ready", "investigation.status" } });
            Assert.That(workflowResult.Success, Is.True, string.Join(";", workflowResult.Issues.Select(issue => issue.Code)));
            var workflow = workflowResult.Workflow;
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-investigation-context-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var correlation = "investigation-context-run";
                var sink = new RuntimeSmokeJsonlTranscriptSink(root, "Artifacts/RuntimeSmokes", "Assets/RuntimeSmokes", correlation, workflow);
                var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, correlation,
                    new PassingStepAdapter(correlation), sink);
                Assert.That(execution.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
                var path = Path.Combine(root, "Artifacts", "RuntimeSmokes", correlation, "failure-context.json");
                var context = File.ReadAllText(path);
                Assert.That(context, Does.Contain("\"sourceScriptPath\":\"workflows/investigation.json\""));
                Assert.That(context, Does.Contain(workflow.InvestigationHash));
                Assert.That(context, Does.Contain("status_query"));
                Assert.That(context, Does.Contain("investigation.status"));
                Assert.That(Path.IsPathRooted(workflow.SourceScriptPath), Is.False);
                Assert.That(workflow.SourceScriptPath, Does.Not.Contain(".."));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public async Task FailureContextIncludesOnlyMatchingPlayerLaunchAndBuildProvenance()
        {
            var workflow = RuntimeSmokeDocumentCompiler.Compile(
                "{\"schemaVersion\":1,\"id\":\"workflows/player-provenance\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready remains true\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"end\",\"type\":\"terminal\"}]}",
                new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.ready" } }).Workflow;
            var execution = await RuntimeSmokeCoordinator.RunAsync(workflow, "player-provenance-run",
                new PassingStepAdapter("player-provenance-run"), new MemoryTranscript());
            var runDirectory = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-player-provenance-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(runDirectory, "logs"));
            try
            {
                var logPath = Path.Combine(runDirectory, "logs", "player.log");
                File.WriteAllText(logPath, "fresh player log");
                var launch = new RuntimeSmokeLaunchDocument
                {
                    correlationId = execution.CorrelationId,
                    workflowId = execution.WorkflowId,
                    scriptHash = execution.ScriptHash,
                    logPath = logPath,
                    target = "editor",
                    mode = "fsd",
                    suiteId = "suite/parity",
                    buildRevision = "revision-123",
                    buildVersion = "1.2.3",
                    profileHash = new string('a', 64),
                    contentHash = new string('b', 64),
                    backend = "IL2CPP",
                    stripping = "high",
                    executableSha256 = new string('c', 64)
                };
                var armed = new RuntimeSmokeArmedDocument
                {
                    correlationId = execution.CorrelationId,
                    workflowId = execution.WorkflowId,
                    scriptHash = execution.ScriptHash,
                    processId = 1234,
                    processStartUtc = "2026-09-23T23:59:00.000Z",
                    executableSha256 = launch.executableSha256
                };
                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-launch.json"), UnityEngine.JsonUtility.ToJson(launch));
                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-armed.json"), UnityEngine.JsonUtility.ToJson(armed));

                var context = RuntimeSmokeFailureContextWriter.Create(workflow, execution, runDirectory: runDirectory);
                Assert.That(context.target, Is.EqualTo("editor"));
                Assert.That(context.suiteId, Is.EqualTo("suite/parity"));
                Assert.That(context.buildRevision, Is.EqualTo("revision-123"));
                Assert.That(context.playerProcessId, Is.EqualTo(1234));
                Assert.That(context.playerExecutableSha256, Is.EqualTo(launch.executableSha256));
                Assert.That(context.playerLogArtifact, Is.EqualTo("logs/player.log"));
                Assert.That(context.playerLogArtifact, Does.Not.Contain(runDirectory));

                armed.correlationId = "other-run";
                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-armed.json"), UnityEngine.JsonUtility.ToJson(armed));
                var mismatched = RuntimeSmokeFailureContextWriter.Create(workflow, execution, runDirectory: runDirectory);
                Assert.That(mismatched.playerProcessId, Is.Zero);
                Assert.That(mismatched.playerLogArtifact, Is.Null);

                armed.correlationId = execution.CorrelationId;
                launch.logPath = Path.GetFullPath(Path.Combine(runDirectory, "..", "private.log"));
                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-armed.json"), UnityEngine.JsonUtility.ToJson(armed));
                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-launch.json"), UnityEngine.JsonUtility.ToJson(launch));
                var outOfRunLog = RuntimeSmokeFailureContextWriter.Create(workflow, execution, runDirectory: runDirectory);
                Assert.That(outOfRunLog.playerProcessId, Is.EqualTo(1234));
                Assert.That(outOfRunLog.playerLogArtifact, Is.Null);

                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-launch.json"), new string('x', 65537));
                var oversizedEnvelope = RuntimeSmokeFailureContextWriter.Create(workflow, execution, runDirectory: runDirectory);
                Assert.That(oversizedEnvelope.playerProcessId, Is.Zero);
            }
            finally { Directory.Delete(runDirectory, true); }
        }

        private sealed class NumericMismatchAdapter : IRuntimeSmokeStepAdapter
        {
            private readonly string _correlation;
            public NumericMismatchAdapter(string correlation) { _correlation = correlation; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = _correlation, Status = RuntimeSmokeStepResponseStatus.Passed, Actual = RuntimeSmokeValue.From(11L) });
        }

        private sealed class PassingStepAdapter : IRuntimeSmokeStepAdapter
        {
            private readonly string _correlation;
            public PassingStepAdapter(string correlation) { _correlation = correlation; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = _correlation, Status = RuntimeSmokeStepResponseStatus.Passed, Actual = RuntimeSmokeValue.From(false) });
        }

        private sealed class MemoryTranscript : IRuntimeSmokeTranscriptSink
        {
            public void Append(RuntimeSmokeTranscriptRecord record) { }
        }

        private sealed class HostStateMismatchAdapter : IRuntimeSmokeStepAdapter, IRuntimeSmokeFailureStateProvider, IRuntimeSmokeCleanup
        {
            private readonly string _correlation;
            public bool CleanupRestored { get; private set; }
            public HostStateMismatchAdapter(string correlation) { _correlation = correlation; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = _correlation, Status = RuntimeSmokeStepResponseStatus.Passed, Actual = RuntimeSmokeValue.From(false) });
            public void RestoreSessionOverrides() { CleanupRestored = true; }
            public RuntimeSmokeValue CaptureFailureState() => RuntimeSmokeValue.FromMap(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, RuntimeSmokeValue>("cleanupRestored", RuntimeSmokeValue.From(CleanupRestored)),
                new System.Collections.Generic.KeyValuePair<string, RuntimeSmokeValue>("hostValid", RuntimeSmokeValue.From(true)),
                new System.Collections.Generic.KeyValuePair<string, RuntimeSmokeValue>("gameReady", RuntimeSmokeValue.From(false))
            });
        }
    }
}
