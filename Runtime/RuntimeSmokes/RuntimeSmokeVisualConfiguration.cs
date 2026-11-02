#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Trusted operator configuration; never an authored workflow field.</summary>
    public sealed class RuntimeSmokeVisualConfiguration
    {
        private string _executable, _model;
        private IReadOnlyDictionary<string,string> _policies, _manifests;
        public static RuntimeSmokeVisualConfiguration FromEnvironment(Func<string,string> read = null)
        {
            var path = (read ?? Environment.GetEnvironmentVariable)("ATH_VISUAL_CONFIG");
            if (string.IsNullOrEmpty(path)) return null;
            Absolute(path);
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1024 * 1024) throw new ArgumentException("Visual configuration is missing or exceeds 1 MiB.");
            return Parse(File.ReadAllText(path));
        }
        public static RuntimeSmokeVisualConfiguration Parse(string json)
        {
            var value = RuntimeSmokeValueJsonParser.Parse(json);
            var keys = new[]{"schemaVersion","executable","model","policies","manifests"};
            if (value.Kind != RuntimeSmokeValueKind.Map || value.Map.Count != keys.Length || keys.Any(k=>!value.Map.ContainsKey(k)) ||
                value.Map["schemaVersion"].Kind != RuntimeSmokeValueKind.Integer || value.Map["schemaVersion"].Integer != 1)
                throw new ArgumentException("Visual configuration requires exactly the version-1 fields.");
            string PathValue(RuntimeSmokeValue item) => item.Kind == RuntimeSmokeValueKind.String ? Absolute(item.String) : throw new ArgumentException("Configured paths must be strings.");
            IReadOnlyDictionary<string,string> Paths(RuntimeSmokeValue item)
            {
                if (item.Kind != RuntimeSmokeValueKind.Map || item.Map.Count > 1024) throw new ArgumentException("Bounded policy/manifest maps required.");
                return item.Map.ToDictionary(p=>RuntimeSmokeVisualAssertion.Token(p.Key)?p.Key:throw new ArgumentException("Unsafe visual configuration identifier."),p=>PathValue(p.Value),StringComparer.Ordinal);
            }
            return new RuntimeSmokeVisualConfiguration { _executable=PathValue(value.Map["executable"]),
                _model=value.Map["model"].Kind == RuntimeSmokeValueKind.Null ? null : PathValue(value.Map["model"]),
                _policies=Paths(value.Map["policies"]),_manifests=Paths(value.Map["manifests"]) };
        }
        public RuntimeSmokeVisualAnalyzer CreateAnalyzer(string runDirectory, Func<string,RuntimeSmokeVisualCapture> capture) =>
            new RuntimeSmokeVisualAnalyzer(_executable,runDirectory,_policies,_manifests,capture,_model);
        private static string Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || Path.GetFullPath(path) != path)
                throw new ArgumentException("Visual configuration requires normalized absolute paths.");
            return path;
        }
    }
}
#endif
