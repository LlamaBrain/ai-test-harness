using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeSuiteExecutorTests
    {
        [Test]
        public async Task MandatoryWorkflowFailureFailsSuiteWithoutSkippingLaterEntries()
        {
            var source = Source();
            var suite = RuntimeSmokeSuiteValidator.Validate(Suite(), source, RuntimeSmokeTarget.Editor,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command" } });
            Assert.That(suite.Success, Is.True, string.Join(";", suite.Issues.Select(issue => issue.Code)));
            var executed = new List<string>();
            var result = await RuntimeSmokeSuiteExecutor.RunAsync(suite, "suite-run", (entry, step, context) =>
            {
                executed.Add(entry.Id);
                return Task.FromResult(new RuntimeSmokeStepResponse
                {
                    CorrelationId = context.CorrelationId,
                    Status = entry.Id == "optional-fails" ? RuntimeSmokeStepResponseStatus.Failed : RuntimeSmokeStepResponseStatus.Passed
                });
            }, correlationFactory: entry => System.Guid.NewGuid().ToString("N") + "-" + entry.Id);

            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeSuiteTerminalState.Failed));
            Assert.That(result.Entries.Select(entry => entry.EntryId), Is.EqualTo(new[] { "optional-fails", "required-passes" }));
            Assert.That(executed.Distinct(), Is.EqualTo(new[] { "optional-fails", "required-passes" }));
        }

        [Test]
        public async Task OptionalFailureDoesNotFailSuiteButEveryRunKeepsItsOwnCorrelation()
        {
            var source = Source();
            var suite = RuntimeSmokeSuiteValidator.Validate(Suite(optionalFirst: true), source, RuntimeSmokeTarget.Editor,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "test.command" } });
            var correlations = new List<string>();
            var result = await RuntimeSmokeSuiteExecutor.RunAsync(suite, "suite-optional", (entry, step, context) =>
            {
                correlations.Add(context.CorrelationId);
                return Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = entry.Id == "optional-fails" ? RuntimeSmokeStepResponseStatus.Failed : RuntimeSmokeStepResponseStatus.Passed });
            }, correlationFactory: entry => System.Guid.NewGuid().ToString("N") + "-" + entry.Id);

            Assert.That(suite.Entries.Select(entry => entry.Required), Is.EqualTo(new[] { false, true }));
            Assert.That(result.Entries.Select(entry => entry.Required), Is.EqualTo(new[] { false, true }));
            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeSuiteTerminalState.Passed),
                string.Join(";", result.Entries.Select(entry => entry.EntryId + ":" + entry.Required + ":" + entry.CorrelationId + ":" + entry.Execution.TerminalState + ":" + entry.Execution.FailureCode + ":" + entry.Execution.Diagnostic)));
            Assert.That(correlations.Distinct().Count(), Is.EqualTo(2));
        }

        private static RuntimeSmokeMemoryDocumentSource Source()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            source.Add("workflows/one", Workflow("workflows/one"));
            source.Add("workflows/two", Workflow("workflows/two"));
            return source;
        }

        private static string Suite(bool optionalFirst = false) => "{\"schemaVersion\":1,\"id\":\"suites/sample\",\"kind\":\"suite\",\"entries\":[" +
            Entry("optional-fails", "workflows/one", !optionalFirst) + "," + Entry("required-passes", "workflows/two", true) + "]}";

        private static string Entry(string id, string script, bool required) =>
            "{\"id\":\"" + id + "\",\"script\":\"" + script + "\",\"target\":\"editor\",\"timeoutSeconds\":10,\"required\":" + (required ? "true" : "false") + "}";

        private static string Workflow(string id) => "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"command completes\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\"],\"requires\":[\"test.command\"],\"timeoutSeconds\":10,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"do\",\"type\":\"command\",\"command\":\"test.command\"},{\"id\":\"end\",\"type\":\"terminal\"}]}";
    }
}
