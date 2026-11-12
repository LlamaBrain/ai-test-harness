using System.Collections.Generic;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeAssertionTests
    {
        [Test]
        public void CanonicalMapsIgnoreKeyOrderWhileArraysRetainOrder()
        {
            var left = RuntimeSmokeValueJsonParser.Parse("{\"b\":2,\"a\":[true,1]}");
            var right = RuntimeSmokeValueJsonParser.Parse("{ \"a\" : [ true , 1 ] , \"b\" : 2 }");
            var reversedArray = RuntimeSmokeValueJsonParser.Parse("{\"a\":[1,true],\"b\":2}");

            Assert.That(left.ToCanonicalJson(), Is.EqualTo("{\"a\":[true,1],\"b\":2}"));
            Assert.That(left.DeepEquals(right), Is.True);
            Assert.That(left.DeepEquals(reversedArray), Is.False);
        }

        [Test]
        public void MissingNullIntegerAndDecimalRemainDistinct()
        {
            var missing = RuntimeSmokeValue.Missing();
            var nullValue = RuntimeSmokeValueJsonParser.Parse("null");
            var integer = RuntimeSmokeValueJsonParser.Parse("9223372036854775807");
            var decimalValue = RuntimeSmokeValueJsonParser.Parse("1.2500");

            Assert.That(missing.Kind, Is.EqualTo(RuntimeSmokeValueKind.Missing));
            Assert.That(nullValue.Kind, Is.EqualTo(RuntimeSmokeValueKind.Null));
            Assert.That(RuntimeSmokeValue.FromArray(new RuntimeSmokeValue[] { null }).Array[0].Kind, Is.EqualTo(RuntimeSmokeValueKind.Null));
            Assert.That(integer.Integer, Is.EqualTo(long.MaxValue));
            Assert.That(decimalValue.ToCanonicalJson(), Is.EqualTo("1.25"));
            Assert.That(missing.Digest(), Is.Not.EqualTo(RuntimeSmokeValueJsonParser.Parse("{\"$state\":\"missing\"}").Digest()));
            Assert.That(() => RuntimeSmokeValueJsonParser.Parse("9223372036854775808"), Throws.TypeOf<System.FormatException>());
            Assert.That(() => RuntimeSmokeValueJsonParser.Parse("NaN"), Throws.TypeOf<System.FormatException>());
            Assert.That(() => RuntimeSmokeValueJsonParser.Parse("{\"x\":1,\"x\":2}"), Throws.TypeOf<System.FormatException>());
            Assert.That(() => RuntimeSmokeValueJsonParser.Parse("١"), Throws.TypeOf<System.FormatException>());
            Assert.That(() => RuntimeSmokeValueJsonParser.Parse("\"\\uD800\""), Throws.TypeOf<System.FormatException>());
            Assert.That(RuntimeSmokeValueJsonParser.Parse("\"\\uD83D\\uDE80\"").String, Is.EqualTo("🚀"));
        }

        [Test]
        public void NumericComparatorSupportsIntegerAndDecimalAndInclusiveAbsoluteBoundary()
        {
            var assertion = Definition(RuntimeSmokeComparison.WithinAbsolute, RuntimeSmokeValue.From(10L), 0.25m);
            var result = RuntimeSmokeAssertionEvaluator.Evaluate(assertion, RuntimeSmokeValue.From(10.25m));

            Assert.That(result.Passed, Is.True);
            Assert.That(result.AssertionSchemaVersion, Is.EqualTo(1));
        }

        [Test]
        public void RelativeToleranceBoundaryIsInclusiveButCannotUseZeroExpectation()
        {
            var assertion = Definition(RuntimeSmokeComparison.WithinRelative, RuntimeSmokeValue.From(100L), 0.1m);
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(assertion, RuntimeSmokeValue.From(110.0m)).Passed, Is.True);

            assertion.Expected = RuntimeSmokeValue.From(0L);
            Assert.That(RuntimeSmokeAssertionEvaluator.Validate(assertion, out _), Is.False);
        }

        [Test]
        public void ExtremeFiniteDecimalsDoNotTurnValidToleranceIntoSchemaFailure()
        {
            var absolute = Definition(RuntimeSmokeComparison.WithinAbsolute, RuntimeSmokeValue.From(decimal.MinValue), decimal.MaxValue);
            var absoluteResult = RuntimeSmokeAssertionEvaluator.Evaluate(absolute, RuntimeSmokeValue.From(decimal.MaxValue));
            Assert.That(absoluteResult.FailureCode, Is.EqualTo(RuntimeSmokeAssertionFailureCode.AssertionMismatch));

            var relative = Definition(RuntimeSmokeComparison.WithinRelative, RuntimeSmokeValue.From(decimal.MinValue), decimal.MaxValue);
            var relativeResult = RuntimeSmokeAssertionEvaluator.Evaluate(relative, RuntimeSmokeValue.From(decimal.MaxValue));
            Assert.That(relativeResult.FailureCode, Is.Not.EqualTo(RuntimeSmokeAssertionFailureCode.AssertionSchemaInvalid));
        }

        [Test]
        public void InvalidComparisonTypesAreRejectedBeforeEvaluation()
        {
            var containsNumber = Definition(RuntimeSmokeComparison.Contains, RuntimeSmokeValue.From(3L));
            var wrongContainer = RuntimeSmokeAssertionEvaluator.Evaluate(containsNumber, RuntimeSmokeValue.From("three"));
            Assert.That(wrongContainer.FailureCode, Is.EqualTo(RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid));

            var numeric = Definition(RuntimeSmokeComparison.GreaterThan, RuntimeSmokeValue.From(3L));
            var result = RuntimeSmokeAssertionEvaluator.Evaluate(numeric, RuntimeSmokeValue.From("four"));
            Assert.That(result.FailureCode, Is.EqualTo(RuntimeSmokeAssertionFailureCode.AssertionAdapterProtocolInvalid));
        }

        [Test]
        public void ComparisonRunsOnFullValueBeforeEvidenceRedaction()
        {
            var secret = new string('x', 2000);
            var assertion = Definition(RuntimeSmokeComparison.Equals, RuntimeSmokeValue.From(secret));
            var result = RuntimeSmokeAssertionEvaluator.Evaluate(assertion, RuntimeSmokeValue.From(secret), 32);

            Assert.That(result.Passed, Is.True);
            Assert.That(result.ExpectedRedacted, Is.True);
            Assert.That(result.ActualRedacted, Is.True);
            Assert.That(result.ExpectedDigest, Is.EqualTo(result.ActualDigest));
            Assert.That(result.ExpectedDisplay, Does.Not.Contain(secret));
        }

        [Test]
        public void ContainsSupportsOrdinalStringsOrderedArraysAndMapSubsets()
        {
            var stringAssertion = Definition(RuntimeSmokeComparison.Contains, RuntimeSmokeValue.From("SAFE"));
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(stringAssertion, RuntimeSmokeValue.From("SAFE value")).Passed, Is.True);

            var arrayAssertion = Definition(RuntimeSmokeComparison.Contains, RuntimeSmokeValue.From(2L));
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(arrayAssertion, RuntimeSmokeValueJsonParser.Parse("[1,2,3]")).Passed, Is.True);

            var expectedMap = RuntimeSmokeValueJsonParser.Parse("{\"roof\":49}");
            var mapAssertion = Definition(RuntimeSmokeComparison.Contains, expectedMap);
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(mapAssertion, RuntimeSmokeValueJsonParser.Parse("{\"roof\":49,\"wall\":20}")).Passed, Is.True);
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(mapAssertion, RuntimeSmokeValueJsonParser.Parse("{\"nested\":{\"roof\":49,\"wall\":20}}")).Passed, Is.False);
            var nested = RuntimeSmokeValueJsonParser.Parse("{\"nested\":{\"roof\":49}}");
            var nestedAssertion = Definition(RuntimeSmokeComparison.Contains, nested);
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(nestedAssertion, RuntimeSmokeValueJsonParser.Parse("{\"nested\":{\"roof\":49,\"wall\":20}}")).Passed, Is.True);
            var notContains = Definition(RuntimeSmokeComparison.NotContains, RuntimeSmokeValue.From("unsafe"));
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(notContains, RuntimeSmokeValue.From("SAFE value")).Passed, Is.True);
        }

        [Test]
        public void RemainingComparisonOperatorsRespectNumericBoundariesAndTypeSensitiveEquality()
        {
            AssertComparison(RuntimeSmokeComparison.NotEquals, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(5m), true);
            AssertComparison(RuntimeSmokeComparison.GreaterThan, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(6m), true);
            AssertComparison(RuntimeSmokeComparison.GreaterThan, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(5m), false);
            AssertComparison(RuntimeSmokeComparison.GreaterOrEqual, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(5m), true);
            AssertComparison(RuntimeSmokeComparison.LessThan, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(4m), true);
            AssertComparison(RuntimeSmokeComparison.LessThan, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(5m), false);
            AssertComparison(RuntimeSmokeComparison.LessOrEqual, RuntimeSmokeValue.From(5L), RuntimeSmokeValue.From(5m), true);

            AssertComparison(RuntimeSmokeComparison.WithinAbsolute, RuntimeSmokeValue.From(-10L), RuntimeSmokeValue.From(-9.5m), true, 0.5m);
            AssertComparison(RuntimeSmokeComparison.WithinAbsolute, RuntimeSmokeValue.From(-10L), RuntimeSmokeValue.From(-9.49m), false, 0.5m);
            AssertComparison(RuntimeSmokeComparison.WithinRelative, RuntimeSmokeValue.From(-100L), RuntimeSmokeValue.From(-90m), true, 0.1m);

            var ordinalContains = Definition(RuntimeSmokeComparison.Contains, RuntimeSmokeValue.From("roof"));
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(ordinalContains, RuntimeSmokeValue.From("ROOF")).Passed, Is.False);
            var arrayNotContains = Definition(RuntimeSmokeComparison.NotContains, RuntimeSmokeValue.From(9L));
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(arrayNotContains, RuntimeSmokeValueJsonParser.Parse("[1,2,3]")).Passed, Is.True);
            var mapNotContains = Definition(RuntimeSmokeComparison.NotContains, RuntimeSmokeValueJsonParser.Parse("{\"roof\":48}"));
            Assert.That(RuntimeSmokeAssertionEvaluator.Evaluate(mapNotContains, RuntimeSmokeValueJsonParser.Parse("{\"roof\":49}")).Passed, Is.True);

            var negativeTolerance = Definition(RuntimeSmokeComparison.WithinAbsolute, RuntimeSmokeValue.From(1L), -0.1m);
            Assert.That(RuntimeSmokeAssertionEvaluator.Validate(negativeTolerance, out _), Is.False);
            var unexpectedTolerance = Definition(RuntimeSmokeComparison.Equals, RuntimeSmokeValue.From(1L), 0m);
            Assert.That(RuntimeSmokeAssertionEvaluator.Validate(unexpectedTolerance, out _), Is.False);
        }

        private static void AssertComparison(RuntimeSmokeComparison comparison, RuntimeSmokeValue expected,
            RuntimeSmokeValue actual, bool passed, decimal? tolerance = null)
        {
            var result = RuntimeSmokeAssertionEvaluator.Evaluate(Definition(comparison, expected, tolerance), actual);
            Assert.That(result.Passed, Is.EqualTo(passed), comparison + " expected=" + expected.ToCanonicalJson() + " actual=" + actual.ToCanonicalJson());
        }

        private static RuntimeSmokeAssertionDefinition Definition(RuntimeSmokeComparison comparison, RuntimeSmokeValue expected, decimal? tolerance = null)
        {
            return new RuntimeSmokeAssertionDefinition
            {
                AssertionSchemaVersion = RuntimeSmokeAssertionSchema.Version,
                Id = "assertions/test-value",
                Name = "typed value contract",
                Predicate = "fixture.value",
                Comparison = comparison,
                Expected = expected,
                Tolerance = tolerance
            };
        }
    }
}
