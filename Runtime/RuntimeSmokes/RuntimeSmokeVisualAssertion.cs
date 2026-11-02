#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Engine-neutral authored request. Paths and executables are host configuration, never document input.</summary>
    public sealed class RuntimeSmokeVisualAssertion
    {
        public const string Capability = "visual.analysis.v1";
        public int SchemaVersion { get; private set; }
        public string CaptureLabel { get; private set; }
        public string PolicyId { get; private set; }
        public string PolicySha256 { get; private set; }
        public string ManifestId { get; private set; }
        public string ManifestSha256 { get; private set; }
        public string Mode { get; private set; }

        internal static RuntimeSmokeVisualAssertion Parse(RuntimeSmokeValue value)
        {
            if (value == null || value.Kind != RuntimeSmokeValueKind.Map) throw new ArgumentException("Visual operand must be an object.");
            var fields = value.Map;
            var allowed = new[] { "schemaVersion", "captureLabel", "policyId", "policySha256", "manifestId", "manifestSha256", "mode" };
            if (fields.Keys.Any(key => !allowed.Contains(key)) || fields.Count != allowed.Length)
                throw new ArgumentException("Visual operand requires exactly the version-1 fields.");
            if (fields["schemaVersion"].Kind != RuntimeSmokeValueKind.Integer || fields["schemaVersion"].Integer != 1)
                throw new ArgumentException("Unsupported visual operand version.");
            string Read(string key) => fields[key].Kind == RuntimeSmokeValueKind.String ? fields[key].String : throw new ArgumentException("Visual operand strings required.");
            var result = new RuntimeSmokeVisualAssertion { SchemaVersion = 1, CaptureLabel = Read("captureLabel"), PolicyId = Read("policyId"),
                PolicySha256 = Read("policySha256"), ManifestId = Read("manifestId"), ManifestSha256 = Read("manifestSha256"), Mode = Read("mode") };
            if (!Token(result.CaptureLabel) || !Token(result.PolicyId) || !Token(result.ManifestId) ||
                !Digest(result.PolicySha256) || !Digest(result.ManifestSha256) || result.Mode != "manifest")
                throw new ArgumentException("Visual verification requires safe identifiers, exact SHA-256 digests and manifest mode.");
            return result;
        }

        internal static bool Token(string value) => !string.IsNullOrEmpty(value) && value.Length <= 96 && value[0] != '.' &&
            !value.Contains("..") && value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');
        internal static bool Digest(string value) => value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
    }
}
#endif
