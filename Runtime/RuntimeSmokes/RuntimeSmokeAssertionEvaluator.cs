#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>A closed, immutable value algebra used at the host adapter boundary.</summary>
    public sealed class RuntimeSmokeValue
    {
        private readonly object _value;

        private RuntimeSmokeValue(RuntimeSmokeValueKind kind, object value)
        {
            Kind = kind;
            _value = value;
        }

        public RuntimeSmokeValueKind Kind { get; }
        public bool Boolean => Kind == RuntimeSmokeValueKind.Boolean ? (bool)_value : throw WrongKind();
        public string String => Kind == RuntimeSmokeValueKind.String ? (string)_value : throw WrongKind();
        public long Integer => Kind == RuntimeSmokeValueKind.Integer ? (long)_value : throw WrongKind();
        public decimal Decimal => Kind == RuntimeSmokeValueKind.Decimal ? (decimal)_value : throw WrongKind();
        public IReadOnlyList<RuntimeSmokeValue> Array => Kind == RuntimeSmokeValueKind.Array ? (IReadOnlyList<RuntimeSmokeValue>)_value : throw WrongKind();
        public IReadOnlyDictionary<string, RuntimeSmokeValue> Map => Kind == RuntimeSmokeValueKind.Map ? (IReadOnlyDictionary<string, RuntimeSmokeValue>)_value : throw WrongKind();

        public static RuntimeSmokeValue Missing() => new RuntimeSmokeValue(RuntimeSmokeValueKind.Missing, null);
        public static RuntimeSmokeValue Null() => new RuntimeSmokeValue(RuntimeSmokeValueKind.Null, null);
        public static RuntimeSmokeValue From(bool value) => new RuntimeSmokeValue(RuntimeSmokeValueKind.Boolean, value);
        public static RuntimeSmokeValue From(string value)
        {
            if (value == null) return Null();
            ValidateUtf16(value, nameof(value));
            return new RuntimeSmokeValue(RuntimeSmokeValueKind.String, value);
        }
        public static RuntimeSmokeValue From(long value) => new RuntimeSmokeValue(RuntimeSmokeValueKind.Integer, value);
        public static RuntimeSmokeValue From(decimal value) => new RuntimeSmokeValue(RuntimeSmokeValueKind.Decimal, value);

        public static RuntimeSmokeValue FromArray(IEnumerable<RuntimeSmokeValue> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            return new RuntimeSmokeValue(RuntimeSmokeValueKind.Array, System.Array.AsReadOnly(values.Select(value => value ?? Null()).ToArray()));
        }

        public static RuntimeSmokeValue FromMap(IEnumerable<KeyValuePair<string, RuntimeSmokeValue>> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var map = new Dictionary<string, RuntimeSmokeValue>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                if (pair.Key == null) throw new ArgumentException("Map keys cannot be null.", nameof(values));
                ValidateUtf16(pair.Key, nameof(values));
                if (map.ContainsKey(pair.Key))
                    throw new ArgumentException("Map keys must be unique using ordinal comparison.", nameof(values));
                map.Add(pair.Key, pair.Value ?? Null());
            }
            return new RuntimeSmokeValue(RuntimeSmokeValueKind.Map, new ReadOnlyDictionary<string, RuntimeSmokeValue>(map));
        }

        public string ToCanonicalJson()
        {
            var builder = new StringBuilder();
            AppendCanonicalJson(builder);
            return builder.ToString();
        }

        public string Digest()
        {
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(Kind + "\n" + ToCanonicalJson())));
        }

        internal bool IsNumeric => Kind == RuntimeSmokeValueKind.Integer || Kind == RuntimeSmokeValueKind.Decimal;

        internal decimal AsDecimal()
        {
            if (Kind == RuntimeSmokeValueKind.Integer) return (decimal)Integer;
            if (Kind == RuntimeSmokeValueKind.Decimal) return Decimal;
            throw WrongKind();
        }

        public bool DeepEquals(RuntimeSmokeValue other)
        {
            if (other == null || Kind != other.Kind) return false;
            switch (Kind)
            {
                case RuntimeSmokeValueKind.Missing:
                case RuntimeSmokeValueKind.Null:
                    return true;
                case RuntimeSmokeValueKind.Boolean:
                    return Boolean == other.Boolean;
                case RuntimeSmokeValueKind.String:
                    return string.Equals(String, other.String, StringComparison.Ordinal);
                case RuntimeSmokeValueKind.Integer:
                    return Integer == other.Integer;
                case RuntimeSmokeValueKind.Decimal:
                    return Decimal == other.Decimal;
                case RuntimeSmokeValueKind.Array:
                    return Array.Count == other.Array.Count && Array.Zip(other.Array, (left, right) => left.DeepEquals(right)).All(equal => equal);
                case RuntimeSmokeValueKind.Map:
                    return Map.Count == other.Map.Count && Map.All(pair => other.Map.TryGetValue(pair.Key, out var value) && pair.Value.DeepEquals(value));
                default:
                    return false;
            }
        }

        internal bool Contains(RuntimeSmokeValue expected)
        {
            if (expected == null) return false;
            switch (Kind)
            {
                case RuntimeSmokeValueKind.String:
                    return expected.Kind == RuntimeSmokeValueKind.String && String.IndexOf(expected.String, StringComparison.Ordinal) >= 0;
                case RuntimeSmokeValueKind.Array:
                    return Array.Any(item => item.DeepEquals(expected));
                case RuntimeSmokeValueKind.Map:
                    if (expected.Kind != RuntimeSmokeValueKind.Map) return false;
                    return expected.Map.All(pair => Map.TryGetValue(pair.Key, out var value) &&
                        (pair.Value.Kind == RuntimeSmokeValueKind.Map ? value.Contains(pair.Value) : value.DeepEquals(pair.Value)));
                default:
                    return false;
            }
        }

        private InvalidOperationException WrongKind() => new InvalidOperationException("Value kind is " + Kind + ".");

        private static void ValidateUtf16(string value, string parameterName)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsHighSurrogate(value[index]))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        throw new ArgumentException("Strings must contain valid Unicode scalar sequences.", parameterName);
                    index++;
                }
                else if (char.IsLowSurrogate(value[index]))
                    throw new ArgumentException("Strings must contain valid Unicode scalar sequences.", parameterName);
            }
        }

        private void AppendCanonicalJson(StringBuilder builder)
        {
            switch (Kind)
            {
                case RuntimeSmokeValueKind.Missing:
                    builder.Append("{\"$state\":\"missing\"}");
                    break;
                case RuntimeSmokeValueKind.Null:
                    builder.Append("null");
                    break;
                case RuntimeSmokeValueKind.Boolean:
                    builder.Append(Boolean ? "true" : "false");
                    break;
                case RuntimeSmokeValueKind.String:
                    AppendJsonString(builder, String);
                    break;
                case RuntimeSmokeValueKind.Integer:
                    builder.Append(Integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case RuntimeSmokeValueKind.Decimal:
                    builder.Append(Decimal.ToString("G29", CultureInfo.InvariantCulture));
                    break;
                case RuntimeSmokeValueKind.Array:
                    builder.Append('[');
                    for (var index = 0; index < Array.Count; index++)
                    {
                        if (index != 0) builder.Append(',');
                        Array[index].AppendCanonicalJson(builder);
                    }
                    builder.Append(']');
                    break;
                case RuntimeSmokeValueKind.Map:
                    builder.Append('{');
                    var first = true;
                    foreach (var pair in Map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        AppendJsonString(builder, pair.Key);
                        builder.Append(':');
                        pair.Value.AppendCanonicalJson(builder);
                    }
                    builder.Append('}');
                    break;
                default:
                    throw new InvalidOperationException("Unsupported RuntimeSmoke value kind.");
            }
        }

        internal static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20 || char.IsSurrogate(character))
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    public sealed class RuntimeSmokeAssertionDefinition
    {
        public int AssertionSchemaVersion { get; set; } = RuntimeSmokeAssertionSchema.Version;
        public string Id { get; set; }
        public string Name { get; set; }
        public string Predicate { get; set; }
        public RuntimeSmokeComparison Comparison { get; set; }
        public RuntimeSmokeValue Expected { get; set; }
        public decimal? Tolerance { get; set; }
        public string Classification { get; set; }
    }

    public sealed class RuntimeSmokeAssertionResult
    {
        public bool Passed { get; internal set; }
        public int AssertionSchemaVersion { get; internal set; } = RuntimeSmokeAssertionSchema.Version;
        public string AssertionId { get; internal set; }
        public string AssertionName { get; internal set; }
        public string CorrelationId { get; internal set; }
        public string WorkflowId { get; internal set; }
        public string Proposition { get; internal set; }
        public string StepId { get; internal set; }
        public string Predicate { get; internal set; }
        public string Classification { get; internal set; }
        public long ElapsedMilliseconds { get; internal set; }
        public RuntimeSmokeComparison Comparison { get; internal set; }
        public decimal? Tolerance { get; internal set; }
        public RuntimeSmokeAssertionFailureCode FailureCode { get; internal set; }
        public RuntimeSmokeValue ExpectedValue { get; internal set; }
        public RuntimeSmokeValue ActualValue { get; internal set; }
        public string ExpectedDigest { get; internal set; }
        public string ActualDigest { get; internal set; }
        public string ExpectedDisplay { get; internal set; }
        public string ActualDisplay { get; internal set; }
        public bool ExpectedRedacted { get; internal set; }
        public bool ActualRedacted { get; internal set; }
        public string Diagnostic { get; internal set; }
    }

    public static class RuntimeSmokeAssertionEvaluator
    {
        private const int DefaultDisplayLimit = 512;

        public static RuntimeSmokeAssertionResult Evaluate(RuntimeSmokeAssertionDefinition assertion, RuntimeSmokeValue actual, int displayLimit = DefaultDisplayLimit)
        {
            var result = NewResult(assertion, actual);
            if (!Validate(assertion, out var schemaError))
                return Complete(result, false, RuntimeSmokeAssertionFailureCode.AssertionSchemaInvalid, schemaError, displayLimit);
            if (actual == null)
                return Complete(result, false, RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid, "Adapter returned no typed value.", displayLimit);
            if (actual.Kind == RuntimeSmokeValueKind.Missing)
                return Complete(result, false, RuntimeSmokeAssertionFailureCode.PredicateMissing, "Predicate returned missing.", displayLimit);
            if (!IsCompatible(assertion, actual))
                return Complete(result, false, RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid,
                    "Adapter returned a value kind incompatible with the declared assertion.", displayLimit);

            try
            {
                var matched = Compare(assertion, actual);
                return Complete(result, matched, matched ? RuntimeSmokeAssertionFailureCode.None : RuntimeSmokeAssertionFailureCode.AssertionMismatch,
                    matched ? null : "Actual value did not satisfy the declared comparison.", displayLimit);
            }
            catch (Exception exception)
            {
                return Complete(result, false, RuntimeSmokeAssertionFailureCode.AssertionSchemaInvalid, exception.Message, displayLimit);
            }
        }

        public static RuntimeSmokeAssertionResult EvaluateError(RuntimeSmokeAssertionDefinition assertion, RuntimeSmokeAssertionFailureCode code, string diagnostic, int displayLimit = DefaultDisplayLimit)
        {
            if (code != RuntimeSmokeAssertionFailureCode.PredicateError && code != RuntimeSmokeAssertionFailureCode.PredicateTimeout && code != RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid)
                code = RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid;
            var result = NewResult(assertion, RuntimeSmokeValue.Missing());
            return Complete(result, false, code, diagnostic, displayLimit);
        }

        public static bool Validate(RuntimeSmokeAssertionDefinition assertion, out string error)
        {
            error = null;
            if (assertion == null) { error = "Assertion is required."; return false; }
            if (assertion.AssertionSchemaVersion != RuntimeSmokeAssertionSchema.Version) { error = "Unsupported assertion schema version."; return false; }
            if (string.IsNullOrWhiteSpace(assertion.Id) || string.IsNullOrWhiteSpace(assertion.Name) || string.IsNullOrWhiteSpace(assertion.Predicate))
            { error = "Assertion id, name, and predicate are required."; return false; }
            if (!string.IsNullOrWhiteSpace(assertion.Classification) &&
                (assertion.Classification.Length > 128 || assertion.Classification.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '_' && character != '-')))
            { error = "Assertion classification must be a bounded identifier."; return false; }
            if (!Enum.IsDefined(typeof(RuntimeSmokeComparison), assertion.Comparison)) { error = "Comparison is not supported."; return false; }
            if (assertion.Expected == null || assertion.Expected.Kind == RuntimeSmokeValueKind.Missing) { error = "Expected value cannot be missing."; return false; }

            var numeric = assertion.Expected.IsNumeric;
            switch (assertion.Comparison)
            {
                case RuntimeSmokeComparison.GreaterThan:
                case RuntimeSmokeComparison.GreaterOrEqual:
                case RuntimeSmokeComparison.LessThan:
                case RuntimeSmokeComparison.LessOrEqual:
                    if (!numeric) { error = "Numeric comparison requires a numeric expected value."; return false; }
                    break;
                case RuntimeSmokeComparison.Contains:
                case RuntimeSmokeComparison.NotContains:
                    // The expected value is a needle for arrays, but a string/map
                    // substring or subset for those containers. Runtime type checks
                    // are performed against the adapter's returned container.
                    break;
                case RuntimeSmokeComparison.WithinAbsolute:
                case RuntimeSmokeComparison.WithinRelative:
                    if (!numeric || !assertion.Tolerance.HasValue || assertion.Tolerance.Value < 0)
                    { error = "Tolerance comparison requires a numeric expected value and non-negative tolerance."; return false; }
                    if (assertion.Comparison == RuntimeSmokeComparison.WithinRelative && assertion.Expected.AsDecimal() == 0)
                    { error = "Relative tolerance requires a non-zero expected value."; return false; }
                    break;
            }
            if (assertion.Tolerance.HasValue && assertion.Comparison != RuntimeSmokeComparison.WithinAbsolute && assertion.Comparison != RuntimeSmokeComparison.WithinRelative)
            { error = "Tolerance is only valid for tolerance comparisons."; return false; }
            return true;
        }

        private static bool Compare(RuntimeSmokeAssertionDefinition assertion, RuntimeSmokeValue actual)
        {
            var expected = assertion.Expected;
            switch (assertion.Comparison)
            {
                case RuntimeSmokeComparison.Equals: return expected.DeepEquals(actual);
                case RuntimeSmokeComparison.NotEquals: return !expected.DeepEquals(actual);
                case RuntimeSmokeComparison.Contains: return actual.Contains(expected);
                case RuntimeSmokeComparison.NotContains: return !actual.Contains(expected);
                case RuntimeSmokeComparison.GreaterThan: return actual.IsNumeric && actual.AsDecimal() > expected.AsDecimal();
                case RuntimeSmokeComparison.GreaterOrEqual: return actual.IsNumeric && actual.AsDecimal() >= expected.AsDecimal();
                case RuntimeSmokeComparison.LessThan: return actual.IsNumeric && actual.AsDecimal() < expected.AsDecimal();
                case RuntimeSmokeComparison.LessOrEqual: return actual.IsNumeric && actual.AsDecimal() <= expected.AsDecimal();
                case RuntimeSmokeComparison.WithinAbsolute: return actual.IsNumeric && IsWithinAbsolute(actual.AsDecimal(), expected.AsDecimal(), assertion.Tolerance.Value);
                case RuntimeSmokeComparison.WithinRelative: return actual.IsNumeric && IsWithinRelative(actual.AsDecimal(), expected.AsDecimal(), assertion.Tolerance.Value);
                default: throw new InvalidOperationException("Unsupported comparison.");
            }
        }

        private static bool IsWithinAbsolute(decimal actual, decimal expected, decimal tolerance)
        {
            if (actual < 0 && expected >= 0) return actual >= expected - tolerance;
            if (actual >= 0 && expected < 0) return expected >= actual - tolerance;
            return actual >= expected ? actual - expected <= tolerance : expected - actual <= tolerance;
        }

        private static bool IsWithinRelative(decimal actual, decimal expected, decimal tolerance)
        {
            try
            {
                var difference = actual >= expected ? actual - expected : expected - actual;
                var magnitude = expected < 0 ? decimal.Negate(expected) : expected;
                return difference <= magnitude * tolerance;
            }
            catch (OverflowException)
            {
                // Decimal inputs remain finite; double's exponent range safely covers
                // the product/absolute-difference domain of two finite decimals.
                var difference = Math.Abs((double)actual - (double)expected);
                return difference <= Math.Abs((double)expected) * (double)tolerance;
            }
        }

        private static bool IsCompatible(RuntimeSmokeAssertionDefinition assertion, RuntimeSmokeValue actual)
        {
            switch (assertion.Comparison)
            {
                case RuntimeSmokeComparison.Contains:
                case RuntimeSmokeComparison.NotContains:
                    return (actual.Kind == RuntimeSmokeValueKind.String && assertion.Expected.Kind == RuntimeSmokeValueKind.String) ||
                           (actual.Kind == RuntimeSmokeValueKind.Array) ||
                           (actual.Kind == RuntimeSmokeValueKind.Map && assertion.Expected.Kind == RuntimeSmokeValueKind.Map);
                case RuntimeSmokeComparison.GreaterThan:
                case RuntimeSmokeComparison.GreaterOrEqual:
                case RuntimeSmokeComparison.LessThan:
                case RuntimeSmokeComparison.LessOrEqual:
                case RuntimeSmokeComparison.WithinAbsolute:
                case RuntimeSmokeComparison.WithinRelative:
                    return actual.IsNumeric;
                default:
                    return true;
            }
        }

        private static RuntimeSmokeAssertionResult NewResult(RuntimeSmokeAssertionDefinition assertion, RuntimeSmokeValue actual)
        {
            return new RuntimeSmokeAssertionResult
            {
                AssertionId = assertion == null ? null : assertion.Id,
                AssertionName = assertion == null ? null : assertion.Name,
                Predicate = assertion == null ? null : assertion.Predicate,
                Classification = assertion == null ? null : assertion.Classification,
                Comparison = assertion == null ? RuntimeSmokeComparison.Equals : assertion.Comparison,
                Tolerance = assertion == null ? null : assertion.Tolerance,
                ExpectedValue = assertion == null ? null : assertion.Expected,
                ActualValue = actual
            };
        }

        private static RuntimeSmokeAssertionResult Complete(RuntimeSmokeAssertionResult result, bool passed, RuntimeSmokeAssertionFailureCode code, string diagnostic, int limit)
        {
            result.Passed = passed;
            result.FailureCode = code;
            if (!passed && string.IsNullOrWhiteSpace(result.Classification))
                result.Classification = RuntimeSmokeCoordinator.AssertionFailureLabel(code);
            result.Diagnostic = diagnostic;
            result.ExpectedDigest = result.ExpectedValue == null ? null : result.ExpectedValue.Digest();
            result.ActualDigest = result.ActualValue == null ? null : result.ActualValue.Digest();
            result.ExpectedDisplay = Render(result.ExpectedValue, limit, out var expectedRedacted);
            result.ActualDisplay = Render(result.ActualValue, limit, out var actualRedacted);
            result.ExpectedRedacted = expectedRedacted;
            result.ActualRedacted = actualRedacted;
            return result;
        }

        private static string Render(RuntimeSmokeValue value, int limit, out bool redacted)
        {
            if (value == null) { redacted = false; return "<adapter-protocol-invalid>"; }
            if (limit < 16) limit = 16;
            var canonical = value.ToCanonicalJson();
            redacted = canonical.Length > limit;
            return redacted ? canonical.Substring(0, limit) + "… [redacted; sha256=" + value.Digest() + "]" : canonical;
        }
    }
}
#endif
