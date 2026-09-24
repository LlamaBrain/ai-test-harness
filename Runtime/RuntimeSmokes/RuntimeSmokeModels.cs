#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    public enum RuntimeSmokeTarget
    {
        Editor,
        Development,
        SmokeHarness
    }

    public enum RuntimeSmokeMode
    {
        Fsd,
        AiObserved,
        Hitl
    }

    public enum RuntimeSmokeValueKind
    {
        Missing,
        Null,
        Boolean,
        String,
        Integer,
        Decimal,
        Array,
        Map
    }

    public enum RuntimeSmokeComparison
    {
        Equals,
        NotEquals,
        Contains,
        NotContains,
        GreaterThan,
        GreaterOrEqual,
        LessThan,
        LessOrEqual,
        WithinAbsolute,
        WithinRelative
    }

    public static class RuntimeSmokeAssertionSchema
    {
        public const int Version = 1;

        public static string ComparisonLabel(RuntimeSmokeComparison comparison)
        {
            switch (comparison)
            {
                case RuntimeSmokeComparison.Equals: return "equals";
                case RuntimeSmokeComparison.NotEquals: return "not_equals";
                case RuntimeSmokeComparison.Contains: return "contains";
                case RuntimeSmokeComparison.NotContains: return "not_contains";
                case RuntimeSmokeComparison.GreaterThan: return "greater_than";
                case RuntimeSmokeComparison.GreaterOrEqual: return "greater_or_equal";
                case RuntimeSmokeComparison.LessThan: return "less_than";
                case RuntimeSmokeComparison.LessOrEqual: return "less_or_equal";
                case RuntimeSmokeComparison.WithinAbsolute: return "within_absolute";
                case RuntimeSmokeComparison.WithinRelative: return "within_relative";
                default: return "unknown";
            }
        }
    }

    public enum RuntimeSmokeAssertionFailureCode
    {
        None,
        AssertionMismatch,
        PredicateMissing,
        PredicateError,
        PredicateTimeout,
        AssertionSchemaInvalid,
        AssertionAdapterProtocolInvalid
    }
}
#endif
