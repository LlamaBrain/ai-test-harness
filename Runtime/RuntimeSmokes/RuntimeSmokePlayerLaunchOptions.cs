#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Validated command-line identity for one externally watched FSD player run.</summary>
    public sealed class RuntimeSmokePlayerLaunchOptions
    {
        internal static RuntimeSmokePlayerLaunchOptions ClaimedCurrent { get; private set; }
        public string WorkflowId { get; private set; }
        public string CorrelationId { get; private set; }
        public string ArtifactRoot { get; private set; }
        public string HostRoot { get; private set; }
        public string OutputRoot { get; private set; }
        public string AuthoredRoot { get; private set; }
        public string Nonce { get; private set; }
        public string BuildToken { get; private set; }
        public string Scenario { get; private set; }
        public string Seed { get; private set; }
        public string ContinuationId { get; private set; }
        public string ContinuationStageId { get; private set; }
        public int ContinuationStageIndex { get; private set; }
        public int ContinuationStageCount { get; private set; }
        public string ContinuationProfileAction { get; private set; }
        public string ContinuationProfileId { get; private set; }

        internal void SetFixtureIdentity(string scenario, string seed)
        {
            Scenario = scenario;
            Seed = seed;
        }

        internal void SetClaimedHostPaths(string hostRoot, string outputRoot, string authoredRoot)
        {
            HostRoot = Path.GetFullPath(hostRoot);
            OutputRoot = Path.GetFullPath(outputRoot);
            AuthoredRoot = Path.GetFullPath(authoredRoot);
        }

        internal void MarkDurablyClaimed() => ClaimedCurrent = this;

        public static RuntimeSmokePlayerLaunchOptions FromEnvironment()
        {
#if !UNITY_EDITOR
            if (ClaimedCurrent != null) return ClaimedCurrent;
#endif
            var args = Environment.GetCommandLineArgs();
            var requested = false;
            foreach (var argument in args)
            {
                if (argument.StartsWith("-runtimeSmoke", StringComparison.Ordinal))
                {
                    requested = true;
                    break;
                }
            }
            if (!requested) return null;
            if (!TryParse(args, out var options, out var failure))
                throw new InvalidOperationException(failure ?? "RuntimeSmoke launch arguments are invalid.");
            return options;
        }

        public static bool TryParse(string[] args, out RuntimeSmokePlayerLaunchOptions options, out string failure)
        {
            options = null;
            failure = null;
            args = args ?? Array.Empty<string>();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new HashSet<string>(new[]
            {
                "-runtimeSmoke", "-runtimeSmokeCorrelation", "-runtimeSmokeArtifactRoot",
                "-runtimeSmokeNonce", "-runtimeSmokeBuildToken", "-runtimeSmokeContinuation",
                "-runtimeSmokeContinuationStage", "-runtimeSmokeContinuationIndex", "-runtimeSmokeContinuationCount",
                "-runtimeSmokeContinuationAction", "-runtimeSmokeContinuationProfile"
            }, StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (string.IsNullOrEmpty(argument)) continue;
                var equals = argument.IndexOf('=');
                var name = equals < 0 ? argument : argument.Substring(0, equals);
                if (!names.Contains(name)) continue;
                if (values.ContainsKey(name))
                {
                    failure = "RuntimeSmoke launch argument is duplicated: " + name;
                    return false;
                }
                var value = equals >= 0 ? argument.Substring(equals + 1) : index + 1 < args.Length ? args[++index] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
                {
                    failure = "RuntimeSmoke launch arguments are missing a value.";
                    return false;
                }
                values.Add(name, value);
            }

            var coreNames = new[] { "-runtimeSmoke", "-runtimeSmokeCorrelation", "-runtimeSmokeArtifactRoot", "-runtimeSmokeNonce", "-runtimeSmokeBuildToken" };
            var continuationNames = names.Where(name => name.StartsWith("-runtimeSmokeContinuation", StringComparison.Ordinal)).ToArray();
            var hasContinuation = continuationNames.Any(values.ContainsKey);
            if (coreNames.Any(name => !values.ContainsKey(name)) || (!hasContinuation && continuationNames.Any(values.ContainsKey)))
            {
                failure = "RuntimeSmoke launch arguments are missing or incomplete.";
                return false;
            }
            if (hasContinuation && continuationNames.Any(name => !values.ContainsKey(name)))
            {
                failure = "RuntimeSmoke continuation arguments are missing or incomplete.";
                return false;
            }
            var workflow = values["-runtimeSmoke"];
            var correlation = values["-runtimeSmokeCorrelation"];
            if (!SafeWorkflowId(workflow))
            {
                failure = "RuntimeSmoke workflow ID is invalid.";
                return false;
            }
            if (!SafeId(correlation))
            {
                failure = "RuntimeSmoke correlation ID is invalid.";
                return false;
            }
            if (!Path.IsPathRooted(values["-runtimeSmokeArtifactRoot"]))
            {
                failure = "RuntimeSmoke artifact root must be an absolute path.";
                return false;
            }
            if (!IsSha256(values["-runtimeSmokeNonce"]) || !IsSha256(values["-runtimeSmokeBuildToken"]))
            {
                failure = "RuntimeSmoke nonce and build token must be lowercase SHA-256 values.";
                return false;
            }
            var continuationId = hasContinuation ? values["-runtimeSmokeContinuation"] : null;
            var continuationStage = hasContinuation ? values["-runtimeSmokeContinuationStage"] : null;
            var continuationProfile = hasContinuation ? values["-runtimeSmokeContinuationProfile"] : null;
            var continuationAction = hasContinuation ? values["-runtimeSmokeContinuationAction"] : null;
            var continuationIndex = 0;
            var continuationCount = 0;
            if (hasContinuation && (!SafeId(continuationId) || !SafeId(continuationStage) || !SafeId(continuationProfile) ||
                !int.TryParse(values["-runtimeSmokeContinuationIndex"], out continuationIndex) ||
                !int.TryParse(values["-runtimeSmokeContinuationCount"], out continuationCount) || continuationCount < 2 ||
                continuationIndex < 0 || continuationIndex >= continuationCount ||
                (continuationIndex == 0 ? continuationAction != "create" : continuationAction != "load")))
            {
                failure = "RuntimeSmoke continuation identity is invalid or incomplete.";
                return false;
            }
            try
            {
                options = new RuntimeSmokePlayerLaunchOptions
                {
                    WorkflowId = workflow,
                    CorrelationId = correlation,
                    ArtifactRoot = Path.GetFullPath(values["-runtimeSmokeArtifactRoot"]),
                    Nonce = values["-runtimeSmokeNonce"],
                    BuildToken = values["-runtimeSmokeBuildToken"],
                    ContinuationId = continuationId,
                    ContinuationStageId = continuationStage,
                    ContinuationStageIndex = continuationIndex,
                    ContinuationStageCount = continuationCount,
                    ContinuationProfileAction = continuationAction,
                    ContinuationProfileId = continuationProfile
                };
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                failure = "RuntimeSmoke artifact root is invalid.";
                return false;
            }
        }

        private static bool SafeWorkflowId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 240 &&
            !value.StartsWith("/", StringComparison.Ordinal) && !value.EndsWith("/", StringComparison.Ordinal) &&
            value.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != ".." && SafeId(segment));

        private static bool SafeId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && value != "." && value != ".." &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static bool IsSha256(string value) => value != null && value.Length == 64 &&
            value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
    }
}
#endif
