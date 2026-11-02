using System;
using LlamaBrainLabs.Ath.Editor.McpSkills;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public class AthTraceWriterTests
    {
        static AthVisualVerificationTrace Visual() => new AthVisualVerificationTrace
        {
            CorrelationId = "run-1", WorkflowId = "ui.menu", StepId = "visual",
            PolicyId = "custom", PolicyVersion = "1", Outcome = "inconclusive", FindingsSummary = "1 uncertain",
            Artifacts = new[] { new AthVisualTraceArtifact { Path = "visual/result.json", Sha256 = new string('a', 64) } }
        };

        [Test]
        public void OptionalVisualPayloadPreservesEnvelopeAndLegacyShape()
        {
            var ev = new AthSmokeCompletedEvent { EventId = "event", Project = "project", Passed = false };
            var legacy = AthTraceWriter.BuildLine(in ev);
            Assert.That(legacy.Contains("visual_verification"), Is.False);
            ev.VisualVerifications = new[] { Visual() };
            var line = AthTraceWriter.BuildLine(in ev);
            Assert.That(line, Does.Contain("\"schema_version\":1"));
            Assert.That(line, Does.Contain("\"kind\":\"ath.smoke.completed\""));
            Assert.That(line, Does.Contain("\"visual_verification\":["));
            Assert.That(line, Does.Contain("\"outcome\":\"inconclusive\""));
            Assert.That(line, Does.Contain("\"correlation_id\":\"run-1\""));
        }

        [Test]
        public void VisualPayloadRejectsUnboundEvidenceAndUnsafeArtifacts()
        {
            var visual = Visual(); visual.Artifacts[0].Path = "../escape.json";
            var ev = new AthSmokeCompletedEvent { VisualVerifications = new[] { visual } };
            Assert.Throws<ArgumentException>(() => AthTraceWriter.BuildLine(in ev));
            visual = Visual(); visual.Artifacts[0].Sha256 = "bad";
            ev.VisualVerifications = new[] { visual };
            Assert.Throws<ArgumentException>(() => AthTraceWriter.BuildLine(in ev));
            visual = Visual(); visual.Outcome = "maybe"; ev.VisualVerifications = new[] { visual };
            Assert.Throws<ArgumentException>(() => AthTraceWriter.BuildLine(in ev));
        }
    }
}
