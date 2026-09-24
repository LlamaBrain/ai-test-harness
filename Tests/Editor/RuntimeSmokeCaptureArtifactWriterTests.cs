using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public sealed class RuntimeSmokeCaptureArtifactWriterTests
    {
        [Test]
        public void NamedPngArtifactsStayUnderCapturesAndNeverOverwriteExistingEvidence()
        {
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-capture-" + Guid.NewGuid().ToString("N"));
            var captures = Path.Combine(root, "captures");
            Directory.CreateDirectory(captures);
            var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
            try
            {
                var writer = new RuntimeSmokeCaptureArtifactWriter(root);
                var receipt = writer.WritePng("review-roof", png);
                var destination = Path.Combine(captures, "review-roof.png");

                Assert.That(receipt.RelativePath, Is.EqualTo("captures/review-roof.png"));
                Assert.That(receipt.ByteLength, Is.EqualTo(png.Length));
                Assert.That(receipt.Sha256, Is.EqualTo(Hash(png)));
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(png));
                Assert.That(receipt.ToRuntimeSmokeValue().Map["artifact"].String, Is.EqualTo(receipt.RelativePath));
                Assert.That(receipt.ToRuntimeSmokeValue().Map["sha256"].String, Is.EqualTo(receipt.Sha256));

                Assert.Throws<IOException>(() => writer.WritePng("review-roof", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 9 }));
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(png), "A capture collision must not replace prior evidence.");
                Assert.Throws<ArgumentException>(() => writer.WritePng("../outside", png));
                Assert.That(File.Exists(Path.Combine(root, "outside.png")), Is.False);
                Assert.That(Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Single(), Is.EqualTo(destination));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void CaptureWriterRejectsMissingRunCaptureDirectoryAndInvalidPng()
        {
            var root = Path.Combine(Path.GetTempPath(), "ath-runtime-smoke-capture-invalid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Assert.Throws<DirectoryNotFoundException>(() => new RuntimeSmokeCaptureArtifactWriter(root));
                Directory.CreateDirectory(Path.Combine(root, "captures"));
                var writer = new RuntimeSmokeCaptureArtifactWriter(root);
                Assert.Throws<ArgumentException>(() => writer.WritePng("empty", Array.Empty<byte>()));
                Assert.Throws<ArgumentException>(() => writer.WritePng("bad-signature", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
                Assert.That(Directory.GetFiles(Path.Combine(root, "captures")), Is.Empty);
            }
            finally { Directory.Delete(root, true); }
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }
    }
}
