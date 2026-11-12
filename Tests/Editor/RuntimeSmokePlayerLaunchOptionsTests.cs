#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokePlayerLaunchOptionsTests
    {
        [Test]
        public void ParsesSeparatedAndEqualsArgumentsAsOneLaunchIdentity()
        {
            var ok = RuntimeSmokePlayerLaunchOptions.TryParse(new[]
            {
                "-runtimeSmoke", "workflows/baseline", "-runtimeSmokeCorrelation=run-1",
                "-runtimeSmokeArtifactRoot", @"C:\Game\Artifacts\run-1",
                "-runtimeSmokeNonce", new string('a', 64), "-runtimeSmokeBuildToken", new string('b', 64)
            }, out var options, out var failure);

            Assert.That(ok, Is.True, failure);
            Assert.That(options.WorkflowId, Is.EqualTo("workflows/baseline"));
            Assert.That(options.CorrelationId, Is.EqualTo("run-1"));
            Assert.That(options.ArtifactRoot, Is.EqualTo(@"C:\Game\Artifacts\run-1"));
            Assert.That(options.Nonce, Is.EqualTo(new string('a', 64)));
            Assert.That(options.BuildToken, Is.EqualTo(new string('b', 64)));
        }

        [Test]
        public void ParsesTypedContinuationStageAndProfileHandoff()
        {
            var args = new[]
            {
                "-runtimeSmoke", "workflows/reload", "-runtimeSmokeCorrelation", "run-2",
                "-runtimeSmokeArtifactRoot", @"C:\Game\Artifacts\run-2",
                "-runtimeSmokeNonce", new string('a', 64), "-runtimeSmokeBuildToken", new string('b', 64),
                "-runtimeSmokeContinuation", "continuation-1", "-runtimeSmokeContinuationStage", "reload",
                "-runtimeSmokeContinuationIndex", "1", "-runtimeSmokeContinuationCount", "2",
                "-runtimeSmokeContinuationAction", "load", "-runtimeSmokeContinuationProfile", "athrs_0123456789abcdef0123456789abcdef"
            };

            Assert.That(RuntimeSmokePlayerLaunchOptions.TryParse(args, out var options, out var failure), Is.True, failure);
            Assert.That(options.ContinuationId, Is.EqualTo("continuation-1"));
            Assert.That(options.ContinuationStageId, Is.EqualTo("reload"));
            Assert.That(options.ContinuationStageIndex, Is.EqualTo(1));
            Assert.That(options.ContinuationStageCount, Is.EqualTo(2));
            Assert.That(options.ContinuationProfileAction, Is.EqualTo("load"));
            Assert.That(options.ContinuationProfileId, Is.EqualTo("athrs_0123456789abcdef0123456789abcdef"));
        }

        [Test]
        public void RejectsPartialContinuationIdentity()
        {
            var args = new[]
            {
                "-runtimeSmoke", "workflows/reload", "-runtimeSmokeCorrelation", "run-2",
                "-runtimeSmokeArtifactRoot", @"C:\Game\Artifacts\run-2",
                "-runtimeSmokeNonce", new string('a', 64), "-runtimeSmokeBuildToken", new string('b', 64),
                "-runtimeSmokeContinuation", "continuation-1"
            };

            Assert.That(RuntimeSmokePlayerLaunchOptions.TryParse(args, out _, out var failure), Is.False);
            Assert.That(failure, Does.Contain("continuation"));
        }

        [TestCase(new string[] { "-runtimeSmoke", "workflows/test" }, "launch arguments are missing")]
        [TestCase(new string[] { "-runtimeSmoke", "workflows/test", "-runtimeSmoke=workflows/other" }, "launch argument is duplicated")]
        [TestCase(new string[] { "-runtimeSmoke", "../outside", "-runtimeSmokeCorrelation", "run-1", "-runtimeSmokeArtifactRoot", @"C:\Game\Artifacts\run-1", "-runtimeSmokeNonce", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "-runtimeSmokeBuildToken", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }, "workflow ID is invalid")]
        [TestCase(new string[] { "-runtimeSmoke", "workflows/test", "-runtimeSmokeCorrelation", "..", "-runtimeSmokeArtifactRoot", @"C:\Game\Artifacts\run-1", "-runtimeSmokeNonce", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "-runtimeSmokeBuildToken", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }, "correlation ID is invalid")]
        public void RejectsPartialDuplicateAndUnsafeIdentities(string[] args, string expectedFailure)
        {
            Assert.That(RuntimeSmokePlayerLaunchOptions.TryParse(args, out _, out var failure), Is.False);
            Assert.That(failure, Does.Contain(expectedFailure));
        }
    }
}
#endif
