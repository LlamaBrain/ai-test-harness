#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>A safe, run-relative receipt for a screenshot or other captured evidence.</summary>
    public sealed class RuntimeSmokeCaptureArtifactReceipt
    {
        public string RelativePath { get; }
        public long ByteLength { get; }
        public string Sha256 { get; }

        internal RuntimeSmokeCaptureArtifactReceipt(string relativePath, long byteLength, string sha256)
        {
            RelativePath = relativePath;
            ByteLength = byteLength;
            Sha256 = sha256;
        }

        public RuntimeSmokeValue ToRuntimeSmokeValue() => RuntimeSmokeValue.FromMap(new[]
        {
            new KeyValuePair<string, RuntimeSmokeValue>("artifact", RuntimeSmokeValue.From(RelativePath)),
            new KeyValuePair<string, RuntimeSmokeValue>("bytes", RuntimeSmokeValue.From(ByteLength)),
            new KeyValuePair<string, RuntimeSmokeValue>("sha256", RuntimeSmokeValue.From(Sha256))
        });
    }

    /// <summary>
    /// Writes named PNG evidence only into the claimed run's pre-created captures directory.
    /// It never creates output roots, follows reparse points, or replaces existing evidence.
    /// </summary>
    public sealed class RuntimeSmokeCaptureArtifactWriter
    {
        public const int MaximumPngBytes = 32 * 1024 * 1024;
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private readonly string _runDirectory;
        private readonly string _captureDirectory;

        public RuntimeSmokeCaptureArtifactWriter(string runDirectory)
        {
            if (string.IsNullOrWhiteSpace(runDirectory)) throw new ArgumentException("RuntimeSmoke run directory is required.", nameof(runDirectory));
            _runDirectory = Path.GetFullPath(runDirectory);
            if (!Directory.Exists(_runDirectory)) throw new DirectoryNotFoundException("RuntimeSmoke run directory must already exist.");
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(_runDirectory);
            _captureDirectory = Path.GetFullPath(Path.Combine(_runDirectory, "captures"));
            if (!IsWithin(_captureDirectory, _runDirectory) || PathEquals(_captureDirectory, _runDirectory))
                throw new InvalidOperationException("RuntimeSmoke captures directory escaped its run directory.");
            if (!Directory.Exists(_captureDirectory)) throw new DirectoryNotFoundException("RuntimeSmoke captures directory must be pre-created by the run owner.");
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(_captureDirectory);
        }

        public RuntimeSmokeCaptureArtifactReceipt WritePng(string label, byte[] pngBytes)
        {
            if (!IsSafeLabel(label)) throw new ArgumentException("Capture label must be a bounded filename token.", nameof(label));
            if (pngBytes == null || pngBytes.Length < PngSignature.Length || pngBytes.Length > MaximumPngBytes)
                throw new ArgumentException("PNG evidence must be present and within the bounded capture size.", nameof(pngBytes));
            for (var index = 0; index < PngSignature.Length; index++)
                if (pngBytes[index] != PngSignature[index]) throw new ArgumentException("Capture data is not a PNG image.", nameof(pngBytes));

            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(_runDirectory);
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(_captureDirectory);
            var fullPath = Path.GetFullPath(Path.Combine(_captureDirectory, label + ".png"));
            if (!IsWithin(fullPath, _captureDirectory) || PathEquals(fullPath, _captureDirectory))
                throw new InvalidOperationException("Capture artifact path escaped the run-local captures directory.");

            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.Write(pngBytes, 0, pngBytes.Length);
                stream.Flush(true);
            }
            RuntimeSmokeJsonlTranscriptSink.EnsureNoReparsePoints(fullPath);
            return new RuntimeSmokeCaptureArtifactReceipt("captures/" + label + ".png", pngBytes.LongLength, Sha256(pngBytes));
        }

        private static bool IsSafeLabel(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 &&
            value != "." && value != ".." && value[0] != '.' && value[value.Length - 1] != '.' &&
            !value.Contains("..") && value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }

        private static bool IsWithin(string path, string root)
        {
            var normalizedPath = TrimEnd(Path.GetFullPath(path));
            var normalizedRoot = TrimEnd(Path.GetFullPath(root));
            return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison) ||
                (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar && normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, PathComparison));
        }

        private static bool PathEquals(string left, string right) => string.Equals(TrimEnd(Path.GetFullPath(left)), TrimEnd(Path.GetFullPath(right)), PathComparison);
        private static string TrimEnd(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
#endif
