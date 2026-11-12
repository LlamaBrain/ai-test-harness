#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Projects authoritative RuntimeSmoke results to the outer runner's JUnit format.</summary>
    public static class RuntimeSmokeJUnitProjection
    {
        public static string Serialize(IEnumerable<RuntimeSmokeExecutionResult> executions)
        {
            if (executions == null) throw new ArgumentNullException(nameof(executions));
            var results = executions.ToArray();
            if (results.Any(result => result == null)) throw new ArgumentException("Execution results cannot contain null entries.", nameof(executions));

            var builder = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true };
            using (var writer = XmlWriter.Create(builder, settings))
            {
                writer.WriteStartElement("testsuites");
                writer.WriteAttributeString("tests", results.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("failures", results.Count(IsAssertionFailure).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("errors", results.Count(result => !IsAssertionFailure(result) && result.TerminalState != RuntimeSmokeRunTerminalState.Passed).ToString(CultureInfo.InvariantCulture));
                foreach (var result in results) WriteTestCase(writer, result);
                writer.WriteEndElement();
            }
            return builder.ToString();
        }

        private static void WriteTestCase(XmlWriter writer, RuntimeSmokeExecutionResult result)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("classname", "RuntimeSmoke");
            writer.WriteAttributeString("name", result.WorkflowId ?? "unknown-workflow");
            writer.WriteAttributeString("time", Math.Max(0, (result.EndedUtc - result.StartedUtc).TotalSeconds).ToString("0.000", CultureInfo.InvariantCulture));
            writer.WriteStartElement("properties");
            Property(writer, "correlationId", result.CorrelationId);
            if (!string.IsNullOrEmpty(result.Scenario)) Property(writer, "scenario", result.Scenario);
            if (!string.IsNullOrEmpty(result.Seed)) Property(writer, "seed", result.Seed);
            if (result.AssertionResult != null && !result.AssertionResult.Passed)
            {
                var assertion = result.AssertionResult;
                Property(writer, "assertionSchemaVersion", assertion.AssertionSchemaVersion.ToString(CultureInfo.InvariantCulture));
                Property(writer, "assertionId", assertion.AssertionId);
                Property(writer, "assertionComparison", RuntimeSmokeAssertionSchema.ComparisonLabel(assertion.Comparison));
                Property(writer, "assertionPredicate", assertion.Predicate);
                Property(writer, "assertionClassification", assertion.Classification);
                Property(writer, "assertionStepId", assertion.StepId);
                Property(writer, "assertionTolerance", assertion.Tolerance.HasValue ? assertion.Tolerance.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
                Property(writer, "assertionElapsedMilliseconds", assertion.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
                Property(writer, "proposition", assertion.Proposition);
            }
            writer.WriteEndElement();
            if (result.AssertionResult != null && !result.AssertionResult.Passed)
            {
                var assertion = result.AssertionResult;
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("type", AssertionCode(assertion.FailureCode));
                writer.WriteAttributeString("message", assertion.Diagnostic ?? "RuntimeSmoke assertion failed.");
                writer.WriteString("assertionId=" + (assertion.AssertionId ?? "unknown") +
                    " expected=" + (assertion.ExpectedDisplay ?? "<unavailable>") +
                    " actual=" + (assertion.ActualDisplay ?? "<unavailable>") +
                    " classification=" + (assertion.Classification ?? AssertionCode(assertion.FailureCode)));
                writer.WriteEndElement();
            }
            else if (result.TerminalState != RuntimeSmokeRunTerminalState.Passed)
            {
                writer.WriteStartElement("error");
                writer.WriteAttributeString("type", RunCode(result.FailureCode));
                writer.WriteAttributeString("message", result.Diagnostic ?? "RuntimeSmoke did not pass.");
                writer.WriteString("RuntimeSmoke terminal state=" + result.TerminalState + " code=" + RunCode(result.FailureCode) +
                    " correlationId=" + (result.CorrelationId ?? "unknown") + " detail=" + (result.Diagnostic ?? "<none>"));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private static void Property(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement("property");
            writer.WriteAttributeString("name", name);
            writer.WriteAttributeString("value", value ?? string.Empty);
            writer.WriteEndElement();
        }

        private static bool IsAssertionFailure(RuntimeSmokeExecutionResult result) =>
            result.AssertionResult != null && !result.AssertionResult.Passed;

        private static string AssertionCode(RuntimeSmokeAssertionFailureCode code)
        {
            switch (code)
            {
                case RuntimeSmokeAssertionFailureCode.AssertionMismatch: return "assertion_mismatch";
                case RuntimeSmokeAssertionFailureCode.PredicateMissing: return "predicate_missing";
                case RuntimeSmokeAssertionFailureCode.PredicateError: return "predicate_error";
                case RuntimeSmokeAssertionFailureCode.PredicateTimeout: return "predicate_timeout";
                case RuntimeSmokeAssertionFailureCode.AssertionSchemaInvalid: return "assertion_schema_invalid";
                default: return "assertion_adapter_protocol_invalid";
            }
        }

        private static string RunCode(RuntimeSmokeRunFailureCode code)
        {
            switch (code)
            {
                case RuntimeSmokeRunFailureCode.RunCancelled: return "run_cancelled";
                case RuntimeSmokeRunFailureCode.RunTimeout: return "run_timeout";
                case RuntimeSmokeRunFailureCode.PlayerCrashed: return "player_crashed";
                case RuntimeSmokeRunFailureCode.WatchdogTimeout: return "watchdog_timeout";
                default: return "protocol_invalid";
            }
        }
    }
}
#endif
