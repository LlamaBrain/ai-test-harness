#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    public interface IRuntimeSmokeDocumentSource
    {
        bool TryLoad(string scriptId, out string json);
    }

    public sealed class RuntimeSmokeMemoryDocumentSource : IRuntimeSmokeDocumentSource
    {
        private readonly Dictionary<string, string> _documents = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Add(string id, string json)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Document id is required.", nameof(id));
            if (json == null) throw new ArgumentNullException(nameof(json));
            _documents[id] = json;
        }

        public bool TryLoad(string scriptId, out string json) => _documents.TryGetValue(scriptId, out json);
    }

    [Serializable]
    public sealed class RuntimeSmokeDocumentDefinition
    {
        public int schemaVersion = 1;
        public string id;
        public string kind;
        public string testClass;
        public string mode;
        public string proposition;
        public string excludedBehavior;
        public string scenario;
        public string seed;
        public string parityContract;
        public string[] targets;
        public string[] requires;
        public int timeoutSeconds;
        public RuntimeSmokeScriptInclude[] include;
        public RuntimeSmokeStepDefinition[] steps;
        public RuntimeSmokeSuiteEntry[] entries;
        public string[] includeSuites;
        public RuntimeSmokeInvestigationDefinition investigation;
        [NonSerialized] public RuntimeSmokeValue investigationValue;
    }

    [Serializable]
    public sealed class RuntimeSmokeScriptInclude
    {
        public string script;
        public string stepPrefix;
        public RuntimeSmokeVariable[] with;
    }

    [Serializable]
    public sealed class RuntimeSmokeVariable
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class RuntimeSmokeStepDefinition
    {
        public string id;
        public string type;
        public string command;
        public string predicate;
        public string value;
        public string reason;
        public int seconds;
        [NonSerialized] public RuntimeSmokeAssertionDefinition assertion;
    }

    [Serializable]
    public sealed class RuntimeSmokeSuiteEntry
    {
        public string id;
        public string script;
        public string target;
        public int timeoutSeconds;
        public bool required = true;
    }

    [Serializable]
    public sealed class RuntimeSmokeInvestigationDefinition
    {
        public string summary;
        public string invariant;
        public string fixtureSemantics;
        public string setupRationale;
        public string expectedProgress;
        public string[] expectedEvidence;
        public string benignShutdownNoise;
        public string failureDisposition;
        public string[] excludedSystems;
        public RuntimeSmokeInvestigationClassification[] classifications;
        public RuntimeSmokeInvestigationAction[] actions;
    }

    [Serializable]
    public sealed class RuntimeSmokeInvestigationClassification
    {
        public string failureCode;
        public string likelyOwner;
        public string rationale;
    }

    [Serializable]
    public sealed class RuntimeSmokeInvestigationAction
    {
        public string id;
        public string kind;
        public string capability;
        public string predicate;
        public string captureLabel;
        public string workflowId;
    }

    public sealed class RuntimeSmokeCompileOptions
    {
        public string[] AvailableCapabilities { get; set; } = Array.Empty<string>();
        public bool BuildSuiteEntry { get; set; }
        public bool RequireInvestigationContract { get; set; }
        public int MaximumDeadlineSeconds { get; set; } = 3600;
    }

    public sealed class RuntimeSmokeCompileIssue
    {
        public string Code { get; internal set; }
        public string ScriptId { get; internal set; }
        public string StepId { get; internal set; }
        public string Message { get; internal set; }
    }

    public sealed class RuntimeSmokeCompiledStep
    {
        public string Id { get; internal set; }
        public string Type { get; internal set; }
        public string Command { get; internal set; }
        public string Predicate { get; internal set; }
        public string Value { get; internal set; }
        public string Reason { get; internal set; }
        public int Seconds { get; internal set; }
        public string SourceScript { get; internal set; }
        public RuntimeSmokeAssertionDefinition Assertion { get; internal set; }
    }

    public sealed class RuntimeSmokeCompiledWorkflow
    {
        public string Id { get; internal set; }
        public string Proposition { get; internal set; }
        public string ExcludedBehavior { get; internal set; }
        public string Scenario { get; internal set; }
        public string Seed { get; internal set; }
        public string ParityContract { get; internal set; }
        public RuntimeSmokeInvestigationDefinition Investigation { get; internal set; }
        public string InvestigationHash { get; internal set; }
        public string SourceScriptPath { get; internal set; }
        public RuntimeSmokeTarget Target { get; internal set; }
        public RuntimeSmokeMode Mode { get; internal set; }
        public int TimeoutSeconds { get; internal set; }
        public string CanonicalHash { get; internal set; }
        public IReadOnlyList<string> RequiredCapabilities { get; internal set; }
        public IReadOnlyList<RuntimeSmokeCompiledStep> Steps { get; internal set; }
    }

    public sealed class RuntimeSmokeCompileResult
    {
        public RuntimeSmokeCompiledWorkflow Workflow { get; internal set; }
        public IReadOnlyList<RuntimeSmokeCompileIssue> Issues { get; internal set; }
        public bool Success => Workflow != null && Issues.Count == 0;
    }

    public static class RuntimeSmokeDocumentCompiler
    {
        private const int MaximumSleepSeconds = 300;
        private const int MaximumIncludeDepth = 32;
        private static readonly string[] AllowedStepTypes = { "start", "command", "assert", "wait", "sleep", "checkpoint", "terminal" };

        public static RuntimeSmokeCompileResult Compile(
            string workflowJson,
            IRuntimeSmokeDocumentSource source,
            RuntimeSmokeTarget target,
            RuntimeSmokeMode mode,
            RuntimeSmokeCompileOptions options = null)
        {
            options = options ?? new RuntimeSmokeCompileOptions();
            var issues = new List<RuntimeSmokeCompileIssue>();
            var steps = new List<RuntimeSmokeCompiledStep>();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            var requiredCapabilities = new HashSet<string>(StringComparer.Ordinal);
            var recursion = new HashSet<string>(StringComparer.Ordinal);
            var hashParts = new List<string>();
            RuntimeSmokeDocumentDefinition root;
            if (!TryParse(workflowJson, out root))
                return Failure("document_invalid", "Workflow JSON is invalid.");
            if (!Enum.IsDefined(typeof(RuntimeSmokeTarget), target) || !Enum.IsDefined(typeof(RuntimeSmokeMode), mode))
                return Failure("target_or_mode_invalid", "Target and execution mode must be declared values.");
            if (!ValidateHeader(root, "workflow", target, mode, options, issues))
                return new RuntimeSmokeCompileResult { Issues = issues };
            ValidateInvestigation(root, options, issues);
            if (issues.Count != 0) return new RuntimeSmokeCompileResult { Issues = issues };
            if (source == null)
                return Failure("source_missing", "A document source is required to expand includes.");

            recursion.Add(root.id);
            hashParts.Add(root.id + "\n" + CanonicalizeJson(workflowJson));
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var capability in root.requires ?? Array.Empty<string>()) requiredCapabilities.Add(capability);
            ExpandDocument(root, source, variables, "", 0, recursion, usedIds, steps, hashParts, requiredCapabilities, issues, true, options);
            recursion.Remove(root.id);
            if (issues.Count != 0) return new RuntimeSmokeCompileResult { Issues = issues };

            var starts = steps.Count(step => step.Type == "start");
            var terminals = steps.Count(step => step.Type == "terminal");
            if (starts != 1 || steps.Count == 0 || steps[0].Type != "start")
                AddIssue(issues, "workflow_start_invalid", root.id, null, "A workflow must begin with exactly one start step.");
            if (terminals != 1 || steps.Count == 0 || steps[steps.Count - 1].Type != "terminal")
                AddIssue(issues, "workflow_terminal_invalid", root.id, null, "A workflow must end with exactly one terminal step.");
            if (issues.Count != 0) return new RuntimeSmokeCompileResult { Issues = issues };

            var hashInput = string.Join("\n--include--\n", hashParts);
            var workflow = new RuntimeSmokeCompiledWorkflow
            {
                Id = root.id,
                Proposition = root.proposition,
                ExcludedBehavior = root.excludedBehavior,
                Scenario = EmptyToNull(root.scenario),
                Seed = EmptyToNull(root.seed),
                ParityContract = root.parityContract,
                Investigation = root.investigation,
                InvestigationHash = root.investigationValue == null ? null : root.investigationValue.Digest(),
                SourceScriptPath = root.id + ".json",
                Target = target,
                Mode = mode,
                TimeoutSeconds = root.timeoutSeconds,
                CanonicalHash = Sha256(hashInput),
                RequiredCapabilities = Array.AsReadOnly(requiredCapabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray()),
                Steps = steps.AsReadOnly()
            };
            return new RuntimeSmokeCompileResult { Workflow = workflow, Issues = Array.Empty<RuntimeSmokeCompileIssue>() };

            RuntimeSmokeCompileResult Failure(string code, string message)
            {
                return new RuntimeSmokeCompileResult
                {
                    Issues = new[] { new RuntimeSmokeCompileIssue { Code = code, Message = message } }
                };
            }
        }

        private static string EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static bool IsOptionalRunToken(string value) => string.IsNullOrWhiteSpace(value) ||
            value.Length <= 128 && value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static void ExpandDocument(
            RuntimeSmokeDocumentDefinition document,
            IRuntimeSmokeDocumentSource source,
            Dictionary<string, string> inheritedVariables,
            string prefix,
            int depth,
            HashSet<string> recursion,
            HashSet<string> usedIds,
            List<RuntimeSmokeCompiledStep> output,
            List<string> hashParts,
            HashSet<string> requiredCapabilities,
            List<RuntimeSmokeCompileIssue> issues,
            bool isRoot,
            RuntimeSmokeCompileOptions options)
        {
            if (depth > MaximumIncludeDepth)
            {
                AddIssue(issues, "include_depth_exceeded", document.id, null, "Include expansion exceeded the maximum depth.");
                return;
            }

            // A workflow's start boundary must remain the first compiled operation, even
            // when its reusable fragments are declared before its authored steps.
            if (isRoot && document.steps != null && document.steps.Length > 0 && document.steps[0] != null && document.steps[0].type == "start")
                AppendStep(document, document.steps[0], inheritedVariables, prefix, usedIds, output, issues, isRoot);

            foreach (var include in document.include ?? Array.Empty<RuntimeSmokeScriptInclude>())
            {
                if (include == null || !IsStableId(include.script))
                {
                    AddIssue(issues, "include_id_invalid", document.id, null, "An include must name a valid script id.");
                    continue;
                }
                if (recursion.Contains(include.script))
                {
                    AddIssue(issues, "include_cycle", document.id, null, "Include cycle detected at '" + include.script + "'.");
                    continue;
                }
                if (!source.TryLoad(include.script, out var childJson) || string.IsNullOrWhiteSpace(childJson))
                {
                    AddIssue(issues, "include_missing", document.id, null, "Included script is missing: " + include.script);
                    continue;
                }
                if (!TryParse(childJson, out var child))
                {
                    AddIssue(issues, "include_invalid", include.script, null, "Included script JSON is invalid.");
                    continue;
                }
                if (child.kind != "fragment")
                {
                    AddIssue(issues, "include_not_fragment", child.id, null, "Includes must reference fragments, not workflows or suites.");
                    continue;
                }
                if (child.schemaVersion != 1 || !IsStableId(child.id) || !string.Equals(child.id, include.script, StringComparison.Ordinal))
                {
                    AddIssue(issues, "fragment_header_invalid", child.id, null, "Fragment id and schema version must be valid and match the include.");
                    continue;
                }
                ValidateFragmentCapabilities(child, options, issues);
                foreach (var capability in child.requires ?? Array.Empty<string>()) requiredCapabilities.Add(capability);
                if (!string.IsNullOrEmpty(include.stepPrefix) && !IsStableId(include.stepPrefix))
                {
                    AddIssue(issues, "step_prefix_invalid", child.id, null, "Include step prefix is invalid.");
                    continue;
                }

                var childVariables = new Dictionary<string, string>(inheritedVariables, StringComparer.Ordinal);
                var parameterNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var variable in include.with ?? Array.Empty<RuntimeSmokeVariable>())
                {
                    if (variable == null || !IsVariableName(variable.name) || variable.value == null || !parameterNames.Add(variable.name))
                    {
                        AddIssue(issues, "parameter_invalid", child.id, null, "Include parameters need unique valid names and string values.");
                        continue;
                    }
                    if (variable.value.Contains("${"))
                    {
                        AddIssue(issues, "parameter_invalid", child.id, null, "Parameter values cannot contain unresolved variable references.");
                        continue;
                    }
                    childVariables[variable.name] = variable.value;
                }
                recursion.Add(child.id);
                hashParts.Add(child.id + "\n" + CanonicalizeJson(childJson) + "\n" + CanonicalVariables(childVariables));
                ExpandDocument(child, source, childVariables, prefix + (include.stepPrefix ?? ""), depth + 1, recursion, usedIds, output, hashParts, requiredCapabilities, issues, false, options);
                recursion.Remove(child.id);
            }

            var firstStepToAppend = isRoot && document.steps != null && document.steps.Length > 0 && document.steps[0] != null && document.steps[0].type == "start" ? 1 : 0;
            for (var stepIndex = firstStepToAppend; stepIndex < (document.steps ?? Array.Empty<RuntimeSmokeStepDefinition>()).Length; stepIndex++)
            {
                AppendStep(document, document.steps[stepIndex], inheritedVariables, prefix, usedIds, output, issues, isRoot);
            }
        }

        private static void AppendStep(RuntimeSmokeDocumentDefinition document, RuntimeSmokeStepDefinition step,
            Dictionary<string, string> variables, string prefix, HashSet<string> usedIds,
            List<RuntimeSmokeCompiledStep> output, List<RuntimeSmokeCompileIssue> issues, bool isRoot)
        {
            if (step == null || !AllowedStepTypes.Contains(step.type, StringComparer.Ordinal))
            {
                AddIssue(issues, "step_type_invalid", document.id, step == null ? null : step.id, "Step type is missing or unsupported.");
                return;
            }
            if (!IsStableId(step.id)) { AddIssue(issues, "step_id_invalid", document.id, step.id, "Step id is invalid."); return; }
            if (!isRoot && (step.type == "start" || step.type == "terminal"))
            { AddIssue(issues, "fragment_runtime_boundary", document.id, step.id, "Fragments cannot start a runtime or publish terminal state."); return; }
            var expandedId = prefix + step.id;
            if (!usedIds.Add(expandedId)) { AddIssue(issues, "step_id_duplicate", document.id, expandedId, "Expanded step ids must be unique."); return; }
            if (step.type == "sleep" && (step.seconds <= 0 || step.seconds > MaximumSleepSeconds || string.IsNullOrWhiteSpace(step.reason) || !string.IsNullOrEmpty(step.predicate)))
            { AddIssue(issues, "sleep_invalid", document.id, expandedId, "Sleep needs a reason, a 1 to 300 second bound, and cannot declare a predicate."); return; }
            if (step.type == "command" && string.IsNullOrWhiteSpace(step.command))
            { AddIssue(issues, "command_invalid", document.id, expandedId, "Command steps require a declared command."); return; }
            if (step.type == "assert" && (step.assertion == null || !RuntimeSmokeAssertionEvaluator.Validate(step.assertion, out _)))
            { AddIssue(issues, "assertion_schema_invalid", document.id, expandedId, "Assert steps require a valid versioned typed assertion."); return; }
            if (step.type == "wait" && string.IsNullOrWhiteSpace(step.predicate))
            { AddIssue(issues, "predicate_invalid", document.id, expandedId, "Wait steps require a typed predicate capability."); return; }
            if (!TrySubstitute(step.command, variables, out var command) || !TrySubstitute(step.predicate, variables, out var predicate) ||
                !TrySubstitute(step.value, variables, out var value) || !TrySubstitute(step.reason, variables, out var reason))
            { AddIssue(issues, "variable_unknown", document.id, expandedId, "Step refers to an undeclared variable."); return; }
            output.Add(new RuntimeSmokeCompiledStep { Id = expandedId, Type = step.type, Command = command, Predicate = step.type == "assert" ? step.assertion.Predicate : predicate, Value = value, Reason = reason, Seconds = step.seconds, SourceScript = document.id, Assertion = step.assertion });
        }

        private static bool ValidateHeader(RuntimeSmokeDocumentDefinition document, string expectedKind, RuntimeSmokeTarget target, RuntimeSmokeMode mode, RuntimeSmokeCompileOptions options, List<RuntimeSmokeCompileIssue> issues)
        {
            if (document.schemaVersion != 1 || document.kind != expectedKind || !IsStableId(document.id))
                AddIssue(issues, "document_header_invalid", document.id, null, "Document schema, kind, or id is invalid.");
            if (expectedKind == "workflow")
            {
                if (document.testClass != "RuntimeSmoke" || string.IsNullOrWhiteSpace(document.proposition) || string.IsNullOrWhiteSpace(document.excludedBehavior))
                    AddIssue(issues, "workflow_contract_invalid", document.id, null, "A workflow must declare RuntimeSmoke class, proposition, and excluded behavior.");
                if (!IsOptionalRunToken(document.scenario) || !IsOptionalRunToken(document.seed))
                    AddIssue(issues, "scenario_or_seed_invalid", document.id, null, "Authored scenario and seed must be safe, bounded identifiers without path separators.");
                var validMode = TryMode(document.mode, out var declaredMode);
                if (!validMode || declaredMode != mode)
                    AddIssue(issues, "mode_invalid", document.id, null, "Workflow mode is invalid or does not match the selected execution mode.");
                if (options.BuildSuiteEntry && (!validMode || declaredMode != RuntimeSmokeMode.Fsd))
                    AddIssue(issues, "build_suite_requires_fsd", document.id, null, "Build suites can contain FSD workflows only.");
                if (document.timeoutSeconds <= 0 || document.timeoutSeconds > Math.Min(options.MaximumDeadlineSeconds, 86400))
                    AddIssue(issues, "deadline_invalid", document.id, null, "Workflow deadline is outside the allowed bound.");
                var targetName = TargetName(target);
                if (!(document.targets ?? Array.Empty<string>()).Contains(targetName, StringComparer.Ordinal))
                    AddIssue(issues, "target_unsupported", document.id, null, "Workflow does not allow target '" + targetName + "'.");
                if (target == RuntimeSmokeTarget.SmokeHarness && mode != RuntimeSmokeMode.Fsd)
                    AddIssue(issues, "mode_target_invalid", document.id, null, "Smoke harness target supports FSD only.");
                if ((mode == RuntimeSmokeMode.Hitl || mode == RuntimeSmokeMode.AiObserved) && target == RuntimeSmokeTarget.SmokeHarness)
                    AddIssue(issues, "mode_target_invalid", document.id, null, "AI-observed and HITL modes require Editor or development target.");
                if (!Enum.IsDefined(typeof(RuntimeSmokeTarget), target))
                    AddIssue(issues, "target_invalid", document.id, null, "Target is not supported.");
                var available = new HashSet<string>(options.AvailableCapabilities ?? Array.Empty<string>(), StringComparer.Ordinal);
                foreach (var capability in document.requires ?? Array.Empty<string>())
                {
                    if (!IsStableId(capability) || !available.Contains(capability))
                        AddIssue(issues, "capability_unavailable", document.id, null, "Required capability is invalid or unavailable: " + capability);
                }
            }
            return issues.Count == 0;
        }

        private static void ValidateInvestigation(RuntimeSmokeDocumentDefinition document, RuntimeSmokeCompileOptions options, List<RuntimeSmokeCompileIssue> issues)
        {
            if (document.investigation == null || document.investigationValue == null || document.investigationValue.Kind == RuntimeSmokeValueKind.Null)
            {
                if (options.RequireInvestigationContract)
                    AddIssue(issues, "investigation_missing", document.id, null, "This host requires a checked-in investigation contract for every workflow.");
                return;
            }
            var contract = document.investigation;
            var raw = document.investigationValue;
            var valid = raw != null && raw.Kind == RuntimeSmokeValueKind.Map &&
                HasOnlyKeys(raw.Map, "summary", "invariant", "fixtureSemantics", "setupRationale", "expectedProgress", "expectedEvidence", "benignShutdownNoise", "failureDisposition", "excludedSystems", "classifications", "actions") &&
                IsBoundedText(contract.summary, 240) && IsBoundedText(contract.invariant, 512) &&
                IsBoundedText(contract.fixtureSemantics, 512) && IsBoundedText(contract.setupRationale, 512) &&
                IsBoundedText(contract.expectedProgress, 512) && IsBoundedText(contract.benignShutdownNoise, 512) &&
                !string.IsNullOrWhiteSpace(document.scenario) && IsOptionalRunToken(document.scenario) &&
                !string.IsNullOrWhiteSpace(document.seed) && IsOptionalRunToken(document.seed) &&
                (contract.expectedEvidence ?? Array.Empty<string>()).Length > 0 &&
                (contract.expectedEvidence ?? Array.Empty<string>()).All(IsSafeEvidenceName) &&
                (contract.excludedSystems ?? Array.Empty<string>()).Length > 0 && (contract.excludedSystems ?? Array.Empty<string>()).All(IsStableId) &&
                (contract.classifications ?? Array.Empty<RuntimeSmokeInvestigationClassification>()).Length > 0 &&
                (contract.failureDisposition == "diagnosis_only" || contract.failureDisposition == "auto_retry_forbidden" || contract.failureDisposition == "repair_candidate");
            if (!valid)
                AddIssue(issues, "investigation_invalid", document.id, null, "Investigation context is incomplete, unbounded, or contains unsupported fields.");

            if (contract.classifications != null && raw != null && raw.Map.TryGetValue("classifications", out var classifications) && classifications.Kind == RuntimeSmokeValueKind.Array)
            {
                for (var index = 0; index < contract.classifications.Length && index < classifications.Array.Count; index++)
                {
                    var item = contract.classifications[index];
                    var rawItem = classifications.Array[index];
                    if (item == null || rawItem.Kind != RuntimeSmokeValueKind.Map || !HasOnlyKeys(rawItem.Map, "failureCode", "likelyOwner", "rationale") ||
                        !IsInvestigationFailureCode(item.failureCode) || !IsStableId(item.likelyOwner) || !IsBoundedText(item.rationale, 512))
                        AddIssue(issues, "investigation_classification_invalid", document.id, item == null ? null : item.failureCode, "Investigation failure classifications must use known codes and bounded owner rationale.");
                }
            }

            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            var declared = new HashSet<string>(document.requires ?? Array.Empty<string>(), StringComparer.Ordinal);
            var available = new HashSet<string>(options.AvailableCapabilities ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (contract.actions == null || contract.actions.Length == 0)
                AddIssue(issues, "investigation_actions_missing", document.id, null, "Investigation contracts must declare at least one bounded diagnostic action.");
            if (contract.actions == null) return;
            RuntimeSmokeValue rawActions = null;
            var rawActionsValid = raw != null && raw.Map.TryGetValue("actions", out rawActions) && rawActions.Kind == RuntimeSmokeValueKind.Array;
            for (var index = 0; index < contract.actions.Length; index++)
            {
                var action = contract.actions[index];
                var rawAction = rawActionsValid && index < rawActions.Array.Count ? rawActions.Array[index] : null;
                var allowedKeys = new[] { "id", "kind", "capability", "predicate", "captureLabel", "workflowId" };
                var actionValid = action != null && rawAction != null && rawAction.Kind == RuntimeSmokeValueKind.Map && HasOnlyKeys(rawAction.Map, allowedKeys) &&
                    IsStableId(action.id) && actionIds.Add(action.id) && IsStableId(action.capability) &&
                    declared.Contains(action.capability) && available.Contains(action.capability);
                if (actionValid)
                {
                    switch (action.kind)
                    {
                        case "status_query":
                        case "fresh_log_inspection":
                            actionValid = string.IsNullOrEmpty(action.predicate) && string.IsNullOrEmpty(action.captureLabel) && string.IsNullOrEmpty(action.workflowId);
                            break;
                        case "predicate_query":
                            actionValid = IsStableId(action.predicate) && declared.Contains(action.predicate) && available.Contains(action.predicate) &&
                                string.IsNullOrEmpty(action.captureLabel) && string.IsNullOrEmpty(action.workflowId);
                            break;
                        case "named_capture":
                            actionValid = IsOptionalRunToken(action.captureLabel) && !string.IsNullOrWhiteSpace(action.captureLabel) &&
                                string.IsNullOrEmpty(action.predicate) && string.IsNullOrEmpty(action.workflowId);
                            break;
                        case "scripted_reproduction":
                            actionValid = IsStableId(action.workflowId) && string.IsNullOrEmpty(action.predicate) && string.IsNullOrEmpty(action.captureLabel);
                            break;
                        default:
                            actionValid = false;
                            break;
                    }
                }
                if (!actionValid)
                    AddIssue(issues, "investigation_action_invalid", document.id, action == null ? null : action.id,
                        "Investigation actions must use an allow-listed operation, declared capability, unique id, and safe typed arguments.");
            }
        }

        private static bool HasOnlyKeys(IReadOnlyDictionary<string, RuntimeSmokeValue> map, params string[] allowed)
        {
            var set = new HashSet<string>(allowed ?? Array.Empty<string>(), StringComparer.Ordinal);
            return map.Keys.All(set.Contains);
        }

        private static bool IsBoundedText(string value, int maximum) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl) &&
            value.IndexOf(':') < 0 && value.IndexOf('\\') < 0 && !value.StartsWith("/", StringComparison.Ordinal) && !value.Contains("..");

        private static bool IsSafeEvidenceName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.IndexOf('\\') >= 0 || value[0] == '/')
                return false;

            var segments = value.Split('/');
            return segments.Length <= 4 && segments.All(IsSafeEvidenceSegment);
        }

        private static bool IsSafeEvidenceSegment(string segment) =>
            !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != ".." &&
            segment.Length <= 96 && !segment.Contains("..") &&
            segment.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static bool IsInvestigationFailureCode(string value) =>
            new[] { "assertion_mismatch", "predicate_missing", "predicate_error", "predicate_timeout", "assertion_schema_invalid", "assertion_adapter_protocol_invalid", "run_cancelled", "run_timeout", "protocol_invalid", "player_crashed", "watchdog_timeout" }.Contains(value, StringComparer.Ordinal);

        private static bool TryParse(string json, out RuntimeSmokeDocumentDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var parsed = RuntimeSmokeValueJsonParser.Parse(json);
                if (parsed.Kind != RuntimeSmokeValueKind.Map) return false;
                definition = JsonUtility.FromJson<RuntimeSmokeDocumentDefinition>(json);
                if (definition != null)
                {
                    if (parsed.Map.TryGetValue("investigation", out var investigation)) definition.investigationValue = investigation;
                    BindAssertions(parsed, definition);
                }
            }
            catch { return false; }
            return definition != null;
        }

        private static void BindAssertions(RuntimeSmokeValue root, RuntimeSmokeDocumentDefinition definition)
        {
            if (!root.Map.TryGetValue("steps", out var rawSteps) || rawSteps.Kind != RuntimeSmokeValueKind.Array || definition.steps == null) return;
            var count = Math.Min(rawSteps.Array.Count, definition.steps.Length);
            for (var index = 0; index < count; index++)
            {
                var step = definition.steps[index];
                var rawStep = rawSteps.Array[index];
                if (step == null || rawStep.Kind != RuntimeSmokeValueKind.Map || !rawStep.Map.TryGetValue("assertion", out var rawAssertion)) continue;
                if (rawAssertion.Kind != RuntimeSmokeValueKind.Map) continue;
                var fields = rawAssertion.Map;
                if (!TryString(fields, "id", out var id) || !TryString(fields, "name", out var name) ||
                    !TryString(fields, "predicate", out var predicate) || !TryString(fields, "comparison", out var comparisonName) ||
                    !fields.TryGetValue("expected", out var expected) || !TryComparison(comparisonName, out var comparison)) continue;
                decimal? tolerance = null;
                if (fields.TryGetValue("tolerance", out var rawTolerance))
                {
                    if (rawTolerance.Kind == RuntimeSmokeValueKind.Decimal) tolerance = rawTolerance.Decimal;
                    else continue;
                }
                var schemaVersion = 0;
                if (fields.TryGetValue("assertionSchemaVersion", out var rawVersion) && rawVersion.Kind == RuntimeSmokeValueKind.Integer && rawVersion.Integer >= int.MinValue && rawVersion.Integer <= int.MaxValue)
                    schemaVersion = (int)rawVersion.Integer;
                TryString(fields, "classification", out var classification);
                step.assertion = new RuntimeSmokeAssertionDefinition
                {
                    AssertionSchemaVersion = schemaVersion,
                    Id = id,
                    Name = name,
                    Predicate = predicate,
                    Comparison = comparison,
                    Expected = expected,
                    Tolerance = tolerance,
                    Classification = classification
                };
            }
        }

        private static bool TryString(IReadOnlyDictionary<string, RuntimeSmokeValue> fields, string name, out string value)
        {
            value = null;
            if (!fields.TryGetValue(name, out var found) || found.Kind != RuntimeSmokeValueKind.String) return false;
            value = found.String;
            return true;
        }

        private static bool TryComparison(string value, out RuntimeSmokeComparison comparison)
        {
            switch (value)
            {
                case "equals": comparison = RuntimeSmokeComparison.Equals; return true;
                case "not_equals": comparison = RuntimeSmokeComparison.NotEquals; return true;
                case "contains": comparison = RuntimeSmokeComparison.Contains; return true;
                case "not_contains": comparison = RuntimeSmokeComparison.NotContains; return true;
                case "greater_than": comparison = RuntimeSmokeComparison.GreaterThan; return true;
                case "greater_or_equal": comparison = RuntimeSmokeComparison.GreaterOrEqual; return true;
                case "less_than": comparison = RuntimeSmokeComparison.LessThan; return true;
                case "less_or_equal": comparison = RuntimeSmokeComparison.LessOrEqual; return true;
                case "within_absolute": comparison = RuntimeSmokeComparison.WithinAbsolute; return true;
                case "within_relative": comparison = RuntimeSmokeComparison.WithinRelative; return true;
                default: comparison = RuntimeSmokeComparison.Equals; return false;
            }
        }

        private static void ValidateFragmentCapabilities(RuntimeSmokeDocumentDefinition fragment, RuntimeSmokeCompileOptions options, List<RuntimeSmokeCompileIssue> issues)
        {
            if (fragment.schemaVersion != 1 || fragment.kind != "fragment" || !IsStableId(fragment.id))
                AddIssue(issues, "fragment_header_invalid", fragment.id, null, "Fragment schema, kind, or id is invalid.");
            var available = new HashSet<string>(options.AvailableCapabilities ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var capability in fragment.requires ?? Array.Empty<string>())
            {
                if (!IsStableId(capability) || !available.Contains(capability))
                    AddIssue(issues, "capability_unavailable", fragment.id, null, "Required capability is invalid or unavailable: " + capability);
            }
        }

        private static bool TrySubstitute(string value, Dictionary<string, string> variables, out string result)
        {
            result = value;
            if (string.IsNullOrEmpty(value)) return true;
            var start = value.IndexOf("${", StringComparison.Ordinal);
            while (start >= 0)
            {
                var end = value.IndexOf('}', start + 2);
                if (end < 0) return false;
                var name = value.Substring(start + 2, end - start - 2);
                if (!variables.TryGetValue(name, out var replacement)) return false;
                result = result.Substring(0, start) + replacement + result.Substring(end + 1);
                start = result.IndexOf("${", start + replacement.Length, StringComparison.Ordinal);
            }
            return true;
        }

        private static bool TryMode(string value, out RuntimeSmokeMode mode)
        {
            switch (value)
            {
                case "fsd": mode = RuntimeSmokeMode.Fsd; return true;
                case "ai_observed": mode = RuntimeSmokeMode.AiObserved; return true;
                case "hitl": mode = RuntimeSmokeMode.Hitl; return true;
                default: mode = RuntimeSmokeMode.Fsd; return false;
            }
        }

        private static string TargetName(RuntimeSmokeTarget target)
        {
            switch (target)
            {
                case RuntimeSmokeTarget.Editor: return "editor";
                case RuntimeSmokeTarget.Development: return "development";
                case RuntimeSmokeTarget.SmokeHarness: return "smoke_harness";
                default: return "invalid";
            }
        }

        private static bool IsVariableName(string value)
        {
            if (string.IsNullOrEmpty(value) || !char.IsLetter(value[0]) && value[0] != '_') return false;
            return value.All(character => char.IsLetterOrDigit(character) || character == '_');
        }

        private static bool IsStableId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 160 || value.StartsWith("/", StringComparison.Ordinal) || value.Contains("..")) return false;
            return value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == '/');
        }

        private static string CanonicalVariables(Dictionary<string, string> variables)
        {
            var builder = new StringBuilder();
            foreach (var pair in variables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append(pair.Key.Length).Append(':').Append(pair.Key).Append(pair.Value.Length).Append(':').Append(pair.Value).Append(';');
            return builder.ToString();
        }

        private static string CanonicalizeJson(string json)
        {
            // Parse and re-emit through the closed value algebra so map key order,
            // whitespace, and equivalent numeric spellings cannot perturb hashes.
            return RuntimeSmokeValueJsonParser.Parse(json).ToCanonicalJson();
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(byteValue => byteValue.ToString("x2")));
        }

        private static void AddIssue(List<RuntimeSmokeCompileIssue> issues, string code, string scriptId, string stepId, string message)
        {
            issues.Add(new RuntimeSmokeCompileIssue { Code = code, ScriptId = scriptId, StepId = stepId, Message = message });
        }
    }

    public sealed class RuntimeSmokeSuiteValidationResult
    {
        public bool Success => Issues.Count == 0;
        public IReadOnlyList<RuntimeSmokeCompileIssue> Issues { get; internal set; }
        public IReadOnlyList<RuntimeSmokeCompiledWorkflow> Workflows { get; internal set; }
        public IReadOnlyList<RuntimeSmokeCompiledSuiteEntry> Entries { get; internal set; }
    }

    public sealed class RuntimeSmokeCompiledSuiteEntry
    {
        public string Id { get; internal set; }
        public bool Required { get; internal set; }
        public int TimeoutSeconds { get; internal set; }
        public RuntimeSmokeTarget Target { get; internal set; }
        public RuntimeSmokeCompiledWorkflow Workflow { get; internal set; }
    }

    public static class RuntimeSmokeSuiteValidator
    {
        public static RuntimeSmokeSuiteValidationResult Validate(
            string suiteJson,
            IRuntimeSmokeDocumentSource source,
            RuntimeSmokeTarget target,
            RuntimeSmokeCompileOptions options = null)
        {
            options = options ?? new RuntimeSmokeCompileOptions();
            var issues = new List<RuntimeSmokeCompileIssue>();
            var workflows = new List<RuntimeSmokeCompiledWorkflow>();
            var entries = new List<RuntimeSmokeCompiledSuiteEntry>();
            RuntimeSmokeDocumentDefinition suite;
            try
            {
                var parsed = RuntimeSmokeValueJsonParser.Parse(suiteJson);
                suite = parsed.Kind == RuntimeSmokeValueKind.Map ? JsonUtility.FromJson<RuntimeSmokeDocumentDefinition>(suiteJson) : null;
            }
            catch { suite = null; }
            if (suite == null || suite.schemaVersion != 1 || suite.kind != "suite" || !IsStableId(suite.id))
                AddIssue(issues, "suite_header_invalid", suite == null ? null : suite.id, null, "Suite schema, kind, or id is invalid.");
            if (source == null) AddIssue(issues, "source_missing", suite == null ? null : suite.id, null, "A suite document source is required.");
            if (suite == null || source == null)
                return new RuntimeSmokeSuiteValidationResult { Issues = issues.AsReadOnly(), Workflows = workflows.AsReadOnly(), Entries = entries.AsReadOnly() };
            if ((suite.entries == null || suite.entries.Length == 0) && (suite.includeSuites == null || suite.includeSuites.Length == 0))
                AddIssue(issues, "suite_empty", suite.id, null, "A suite must include at least one workflow or nested suite.");

            var seenScripts = new HashSet<string>(StringComparer.Ordinal);
            var seenEntryIds = new HashSet<string>(StringComparer.Ordinal);
            var activeSuites = new HashSet<string>(StringComparer.Ordinal) { suite.id };
            ExpandSuite(suite, source, target, options, seenScripts, seenEntryIds, activeSuites, workflows, entries, issues);
            return new RuntimeSmokeSuiteValidationResult { Issues = issues.AsReadOnly(), Workflows = workflows.AsReadOnly(), Entries = entries.AsReadOnly() };
        }

        private static void ExpandSuite(RuntimeSmokeDocumentDefinition suite, IRuntimeSmokeDocumentSource source, RuntimeSmokeTarget target,
            RuntimeSmokeCompileOptions options, HashSet<string> seenScripts, HashSet<string> seenEntryIds, HashSet<string> activeSuites,
            List<RuntimeSmokeCompiledWorkflow> workflows, List<RuntimeSmokeCompiledSuiteEntry> entries, List<RuntimeSmokeCompileIssue> issues)
        {
            foreach (var nestedId in suite.includeSuites ?? Array.Empty<string>())
            {
                if (!IsStableId(nestedId))
                {
                    AddIssue(issues, "suite_id_invalid", suite.id, null, "Nested suite id is invalid.");
                    continue;
                }
                if (activeSuites.Contains(nestedId))
                {
                    AddIssue(issues, "suite_cycle", suite.id, null, "Nested suite cycle detected at '" + nestedId + "'.");
                    continue;
                }
                if (!source.TryLoad(nestedId, out var nestedJson))
                {
                    AddIssue(issues, "suite_missing", suite.id, null, "Nested suite is missing: " + nestedId);
                    continue;
                }
                RuntimeSmokeDocumentDefinition nested;
                try
                {
                    var parsed = RuntimeSmokeValueJsonParser.Parse(nestedJson);
                    nested = parsed.Kind == RuntimeSmokeValueKind.Map ? JsonUtility.FromJson<RuntimeSmokeDocumentDefinition>(nestedJson) : null;
                }
                catch { nested = null; }
                if (nested == null || nested.kind != "suite" || nested.id != nestedId)
                {
                    AddIssue(issues, "suite_invalid", nestedId, null, "Nested suite id or kind is invalid.");
                    continue;
                }
                activeSuites.Add(nestedId);
                if (nested.schemaVersion != 1 || !IsStableId(nested.id) || nested.timeoutSeconds < 0)
                {
                    AddIssue(issues, "suite_invalid", nestedId, null, "Nested suite schema or metadata is invalid.");
                    activeSuites.Remove(nestedId);
                    continue;
                }
                ExpandSuite(nested, source, target, options, seenScripts, seenEntryIds, activeSuites, workflows, entries, issues);
                activeSuites.Remove(nestedId);
            }

            foreach (var entry in suite.entries ?? Array.Empty<RuntimeSmokeSuiteEntry>())
            {
                if (entry == null || !IsStableId(entry.id) || !seenEntryIds.Add(entry.id) ||
                    !IsStableId(entry.script) || !seenScripts.Add(entry.script) || entry.timeoutSeconds <= 0)
                {
                    AddIssue(issues, "suite_entry_duplicate_or_invalid", suite.id, entry == null ? null : entry.id, "Suite entries need unique ids, workflow references, and positive deadlines.");
                    continue;
                }
                if (entry.target != TargetName(target))
                {
                    AddIssue(issues, "suite_target_mismatch", suite.id, entry.id, "Suite entry target does not match the selected lane.");
                    continue;
                }
                if (!source.TryLoad(entry.script, out var workflowJson))
                {
                    AddIssue(issues, "suite_workflow_missing", entry.script, entry.id, "Suite workflow is missing.");
                    continue;
                }
                var workflowOptions = new RuntimeSmokeCompileOptions
                {
                    AvailableCapabilities = options.AvailableCapabilities,
                    BuildSuiteEntry = true,
                    MaximumDeadlineSeconds = Math.Min(options.MaximumDeadlineSeconds, entry.timeoutSeconds > 0 ? entry.timeoutSeconds : options.MaximumDeadlineSeconds)
                };
                var result = RuntimeSmokeDocumentCompiler.Compile(workflowJson, source, target, RuntimeSmokeMode.Fsd, workflowOptions);
                if (!result.Success) issues.AddRange(result.Issues);
                else
                {
                    workflows.Add(result.Workflow);
                    entries.Add(new RuntimeSmokeCompiledSuiteEntry
                    {
                        Id = entry.id,
                        Required = entry.required,
                        TimeoutSeconds = Math.Min(entry.timeoutSeconds, result.Workflow.TimeoutSeconds),
                        Target = target,
                        Workflow = result.Workflow
                    });
                }
            }
        }

        private static bool IsStableId(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 160 && !value.Contains("..") &&
                   value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-' || character == '/');
        }

        private static string TargetName(RuntimeSmokeTarget target)
        {
            switch (target)
            {
                case RuntimeSmokeTarget.Editor: return "editor";
                case RuntimeSmokeTarget.Development: return "development";
                case RuntimeSmokeTarget.SmokeHarness: return "smoke_harness";
                default: return "invalid";
            }
        }

        private static void AddIssue(List<RuntimeSmokeCompileIssue> issues, string code, string scriptId, string stepId, string message)
        {
            issues.Add(new RuntimeSmokeCompileIssue { Code = code, ScriptId = scriptId, StepId = stepId, Message = message });
        }
    }
}
#endif
