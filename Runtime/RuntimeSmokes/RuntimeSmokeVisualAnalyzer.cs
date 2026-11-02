#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Trusted host binding to an existing completed capture receipt. Never deserialize this from workflow input.</summary>
    public sealed class RuntimeSmokeVisualCapture
    {
        public string CorrelationId { get; }
        public string WorkflowId { get; }
        public RuntimeSmokeCaptureArtifactReceipt Receipt { get; }
        public RuntimeSmokeVisualCapture(string correlationId, string workflowId, RuntimeSmokeCaptureArtifactReceipt receipt)
        { CorrelationId = correlationId; WorkflowId = workflowId; Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt)); }
    }

    /// <summary>Runs the standalone analyzer with trusted paths and bounded, run-owned evidence. No engine capture or shell execution.</summary>
    public sealed class RuntimeSmokeVisualAnalyzer : IRuntimeSmokeStepAdapter
    {
        private readonly string _executable, _runDirectory, _model;
        private readonly IReadOnlyDictionary<string, string> _policies, _manifests;
        private readonly Func<string, RuntimeSmokeVisualCapture> _capture;
        private readonly object _processGate = new object();
        private Process _activeProcess;

        internal void Cancel()
        { lock (_processGate) { if (_activeProcess != null && !_activeProcess.HasExited) _activeProcess.Kill(); } }

        public RuntimeSmokeVisualAnalyzer(string executable, string runDirectory,
            IReadOnlyDictionary<string, string> policies, IReadOnlyDictionary<string, string> manifests,
            Func<string, RuntimeSmokeVisualCapture> capture, string model = null)
        {
            _executable = Path.GetFullPath(executable ?? throw new ArgumentNullException(nameof(executable)));
            _runDirectory = Path.GetFullPath(runDirectory ?? throw new ArgumentNullException(nameof(runDirectory)));
            _policies = (policies ?? throw new ArgumentNullException(nameof(policies))).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _manifests = (manifests ?? throw new ArgumentNullException(nameof(manifests))).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _model = model == null ? null : Path.GetFullPath(model);
        }

        public async Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context)
        {
            try
            {
                var request = step.Assertion?.Visual ?? throw new InvalidOperationException("Typed visual request required.");
                var capture = _capture(request.CaptureLabel) ?? throw new InvalidOperationException("Required capture has not completed.");
                if (capture.CorrelationId != context.CorrelationId || capture.WorkflowId != context.WorkflowId)
                    throw new InvalidOperationException("Capture correlation or workflow mismatch.");
                if (capture.Receipt.RelativePath != "captures/" + request.CaptureLabel + ".png")
                    throw new InvalidOperationException("Capture receipt label mismatch.");
                var inputPath = Path.Combine(_runDirectory, capture.Receipt.RelativePath);
                var bytes = Read(inputPath, RuntimeSmokeCaptureArtifactWriter.MaximumPngBytes);
                if (bytes.LongLength != capture.Receipt.ByteLength || Hash(bytes) != capture.Receipt.Sha256)
                    throw new InvalidOperationException("Capture receipt integrity mismatch.");
                if (!_policies.TryGetValue(request.PolicyId, out var policyPath) || !_manifests.TryGetValue(request.ManifestId, out var manifestPath))
                    throw new InvalidOperationException("Visual policy or manifest is not registered by the host.");
                var policy = Read(policyPath, 1024 * 1024); var manifest = Read(manifestPath, 1024 * 1024);
                if (Hash(policy) != request.PolicySha256 || Hash(manifest) != request.ManifestSha256)
                    throw new InvalidOperationException("Visual policy or manifest digest mismatch.");
                RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(_runDirectory);
                if (!Directory.Exists(_runDirectory)) throw new DirectoryNotFoundException("Run directory must already exist.");
                var name = "visual-" + Hash(Encoding.UTF8.GetBytes(context.StepId));
                var directory = Path.Combine(_runDirectory, name);
                if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Visual evidence already exists for this step.");
                Directory.CreateDirectory(directory);
                RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(directory);
                // Snapshot the verified inputs so the analyzer reads exactly the bytes whose identities were checked.
                Write(Path.Combine(directory, "input.png"), bytes);
                Write(Path.Combine(directory, "policy.json"), policy);
                Write(Path.Combine(directory, "manifest.json"), manifest);
                var resultPath = Path.Combine(directory, "result.json");
                var annotated = Path.Combine(directory, "annotated.png");
                var args = new List<string> { "--input", Path.Combine(directory,"input.png"), "--policy", Path.Combine(directory,"policy.json"),
                    "--manifest", Path.Combine(directory,"manifest.json"), "--output", resultPath, "--annotated", annotated };
                var modelDigest = _model == null ? null : Hash(Read(_model,128*1024*1024));
                if (_model != null) { args.Add("--model"); args.Add(_model); }
                RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(_executable);
                if (!File.Exists(_executable)) throw new FileNotFoundException("Configured visual analyzer is missing.");
                var analyzerDigest = Hash(Read(_executable,128*1024*1024));
                var code = await RunProcess(args, context.DeadlineUtc);
                if (code < 0 || code > 2) throw new InvalidOperationException("Visual analyzer exited with infrastructure error code " + code + ".");
                if (Hash(Read(_executable,128*1024*1024)) != analyzerDigest || _model != null && Hash(Read(_model,128*1024*1024)) != modelDigest)
                    throw new InvalidOperationException("Analyzer or model changed during analysis.");
                if (Hash(Read(inputPath, RuntimeSmokeCaptureArtifactWriter.MaximumPngBytes)) != capture.Receipt.Sha256 ||
                    Hash(Read(Path.Combine(directory,"input.png"), RuntimeSmokeCaptureArtifactWriter.MaximumPngBytes)) != capture.Receipt.Sha256 ||
                    Hash(Read(Path.Combine(directory,"policy.json"),1024*1024)) != request.PolicySha256 ||
                    Hash(Read(Path.Combine(directory,"manifest.json"),1024*1024)) != request.ManifestSha256)
                    throw new InvalidOperationException("Visual inputs changed during analysis.");
                var resultBytes = Read(resultPath, 4 * 1024 * 1024);
                var result = RuntimeSmokeValueJsonParser.Parse(Encoding.UTF8.GetString(resultBytes));
                var outcome = ValidateResult(result, code);
                if (outcome == "pass" && _model == null) throw new InvalidDataException("Strict visual pass requires a configured text detection model.");
                var manifestValue = RuntimeSmokeValueJsonParser.Parse(Encoding.UTF8.GetString(manifest));
                var policyValue = RuntimeSmokeValueJsonParser.Parse(Encoding.UTF8.GetString(policy));
                if (bytes.Length < 24 || result.Map["image"].Map["width"].Integer != BigEndian(bytes,16) ||
                    result.Map["image"].Map["height"].Integer != BigEndian(bytes,20) ||
                    result.Map["image"].ToCanonicalJson() != manifestValue.Map["resolution"].ToCanonicalJson() ||
                    result.Map["policy"].Map["id"].String != policyValue.Map["id"].String ||
                    result.Map["policy"].Map["version"].String != policyValue.Map["version"].String ||
                    Number(result.Map["policy"].Map["minHeightPx"]) != Number(policyValue.Map["minHeightPx"]) ||
                    Number(result.Map["policy"].Map["minContrastRatio"]) != Number(policyValue.Map["minContrastRatio"]))
                    throw new InvalidDataException("Analyzer dimensions or policy identity mismatch.");
                if (result.Map["coverage"].Map["expected"].Integer != manifestValue.Map["elements"].Array.Count)
                    throw new InvalidDataException("Analyzer expected coverage differs from manifest.");
                foreach(var key in new[]{"screenId","screenshotId","locale","uiScale"})
                    if(result.Map["context"].Map[key].ToCanonicalJson()!=manifestValue.Map[key].ToCanonicalJson())
                        throw new InvalidDataException("Analyzer capture context differs from manifest.");
                ValidateFindings(result, manifestValue, policyValue);
                var evidence = RuntimeSmokeValue.FromMap(new[] {
                    Pair("schemaVersion", RuntimeSmokeValue.From(1L)), Pair("correlationId",RuntimeSmokeValue.From(context.CorrelationId)),
                    Pair("workflowId",RuntimeSmokeValue.From(context.WorkflowId)), Pair("stepId",RuntimeSmokeValue.From(context.StepId)),
                    Pair("capture",capture.Receipt.ToRuntimeSmokeValue()), Pair("policySha256",RuntimeSmokeValue.From(request.PolicySha256)),
                    Pair("manifestSha256",RuntimeSmokeValue.From(request.ManifestSha256)), Pair("outcome",RuntimeSmokeValue.From(outcome)),
                    Pair("analyzerSha256",RuntimeSmokeValue.From(analyzerDigest)), Pair("modelSha256",RuntimeSmokeValue.From(modelDigest)),
                    Pair("resultArtifact",RuntimeSmokeValue.From(name + "/result.json")), Pair("resultSha256",RuntimeSmokeValue.From(Hash(resultBytes))),
                    Pair("annotatedArtifact",RuntimeSmokeValue.From(name + "/annotated.png")),
                    Pair("annotatedSha256",RuntimeSmokeValue.From(Hash(Read(annotated,RuntimeSmokeCaptureArtifactWriter.MaximumPngBytes))))
                }).ToCanonicalJson();
                Write(Path.Combine(directory,"receipt.json"),Encoding.UTF8.GetBytes(evidence));
                return new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Passed,
                    Actual = RuntimeSmokeValue.From(outcome), VisualEvidenceJson = evidence,
                    Diagnostic = "Visual " + outcome + "; evidence " + name + "/receipt.json" };
            }
            catch (TimeoutException exception) { return Error(context, exception.Message, RuntimeSmokeStepResponseStatus.TimedOut); }
            catch (Exception exception) { return Error(context, exception.Message, RuntimeSmokeStepResponseStatus.Failed); }
        }

        private async Task<int> RunProcess(List<string> args, DateTime deadline)
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo { FileName = _executable,
                Arguments = string.Join(" ",args.Select(Quote)), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _runDirectory } })
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Visual deadline expired before launch.");
                if (!process.Start()) throw new InvalidOperationException("Visual analyzer did not start.");
                lock (_processGate) _activeProcess = process;
                var stdout = Drain(process.StandardOutput, process); var stderr = Drain(process.StandardError, process);
                try
                {
                    while (!process.HasExited)
                    {
                        if (DateTime.UtcNow >= deadline) throw new TimeoutException("Visual analyzer exceeded workflow deadline.");
                        await Task.Delay(20).ConfigureAwait(false);
                    }
                    await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                    return process.ExitCode;
                }
                finally { lock (_processGate) { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } _activeProcess = null; } }
            }
        }

        private static async Task Drain(StreamReader reader, Process process)
        {
            var buffer = new char[4096]; var total = 0; int count;
            while ((count = await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false)) != 0)
            { total += count; if (total > 4 * 1024 * 1024) { if (!process.HasExited) process.Kill(); throw new IOException("Analyzer output exceeded 4 MiB."); } }
        }

        internal static string ValidateResult(RuntimeSmokeValue result, int exitCode)
        {
            if (result.Kind != RuntimeSmokeValueKind.Map) throw new InvalidDataException("Analyzer result must be an object.");
            var map = result.Map;
            if (!map.TryGetValue("schemaVersion",out var version) || version.Kind != RuntimeSmokeValueKind.Integer || version.Integer != 1 ||
                !map.TryGetValue("outcome",out var outcome) || outcome.Kind != RuntimeSmokeValueKind.String)
                throw new InvalidDataException("Unsupported analyzer result schema.");
            var code = outcome.String == "pass" ? 0 : outcome.String == "fail" ? 1 : outcome.String == "inconclusive" ? 2 : -1;
            if (code < 0 || code != exitCode) throw new InvalidDataException("Analyzer failed or exit/result mismatch.");
            if (!map.TryGetValue("coverage",out var coverage) || coverage.Kind != RuntimeSmokeValueKind.Map ||
                !coverage.Map.TryGetValue("mode",out var mode) || mode.Kind != RuntimeSmokeValueKind.String || mode.String != "manifest" ||
                !map.TryGetValue("findings",out var findings) || findings.Kind != RuntimeSmokeValueKind.Array || findings.Array.Count == 0)
                throw new InvalidDataException("Analyzer lacks manifest coverage or findings.");
            if (outcome.String == "pass")
            {
                foreach (var key in new[] { "expected", "measured" })
                    if (!coverage.Map.TryGetValue(key,out var count) || count.Kind != RuntimeSmokeValueKind.Integer || count.Integer <= 0)
                        throw new InvalidDataException("Empty visual coverage cannot pass.");
                if (coverage.Map["expected"].Integer != coverage.Map["measured"].Integer)
                    throw new InvalidDataException("Incomplete visual coverage cannot pass.");
                foreach (var finding in findings.Array)
                    if (finding.Kind != RuntimeSmokeValueKind.Map || !finding.Map.TryGetValue("outcome",out var status) ||
                        status.Kind != RuntimeSmokeValueKind.String || status.String != "pass")
                        throw new InvalidDataException("Nonpassing finding contradicts analyzer pass.");
            }
            return outcome.String;
        }

        internal static void ValidateFindings(RuntimeSmokeValue result, RuntimeSmokeValue manifest, RuntimeSmokeValue policy)
        {
            var elements = manifest.Map["elements"].Array.ToDictionary(e => e.Map["id"].String, e => e, StringComparer.Ordinal);
            var rules = new[] { "text.height", "text.contrast", "text.clipping" };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var finding in result.Map["findings"].Array)
            {
                var f = finding.Map; var id = f["elementId"].String; var rule = f["ruleId"].String;
                if (!elements.TryGetValue(id, out var element) || !rules.Contains(rule) || !seen.Add(id + "\n" + rule))
                    throw new InvalidDataException("Unknown or duplicate visual element/rule.");
                if (f["expectedBounds"].ToCanonicalJson() != element.Map["bounds"].ToCanonicalJson() ||
                    !Contained(f["bounds"],element.Map["bounds"]))
                    throw new InvalidDataException("Finding bounds are not contained in the declared element.");
                var status = f["outcome"].String;
                if (status != "pass" && status != "fail" && status != "inconclusive") throw new InvalidDataException("Invalid finding outcome.");
                foreach (var confidence in new[] { "measurementConfidence", "detectionConfidence" })
                {
                    var value = Number(f[confidence]);
                    if (value < 0 || value > 1 || status == "pass" && value == 0) throw new InvalidDataException("Invalid finding confidence.");
                }
                if (status == "pass")
                {
                    var bounds=f["bounds"].Map; var resolution=manifest.Map["resolution"].Map;
                    if(bounds["x"].Integer<0 || bounds["y"].Integer<0 || bounds["x"].Integer+bounds["width"].Integer>resolution["width"].Integer ||
                        bounds["y"].Integer+bounds["height"].Integer>resolution["height"].Integer)
                        throw new InvalidDataException("Passing finding lies outside the screenshot.");
                    if(rule=="text.height")
                    {
                        var measured=Number(f["measuredHeightPx"]);
                        if(measured<=0 || measured>Number(f["bounds"].Map["height"]) || measured<Number(policy.Map["minHeightPx"]))
                            throw new InvalidDataException("Passing height finding lacks a valid authoritative measurement.");
                    }
                    if(rule=="text.contrast")
                    {
                        var contrast=Number(f["contrastRatio"]);
                        if(contrast<1 || contrast>21 || contrast<Number(policy.Map["minContrastRatio"]))
                            throw new InvalidDataException("Passing contrast finding lacks a valid authoritative measurement.");
                    }
                }
            }
            if (seen.Count != elements.Count * rules.Length) throw new InvalidDataException("Analyzer omitted required element rules.");
        }

        private static decimal Number(RuntimeSmokeValue value) => value.Kind == RuntimeSmokeValueKind.Integer ? value.Integer :
            value.Kind == RuntimeSmokeValueKind.Decimal ? value.Decimal : throw new InvalidDataException("Numeric measurement required.");
        private static bool Contained(RuntimeSmokeValue inner, RuntimeSmokeValue outer)
        {
            var a=inner.Map; var b=outer.Map;
            foreach(var bounds in new[]{a,b})
                if(bounds["width"].Integer<=0 || bounds["height"].Integer<=0 || bounds["width"].Integer>16384 || bounds["height"].Integer>16384 ||
                    bounds["x"].Integer<int.MinValue || bounds["x"].Integer>int.MaxValue || bounds["y"].Integer<int.MinValue || bounds["y"].Integer>int.MaxValue) return false;
            return a["x"].Integer>=b["x"].Integer && a["y"].Integer>=b["y"].Integer &&
                a["x"].Integer+a["width"].Integer<=b["x"].Integer+b["width"].Integer && a["y"].Integer+a["height"].Integer<=b["y"].Integer+b["height"].Integer;
        }
        private static long BigEndian(byte[] bytes,int offset) => ((long)bytes[offset]<<24)|((long)bytes[offset+1]<<16)|((long)bytes[offset+2]<<8)|bytes[offset+3];

        private static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { '\r','\n','\0' }) >= 0) throw new ArgumentException("Invalid process argument.");
            var output = new StringBuilder("\""); var slashes = 0;
            foreach (var c in value) { if (c == '\\') { slashes++; continue; } if (c == '"') output.Append('\\',slashes*2+1); else output.Append('\\',slashes); output.Append(c); slashes=0; }
            return output.Append('\\',slashes*2).Append('"').ToString();
        }
        private static byte[] Read(string path, int maximum)
        {
            var full = Path.GetFullPath(path); RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(full);
            using (var input = new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read))
            { if (input.Length <= 0 || input.Length > maximum) throw new InvalidDataException("Visual evidence size outside bounds.");
                var bytes = new byte[(int)input.Length]; var offset=0; while(offset<bytes.Length) { var count=input.Read(bytes,offset,bytes.Length-offset); if(count==0) throw new EndOfStreamException(); offset+=count; } return bytes; }
        }
        private static void Write(string path, byte[] bytes)
        { RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(Path.GetDirectoryName(path)); using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); } }
        private static string Hash(byte[] bytes) { using(var sha=SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b=>b.ToString("x2"))); }
        private static KeyValuePair<string,RuntimeSmokeValue> Pair(string key, RuntimeSmokeValue value) => new KeyValuePair<string,RuntimeSmokeValue>(key,value);
        private static RuntimeSmokeStepResponse Error(RuntimeSmokeStepContext context,string diagnostic,RuntimeSmokeStepResponseStatus status) =>
            new RuntimeSmokeStepResponse { CorrelationId=context.CorrelationId,Status=status,Diagnostic=diagnostic };
    }
}
#endif
