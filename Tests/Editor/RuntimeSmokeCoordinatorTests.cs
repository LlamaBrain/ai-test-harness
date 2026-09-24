using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeCoordinatorTests
    {
        [Test]
        public async Task PassWritesOrderedTranscriptAndExactlyOneTerminalRecord()
        {
            var sink = new MemorySink();
            var adapter = new FakeAdapter((step, _) => Response("run-1", RuntimeSmokeStepResponseStatus.Passed));
            var result = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "run-1", adapter, sink);

            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Passed));
            Assert.That(result.Transcript.Select(record => record.type), Is.EqualTo(new[] { "run_started", "step_started", "step_completed", "step_started", "step_completed", "run_terminal" }));
            Assert.That(sink.Records.Count(record => record.type == "run_terminal"), Is.EqualTo(1));
        }

        [Test]
        public async Task ScenarioAndSeedReachEveryStepAndTranscript()
        {
            RuntimeSmokeStepContext observed = null;
            var sink = new MemorySink();
            var adapter = new FakeAdapter((step, context) =>
            {
                observed = context;
                return Response(context.CorrelationId, RuntimeSmokeStepResponseStatus.Passed);
            });
            var result = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "seeded-run", adapter, sink,
                scenario: "scenario-7x7", seed: "9001");

            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Passed));
            Assert.That(observed.Scenario, Is.EqualTo("scenario-7x7"));
            Assert.That(observed.Seed, Is.EqualTo("9001"));
            Assert.That(sink.Records.All(record => record.scenario == "scenario-7x7" && record.seed == "9001"), Is.True);
        }

        [Test]
        public async Task CoordinatorRejectsScenarioOrSeedOverridesOfAuthoredFixture()
        {
            var scenarioOverride = await RuntimeSmokeCoordinator.RunAsync(WorkflowWithFixture(), "fixture-scenario-override",
                new FakeAdapter((step, context) => Response(context.CorrelationId, RuntimeSmokeStepResponseStatus.Passed)),
                new MemorySink(), scenario: "different-scenario", seed: "42");
            var seedOverride = await RuntimeSmokeCoordinator.RunAsync(WorkflowWithFixture(), "fixture-seed-override",
                new FakeAdapter((step, context) => Response(context.CorrelationId, RuntimeSmokeStepResponseStatus.Passed)),
                new MemorySink(), scenario: "scenario-authored", seed: "43");

            Assert.That(scenarioOverride.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(scenarioOverride.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.ProtocolInvalid));
            Assert.That(scenarioOverride.Scenario, Is.EqualTo("scenario-authored"));
            Assert.That(seedOverride.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(seedOverride.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.ProtocolInvalid));
            Assert.That(seedOverride.Seed, Is.EqualTo("42"));
        }

        [Test]
        public async Task AssertionMismatchReturnsTypedFailureAndStillCleansUp()
        {
            var sink = new MemorySink();
            var adapter = new FakeAdapter((step, _) => Response("run-assert", RuntimeSmokeStepResponseStatus.Passed, RuntimeSmokeValue.From(false)));
            var cleanup = new FakeCleanup();
            var result = await RuntimeSmokeCoordinator.RunAsync(AssertionWorkflow(), "run-assert", adapter, sink, cleanup);

            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(result.AssertionResult.FailureCode, Is.EqualTo(RuntimeSmokeAssertionFailureCode.AssertionMismatch));
            Assert.That(result.AssertionResult.AssertionId, Is.EqualTo("is-ready"));
            var failureRecord = result.Transcript.Single(record => record.type == "assertion_failed");
            Assert.That(failureRecord.failureCode, Is.EqualTo("assertion_mismatch"));
            Assert.That(failureRecord.assertionSchemaVersion, Is.EqualTo(1));
            Assert.That(failureRecord.expectedDigest, Is.Not.Empty);
            Assert.That(failureRecord.actualDigest, Is.Not.Empty);
            Assert.That(cleanup.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task MissingTypedPredicateResultAndWrongCorrelationCannotPass()
        {
            var missing = await RuntimeSmokeCoordinator.RunAsync(AssertionWorkflow(), "missing", new FakeAdapter((step, _) => Response("missing", RuntimeSmokeStepResponseStatus.Passed)), new MemorySink());
            Assert.That(missing.AssertionResult.FailureCode, Is.EqualTo(RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid));

            var wrongCorrelation = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "expected", new FakeAdapter((step, _) => Response("other", RuntimeSmokeStepResponseStatus.Passed)), new MemorySink());
            Assert.That(wrongCorrelation.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.ProtocolInvalid));
        }

        [Test]
        public async Task AdapterExceptionTimeoutAndCancellationAreTerminalAndCleanup()
        {
            var cleanup = new FakeCleanup();
            var exception = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "exception", new FakeAdapter((step, _) => throw new InvalidOperationException("test")), new MemorySink(), cleanup);
            Assert.That(exception.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.ProtocolInvalid));
            Assert.That(cleanup.Count, Is.EqualTo(1));

            var timeout = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "timeout", new FakeAdapter((step, _) => Response("timeout", RuntimeSmokeStepResponseStatus.TimedOut)), new MemorySink());
            Assert.That(timeout.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.RunTimeout));

            var cancelledCleanup = new FakeCleanup();
            var cancelled = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "cancel", new FakeAdapter((step, _) => Response("cancel", RuntimeSmokeStepResponseStatus.Passed)), new MemorySink(), cancelledCleanup, () => true);
            Assert.That(cancelled.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Cancelled));
            Assert.That(cancelledCleanup.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task WorkflowDeadlineAndPredicateTimeoutAreExplicitFailures()
        {
            var current = DateTime.UtcNow;
            var deadline = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "deadline", new FakeAdapter((step, _) => Response("deadline", RuntimeSmokeStepResponseStatus.Passed)),
                new MemorySink(), utcNow: () => { var value = current; current = current.AddSeconds(6); return value; });
            Assert.That(deadline.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.RunTimeout));

            var predicateTimeout = await RuntimeSmokeCoordinator.RunAsync(AssertionWorkflow(), "predicate-timeout",
                new FakeAdapter((step, _) => Response("predicate-timeout", step.Type == "assert" ? RuntimeSmokeStepResponseStatus.TimedOut : RuntimeSmokeStepResponseStatus.Passed)), new MemorySink());
            Assert.That(predicateTimeout.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.RunTimeout));
            Assert.That(predicateTimeout.AssertionResult.FailureCode, Is.EqualTo(RuntimeSmokeAssertionFailureCode.PredicateTimeout));
        }

        [Test]
        public async Task PendingWaitPredicateIsPolledUntilPassWithinDeadline()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/pending\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready predicate resolves\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"wait\",\"type\":\"wait\",\"predicate\":\"test.ready\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflow = Compile(json);
            var pendingResponses = 0;
            var result = await RuntimeSmokeCoordinator.RunAsync(workflow, "pending", new FakeAdapter((step, _) =>
            {
                if (step.Type == "wait" && pendingResponses++ == 0) return Response("pending", RuntimeSmokeStepResponseStatus.Pending);
                return Response("pending", RuntimeSmokeStepResponseStatus.Passed);
            }), new MemorySink());

            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Passed));
            Assert.That(pendingResponses, Is.EqualTo(2));
        }

        [Test]
        public async Task SleepIsCoordinatorPacingAndDoesNotBecomeAnAdapterCompletionProof()
        {
            const string json = "{\"schemaVersion\":1,\"id\":\"workflows/sleep\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"pacing remains bounded\",\"excludedBehavior\":\"readiness\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"pace\",\"type\":\"sleep\",\"seconds\":1,\"reason\":\"Allow an observable gameplay interval\"},{\"id\":\"do\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var workflow = Compile(json);
            var adapterCalls = 0;
            var result = await RuntimeSmokeCoordinator.RunAsync(workflow, "sleep-run", new FakeAdapter((step, _) =>
            {
                adapterCalls++;
                return Response("sleep-run", RuntimeSmokeStepResponseStatus.Passed);
            }), new MemorySink());

            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Passed));
            Assert.That(adapterCalls, Is.EqualTo(2), "Start and command reach the host adapter; sleep is coordinator-owned pacing.");
            Assert.That(result.Transcript.Count(record => record.type == "step_started" && record.stepId == "pace"), Is.EqualTo(1));
            Assert.That(result.Transcript.Count(record => record.type == "step_completed" && record.stepId == "pace"), Is.EqualTo(1));
        }

        [Test]
        public async Task HungAdapterCallIsBoundedByCancellationAndWorkflowDeadline()
        {
            var workflow = Workflow();
            var cleanup = new FakeCleanup();
            var cancellation = new System.Threading.CancellationTokenSource();
            var cancelAfterStart = Task.Delay(75).ContinueWith(_ => cancellation.Cancel());
            var cancelTimer = System.Diagnostics.Stopwatch.StartNew();
            var cancelled = await RuntimeSmokeCoordinator.RunAsync(workflow, "hung-cancel",
                new DelayedAdapter(1100), new MemorySink(), cleanup, () => cancellation.IsCancellationRequested);
            cancelTimer.Stop();
            await cancelAfterStart;
            Assert.That(cancelled.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Cancelled));
            Assert.That(cleanup.Count, Is.EqualTo(1));
            Assert.That(cancelTimer.ElapsedMilliseconds, Is.LessThan(800), "Cancellation must interrupt an in-flight adapter operation.");

            var current = DateTime.UtcNow;
            var timeoutTimer = System.Diagnostics.Stopwatch.StartNew();
            var timedOut = await RuntimeSmokeCoordinator.RunAsync(workflow, "hung-timeout",
                new DelayedAdapter(1100), new MemorySink(), utcNow: () => { var value = current; current = current.AddSeconds(6); return value; });
            timeoutTimer.Stop();
            Assert.That(timedOut.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(timedOut.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.RunTimeout));
            Assert.That(timeoutTimer.ElapsedMilliseconds, Is.LessThan(800), "The workflow deadline must bound an in-flight adapter operation.");
        }

        [Test]
        public async Task DuplicateCorrelationIsRejectedAndTranscriptSinkFailureCannotPublishPass()
        {
            var workflow = Workflow();
            await RuntimeSmokeCoordinator.RunAsync(workflow, "duplicate", new FakeAdapter((step, _) => Response("duplicate", RuntimeSmokeStepResponseStatus.Passed)), new MemorySink());
            var duplicate = await RuntimeSmokeCoordinator.RunAsync(workflow, "duplicate", new FakeAdapter((step, _) => Response("duplicate", RuntimeSmokeStepResponseStatus.Passed)), new MemorySink());
            Assert.That(duplicate.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.ProtocolInvalid));

            var sinkFailure = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "sink", new FakeAdapter((step, _) => Response("sink", RuntimeSmokeStepResponseStatus.Passed)), new ThrowingSink());
            Assert.That(sinkFailure.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
        }

        [Test]
        public async Task ResultArtifactWriteFailureIsReflectedInTerminalTranscriptAndCannotProjectPass()
        {
            var sink = new ThrowingResultSink();
            var execution = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "result-write-failure",
                new FakeAdapter((step, _) => Response("result-write-failure", RuntimeSmokeStepResponseStatus.Passed)), sink);

            Assert.That(execution.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(execution.FailureCode, Is.EqualTo(RuntimeSmokeRunFailureCode.ProtocolInvalid));
            var terminal = execution.Transcript.Single(record => record.type == "run_terminal");
            Assert.That(terminal.status, Is.EqualTo("failed"));
            Assert.That(terminal.failureCode, Is.EqualTo("protocol_invalid"));
            var junit = System.Xml.Linq.XDocument.Parse(RuntimeSmokeJUnitProjection.Serialize(new[] { execution }));
            Assert.That(junit.Descendants("testcase").Single().Element("error"), Is.Not.Null);
        }

        [Test]
        public async Task JsonlSinkClaimsFreshRunDirectoryAndWritesAppendOnlyUtf8Records()
        {
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var output = Path.Combine(root, "Artifacts", "RuntimeSmokes");
                var authored = Path.Combine(root, "Assets", "Testing", "RuntimeSmokes");
                var sink = new RuntimeSmokeJsonlTranscriptSink(root, output, authored, "file-run");
                var execution = await RuntimeSmokeCoordinator.RunAsync(Workflow(), "file-run",
                    new FakeAdapter((step, _) => Response("file-run", RuntimeSmokeStepResponseStatus.Passed)), sink);
                var lines = File.ReadAllLines(Path.Combine(output, "file-run", "transcript.jsonl"));
                Assert.That(lines.Length, Is.GreaterThan(1));
                Assert.That(lines[0], Does.Contain("run_started"));
                var resultJson = File.ReadAllText(Path.Combine(output, "file-run", "result.json"));
                Assert.That(resultJson, Does.Contain("\"terminalState\":\"passed\""));
                Assert.That(execution.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Passed));
                Assert.That(() => new RuntimeSmokeJsonlTranscriptSink(root, output, authored, "file-run"), Throws.TypeOf<IOException>());
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void JsonlSinkContinuesAPlayerClaimedTranscriptWithoutReusingSequenceZero()
        {
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-player-sink-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var output = Path.Combine(root, "Artifacts", "RuntimeSmokes");
                var authored = Path.Combine(root, "Assets", "Testing", "RuntimeSmokes");
                var runDirectory = Path.Combine(output, "player-run-id");
                Directory.CreateDirectory(authored);
                Directory.CreateDirectory(Path.Combine(runDirectory, "logs"));
                Directory.CreateDirectory(Path.Combine(runDirectory, "captures"));
                var workflow = Workflow();
                const string correlation = "player-correlation";
                File.WriteAllText(Path.Combine(runDirectory, "runtime-smoke-armed.json"), "{}");
                File.WriteAllText(Path.Combine(runDirectory, "transcript.jsonl"),
                    "{\"sequence\":0,\"type\":\"armed\",\"timestampUtc\":\"2026-09-23T18:00:00.000Z\",\"workflowId\":\"" + workflow.Id +
                    "\",\"scriptHash\":\"" + workflow.CanonicalHash + "\",\"correlationId\":\"" + correlation + "\",\"scenario\":\"fixture\",\"seed\":\"42\",\"status\":\"armed\"}\n");
                Assert.That(RuntimeSmokePlayerLaunchOptions.TryParse(new[]
                {
                    "-runtimeSmoke", workflow.Id, "-runtimeSmokeCorrelation", correlation,
                    "-runtimeSmokeArtifactRoot", runDirectory, "-runtimeSmokeNonce", new string('a', 64),
                    "-runtimeSmokeBuildToken", new string('b', 64)
                }, out var options, out var failure), Is.True, failure);
                typeof(RuntimeSmokePlayerLaunchOptions).GetMethod("SetClaimedHostPaths", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(options, new object[] { root, output, authored });

                var sink = new RuntimeSmokeJsonlTranscriptSink(root, output, authored, correlation, workflow, options);
                sink.Append(new RuntimeSmokeTranscriptRecord
                {
                    sequence = 0, type = "run_started", status = "running", workflowId = workflow.Id,
                    scriptHash = workflow.CanonicalHash, correlationId = correlation, scenario = "fixture", seed = "42",
                    timestampUtc = "2026-09-23T18:00:01.000Z"
                });
                var lines = File.ReadAllLines(Path.Combine(runDirectory, "transcript.jsonl"));
                Assert.That(lines.Length, Is.EqualTo(2));
                Assert.That(lines[1], Does.Contain("\"sequence\":1"));
                Assert.That(lines[1], Does.Contain("run_started"));
                Assert.That(lines[1], Does.Contain("\"scenario\":\"fixture\""));
                Assert.That(lines[1], Does.Contain("\"seed\":\"42\""));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void JsonlSinkRejectsWrongWorktreeTraversalAndAuthoredInputOverlap()
        {
            var parent = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-containment-" + Guid.NewGuid().ToString("N"));
            var host = Path.Combine(parent, "host");
            var otherHost = Path.Combine(parent, "other-host");
            var authored = Path.Combine(host, "Assets", "Testing", "RuntimeSmokes");
            Directory.CreateDirectory(authored);
            Directory.CreateDirectory(otherHost);
            try
            {
                Assert.That(() => new RuntimeSmokeJsonlTranscriptSink(host, Path.Combine(otherHost, "Artifacts", "RuntimeSmokes"), authored, "wrong-host"),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => new RuntimeSmokeJsonlTranscriptSink(host, Path.Combine(authored, "generated"), authored, "overlap"),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => new RuntimeSmokeJsonlTranscriptSink(host, Path.Combine(host, "Artifacts", "..", "Assets", "Testing", "RuntimeSmokes", "run"), authored, "traversal"),
                    Throws.TypeOf<InvalidOperationException>());

                var safeOutput = Path.Combine(host, "Artifacts", "RuntimeSmokes");
                var sink = new RuntimeSmokeJsonlTranscriptSink(host, safeOutput, authored, "safe-run");
                sink.Append(new RuntimeSmokeTranscriptRecord { type = "test" });
                var authoredMarker = Path.Combine(authored, "keep.json");
                File.WriteAllText(authoredMarker, "keep");
                Assert.That(File.ReadAllText(authoredMarker), Is.EqualTo("keep"));
            }
            finally
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }

        [Test]
        public void JsonlSinkRejectsJunctionEscapes()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false)
                Assert.Ignore("Junction escape test requires Windows NTFS junctions.");
            var parent = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-junction-" + Guid.NewGuid().ToString("N"));
            var host = Path.Combine(parent, "host");
            var outside = Path.Combine(parent, "outside");
            var junction = Path.Combine(host, "linked-output");
            var authoredJunction = Path.Combine(host, "linked-authored");
            Directory.CreateDirectory(host);
            Directory.CreateDirectory(outside);
            try
            {
                using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + outside + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) Assert.Ignore("The local Windows identity cannot create an NTFS junction.");
                }
                Assert.That(() => new RuntimeSmokeJsonlTranscriptSink(host, Path.Combine(junction, "Artifacts"), Path.Combine(host, "Assets", "Testing"), "junction-run"),
                    Throws.TypeOf<InvalidOperationException>());
                using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + authoredJunction + "\" \"" + outside + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) Assert.Ignore("The local Windows identity cannot create an NTFS junction.");
                }
                Assert.That(() => new RuntimeSmokeJsonlTranscriptSink(host, Path.Combine(host, "Artifacts"), authoredJunction, "authored-junction"),
                    Throws.TypeOf<InvalidOperationException>());
            }
            finally
            {
                if (Directory.Exists(junction)) Directory.Delete(junction, false);
                if (Directory.Exists(authoredJunction)) Directory.Delete(authoredJunction, false);
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }

        private static RuntimeSmokeCompiledWorkflow Workflow() => Compile("{\"schemaVersion\":1,\"id\":\"workflows/coordinator\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"commands complete\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"do\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}");

        private static RuntimeSmokeCompiledWorkflow WorkflowWithFixture() => Compile("{\"schemaVersion\":1,\"id\":\"workflows/coordinator-fixture\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"commands complete for the authored fixture\",\"excludedBehavior\":\"save/load\",\"scenario\":\"scenario-authored\",\"seed\":\"42\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"do\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}");

        private static RuntimeSmokeCompiledWorkflow AssertionWorkflow() => Compile("{\"schemaVersion\":1,\"id\":\"workflows/assertion\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"ready state holds\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.ready\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"is-ready\",\"name\":\"Is ready\",\"predicate\":\"test.ready\",\"comparison\":\"equals\",\"expected\":true}},{\"id\":\"finish\",\"type\":\"terminal\"}]}");

        private static RuntimeSmokeCompiledWorkflow Compile(string json)
        {
            var result = RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command", "test.ready" } });
            Assert.That(result.Success, Is.True, string.Join(";", result.Issues.Select(issue => issue.Code)));
            return result.Workflow;
        }

        private static RuntimeSmokeStepResponse Response(string correlation, RuntimeSmokeStepResponseStatus status, RuntimeSmokeValue actual = null) =>
            new RuntimeSmokeStepResponse { CorrelationId = correlation, Status = status, Actual = actual };

        private sealed class FakeAdapter : IRuntimeSmokeStepAdapter
        {
            private readonly Func<RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, RuntimeSmokeStepResponse> _execute;
            public FakeAdapter(Func<RuntimeSmokeCompiledStep, RuntimeSmokeStepContext, RuntimeSmokeStepResponse> execute) { _execute = execute; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) => Task.FromResult(_execute(step, context));
        }

        private sealed class DelayedAdapter : IRuntimeSmokeStepAdapter
        {
            private readonly int _delayMilliseconds;
            public DelayedAdapter(int delayMilliseconds) { _delayMilliseconds = delayMilliseconds; }
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) =>
                Task.Delay(_delayMilliseconds).ContinueWith(_ => Response(context.CorrelationId, RuntimeSmokeStepResponseStatus.Passed));
        }

        private sealed class FakeCleanup : IRuntimeSmokeCleanup
        {
            public int Count { get; private set; }
            public void RestoreSessionOverrides() { Count++; }
        }

        private sealed class MemorySink : IRuntimeSmokeTranscriptSink
        {
            public readonly List<RuntimeSmokeTranscriptRecord> Records = new List<RuntimeSmokeTranscriptRecord>();
            public void Append(RuntimeSmokeTranscriptRecord record) { Records.Add(record); }
        }

        private sealed class ThrowingSink : IRuntimeSmokeTranscriptSink
        {
            public void Append(RuntimeSmokeTranscriptRecord record) => throw new InvalidOperationException("sink failed");
        }

        private sealed class ThrowingResultSink : IRuntimeSmokeTranscriptSink, IRuntimeSmokeResultSink
        {
            public void Append(RuntimeSmokeTranscriptRecord record) { }
            public void WriteResult(RuntimeSmokeExecutionResult result) => throw new IOException("disk full");
        }
    }
}
