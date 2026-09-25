#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeDurableLaunchClaimTests
    {
        [Test]
        public void CurrentProcessIdentityIncludesExecutablePathHashAndUtcStart()
        {
            var identity = RuntimeSmokePlayerProcessIdentity.CaptureCurrent();

            Assert.That(identity.ProcessId, Is.GreaterThan(0));
            Assert.That(identity.ProcessStartUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(Path.IsPathRooted(identity.ExecutablePath), Is.True);
            Assert.That(identity.ExecutableSha256, Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void PlayerLaunchClaimRunsBeforeAnySceneAwakeMutation()
        {
            var bootstrap = typeof(RuntimeSmokeDurableLaunchClaim).Assembly.GetType(
                "LlamaBrainLabs.Ath.RuntimeSmokes.RuntimeSmokePlayerBootstrap", throwOnError: true);
            var initialize = bootstrap.GetMethod("Initialize", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(initialize, Is.Not.Null);
            var attribute = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(
                initialize, typeof(RuntimeInitializeOnLoadMethodAttribute));
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.loadType, Is.EqualTo(RuntimeInitializeLoadType.BeforeSceneLoad));
        }

        [Test]
        public void BuildTokenCanonicalizationMatchesTheExeClientGoldenVector()
        {
            var launch = new RuntimeSmokeLaunchDocument
            {
                artifactRoot = "C:/runs/r1", backend = "IL2CPP", buildEvidenceHash = new string('1', 64),
                buildEvidenceSnapshotPath = "C:/runs/r1/build-evidence.json", buildRevision = "rev-1",
                buildVersion = "1.2.3", contentHash = new string('2', 64), correlationId = "corr-1",
                executablePath = "C:/build/game.exe", executableSha256 = new string('3', 64), hostRoot = "C:/runs",
                authoredRoot = "C:/runs/Assets/RuntimeSmokes", outputRoot = "C:/runs/output",
                logPath = "C:/runs/r1/logs/player.log", mode = "fsd", nonce = new string('4', 64),
                profileHash = new string('5', 64), requiredStepIds = new[] { "ready", "finish" },
                runDirectory = "C:/runs/r1", runId = "r1", scenario = "scenario-a", schemaVersion = 1,
                scriptHash = new string('6', 64), seed = "42", startedUtc = "2026-09-23T18:00:00.000Z",
                state = "awaiting_player", stripping = "High", suiteId = "suite-a", target = "development",
                timeoutSeconds = 90, workflowId = "workflows/test"
            };
            Assert.That(RuntimeSmokeDurableLaunchClaim.ComputeBuildToken(launch),
                Is.EqualTo("d0c037f9baf8fdcff2d6d509f5f26f565c5d5b329ac9e1badd2013e0eccf8450"));
        }

        [Test]
        public void ClaimBindsLaunchIdentityToActualImageAndCannotBeReused()
        {
            WithFixture((root, options, identity, document) =>
            {
                WriteLaunchAndArmedPrelude(root, document);
                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(options, identity, Utc(3), out var armed, out var failure), Is.True, failure);
                Assert.That(armed.processId, Is.EqualTo(identity.ProcessId));
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-claim.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-armed.json")), Is.True);
                var transcript = File.ReadAllLines(Path.Combine(root, "transcript.jsonl"));
                Assert.That(transcript, Has.Length.EqualTo(1));
                var prelude = JsonUtility.FromJson<RuntimeSmokeTranscriptRecord>(transcript[0]);
                Assert.That(prelude.sequence, Is.EqualTo(0));
                Assert.That(prelude.type, Is.EqualTo("armed"));
                Assert.That(prelude.correlationId, Is.EqualTo(document.correlationId));

                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(options, identity, Utc(4), out _, out failure), Is.False);
                Assert.That(failure, Does.Contain("already claimed"));
            });
        }

        [Test]
        public void ClaimRejectsChangedNonceOrImageBeforeCreatingAnyClaimFile()
        {
            WithFixture((root, options, identity, document) =>
            {
                WriteLaunchAndArmedPrelude(root, document);
                var changed = ParseOptions(options.WorkflowId, options.CorrelationId, options.ArtifactRoot, new string('c', 64), options.BuildToken);
                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(changed, identity, Utc(3), out _, out var failure), Is.False);
                Assert.That(failure, Does.Contain("provenance"));
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-claim.json")), Is.False);

                var wrongImage = new RuntimeSmokePlayerProcessIdentity(identity.ProcessId, identity.ProcessStartUtc,
                    identity.ExecutablePath, new string('f', 64));
                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(options, wrongImage, Utc(3), out _, out failure), Is.False);
                Assert.That(failure, Does.Contain("provenance"));
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-claim.json")), Is.False);
            });
        }

        [Test]
        public void ClaimRejectsMissingRunnerArmedPreludeBeforeCreatingPlayerClaim()
        {
            WithFixture((root, options, identity, document) =>
            {
                File.WriteAllText(Path.Combine(root, "runtime-smoke-launch.json"), SerializeExternalLaunch(document));
                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(options, identity, Utc(3), out _, out var failure), Is.False);
                Assert.That(failure, Does.Contain("runner-armed prelude"));
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-claim.json")), Is.False);
            });
        }

        [Test]
        public void ClaimRejectsEnvelopeOrBuildEvidenceTamperingBeforeRuntimeStarts()
        {
            WithFixture((root, options, identity, document) =>
            {
                document.scriptHash = new string('4', 64);
                WriteLaunchAndArmedPrelude(root, document);
                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(options, identity, Utc(3), out _, out var failure), Is.False);
                Assert.That(failure, Does.Contain("provenance"));
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-claim.json")), Is.False);
            });

            WithFixture((root, options, identity, document) =>
            {
                WriteLaunchAndArmedPrelude(root, document);
                var evidencePath = Path.Combine(root, "build-evidence.json");
                var changed = JsonUtility.FromJson<RuntimeSmokeBuildEvidenceDocument>(File.ReadAllText(evidencePath));
                changed.revision = "tampered-revision";
                File.WriteAllText(evidencePath, JsonUtility.ToJson(changed));
                Assert.That(RuntimeSmokeDurableLaunchClaim.TryClaim(options, identity, Utc(3), out _, out var failure), Is.False);
                Assert.That(failure, Does.Contain("provenance"));
                Assert.That(File.Exists(Path.Combine(root, "runtime-smoke-claim.json")), Is.False);
            });
        }

        private static DateTime Utc(int seconds) => new DateTime(2026, 9, 23, 18, 0, seconds, DateTimeKind.Utc);

        private static void WithFixture(Action<string, RuntimeSmokePlayerLaunchOptions, RuntimeSmokePlayerProcessIdentity,
            RuntimeSmokeLaunchDocument> run)
        {
            var parent = Path.Combine(Path.GetTempPath(), "ath-launch-claim-" + Guid.NewGuid().ToString("N"));
            var root = Path.Combine(parent, "Artifacts", "claim-run");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            Directory.CreateDirectory(Path.Combine(parent, "Assets", "RuntimeSmokes"));
            try
            {
                var executable = Path.Combine(parent, "Builds", "game.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executable));
                File.WriteAllText(executable, "fixture-image", new UTF8Encoding(false));
                string executableHash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(executable))
                    executableHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                var nonce = new string('a', 64);
                var identity = new RuntimeSmokePlayerProcessIdentity(44, Utc(2), executable, executableHash);
                var buildEvidence = new RuntimeSmokeBuildEvidenceDocument
                {
                    schemaVersion = 1, artifactPath = executable, artifactSha256 = executableHash, revision = "rev-1",
                    version = "1.2.3", profileHash = new string('2', 64), contentHash = new string('3', 64),
                    backend = "IL2CPP", stripping = "High", lane = "development"
                };
                File.WriteAllText(Path.Combine(root, "build-evidence.json"), JsonUtility.ToJson(buildEvidence));
                var document = new RuntimeSmokeLaunchDocument
                {
                    schemaVersion = 1, state = "awaiting_player", runId = "claim-run", correlationId = "claim-run",
                    workflowId = "workflows/baseline", scriptHash = new string('1', 64), hostRoot = parent,
                    outputRoot = Path.Combine(parent, "Artifacts"), authoredRoot = Path.Combine(parent, "Assets", "RuntimeSmokes"),
                    runDirectory = root, artifactRoot = root,
                    executablePath = executable, executableSha256 = executableHash, logPath = Path.Combine(root, "logs", "player.log"),
                    buildEvidenceSnapshotPath = Path.Combine(root, "build-evidence.json"),
                    startedUtc = Utc(1).ToString("o"), timeoutSeconds = 60, target = "development", mode = "fsd",
                    buildRevision = "rev-1", buildVersion = "1.2.3", profileHash = new string('2', 64), contentHash = new string('3', 64),
                    backend = "IL2CPP", stripping = "High", buildEvidenceHash = RuntimeSmokeDurableLaunchClaim.ComputeBuildEvidenceHash(buildEvidence),
                    nonce = nonce, requiredStepIds = new[] { "ready" }
                };
                document.buildToken = RuntimeSmokeDurableLaunchClaim.ComputeBuildToken(document);
                var options = ParseOptions("workflows/baseline", "claim-run", root, nonce, document.buildToken);
                document.buildToken = RuntimeSmokeDurableLaunchClaim.ComputeBuildToken(document);
                run(root, options, identity, document);
            }
            finally { Directory.Delete(parent, true); }
        }

        private static RuntimeSmokePlayerLaunchOptions ParseOptions(string workflow, string correlation, string root, string nonce, string token)
        {
            Assert.That(RuntimeSmokePlayerLaunchOptions.TryParse(new[] { "-runtimeSmoke", workflow, "-runtimeSmokeCorrelation", correlation,
                "-runtimeSmokeArtifactRoot", root, "-runtimeSmokeNonce", nonce, "-runtimeSmokeBuildToken", token }, out var options, out var failure), Is.True, failure);
            return options;
        }

        private static string SerializeExternalLaunch(RuntimeSmokeLaunchDocument document)
        {
            var json = JsonUtility.ToJson(document);
            foreach (var nullableField in new[] { "suiteId", "scenario", "seed", "processStartUtc" })
                json = json.Replace("\"" + nullableField + "\":\"\"", "\"" + nullableField + "\":null");
            return json;
        }

        private static void WriteLaunchAndArmedPrelude(string root, RuntimeSmokeLaunchDocument document)
        {
            File.WriteAllText(Path.Combine(root, "runtime-smoke-launch.json"), SerializeExternalLaunch(document));
            var prelude = new RuntimeSmokeTranscriptRecord
            {
                schemaVersion = 1,
                sequence = 0,
                type = "armed",
                timestampUtc = document.startedUtc,
                workflowId = document.workflowId,
                scriptHash = document.scriptHash,
                correlationId = document.correlationId,
                scenario = document.scenario,
                seed = document.seed,
                status = "armed"
            };
            File.WriteAllText(Path.Combine(root, "transcript.jsonl"), JsonUtility.ToJson(prelude) + "\n");
        }
    }
}
#endif
