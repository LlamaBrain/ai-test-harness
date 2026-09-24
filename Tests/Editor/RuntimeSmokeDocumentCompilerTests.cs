using System.Linq;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeDocumentCompilerTests
    {
        private const string RootPrefix = "{\"schemaVersion\":1,\"id\":\"workflows/build\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"build a structure\",\"excludedBehavior\":\"save/load\",\"targets\":[\"editor\",\"development\"],\"requires\":[\"ath.start\",\"builder.create\"],\"timeoutSeconds\":60,";
        private const string RootSuffix = ",\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
        private const string Fragment = "{\"schemaVersion\":1,\"id\":\"actions/create\",\"kind\":\"fragment\",\"steps\":[{\"id\":\"place\",\"type\":\"command\",\"command\":\"builder.create ${width}\"}]}";

        [Test]
        public void IncludesExpandWithParametersAndCanonicalHashIgnoresFormatting()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            source.Add("actions/create", Fragment);
            var compact = RootPrefix + "\"include\":[{\"script\":\"actions/create\",\"with\":[{\"name\":\"width\",\"value\":\"7\"}]}]" + RootSuffix;
            var spaced = RootPrefix + " \"include\" : [ { \"script\" : \"actions/create\", \"with\" : [ { \"name\" : \"width\", \"value\" : \"7\" } ] } ] " + RootSuffix;
            var reordered = compact.Replace("\"schemaVersion\":1,\"id\":\"workflows/build\"", "\"id\":\"workflows/build\",\"schemaVersion\":1");

            var first = Compile(compact, source);
            var second = Compile(spaced, source);
            var third = Compile(reordered, source);

            Assert.That(first.Success, Is.True, Issues(first));
            Assert.That(first.Workflow.Steps.Select(step => step.Id), Is.EqualTo(new[] { "begin", "place", "finish" }));
            Assert.That(first.Workflow.Steps[1].Command, Is.EqualTo("builder.create 7"));
            Assert.That(first.Workflow.CanonicalHash, Is.EqualTo(second.Workflow.CanonicalHash));
            Assert.That(first.Workflow.CanonicalHash, Is.EqualTo(third.Workflow.CanonicalHash));
            Assert.That(first.Workflow.CanonicalHash, Has.Length.EqualTo(64));
        }

        [Test]
        public void AuthoredScenarioAndSeedAreCompiledAndParticipateInScriptIdentity()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            var authored = Compile(RootPrefix + "\"scenario\":\"flat.colony_survival_7\",\"seed\":\"7319\",\"include\":[]" + RootSuffix, source);
            var differentSeed = Compile(RootPrefix + "\"scenario\":\"flat.colony_survival_7\",\"seed\":\"7320\",\"include\":[]" + RootSuffix, source);

            Assert.That(authored.Success, Is.True, Issues(authored));
            Assert.That(authored.Workflow.GetType().GetProperty("Scenario"), Is.Not.Null);
            Assert.That(authored.Workflow.GetType().GetProperty("Seed"), Is.Not.Null);
            Assert.That(authored.Workflow.GetType().GetProperty("Scenario").GetValue(authored.Workflow, null), Is.EqualTo("flat.colony_survival_7"));
            Assert.That(authored.Workflow.GetType().GetProperty("Seed").GetValue(authored.Workflow, null), Is.EqualTo("7319"));
            Assert.That(authored.Workflow.CanonicalHash, Is.Not.EqualTo(differentSeed.Workflow.CanonicalHash));

            var unsafeScenario = Compile(RootPrefix + "\"scenario\":\"../outside\",\"include\":[]" + RootSuffix, source);
            Assert.That(HasIssue(unsafeScenario, "scenario_or_seed_invalid"), Is.True);
        }

        [Test]
        public void MissingIncludeAndCyclesAreRejectedBeforeExpansion()
        {
            var missing = Compile(RootPrefix + "\"include\":[{\"script\":\"setup/missing\"}]" + RootSuffix,
                new RuntimeSmokeMemoryDocumentSource());
            Assert.That(HasIssue(missing, "include_missing"), Is.True);

            var source = new RuntimeSmokeMemoryDocumentSource();
            source.Add("fragments/a", "{\"schemaVersion\":1,\"id\":\"fragments/a\",\"kind\":\"fragment\",\"include\":[{\"script\":\"fragments/b\"}]}");
            source.Add("fragments/b", "{\"schemaVersion\":1,\"id\":\"fragments/b\",\"kind\":\"fragment\",\"include\":[{\"script\":\"fragments/a\"}]}");
            var cycle = Compile(RootPrefix + "\"include\":[{\"script\":\"fragments/a\"}]" + RootSuffix, source);
            Assert.That(HasIssue(cycle, "include_cycle"), Is.True);
        }

        [Test]
        public void DuplicateParametersAreRejectedAndRootBoundaryPrecedesFragments()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            source.Add("actions/create", Fragment);
            var duplicateParameter = RootPrefix + "\"include\":[{\"script\":\"actions/create\",\"with\":[{\"name\":\"width\",\"value\":\"7\"},{\"name\":\"width\",\"value\":\"8\"}]}]" + RootSuffix;
            Assert.That(HasIssue(Compile(duplicateParameter, source), "parameter_invalid"), Is.True);

            var valid = Compile(RootPrefix + "\"include\":[{\"script\":\"actions/create\",\"with\":[{\"name\":\"width\",\"value\":\"7\"}]}]" + RootSuffix, source);
            Assert.That(valid.Workflow.Steps[0].Type, Is.EqualTo("start"));
            Assert.That(valid.Workflow.Steps[1].Type, Is.EqualTo("command"));
            Assert.That(valid.Workflow.Steps[2].Type, Is.EqualTo("terminal"));
        }

        [Test]
        public void DuplicateExpandedStepIdsAndUnknownVariablesAreRejected()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            source.Add("actions/create", Fragment);
            var twice = RootPrefix + "\"include\":[{\"script\":\"actions/create\",\"with\":[{\"name\":\"width\",\"value\":\"7\"}]},{\"script\":\"actions/create\",\"stepPrefix\":\"\",\"with\":[{\"name\":\"width\",\"value\":\"8\"}]}]" + RootSuffix;
            Assert.That(HasIssue(Compile(twice, source), "step_id_duplicate"), Is.True);

            source.Add("actions/unknown", Fragment.Replace("actions/create", "actions/unknown").Replace("${width}", "${unknown}"));
            var unknown = Compile(RootPrefix + "\"include\":[{\"script\":\"actions/unknown\"}]" + RootSuffix, source);
            Assert.That(HasIssue(unknown, "variable_unknown"), Is.True);
        }

        [Test]
        public void InvalidTargetCapabilityModeAndBuildLaneAreRejected()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            var unsupportedTarget = Compile(RootPrefix.Replace("\"targets\":[\"editor\",\"development\"]", "\"targets\":[\"development\"]") + "\"include\":[]" + RootSuffix, source);
            Assert.That(HasIssue(unsupportedTarget, "target_unsupported"), Is.True);

            var options = new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start" } };
            var unavailableCapability = RuntimeSmokeDocumentCompiler.Compile(RootPrefix + "\"include\":[]" + RootSuffix,
                source, RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, options);
            Assert.That(HasIssue(unavailableCapability, "capability_unavailable"), Is.True);

            var badMode = Compile(RootPrefix.Replace("\"fsd\"", "\"not-a-mode\"") + "\"include\":[]" + RootSuffix, source);
            Assert.That(HasIssue(badMode, "mode_invalid"), Is.True);

            var hitl = RootPrefix.Replace("\"fsd\"", "\"hitl\"").Replace("\"targets\":[\"editor\",\"development\"]", "\"targets\":[\"development\"]") + "\"include\":[]" + RootSuffix;
            var buildOptions = new RuntimeSmokeCompileOptions { BuildSuiteEntry = true, AvailableCapabilities = new[] { "ath.start", "builder.create" } };
            var buildResult = RuntimeSmokeDocumentCompiler.Compile(hitl, source, RuntimeSmokeTarget.Development, RuntimeSmokeMode.Hitl, buildOptions);
            Assert.That(HasIssue(buildResult, "build_suite_requires_fsd"), Is.True);
        }

        [Test]
        public void FragmentsCannotStartOrEndARunAndSleepMustBeBoundedAndExplained()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            source.Add("actions/boundary", "{\"schemaVersion\":1,\"id\":\"actions/boundary\",\"kind\":\"fragment\",\"steps\":[{\"id\":\"start\",\"type\":\"start\"}]}");
            var boundary = Compile(RootPrefix + "\"include\":[{\"script\":\"actions/boundary\"}]" + RootSuffix, source);
            Assert.That(HasIssue(boundary, "fragment_runtime_boundary"), Is.True);

            var invalidSleep = RootPrefix + "\"include\":[],\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"pause\",\"type\":\"sleep\",\"seconds\":0},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            Assert.That(HasIssue(Compile(invalidSleep, source), "sleep_invalid"), Is.True);
        }

        [Test]
        public void SuiteBuildGateRejectsNonFsdWorkflow()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            var workflow = RootPrefix.Replace("\"fsd\"", "\"ai_observed\"") + "\"include\":[]" + RootSuffix;
            source.Add("workflows/build", workflow);
            const string suite = "{\"schemaVersion\":1,\"id\":\"suites/required\",\"kind\":\"suite\",\"entries\":[{\"id\":\"build\",\"script\":\"workflows/build\",\"target\":\"editor\",\"timeoutSeconds\":60}]}";

            var result = RuntimeSmokeSuiteValidator.Validate(suite, source, RuntimeSmokeTarget.Editor,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start", "builder.create" } });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Issues.Any(issue => issue.Code == "build_suite_requires_fsd"), Is.True);
        }

        [Test]
        public void SuiteRequiresStrictJsonNonemptyEntriesAndDeclaredTarget()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            const string emptySuite = "{\"schemaVersion\":1,\"id\":\"suites/empty\",\"kind\":\"suite\",\"entries\":[]}";
            Assert.That(RuntimeSmokeSuiteValidator.Validate(emptySuite, source, RuntimeSmokeTarget.Editor).Issues.Any(issue => issue.Code == "suite_empty"), Is.True);

            const string duplicateKeySuite = "{\"schemaVersion\":1,\"schemaVersion\":1,\"id\":\"suites/empty\",\"kind\":\"suite\",\"entries\":[]}";
            Assert.That(RuntimeSmokeSuiteValidator.Validate(duplicateKeySuite, source, RuntimeSmokeTarget.Editor).Success, Is.False);

            source.Add("workflows/build", RootPrefix + "\"include\":[]" + RootSuffix);
            const string optionalSuite = "{\"schemaVersion\":1,\"id\":\"suites/optional\",\"kind\":\"suite\",\"entries\":[{\"id\":\"build\",\"script\":\"workflows/build\",\"target\":\"editor\",\"timeoutSeconds\":60,\"required\":false}]}";
            var optional = RuntimeSmokeSuiteValidator.Validate(optionalSuite, source, RuntimeSmokeTarget.Editor,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start", "builder.create" } });
            Assert.That(optional.Success, Is.True, string.Join("; ", optional.Issues.Select(issue => issue.Code)));
            Assert.That(optional.Entries.Single().Required, Is.False);

            const string missingTargetSuite = "{\"schemaVersion\":1,\"id\":\"suites/required\",\"kind\":\"suite\",\"entries\":[{\"id\":\"build\",\"script\":\"workflows/build\",\"timeoutSeconds\":60}]}";
            Assert.That(RuntimeSmokeSuiteValidator.Validate(missingTargetSuite, source, RuntimeSmokeTarget.Editor,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start", "builder.create" } }).Issues.Any(issue => issue.Code == "suite_target_mismatch"), Is.True);
        }

        [Test]
        public void TargetAndModeMatrixRejectsHumanHarnessAndUnknownTargets()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            var workflow = RootPrefix + "\"include\":[]" + RootSuffix;
            var harnessFsd = RuntimeSmokeDocumentCompiler.Compile(workflow, source, RuntimeSmokeTarget.SmokeHarness, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start", "builder.create" } });
            Assert.That(HasIssue(harnessFsd, "target_unsupported"), Is.True);

            var hitl = workflow.Replace("\"mode\":\"fsd\"", "\"mode\":\"hitl\"").Replace("\"targets\":[\"editor\",\"development\"]", "\"targets\":[\"smoke_harness\"]");
            var humanHarness = RuntimeSmokeDocumentCompiler.Compile(hitl, source, RuntimeSmokeTarget.SmokeHarness, RuntimeSmokeMode.Hitl,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start", "builder.create" } });
            Assert.That(HasIssue(humanHarness, "mode_target_invalid"), Is.True);

            var invalidTarget = RuntimeSmokeDocumentCompiler.Compile(workflow, source, (RuntimeSmokeTarget)99, RuntimeSmokeMode.Fsd);
            Assert.That(invalidTarget.Success, Is.False);
        }

        [Test]
        public void WorkflowAssertionCompilesFromCanonicalTypedExpectedValue()
        {
            const string workflow = "{\"schemaVersion\":1,\"id\":\"workflows/assert\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"state is ready\",\"excludedBehavior\":\"persistence\",\"targets\":[\"editor\"],\"requires\":[\"state.ready\"],\"timeoutSeconds\":30,\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"ready\",\"name\":\"Ready state\",\"predicate\":\"state.ready\",\"comparison\":\"contains\",\"expected\":{\"ready\":true}}},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var result = RuntimeSmokeDocumentCompiler.Compile(workflow, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "state.ready" } });

            Assert.That(result.Success, Is.True, Issues(result));
            Assert.That(result.Workflow.Steps[1].Assertion, Is.Not.Null);
            Assert.That(result.Workflow.Steps[1].Assertion.Expected.Map["ready"].Boolean, Is.True);
        }

        [Test]
        public void InvalidDeclaredComparisonTypeAndZeroRelativeExpectationAreRejectedBeforeExecution()
        {
            const string invalidNumericPair = "{\"assertionSchemaVersion\":1,\"id\":\"ready\",\"name\":\"Ready is greater\",\"predicate\":\"state.ready\",\"comparison\":\"greater_than\",\"expected\":\"five\"}";
            const string zeroRelativePair = "{\"assertionSchemaVersion\":1,\"id\":\"ratio\",\"name\":\"Ratio is close\",\"predicate\":\"state.ratio\",\"comparison\":\"within_relative\",\"expected\":0,\"tolerance\":0.1}";
            var numericWorkflow = RootPrefix + "\"include\":[],\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ready\",\"type\":\"assert\",\"assertion\":" + invalidNumericPair + "},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var zeroRelativeWorkflow = RootPrefix + "\"include\":[],\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ratio\",\"type\":\"assert\",\"assertion\":" + zeroRelativePair + "},{\"id\":\"finish\",\"type\":\"terminal\"}]}";
            var source = new RuntimeSmokeMemoryDocumentSource();

            var invalidNumeric = Compile(numericWorkflow, source);
            var zeroRelative = Compile(zeroRelativeWorkflow, source);

            Assert.That(invalidNumeric.Success, Is.False);
            Assert.That(HasIssue(invalidNumeric, "assertion_schema_invalid"), Is.True);
            Assert.That(zeroRelative.Success, Is.False);
            Assert.That(HasIssue(zeroRelative, "assertion_schema_invalid"), Is.True);
        }

        [Test]
        public void ToleranceMustBeAuthoredAsFiniteDecimalRatherThanInteger()
        {
            const string integerTolerance = "{\"assertionSchemaVersion\":1,\"id\":\"ratio\",\"name\":\"Ratio is close\",\"predicate\":\"state.ratio\",\"comparison\":\"within_absolute\",\"expected\":1,\"tolerance\":1}";
            const string decimalTolerance = "{\"assertionSchemaVersion\":1,\"id\":\"ratio\",\"name\":\"Ratio is close\",\"predicate\":\"state.ratio\",\"comparison\":\"within_absolute\",\"expected\":1,\"tolerance\":1.0}";
            var source = new RuntimeSmokeMemoryDocumentSource();
            var integerWorkflow = Compile(RootPrefix + "\"include\":[],\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ratio\",\"type\":\"assert\",\"assertion\":" + integerTolerance + "},{\"id\":\"finish\",\"type\":\"terminal\"}]}", source);
            var decimalWorkflow = Compile(RootPrefix + "\"include\":[],\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ratio\",\"type\":\"assert\",\"assertion\":" + decimalTolerance + "},{\"id\":\"finish\",\"type\":\"terminal\"}]}", source);

            Assert.That(integerWorkflow.Success, Is.False, "An integer token must not be coerced into a schema-v1 decimal tolerance.");
            Assert.That(HasIssue(integerWorkflow, "assertion_schema_invalid"), Is.True);
            Assert.That(decimalWorkflow.Success, Is.True, Issues(decimalWorkflow));

            const string unsafeClassification = "{\"assertionSchemaVersion\":1,\"id\":\"ratio\",\"name\":\"Ratio is close\",\"predicate\":\"state.ratio\",\"comparison\":\"equals\",\"expected\":1,\"classification\":\"../outside\"}";
            var unsafeClassificationWorkflow = Compile(RootPrefix + "\"include\":[],\"steps\":[{\"id\":\"begin\",\"type\":\"start\"},{\"id\":\"ratio\",\"type\":\"assert\",\"assertion\":" + unsafeClassification + "},{\"id\":\"finish\",\"type\":\"terminal\"}]}", source);
            Assert.That(unsafeClassificationWorkflow.Success, Is.False);
            Assert.That(HasIssue(unsafeClassificationWorkflow, "assertion_schema_invalid"), Is.True);
        }

        [Test]
        public void InvestigationContractCompilesWithSafeActionsAndSeparateContextHash()
        {
            const string investigation = "\"investigation\":{\"summary\":\"Inspect construction readiness\",\"invariant\":\"The declared structure is complete\",\"fixtureSemantics\":\"A deterministic empty 7x7 build area\",\"setupRationale\":\"Isolate the construction loop\",\"expectedProgress\":\"Walls then roof\",\"expectedEvidence\":[\"result.json\",\"player.log\",\"captures/construction-failure.png\"],\"benignShutdownNoise\":\"Known editor shutdown warning\",\"failureDisposition\":\"auto_retry_forbidden\",\"excludedSystems\":[\"save_load\"],\"classifications\":[{\"failureCode\":\"assertion_mismatch\",\"likelyOwner\":\"construction\",\"rationale\":\"The measured structure differs\"}],\"actions\":[{\"id\":\"status\",\"kind\":\"status_query\",\"capability\":\"investigation.status\"},{\"id\":\"state\",\"kind\":\"predicate_query\",\"capability\":\"investigation.predicate\",\"predicate\":\"state.structure_counts\"},{\"id\":\"capture\",\"kind\":\"named_capture\",\"capability\":\"investigation.capture\",\"captureLabel\":\"construction-failure\"}]}";
            var json = RootPrefix.Replace("\"builder.create\"", "\"builder.create\",\"investigation.status\",\"investigation.predicate\",\"investigation.capture\",\"state.structure_counts\"") + "\"scenario\":\"scenario-authored\",\"seed\":\"7319\"," + investigation + ",\"include\":[]" + RootSuffix;
            var options = StrictOptions("ath.start", "builder.create", "investigation.status", "investigation.predicate", "investigation.capture", "state.structure_counts");
            var result = RuntimeSmokeDocumentCompiler.Compile(json, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, options);

            Assert.That(result.Success, Is.True, Issues(result));
            var workflow = result.Workflow;
            Assert.That(workflow.GetType().GetProperty("Investigation"), Is.Not.Null);
            Assert.That(workflow.GetType().GetProperty("InvestigationHash"), Is.Not.Null);
            var contract = workflow.GetType().GetProperty("Investigation").GetValue(workflow, null);
            Assert.That(contract.GetType().GetField("actions").GetValue(contract), Has.Length.EqualTo(3));
            Assert.That(workflow.GetType().GetProperty("InvestigationHash").GetValue(workflow, null), Is.Not.Empty);

            var changed = json.Replace("Inspect construction readiness", "Inspect the changed construction behavior");
            var changedResult = RuntimeSmokeDocumentCompiler.Compile(changed, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, options);
            Assert.That(changedResult.Success, Is.True, Issues(changedResult));
            Assert.That(workflow.CanonicalHash, Is.Not.EqualTo(changedResult.Workflow.CanonicalHash));
            Assert.That(workflow.GetType().GetProperty("InvestigationHash").GetValue(workflow, null),
                Is.Not.EqualTo(changedResult.Workflow.GetType().GetProperty("InvestigationHash").GetValue(changedResult.Workflow, null)));

            var privatePath = json.Replace("Inspect construction readiness", "Inspect C:/private/session");
            var privatePathResult = RuntimeSmokeDocumentCompiler.Compile(privatePath, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, options);
            Assert.That(HasIssue(privatePathResult, "investigation_invalid"), Is.True);

            var traversal = json.Replace("captures/construction-failure.png", "captures/../private.png");
            var traversalResult = RuntimeSmokeDocumentCompiler.Compile(traversal, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, options);
            Assert.That(HasIssue(traversalResult, "investigation_invalid"), Is.True);

            var absolute = json.Replace("captures/construction-failure.png", "/private.png");
            var absoluteResult = RuntimeSmokeDocumentCompiler.Compile(absolute, new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd, options);
            Assert.That(HasIssue(absoluteResult, "investigation_invalid"), Is.True);
        }

        [Test]
        public void InvestigationContractRequiredByHostAndRejectsUnknownOrArbitraryActionFields()
        {
            var source = new RuntimeSmokeMemoryDocumentSource();
            var missing = RuntimeSmokeDocumentCompiler.Compile(RootPrefix + "\"include\":[]" + RootSuffix, source,
                RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                StrictOptions("ath.start", "builder.create"));
            Assert.That(HasIssue(missing, "investigation_missing"), Is.True, Issues(missing));

            var unsafeJson = RootPrefix + "\"investigation\":{\"summary\":\"Inspect safely\",\"invariant\":\"The state is known\",\"fixtureSemantics\":\"Fixed fixture\",\"setupRationale\":\"No extra mutations\",\"expectedProgress\":\"State query\",\"failureDisposition\":\"diagnosis_only\",\"actions\":[{\"id\":\"arbitrary\",\"kind\":\"console_command\",\"capability\":\"ath.command\",\"command\":\"arbitrary mutate\",\"path\":\"../../outside\"}]},\"include\":[]" + RootSuffix;
            var unsafeResult = RuntimeSmokeDocumentCompiler.Compile(unsafeJson, source, RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                StrictOptions("ath.start", "builder.create", "ath.command"));
            Assert.That(HasIssue(unsafeResult, "investigation_action_invalid"), Is.True, Issues(unsafeResult));
        }

        private static RuntimeSmokeCompileResult Compile(string json, IRuntimeSmokeDocumentSource source)
        {
            return RuntimeSmokeDocumentCompiler.Compile(json, source, RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
                new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "ath.start", "builder.create" } });
        }

        private static RuntimeSmokeCompileOptions StrictOptions(params string[] capabilities)
        {
            var options = new RuntimeSmokeCompileOptions { AvailableCapabilities = capabilities };
            var property = options.GetType().GetProperty("RequireInvestigationContract");
            Assert.That(property, Is.Not.Null, "Host workflows need an explicit strict investigation-contract policy.");
            property?.SetValue(options, true, null);
            return options;
        }

        private static bool HasIssue(RuntimeSmokeCompileResult result, string code) => result.Issues.Any(issue => issue.Code == code);
        private static string Issues(RuntimeSmokeCompileResult result) => string.Join("; ", result.Issues.Select(issue => issue.Code + ":" + issue.Message));
    }
}
