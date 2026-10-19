// EditMode unit tests for the pure media helpers (no Editor/PlayMode coupling).
// AthMediaUtil + AthTraceEmitter are internal; this assembly sees them via
// [assembly: InternalsVisibleTo("LlamaBrainLabs.Ath.Editor.Tests")] in
// Editor/AssemblyInfo.cs. Constraint-model asserts (Assert.That / Is.* / Does.*)
// compile under both Unity's NUnit 3.5 and standalone NUnit 4.x.

using System;
using System.IO;
using LlamaBrainLabs.Ath.Editor.McpSkills;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    [TestFixture]
    public class AthMediaUtilTests
    {
        [Test]
        public void SanitizeLabel_KeepsSafeChars_CollapsesOthers()
        {
            Assert.That(AthMediaUtil.SanitizeLabel(null), Is.EqualTo(""));
            Assert.That(AthMediaUtil.SanitizeLabel("   "), Is.EqualTo(""));
            Assert.That(AthMediaUtil.SanitizeLabel("abc_123-x"), Is.EqualTo("abc_123-x"));
            Assert.That(AthMediaUtil.SanitizeLabel("a b/c"), Is.EqualTo("a-b-c"));
            Assert.That(AthMediaUtil.SanitizeLabel("a   b"), Is.EqualTo("a-b"));   // run collapses to one '-'
            Assert.That(AthMediaUtil.SanitizeLabel("  hello!!  "), Is.EqualTo("hello"));
            Assert.That(AthMediaUtil.SanitizeLabel(new string('x', 80)).Length, Is.LessThanOrEqualTo(40));
        }

        [TestCase("media/foo.png", true)]
        [TestCase("foo.png", true)]
        [TestCase("a/b/c.png", true)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase("/abs/foo.png", false)]
        [TestCase("..\\foo.png", false)]
        [TestCase("a\\b.png", false)]
        [TestCase("../foo.png", false)]
        [TestCase("a/../b.png", false)]
        [TestCase("a//b.png", false)]
        [TestCase("./foo.png", false)]
        [TestCase("C:/foo.png", false)]
        [TestCase("C:\\foo.png", false)]
        public void IsSafeTraceRelativeArtifact_Cases(string rel, bool expected)
        {
            Assert.That(AthMediaUtil.IsSafeTraceRelativeArtifact(rel), Is.EqualTo(expected));
        }

        [Test]
        public void ResolveTraceRelative_ResolvesUnderTraceDir_AndRejectsUnsafe()
        {
            var root     = Path.Combine(Path.GetTempPath(), "ath-mt-" + Guid.NewGuid().ToString("N"));
            var traceDir = AthTraceEmitter.TraceDir(root);

            Assert.That(
                AthMediaUtil.ResolveTraceRelative(root, "media/foo.png"),
                Is.EqualTo(Path.GetFullPath(Path.Combine(traceDir, "media", "foo.png"))));

            Assert.That(
                AthMediaUtil.ResolveTraceRelative(root, "foo.png"),
                Is.EqualTo(Path.GetFullPath(Path.Combine(traceDir, "foo.png"))));

            Assert.That(AthMediaUtil.ResolveTraceRelative(root, "../escape.png"), Is.Null);
            Assert.That(AthMediaUtil.ResolveTraceRelative(root, "/abs.png"), Is.Null);
            Assert.That(AthMediaUtil.ResolveTraceRelative(root, "a\\b.png"), Is.Null);
        }

        [Test]
        public void TryReadPngSize_ValidHeader_ReturnsDims()
        {
            var path = TempPath(".png");
            File.WriteAllBytes(path, MakePngHeader(1280, 720));
            try
            {
                var ok = AthMediaUtil.TryReadPngSize(path, out var w, out var h, out var status);
                Assert.That(ok, Is.True);
                Assert.That(w, Is.EqualTo(1280));
                Assert.That(h, Is.EqualTo(720));
                Assert.That(status, Is.EqualTo(""));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void TryReadPngSize_Missing_IsPending()
        {
            var ok = AthMediaUtil.TryReadPngSize(TempPath(".png"), out _, out _, out var status);
            Assert.That(ok, Is.False);
            Assert.That(status, Is.EqualTo("pending"));
        }

        [Test]
        public void TryReadPngSize_Truncated_IsPending()
        {
            var path = TempPath(".png");
            File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });   // 4 of 24 bytes
            try
            {
                var ok = AthMediaUtil.TryReadPngSize(path, out _, out _, out var status);
                Assert.That(ok, Is.False);
                Assert.That(status, Is.EqualTo("pending"));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void TryReadPngSize_NotPng_IsIoError()
        {
            var path = TempPath(".png");
            File.WriteAllBytes(path, new byte[24]);   // right length, wrong signature
            try
            {
                var ok = AthMediaUtil.TryReadPngSize(path, out _, out _, out var status);
                Assert.That(ok, Is.False);
                Assert.That(status, Is.EqualTo("io_error:not_png"));
            }
            finally { File.Delete(path); }
        }

        static string TempPath(string ext) =>
            Path.Combine(Path.GetTempPath(), "ath-" + Guid.NewGuid().ToString("N") + ext);

        static byte[] MakePngHeader(int width, int height)
        {
            var b = new byte[24];
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Array.Copy(sig, b, 8);
            b[8] = 0; b[9] = 0; b[10] = 0; b[11] = 13;     // IHDR chunk length
            b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
            b[16] = (byte)(width  >> 24); b[17] = (byte)(width  >> 16); b[18] = (byte)(width  >> 8); b[19] = (byte)width;
            b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
            return b;
        }
    }
}
