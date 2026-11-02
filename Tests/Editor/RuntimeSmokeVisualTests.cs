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
        [Test]
        public void TrustedVisualConfigurationRejectsAuthoredPathsAndUnsupportedSchemas()
        {
            Assert.Throws<ArgumentException>(() => RuntimeSmokeVisualConfiguration.Parse("{\"schemaVersion\":2}"));
            Assert.Throws<ArgumentException>(() => RuntimeSmokeVisualConfiguration.Parse("{\"schemaVersion\":1,\"executable\":\"relative.exe\",\"model\":null,\"policies\":{},\"manifests\":{}}"));
            Assert.That(RuntimeSmokeVisualConfiguration.FromEnvironment(_ => null), Is.Null);
        }

        [Test]
        public async Task NonvisualRunsIgnoreConfigurationAndExplicitFactoryTakesPrecedence()
        {
            var previous=Environment.GetEnvironmentVariable("ATH_VISUAL_CONFIG");
            try
            {
                Environment.SetEnvironmentVariable("ATH_VISUAL_CONFIG","invalid-relative-config.json");
                var plain=System.Text.RegularExpressions.Regex.Replace(Document(),",\\{\"id\":\"visual\".*?(?=,\\{\"id\":\"end\")","");
                var workflow=Compile(plain).Workflow;
                Assert.That(workflow,Is.Not.Null);
                var legacy=new RuntimeSmokeRunRegistry(new VisualCatalog(workflow),(_,__)=>new SpoofAdapter(),(_,__)=>new Sink());
                var correlation="visual-legacy-"+Guid.NewGuid().ToString("N");
                Assert.That(legacy.Start(workflow.Id,correlation,RuntimeSmokeTarget.Editor,RuntimeSmokeMode.Fsd).Accepted,Is.True);
                Assert.That((await legacy.WaitForCompletionAsync(correlation)).State,Is.EqualTo(RuntimeSmokeRegistryRunState.Passed));
                workflow=Compile(Document()).Workflow;
                var called=false;
                var custom=new RuntimeSmokeRunRegistry(new VisualCatalog(workflow),(_,__)=>new SpoofAdapter(),(_,__)=>new Sink(),
                    visualAnalyzerFactory:(_,__,___)=>{called=true;return null;});
                correlation="visual-custom-"+Guid.NewGuid().ToString("N");
                Assert.That(custom.Start(workflow.Id,correlation,RuntimeSmokeTarget.Editor,RuntimeSmokeMode.Fsd).Accepted,Is.True);
                Assert.That(called,Is.True);
                Assert.That((await custom.WaitForCompletionAsync(correlation)).State,Is.EqualTo(RuntimeSmokeRegistryRunState.Failed));
            }
            finally { Environment.SetEnvironmentVariable("ATH_VISUAL_CONFIG",previous); }
        }

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
            var adjustablePolicy=RuntimeSmokeValueJsonParser.Parse("{\"minHeightPx\":20,\"minContrastRatio\":4.5,\"scope\":\"adjustable-size-capability\"}");
            var adjustment="{\"ruleId\":\"text.adjustability\",\"elementId\":\"\",\"outcome\":\"inconclusive\",\"bounds\":null,\"measuredHeightPx\":null,\"contrastRatio\":null,\"detectionConfidence\":null,\"measurementConfidence\":null}";
            Action<string,string> verifyAdjustment=(outcome,extra)=>method.Invoke(null,new object[]{RuntimeSmokeValueJsonParser.Parse("{\"outcome\":\""+outcome+"\",\"findings\":["+all+extra+"]}"),manifest,adjustablePolicy});
            Assert.Throws<TargetInvocationException>(()=>verify(all+","+adjustment));
            Assert.DoesNotThrow(()=>verifyAdjustment("inconclusive",","+adjustment));
            Assert.Throws<TargetInvocationException>(()=>verifyAdjustment("pass",","+adjustment));
            Assert.Throws<TargetInvocationException>(()=>verifyAdjustment("inconclusive",""));
            Assert.Throws<TargetInvocationException>(()=>verifyAdjustment("inconclusive",","+adjustment+","+adjustment));
            var anonymous=RuntimeSmokeValueJsonParser.Parse("{\"resolution\":{\"width\":100,\"height\":100},\"elements\":[{\"bounds\":{\"x\":1,\"y\":1,\"width\":40,\"height\":40}}]}");
            Assert.DoesNotThrow(()=>method.Invoke(null,new object[]{RuntimeSmokeValueJsonParser.Parse("{\"findings\":["+all.Replace("menu","expected-0")+"]}"),anonymous,policy}));
            var boundless=RuntimeSmokeValueJsonParser.Parse("{\"resolution\":{\"width\":100,\"height\":100},\"elements\":[{}]}");
            var noBounds=all.Replace("menu","expected-0").Replace("{\"x\":1,\"y\":1,\"width\":40,\"height\":40}","null")
                .Replace("\"outcome\":\"pass\"","\"outcome\":\"inconclusive\"").Replace("\"measuredHeightPx\":25","\"measuredHeightPx\":null")
                .Replace("\"contrastRatio\":21","\"contrastRatio\":null").Replace("Confidence\":1","Confidence\":0");
            Assert.DoesNotThrow(()=>method.Invoke(null,new object[]{RuntimeSmokeValueJsonParser.Parse("{\"findings\":["+noBounds+"]}"),boundless,policy}));
            Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,new object[]{RuntimeSmokeValueJsonParser.Parse("{\"findings\":["+noBounds.Replace("inconclusive","pass")+"]}"),boundless,policy}));
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
            Task.Run(async () => { await RunNativeOutcomes(executable, fixture, model); await RunConfiguredRegistry(executable, fixture, model); }).GetAwaiter().GetResult();
        }

        private static async Task RunNativeOutcomes(string executable, string fixture, string model)
        {
            // Native generated Arial fixture contains representative ascenders and descenders, with an adjacent manifest.
            var png = File.ReadAllBytes(fixture);
            var originalManifest = File.ReadAllText(Path.ChangeExtension(fixture,".json"));
            foreach (var expected in new[] { "pass", "fail", "inconclusive", "corrupt", "wrong-correlation", "missing-analyzer", "adjustable", "unresolved-profile", "missing-source", "optional-id", "boundless" })
            {
                var directory = Path.Combine(Path.GetTempPath(), "ath-visual-runtime-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(directory,"captures"));
                try
                {
                    var correlation="visual-native-"+expected+"-"+Guid.NewGuid().ToString("N");
                    var receipt = new RuntimeSmokeCaptureArtifactWriter(directory).WritePng("menu", expected == "corrupt" ? png.Take(24).ToArray() : png);
                    var policy = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"project\",\"version\":\"1\",\"source\":\"fixture://runtime-smoke\",\"minHeightPx\":" + (expected == "pass" || expected == "adjustable" || expected == "unresolved-profile" || expected == "missing-source" || expected == "optional-id" || expected == "boundless" ? "20" : "80") + ",\"minContrastRatio\":4.5}");
                    if(expected=="adjustable") policy=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(policy).Replace("\"version\":\"1\"","\"version\":\"1\",\"scope\":\"adjustable-size-capability\""));
                    if(expected=="unresolved-profile") policy=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(policy).Replace("\"version\":\"1\"","\"version\":\"1\",\"referenceHeight\":1080,\"scaling\":\"linear-height\""));
                    if(expected=="missing-source") policy=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(policy).Replace("\"source\":\"fixture://runtime-smoke\",",""));
                    var selectedManifest=originalManifest;
                    if(expected=="optional-id") selectedManifest=System.Text.RegularExpressions.Regex.Replace(selectedManifest,"\"id\"\\s*:\\s*\"[^\"]+\"\\s*,?","");
                    if(expected=="boundless") selectedManifest=System.Text.RegularExpressions.Regex.Replace(selectedManifest,"\"bounds\"\\s*:\\s*\\{[^}]*\\}\\s*,?","");
                    var manifest = Encoding.UTF8.GetBytes(expected=="inconclusive" ? System.Text.RegularExpressions.Regex.Replace(originalManifest,
                        "\"representativeGlyphs\"\\s*:\\s*true", "\"representativeGlyphs\":false") : selectedManifest);
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
                    Assert.That(suiteResult.TerminalState,Is.EqualTo((expected=="pass"||expected=="optional-id")?RuntimeSmokeSuiteTerminalState.Passed:RuntimeSmokeSuiteTerminalState.Failed),
                        expected+": "+string.Join(";",suiteResult.Entries.Select(e=>e.Execution.AssertionResult?.Diagnostic??e.Execution.Diagnostic)));
                    var result = suiteResult.Entries.Single().Execution;
                    Assert.That(result.TerminalState, Is.EqualTo((expected == "pass" || expected=="optional-id") ? RuntimeSmokeRunTerminalState.Passed : RuntimeSmokeRunTerminalState.Failed), expected + ": " + result.AssertionResult?.Diagnostic);
                    Assert.That(result.Transcript.Count(r=>r.type=="run_terminal"),Is.EqualTo(1));
                    if (expected == "pass" || expected == "fail" || expected == "inconclusive" || expected == "adjustable" || expected=="optional-id" || expected=="boundless")
                    {
                        Assert.That(result.AssertionResult.VisualEvidenceJson, Does.Contain("resultSha256"));
                        Assert.That(RuntimeSmokeJUnitProjection.Serialize(new[]{result}), Does.Contain("visualEvidenceJson"));
                        Assert.That(result.Transcript.Any(r=>!string.IsNullOrEmpty(r.visualEvidenceJson)),Is.True);
                        Assert.That(result.AssertionResult.ActualDisplay, Does.Contain(expected=="adjustable"||expected=="boundless"?"inconclusive":expected=="optional-id"?"pass":expected));
                    }
                }
                finally { Directory.Delete(directory,true); }
            }
        }

        private static async Task RunConfiguredRegistry(string executable, string fixture, string model)
        {
            var directory=Path.Combine(Path.GetTempPath(),"ath-visual-configured-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory,"captures"));
            var previous=Environment.GetEnvironmentVariable("ATH_VISUAL_CONFIG");
            try
            {
                var policyPath=Path.Combine(directory,"policy.json"); var manifestPath=Path.Combine(directory,"manifest.json");
                File.WriteAllText(policyPath,"{\"schemaVersion\":1,\"id\":\"project\",\"version\":\"1\",\"source\":\"fixture://runtime-smoke\",\"minHeightPx\":20,\"minContrastRatio\":4.5}");
                File.Copy(Path.ChangeExtension(fixture,".json"),manifestPath);
                Func<string,string> quote=s=>RuntimeSmokeValue.From(Path.GetFullPath(s)).ToCanonicalJson();
                var configPath=Path.Combine(directory,"config.json");
                File.WriteAllText(configPath,"{\"schemaVersion\":1,\"executable\":"+quote(executable)+",\"model\":"+quote(model)+",\"policies\":{\"project\":"+quote(policyPath)+"},\"manifests\":{\"menu\":"+quote(manifestPath)+"}}");
                Environment.SetEnvironmentVariable("ATH_VISUAL_CONFIG",configPath);
                const string investigation="\"scenario\":\"menu\",\"seed\":\"fixed\",\"investigation\":{\"summary\":\"menu capture\",\"invariant\":\"legible menu\",\"fixtureSemantics\":\"fixed PNG\",\"setupRationale\":\"registry integration\",\"expectedProgress\":\"capture then assert\",\"expectedEvidence\":[\"result.json\"],\"benignShutdownNoise\":\"none\",\"failureDisposition\":\"diagnosis_only\",\"excludedSystems\":[\"save_load\"],\"classifications\":[{\"failureCode\":\"assertion_mismatch\",\"likelyOwner\":\"ui\",\"rationale\":\"text differs\"}],\"actions\":[{\"id\":\"capture\",\"kind\":\"named_capture\",\"capability\":\"investigation.capture\",\"captureLabel\":\"menu\"}]},";
                var document=Document().Replace(new string('a',64),Digest(File.ReadAllBytes(policyPath))).Replace(new string('b',64),Digest(File.ReadAllBytes(manifestPath)))
                    .Replace("\"mode\":\"fsd\"","\"mode\":\"hitl\"").Replace("\"requires\":[\"visual.analysis.v1\"]","\"requires\":[\"visual.analysis.v1\",\"investigation.capture\"]")
                    .Replace("\"steps\":[",investigation+"\"steps\":[").Replace("{\"id\":\"visual\"","{\"id\":\"review\",\"type\":\"checkpoint\"},{\"id\":\"visual\"");
                var compiled=RuntimeSmokeDocumentCompiler.Compile(document,new RuntimeSmokeMemoryDocumentSource(),RuntimeSmokeTarget.Editor,RuntimeSmokeMode.Hitl,
                    new RuntimeSmokeCompileOptions{AvailableCapabilities=new[]{"visual.analysis.v1","investigation.capture"}});
                Assert.That(compiled.Success,Is.True,string.Join(";",compiled.Issues.Select(i=>i.Message)));
                foreach(var mode in new[]{"no-config","missing-capture","pass"})
                {
                    var capture=mode=="pass";
                    Environment.SetEnvironmentVariable("ATH_VISUAL_CONFIG",mode=="no-config"?null:configPath);
                    var host=new RegistryVisualHost(directory,File.ReadAllBytes(fixture));
                    var registry=new RuntimeSmokeRunRegistry(new VisualCatalog(compiled.Workflow),(_,__)=>host,(_,__)=>host,investigationAdapterFactory:(_,__)=>host);
                    var correlation="visual-stock-"+Guid.NewGuid().ToString("N");
                    Assert.That(registry.Start(compiled.Workflow.Id,correlation,RuntimeSmokeTarget.Editor,RuntimeSmokeMode.Hitl,"menu","fixed").Accepted,Is.True);
                    for(var i=0;i<100 && registry.GetStatus(correlation).State!=RuntimeSmokeRegistryRunState.ReviewReady;i++) await Task.Delay(10);
                    Assert.That(registry.GetStatus(correlation).State,Is.EqualTo(RuntimeSmokeRegistryRunState.ReviewReady));
                    if(capture) Assert.That((await registry.CaptureAsync(correlation,"capture")).Accepted,Is.True);
                    Assert.That(registry.Resume(correlation,"review").Accepted,Is.True);
                    var completed=await registry.WaitForCompletionAsync(correlation);
                    Assert.That(completed.State,Is.EqualTo(capture?RuntimeSmokeRegistryRunState.Passed:RuntimeSmokeRegistryRunState.Failed),completed.Execution?.AssertionResult?.Diagnostic);
                }
            }
            finally {Environment.SetEnvironmentVariable("ATH_VISUAL_CONFIG",previous);Directory.Delete(directory,true);}
        }
        private sealed class VisualCatalog : IRuntimeSmokeWorkflowCatalog
        {
            private readonly RuntimeSmokeCompiledWorkflow _workflow;
            public VisualCatalog(RuntimeSmokeCompiledWorkflow workflow){_workflow=workflow;}
            public IReadOnlyList<RuntimeSmokeCompiledWorkflow> List(RuntimeSmokeTarget target,RuntimeSmokeMode mode)=>new[]{_workflow};
            public bool TryGet(string id,RuntimeSmokeTarget target,RuntimeSmokeMode mode,out RuntimeSmokeCompiledWorkflow workflow){workflow=_workflow;return id==_workflow.Id;}
        }
        private sealed class RegistryVisualHost : IRuntimeSmokeStepAdapter,IRuntimeSmokeReviewCheckpointAdapter,IRuntimeSmokeInvestigationAdapter,IRuntimeSmokeTranscriptSink,IRuntimeSmokeArtifactLocation
        {
            private readonly byte[] _png;
            public string RunDirectory{get;}
            public RegistryVisualHost(string directory,byte[] png){RunDirectory=directory;_png=png;}
            public void Append(RuntimeSmokeTranscriptRecord record) { File.AppendAllText(Path.Combine(RunDirectory,"registry.jsonl"),UnityEngine.JsonUtility.ToJson(record)+"\n"); }
            public void PauseForReview(RuntimeSmokeCompiledStep step,RuntimeSmokeStepContext context){}
            public void ResumeFromReview(RuntimeSmokeCompiledStep step,RuntimeSmokeStepContext context){}
            public Task<RuntimeSmokeStepResponse> ExecuteAsync(RuntimeSmokeCompiledStep step,RuntimeSmokeStepContext context)=>new SpoofAdapter().ExecuteAsync(step,context);
            public Task<RuntimeSmokeInvestigationResponse> ExecuteAsync(RuntimeSmokeInvestigationAction action,RuntimeSmokeInvestigationContext context)=>Task.FromResult(new RuntimeSmokeInvestigationResponse{
                Succeeded=true,Value=context.CreateCaptureWriter().WritePng(action.captureLabel,_png).ToRuntimeSmokeValue()});
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
