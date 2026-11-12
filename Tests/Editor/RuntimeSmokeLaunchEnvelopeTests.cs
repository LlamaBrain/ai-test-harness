using System;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeLaunchEnvelopeTests
    {
        [Test]
        public void ValidProvenanceIsAcceptedAndWrongProcessIdentityIsRejected()
        {
            var envelope = Envelope();
            var observation = Observation();
            var result = RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3));
            Assert.That(result.State, Is.EqualTo(RuntimeSmokeLaunchWatchdogState.Running));
            Assert.That(result.FailureCode, Is.EqualTo(RuntimeSmokeLaunchFailureCode.None));

            observation.ProcessId++;
            result = RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3));
            Assert.That(result.State, Is.EqualTo(RuntimeSmokeLaunchWatchdogState.Failed));
            Assert.That(result.FailureCode, Is.EqualTo(RuntimeSmokeLaunchFailureCode.ProcessIdentityMismatch));
        }

        [Test]
        public void ReusedPidFromAnotherProcessStartIsNotClaimed()
        {
            var result = RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(Envelope(), new RuntimeSmokeProcessObservation
            {
                ProcessId = 42,
                ProcessStartUtc = T0.AddSeconds(-1),
                ExecutablePath = @"C:\Game\game.exe",
                IsRunning = true,
                LogPath = @"C:\Game\Artifacts\run\player.log",
                LogLength = 20,
                LogLastWriteUtc = T0.AddSeconds(1)
            }, T0.AddSeconds(3));
            Assert.That(result.FailureCode, Is.EqualTo(RuntimeSmokeLaunchFailureCode.ProcessIdentityMismatch));
        }

        [Test]
        public void WatchdogDoesNotPassUntilFreshTerminalEvidenceAndProcessExit()
        {
            var envelope = Envelope();
            var observation = Observation();
            observation.TerminalMarker = "[RuntimeSmoke] terminal";
            observation.IsRunning = true;
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3)).State,
                Is.EqualTo(RuntimeSmokeLaunchWatchdogState.Running));
            observation.IsRunning = false;
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3)).State,
                Is.EqualTo(RuntimeSmokeLaunchWatchdogState.Passed));
        }

        [Test]
        public void DifferentTerminalMarkerDoesNotCountAsSuccess()
        {
            var observation = Observation();
            observation.TerminalMarker = "[RuntimeSmoke] failed";
            observation.IsRunning = false;
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(Envelope(), observation, T0.AddSeconds(3)).FailureCode,
                Is.EqualTo(RuntimeSmokeLaunchFailureCode.ProcessExitedWithoutTerminal));
        }

        [Test]
        public void ExitWithoutTerminalEvidenceAndExpiredWatchdogFailClosed()
        {
            var envelope = Envelope();
            var observation = Observation();
            observation.IsRunning = false;
            var result = RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3));
            Assert.That(result.FailureCode, Is.EqualTo(RuntimeSmokeLaunchFailureCode.ProcessExitedWithoutTerminal));

            observation.IsRunning = true;
            result = RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(11));
            Assert.That(result.FailureCode, Is.EqualTo(RuntimeSmokeLaunchFailureCode.WatchdogTimeout));
        }

        [Test]
        public void StaleOrMislocatedLogCannotSatisfyProvenance()
        {
            var envelope = Envelope();
            var observation = Observation();
            observation.LogLastWriteUtc = T0.AddSeconds(-1);
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3)).FailureCode,
                Is.EqualTo(RuntimeSmokeLaunchFailureCode.LogNotFresh));
            observation = Observation();
            observation.LogPath = @"C:\Other\player.log";
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, T0.AddSeconds(3)).FailureCode,
                Is.EqualTo(RuntimeSmokeLaunchFailureCode.LogNotFresh));
        }

        [Test]
        public void ClaimLedgerPreventsRunOrCorrelationReuse()
        {
            var ledger = new RuntimeSmokeLaunchClaimLedger();
            Assert.That(ledger.Claim(Envelope(), Observation(), T0.AddSeconds(3)).Claimed, Is.True);
            var duplicate = ledger.Claim(Envelope(), Observation(), T0.AddSeconds(3));
            Assert.That(duplicate.Claimed, Is.False);
            Assert.That(duplicate.FailureCode, Is.EqualTo(RuntimeSmokeLaunchFailureCode.ProvenanceMismatch));
        }

        [Test]
        public void EnvelopeCannotEscapeHostOrUseRelativePaths()
        {
            var envelope = Envelope();
            envelope.LogPath = @"C:\Elsewhere\player.log";
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, Observation(), T0.AddSeconds(3)).FailureCode,
                Is.EqualTo(RuntimeSmokeLaunchFailureCode.EnvelopeInvalid));
            envelope = Envelope();
            envelope.HostRoot = "relative-host";
            Assert.That(RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, Observation(), T0.AddSeconds(3)).FailureCode,
                Is.EqualTo(RuntimeSmokeLaunchFailureCode.EnvelopeInvalid));
        }

        private static readonly DateTime T0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        private static RuntimeSmokeLaunchEnvelope Envelope() => new RuntimeSmokeLaunchEnvelope
        {
            RunId = "run-1", CorrelationId = "corr-1", WorkflowId = "workflows/smoke", ScriptHash = new string('a', 64),
            HostRoot = @"C:\Game", RunDirectory = @"C:\Game\Artifacts\run", ExecutablePath = @"C:\Game\game.exe",
            LogPath = @"C:\Game\Artifacts\run\player.log", ProcessId = 42, ProcessStartUtc = T0,
            StartedUtc = T0, TimeoutSeconds = 10, ExpectedTerminalMarker = "[RuntimeSmoke] terminal"
        };
        private static RuntimeSmokeProcessObservation Observation() => new RuntimeSmokeProcessObservation
        {
            ProcessId = 42, ProcessStartUtc = T0, ExecutablePath = @"C:\Game\game.exe", IsRunning = true,
            LogPath = @"C:\Game\Artifacts\run\player.log", LogLength = 100, LogLastWriteUtc = T0.AddSeconds(1)
        };
    }
}
