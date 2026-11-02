using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading.Tasks;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeVisualTests
    {
        private static string Document(string requires = "visual.analysis.v1", string label = "menu") =>
            "{\"schemaVersion\":1,\"id\":\"workflows/visual\",\"kind\":\"workflow\",\"testClass\":\"RuntimeSmoke\",\"mode\":\"fsd\",\"proposition\":\"menu is legible\",\"excludedBehavior\":\"other screens\",\"targets\":[\"editor\"],\"requires\":[\"" + requires + "\"],\"timeoutSeconds\":30,\"steps\":[{\"id\":\"start\",\"type\":\"start\"},{\"id\":\"visual\",\"type\":\"assert\",\"assertion\":{\"assertionSchemaVersion\":1,\"id\":\"menu\",\"name\":\"menu\",\"predicate\":\"visual.analysis.v1\",\"comparison\":\"equals\",\"expected\":\"pass\",\"visual\":{\"schemaVersion\":1,\"captureLabel\":\"" + label + "\",\"policyId\":\"project\",\"policySha256\":\"" + new string('a',64) + "\",\"manifestId\":\"menu\",\"manifestSha256\":\"" + new string('b',64) + "\",\"mode\":\"manifest\"}}},{\"id\":\"end\",\"type\":\"terminal\"}]}";

        private static RuntimeSmokeCompileResult Compile(string json) => RuntimeSmokeDocumentCompiler.Compile(json,
            new RuntimeSmokeMemoryDocumentSource(), RuntimeSmokeTarget.Editor, RuntimeSmokeMode.Fsd,
            new RuntimeSmokeCompileOptions { AvailableCapabilities = new[] { "visual.analysis.v1", "ath.start" } });

        [Test]
        public void VisualOperandRequiresCapabilityAndSafeCaptureIdentity()
        {
            Assert.That(Compile(Document()).Success, Is.True);
            Assert.That(Compile(Document("ath.start")).Success, Is.False);
            Assert.That(Compile(Document(label: "../escape")).Success, Is.False);
        }

        [Test]
        public async Task GenericHostCannotSpoofVisualPassWithoutAnalyzer()
        {
            var compiled = Compile(Document());
            Assert.That(compiled.Success, Is.True);
            var result = await RuntimeSmokeCoordinator.RunAsync(compiled.Workflow, "visual-run", new SpoofAdapter(), new Sink());
            Assert.That(result.TerminalState, Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
            Assert.That(result.Transcript.Count(r => r.type == "run_terminal"), Is.EqualTo(1));
        }

        [Test]
        public void VisualPolicyParticipatesInCanonicalWorkflowHash()
        {
            var first = Compile(Document());
            var second = Compile(Document().Replace(new string('a',64), new string('c',64)));
            Assert.That(first.Success && second.Success, Is.True);
            Assert.That(first.Workflow.CanonicalHash, Is.Not.EqualTo(second.Workflow.CanonicalHash));
        }

        [Test]
        public void MissingMeasurementsWrongElementsAndOmittedRulesCannotPass()
        {
            var manifest = RuntimeSmokeValueJsonParser.Parse("{\"resolution\":{\"width\":100,\"height\":100},\"elements\":[{\"id\":\"menu\",\"bounds\":{\"x\":1,\"y\":1,\"width\":40,\"height\":40}}]}");
            var policy = RuntimeSmokeValueJsonParser.Parse("{\"minHeightPx\":20,\"minContrastRatio\":4.5}");
            var finding = "{\"elementId\":\"menu\",\"ruleId\":\"text.height\",\"outcome\":\"pass\",\"bounds\":{\"x\":1,\"y\":1,\"width\":40,\"height\":40},\"measurementConfidence\":1,\"detectionConfidence\":1,\"measuredHeightPx\":25,\"contrastRatio\":21}";
            finding = finding.Replace("\"measurementConfidence\"", "\"expectedBounds\":{\"x\":1,\"y\":1,\"width\":40,\"height\":40},\"measurementConfidence\"");
            var all = finding + "," + finding.Replace("text.height","text.contrast") + "," + finding.Replace("text.height","text.clipping");
            var method = typeof(RuntimeSmokeVisualAnalyzer).GetMethod("ValidateFindings", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Action<string> verify = json => method.Invoke(null, new object[] { RuntimeSmokeValueJsonParser.Parse("{\"findings\":["+json+"]}"), manifest, policy });
            Assert.DoesNotThrow(() => verify(all));
            Assert.Throws<TargetInvocationException>(() => verify(finding));
            Assert.Throws<TargetInvocationException>(() => verify(all.Replace("menu","wrong")));
            Assert.Throws<TargetInvocationException>(() => verify(all.Replace("\"measuredHeightPx\":25","\"measuredHeightPx\":null")));
            Assert.Throws<TargetInvocationException>(() => verify(all.Replace("\"contrastRatio\":21","\"contrastRatio\":2")));
            Assert.Throws<TargetInvocationException>(() => verify("{\"outcome\":\"pass\"}"));
            var heightOnly=finding.Replace("\"contrastRatio\":21","\"contrastRatio\":null");
            Assert.DoesNotThrow(()=>verify(heightOnly+","+heightOnly.Replace("text.height","text.contrast").Replace("\"outcome\":\"pass\"","\"outcome\":\"inconclusive\"")+","+
                heightOnly.Replace("text.height","text.clipping")));
        }

        [Test]
        public async Task MissingAndTamperedCaptureReceiptsFailBeforeProcessLaunch()
        {
            foreach (var mode in new[] { "missing", "tampered", "wrong-correlation" })
            {
                var directory=Path.Combine(Path.GetTempPath(),"ath-visual-integrity-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(directory,"captures"));
                try
                {
                    var correlation="visual-integrity-"+mode+"-"+Guid.NewGuid().ToString("N");
                    var png=new byte[]{137,80,78,71,13,10,26,10,1};
                    var receipt=new RuntimeSmokeCaptureArtifactWriter(directory).WritePng("menu",png);
                    if(mode=="tampered") File.AppendAllText(Path.Combine(directory,"captures/menu.png"),"changed");
                    var compiled=Compile(Document());
                    var analyzer=new RuntimeSmokeVisualAnalyzer(Path.Combine(directory,"must-not-launch.exe"),directory,
                        new Dictionary<string,string>(),new Dictionary<string,string>(),
                        _=>mode=="missing"?null:new RuntimeSmokeVisualCapture(mode=="wrong-correlation"?"foreign":correlation,compiled.Workflow.Id,receipt));
                    var result=await RuntimeSmokeCoordinator.RunAsync(compiled.Workflow,correlation,new SpoofAdapter(),new Sink(),visualAnalyzer:analyzer);
                    Assert.That(result.TerminalState,Is.EqualTo(RuntimeSmokeRunTerminalState.Failed));
                    Assert.That(result.AssertionResult.FailureCode,Is.EqualTo(RuntimeSmokeAssertionFailureCode.PredicateError));
                    Assert.That(result.AssertionResult.Diagnostic,Does.Not.Contain("analyzer is missing"));
                    Assert.That(Directory.GetDirectories(directory,"visual-*").Length,Is.EqualTo(0));
                }
                finally { Directory.Delete(directory,true); }
            }
        }

        [Test]
        public void NativeAnalyzerOutcomesReachTheAuthoritativeCoordinator()
        {
            var executable = Environment.GetEnvironmentVariable("ATH_VISUAL_ANALYZER");
            if (string.IsNullOrEmpty(executable)) Assert.Ignore("Set ATH_VISUAL_ANALYZER to the built native executable to run integration.");
            var fixture = Environment.GetEnvironmentVariable("ATH_VISUAL_FIXTURE");
            var model = Environment.GetEnvironmentVariable("ATH_VISUAL_MODEL");
            if (string.IsNullOrEmpty(fixture) || string.IsNullOrEmpty(model)) Assert.Ignore("Set ATH_VISUAL_FIXTURE to representative-1080-light.png (with adjacent .json manifest) and ATH_VISUAL_MODEL to the verified PP-OCR model.");
            // Keep optional skip synchronous: Unity Test Framework 1.4.3's Task wrapper dereferences null on IgnoreException.
            Task.Run(() => RunNativeOutcomes(executable, fixture, model)).GetAwaiter().GetResult();
        }

        private static async Task RunNativeOutcomes(string executable, string fixture, string model)
        {
            // Native generated Arial fixture contains representative ascenders and descenders, with an adjacent manifest.
            var png = File.ReadAllBytes(fixture);
            var originalManifest = File.ReadAllText(Path.ChangeExtension(fixture,".json"));
            foreach (var expected in new[] { "pass", "fail", "inconclusive", "corrupt", "wrong-correlation", "missing-analyzer" })
            {
                var directory = Path.Combine(Path.GetTempPath(), "ath-visual-runtime-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(directory,"captures"));
                try
                {
                    var correlation="visual-native-"+expected+"-"+Guid.NewGuid().ToString("N");
                    var receipt = new RuntimeSmokeCaptureArtifactWriter(directory).WritePng("menu", expected == "corrupt" ? png.Take(24).ToArray() : png);
                    var policy = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"project\",\"version\":\"1\",\"minHeightPx\":" + (expected == "pass" ? "20" : "80") + ",\"minContrastRatio\":4.5}");
                    var manifest = Encoding.UTF8.GetBytes(expected=="inconclusive" ? System.Text.RegularExpressions.Regex.Replace(originalManifest,
                        "\"representativeGlyphs\"\\s*:\\s*true", "\"representativeGlyphs\":false") : originalManifest);
                    var policyPath = Path.Combine(directory,"source-policy.json"); var manifestPath = Path.Combine(directory,"source-manifest.json");
                    File.WriteAllBytes(policyPath,policy); File.WriteAllBytes(manifestPath,manifest);
                    var document = Document().Replace(new string('a',64), Digest(policy)).Replace(new string('b',64),Digest(manifest));
                    var compiled = Compile(document); Assert.That(compiled.Success, Is.True);
                    var analyzer = new RuntimeSmokeVisualAnalyzer(expected == "missing-analyzer" ? Path.Combine(directory,"absent.exe") : executable,directory,
                        new Dictionary<string,string>{{"project",policyPath}},new Dictionary<string,string>{{"menu",manifestPath}},
                        _ => new RuntimeSmokeVisualCapture(expected == "wrong-correlation" ? "wrong" : correlation,compiled.Workflow.Id,receipt),model);
                    var source = new RuntimeSmokeMemoryDocumentSource(); source.Add("workflows/visual",document);
                    var suite = RuntimeSmokeSuiteValidator.Validate("{\"schemaVersion\":1,\"id\":\"suites/visual\",\"kind\":\"suite\",\"entries\":[{\"id\":\"menu\",\"script\":\"workflows/visual\",\"target\":\"editor\",\"timeoutSeconds\":30,\"required\":true}]}",
                        source,RuntimeSmokeTarget.Editor,new RuntimeSmokeCompileOptions{AvailableCapabilities=new[]{"visual.analysis.v1"}});
                    Assert.That(suite.Success,Is.True);
                    var suiteResult=await RuntimeSmokeSuiteExecutor.RunAsync(suite,"visual-native-suite",(entry,step,context)=>new SpoofAdapter().ExecuteAsync(step,context),
                        correlationFactory: _=>correlation,visualAnalyzerFactory: (entry,runId)=>analyzer);
                    Assert.That(suiteResult.TerminalState,Is.EqualTo(expected=="pass"?RuntimeSmokeSuiteTerminalState.Passed:RuntimeSmokeSuiteTerminalState.Failed),
                        expected+": "+string.Join(";",suiteResult.Entries.Select(e=>e.Execution.AssertionResult?.Diagnostic??e.Execution.Diagnostic)));
                    var result = suiteResult.Entries.Single().Execution;
                    Assert.That(result.TerminalState, Is.EqualTo(expected == "pass" ? RuntimeSmokeRunTerminalState.Passed : RuntimeSmokeRunTerminalState.Failed), expected + ": " + result.AssertionResult?.Diagnostic);
                    Assert.That(result.Transcript.Count(r=>r.type=="run_terminal"),Is.EqualTo(1));
                    if (expected == "pass" || expected == "fail" || expected == "inconclusive")
                    {
                        Assert.That(result.AssertionResult.VisualEvidenceJson, Does.Contain("resultSha256"));
                        Assert.That(RuntimeSmokeJUnitProjection.Serialize(new[]{result}), Does.Contain("visualEvidenceJson"));
                        Assert.That(result.Transcript.Any(r=>!string.IsNullOrEmpty(r.visualEvidenceJson)),Is.True);
                        Assert.That(result.AssertionResult.ActualDisplay, Does.Contain(expected));
                    }
                }
                finally { Directory.Delete(directory,true); }
            }
        }

        private static string Digest(byte[] bytes) { using(var hash=SHA256.Create()) return string.Concat(hash.ComputeHash(bytes).Select(b=>b.ToString("x2"))); }

        private sealed class SpoofAdapter : IRuntimeSmokeStepAdapter
        {
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step, RuntimeSmokeStepContext context) =>
                Task.FromResult(new RuntimeSmokeStepResponse { CorrelationId = context.CorrelationId, Status = RuntimeSmokeStepResponseStatus.Passed,
                    Actual = RuntimeSmokeValue.From("pass") });
        }
        private sealed class Sink : IRuntimeSmokeTranscriptSink { public void Append(RuntimeSmokeTranscriptRecord record) { } }
    }
}
