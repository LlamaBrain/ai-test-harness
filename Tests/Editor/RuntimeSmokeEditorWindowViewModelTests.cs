using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using LlamaBrainLabs.Ath.Editor.RuntimeSmokes;
using LlamaBrainLabs.Ath.Editor.McpSkills;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeEditorWindowViewModelTests
    {
        [Test]
        public void FilterMatchesWorkflowIdPropositionAndCapabilities()
        {
            var workflow = Workflow("workflows/colony-ready", "Colony reaches game ready", "bts.game_ready");
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Workflows = new[] { workflow },
                Filter = "GAME READY"
            });

            Assert.That(state.VisibleWorkflows, Is.EqualTo(new[] { workflow }));
            Assert.That(RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Workflows = new[] { workflow },
                Filter = "bts.game"
            }).VisibleWorkflows, Is.EqualTo(new[] { workflow }));
        }

        [Test]
        public void SelectedWorkflowProjectsItsExecutionMetadataForTheWindow()
        {
            var workflow = Workflow("workflows/deterministic", "Use the authored fixture", "ath.start");
            Set(workflow, "Target", "development");
            Set(workflow, "Mode", "fsd");
            Set(workflow, "Scenario", "flat.colony_survival_7");
            Set(workflow, "Seed", "9001");
            Set(workflow, "InvestigationSummary", "Inspect deterministic fixture state");
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Workflows = new[] { workflow },
                SelectedWorkflowId = workflow.Id,
                Snapshot = Snapshot(RuntimeSmokeRegistryRunState.Running, null)
            });

            var selected = state.SelectedWorkflow;
            Assert.That(selected, Is.SameAs(workflow));
            Assert.That(selected.Scenario, Is.EqualTo("flat.colony_survival_7"));
            Assert.That(selected.Seed, Is.EqualTo("9001"));
            Assert.That(selected.InvestigationSummary, Is.EqualTo("Inspect deterministic fixture state"));
            Assert.That(state.Scenario, Is.EqualTo("scenario-run"));
            Assert.That(state.Seed, Is.EqualTo("123"));
            Assert.That(RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true, Workflows = new[] { workflow }, Filter = "9001", SelectedWorkflowId = workflow.Id
            }).VisibleWorkflows, Is.EqualTo(new[] { workflow }));
            Assert.That(RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true, Workflows = new[] { workflow }, Filter = "inspect deterministic", SelectedWorkflowId = workflow.Id
            }).VisibleWorkflows, Is.EqualTo(new[] { workflow }));
        }

        [Test]
        public void ControlsReflectRegistryAndCheckpointState()
        {
            var workflow = Workflow("workflows/hitl", "Review the result");
            var idle = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Workflows = new[] { workflow },
                SelectedWorkflowId = workflow.Id
            });
            Assert.That(idle.Controls.CanStart, Is.True);
            Assert.That(idle.Controls.CanCancel, Is.False);
            Assert.That(idle.Controls.CanResume, Is.False);

            var review = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Workflows = new[] { workflow },
                SelectedWorkflowId = workflow.Id,
                Snapshot = Snapshot(RuntimeSmokeRegistryRunState.ReviewReady, "review-walls")
            });
            Assert.That(review.Controls.CanStart, Is.False);
            Assert.That(review.Controls.CanCancel, Is.True);
            Assert.That(review.Controls.CanResume, Is.True);
            Assert.That(review.ResumeCheckpointId, Is.EqualTo("review-walls"));
        }

        [Test]
        public void RunningProgressIsIndeterminateAndCountsCompletedTranscriptSteps()
        {
            var snapshot = Snapshot(RuntimeSmokeRegistryRunState.Running, null,
                new RuntimeSmokeTranscriptRecord { type = "step_completed", stepId = "start-game" },
                new RuntimeSmokeTranscriptRecord { type = "step_started", stepId = "wait-ready" });
            Set(snapshot, "ActiveStepId", "wait-ready");
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Snapshot = snapshot
            });

            Assert.That(state.Progress.IsIndeterminate, Is.True);
            Assert.That(state.Progress.CompletedSteps, Is.EqualTo(1));
            Assert.That(state.Progress.ActiveStepId, Is.EqualTo("wait-ready"));
        }

        [Test]
        public void WeightedProgressUsesDeclaredStepDurationAndLeavesActiveWorkIndeterminate()
        {
            var steps = new[]
            {
                CompiledStep("start", 1),
                CompiledStep("wait-ready", 9),
            };
            var calculator = typeof(RuntimeSmokeRegistrySnapshot).Assembly.GetType(
                "LlamaBrainLabs.Ath.RuntimeSmokes.RuntimeSmokeProgressCalculator");
            Assert.That(calculator, Is.Not.Null, "The registry should expose a shared weighted progress calculator.");
            var calculate = calculator.GetMethod("Calculate", BindingFlags.Static | BindingFlags.Public);
            Assert.That(calculate, Is.Not.Null);
            var projection = calculate.Invoke(null, new object[] { steps, new[]
            {
                new RuntimeSmokeTranscriptRecord { type = "step_completed", stepId = "start" },
            }, false });
            Assert.That(Property(projection, "CompletedWeight"), Is.EqualTo(1L));
            Assert.That(Property(projection, "TotalWeight"), Is.EqualTo(10L));
            Assert.That(Property(projection, "Percent"), Is.EqualTo(10));

            var snapshot = Snapshot(RuntimeSmokeRegistryRunState.Running, null,
                new RuntimeSmokeTranscriptRecord { type = "step_completed", stepId = "start" },
                new RuntimeSmokeTranscriptRecord { type = "step_started", stepId = "wait-ready" });
            Set(snapshot, "CompletedProgressWeight", Property(projection, "CompletedWeight"));
            Set(snapshot, "TotalProgressWeight", Property(projection, "TotalWeight"));
            Set(snapshot, "ProgressPercent", Property(projection, "Percent"));
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput { Registered = true, Snapshot = snapshot });
            Assert.That(state.Progress.IsIndeterminate, Is.True);
            Assert.That(state.Progress.Percent, Is.EqualTo(10));
        }

        [Test]
        public void FailureProjectionRetainsAssertionValuesTranscriptAndArtifactNames()
        {
            var snapshot = Snapshot(RuntimeSmokeRegistryRunState.Failed, null,
                new RuntimeSmokeTranscriptRecord
                {
                    type = "assertion_failed", stepId = "ready", assertionId = "is-ready",
                    assertionClassification = "construction_noop",
                    expectedDisplay = "true", actualDisplay = "false"
                });
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Snapshot = snapshot
            });

            Assert.That(state.AssertionFailure.AssertionId, Is.EqualTo("is-ready"));
            Assert.That(state.AssertionFailure.Classification, Is.EqualTo("construction_noop"));
            Assert.That(state.AssertionFailure.Expected, Is.EqualTo("true"));
            Assert.That(state.AssertionFailure.Actual, Is.EqualTo("false"));
            Assert.That(state.Transcript, Has.Count.EqualTo(1));
            Assert.That(state.Artifacts, Is.EquivalentTo(new[] { "transcript.jsonl", "result.json", "failure-context.json" }));
            Assert.That(state.Progress.IsIndeterminate, Is.False);
            Assert.That(state.Progress.Percent, Is.EqualTo(100));
        }

        [Test]
        public void FailedRunExposesOnlyItsDeclaredInvestigationActionsToTheWindow()
        {
            var failed = Snapshot(RuntimeSmokeRegistryRunState.Failed, null);
            Set(failed, "InvestigationActions", new[]
            {
                new RuntimeSmokeInvestigationAction { id = "status", kind = "status_query", capability = "investigation.status" }
            });
            var failedState = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput { Registered = true, Snapshot = failed });
            Assert.That(failedState.InvestigationActions.Select(action => action.id), Is.EqualTo(new[] { "status" }));

            var passed = Snapshot(RuntimeSmokeRegistryRunState.Passed, null);
            Set(passed, "InvestigationActions", new[]
            {
                new RuntimeSmokeInvestigationAction { id = "status", kind = "status_query", capability = "investigation.status" }
            });
            Assert.That(RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput { Registered = true, Snapshot = passed }).InvestigationActions, Is.Empty);
        }

        [Test]
        public void StateProjectsTheExactRegistrySnapshotForMcpParity()
        {
            var snapshot = Snapshot(RuntimeSmokeRegistryRunState.Passed, null);
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Snapshot = snapshot
            });

            Assert.That(state.Snapshot, Is.SameAs(snapshot));
            Assert.That(state.CorrelationId, Is.EqualTo(snapshot.CorrelationId));
            Assert.That(state.WorkflowId, Is.EqualTo(snapshot.WorkflowId));
            Assert.That(state.RunState, Is.EqualTo(snapshot.State));
        }

        [Test]
        public void McpAndWindowProjectionsExposeMatchingCheckpointProgressArtifactsAndActions()
        {
            var reviewSnapshot = Snapshot(RuntimeSmokeRegistryRunState.ReviewReady, "review-roof",
                new RuntimeSmokeTranscriptRecord { type = "step_completed", stepId = "walls" },
                new RuntimeSmokeTranscriptRecord { type = "step_started", stepId = "review-roof" });
            Set(reviewSnapshot, "ActiveStepId", "review-roof");
            Set(reviewSnapshot, "CaptureActions", new[]
            {
                new RuntimeSmokeInvestigationAction { id = "review-capture", kind = "named_capture", capability = "investigation.capture" }
            });
            Set(reviewSnapshot, "CaptureArtifacts", new[]
            {
                new RuntimeSmokeCaptureArtifactReference { path = "captures/review-checkpoint.png", bytes = 123, sha256 = new string('a', 64) }
            });
            Set(reviewSnapshot, "CompletedProgressWeight", 3);
            Set(reviewSnapshot, "TotalProgressWeight", 8);
            Set(reviewSnapshot, "ProgressPercent", 37);
            var windowReview = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput { Registered = true, Snapshot = reviewSnapshot });
            var mcpReview = ProjectMcpStatus(reviewSnapshot);

            Assert.That(Field(mcpReview, "State"), Is.EqualTo("review_ready"));
            Assert.That(((RuntimeSmokeInvestigationAction[])Field(mcpReview, "CaptureActions")).Length, Is.EqualTo(1));
            Assert.That(Field(Field(windowReview, "Controls"), "CanCapture"), Is.EqualTo(true));
            Assert.That(((RuntimeSmokeInvestigationAction[])Field(mcpReview, "CaptureActions")).Single().id, Is.EqualTo("review-capture"));
            Assert.That(Field(mcpReview, "ActiveCheckpointId"), Is.EqualTo(windowReview.ResumeCheckpointId));
            Assert.That(windowReview.Artifacts, Does.Contain("captures/review-checkpoint.png"));
            Assert.That(((string[])Field(mcpReview, "ArtifactNames")), Does.Contain("captures/review-checkpoint.png"));
            Assert.That(Field(Field(mcpReview, "Progress"), "IsIndeterminate"), Is.EqualTo(windowReview.Progress.IsIndeterminate));
            Assert.That(Field(Field(mcpReview, "Progress"), "CompletedSteps"), Is.EqualTo(windowReview.Progress.CompletedSteps));
            Assert.That(Field(Field(mcpReview, "Progress"), "ActiveStepId"), Is.EqualTo(windowReview.Progress.ActiveStepId));
            Assert.That(Field(Field(mcpReview, "Progress"), "CompletedWeight"), Is.EqualTo(3));
            Assert.That(Field(Field(mcpReview, "Progress"), "TotalWeight"), Is.EqualTo(8));
            Assert.That(Field(Field(mcpReview, "Progress"), "Percent"), Is.EqualTo(37));
            Assert.That(windowReview.Progress.Percent, Is.EqualTo(37));

            var failedSnapshot = Snapshot(RuntimeSmokeRegistryRunState.Failed, null,
                new RuntimeSmokeTranscriptRecord { type = "assertion_failed", stepId = "roof", assertionId = "roof-ready" });
            Set(failedSnapshot, "ArtifactDirectory", "C:/host/Artifacts/RuntimeSmokes/parity-run");
            var failureContext = new RuntimeSmokeFailureContext
            {
                hostStateDisplay = "{\"gameReady\":false}",
                hostStateDigest = new string('b', 64),
                target = "editor",
                mode = "fsd",
                failureCode = "assertion_mismatch",
                reproductionTransport = "editor-mcp",
                reproductionCommand = "ath-runtime-smoke-run workflowId=\"workflows/parity\" target=\"editor\" mode=\"fsd\" correlationId=\"fresh-id\"",
                captureArtifacts = new[] { new RuntimeSmokeCaptureArtifactReference
                    { path = "captures/game-view.png", bytes = 128, sha256 = new string('a', 64) } }
            };
            Set(failedSnapshot, "FailureContext", failureContext);
            Set(failedSnapshot, "InvestigationActions", new[]
            {
                new RuntimeSmokeInvestigationAction { id = "status", kind = "status_query", capability = "investigation.status" }
            });
            var windowFailed = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput { Registered = true, Snapshot = failedSnapshot });
            var mcpFailed = ProjectMcpStatus(failedSnapshot);

            Assert.That(Field(mcpFailed, "State"), Is.EqualTo("failed"));
            Assert.That(Field(mcpFailed, "FailureCode"), Is.EqualTo("assertion_mismatch"));
            Assert.That(Field(mcpFailed, "ArtifactDirectory"), Is.EqualTo(windowFailed.ArtifactDirectory));
            Assert.That(((string[])Field(mcpFailed, "ArtifactNames")).OrderBy(value => value), Is.EqualTo(windowFailed.Artifacts.OrderBy(value => value)));
            Assert.That(windowFailed.Artifacts, Does.Contain("captures/game-view.png"));
            Assert.That(windowFailed.FailureContext, Is.SameAs(failureContext));
            var mcpFailureContext = Field(mcpFailed, "FailureContext");
            Assert.That(Field(mcpFailureContext, "hostStateDisplay"), Is.EqualTo(failureContext.hostStateDisplay));
            Assert.That(Field(mcpFailureContext, "reproductionCommand"), Is.EqualTo(failureContext.reproductionCommand));
            Assert.That(((RuntimeSmokeInvestigationAction[])Field(mcpFailed, "InvestigationActions")).Select(action => action.id),
                Is.EqualTo(windowFailed.InvestigationActions.Select(action => action.id)));
            Assert.That(Field(Field(mcpFailed, "Progress"), "Percent"), Is.EqualTo(windowFailed.Progress.Percent));
        }

        [Test]
        public void ArtifactProjectionUsesTheExactRegistryRunDirectoryWhenAvailable()
        {
            var snapshot = Snapshot(RuntimeSmokeRegistryRunState.Failed, null);
            Set(snapshot, "ArtifactDirectory", "C:/custom-host/output/exact-run");
            var state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
            {
                Registered = true,
                Snapshot = snapshot
            });

            Assert.That(state.ArtifactDirectory, Is.EqualTo("C:/custom-host/output/exact-run"));
        }

        private static RuntimeSmokeWorkflowListItem Workflow(string id, string proposition, params string[] capabilities)
        {
            var item = new RuntimeSmokeWorkflowListItem();
            Set(item, "Id", id);
            Set(item, "Proposition", proposition);
            Set(item, "RequiredCapabilities", capabilities);
            Set(item, "ScriptHash", "hash");
            return item;
        }

        private static RuntimeSmokeCompiledStep CompiledStep(string id, int seconds)
        {
            var step = new RuntimeSmokeCompiledStep();
            Set(step, "Id", id);
            Set(step, "Seconds", seconds);
            return step;
        }

        private static RuntimeSmokeRegistrySnapshot Snapshot(RuntimeSmokeRegistryRunState status, string checkpoint,
            params RuntimeSmokeTranscriptRecord[] transcript)
        {
            var snapshot = new RuntimeSmokeRegistrySnapshot();
            Set(snapshot, "Found", true);
            Set(snapshot, "CorrelationId", "ui-run");
            Set(snapshot, "WorkflowId", "workflows/hitl");
            Set(snapshot, "ScriptHash", "hash");
            Set(snapshot, "State", status);
            Set(snapshot, "ActiveStepId", status == RuntimeSmokeRegistryRunState.Running ? "step" : null);
            Set(snapshot, "ActiveCheckpointId", checkpoint);
            Set(snapshot, "Scenario", "scenario-run");
            Set(snapshot, "Seed", "123");
            Set(snapshot, "Transcript", (IReadOnlyList<RuntimeSmokeTranscriptRecord>)(transcript ?? new RuntimeSmokeTranscriptRecord[0]));
            Set(snapshot, "CompletedProgressWeight", 0L);
            Set(snapshot, "TotalProgressWeight", 1L);
            Set(snapshot, "ProgressPercent", status == RuntimeSmokeRegistryRunState.Passed ||
                status == RuntimeSmokeRegistryRunState.Failed || status == RuntimeSmokeRegistryRunState.Cancelled ? 100 : 0);
            return snapshot;
        }

        private static void Set(object target, string property, object value)
        {
            var member = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(member, Is.Not.Null, "Missing test projection property " + property);
            member.SetValue(target, value, null);
        }

        private static object ProjectMcpStatus(RuntimeSmokeRegistrySnapshot snapshot)
        {
            var method = typeof(Tool_AthRuntimeSmoke).GetMethod("ProjectStatus", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "MCP status should expose a testable projection of the registry snapshot.");
            return method.Invoke(null, new object[] { snapshot });
        }

        private static object Field(object target, string name)
        {
            Assert.That(target, Is.Not.Null, "Missing projected object for field " + name);
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing MCP status field " + name);
            return field.GetValue(target);
        }

        private static object Property(object target, string name)
        {
            Assert.That(target, Is.Not.Null, "Missing projected object for property " + name);
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, "Missing projection property " + name);
            return property.GetValue(target, null);
        }
    }
}
