// AthMediaUtil — pure helpers for capture-produced media artifacts. Resolves
// the consuming project's .captain-sdlc/trace/media/ directory, sanitizes
// free-form labels into filename-safe tokens, validates and resolves
// trace-relative artifact paths (so a caller can never escape the trace dir),
// and reads a PNG's pixel dimensions from its IHDR header without decoding the
// image.
//
// Everything here is pure System.IO/Text and derives every path from the
// projectRoot argument, so it is unit-testable without an MCP attachment or a
// live Editor — mirroring the AthTraceEmitter / AthTraceWriter split. The media
// dir lives under the trace dir, so it inherits the trace .gitignore.

#nullable enable

using System.IO;
using System.Text;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    internal static class AthMediaUtil
    {
        public const string MediaDirName = "media";

        /// <summary>The media subdir of the trace dir: <c>&lt;root&gt;/.captain-sdlc/trace/media/</c>.</summary>
        public static string MediaDir(string projectRoot) =>
            Path.Combine(AthTraceEmitter.TraceDir(projectRoot), MediaDirName);

        /// <summary>
        /// Create the media directory (and the trace .gitignore, reused from the
        /// emitter so media files are never committed). Returns the media dir.
        /// </summary>
        public static string EnsureMediaDir(string projectRoot)
        {
            var dir = MediaDir(projectRoot);
            Directory.CreateDirectory(dir);
            AthTraceEmitter.EnsureGitignore(projectRoot);
            return dir;
        }

        /// <summary>
        /// Reduce a free-form label to a filename-safe token: ASCII letters,
        /// digits, '_' and '-' are kept; any run of other characters collapses
        /// to a single '-'; the result is trimmed of leading/trailing '-' and
        /// capped at 40 chars. Null/blank → "".
        /// </summary>
        public static string SanitizeLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";

            var sb = new StringBuilder(label!.Length);
            foreach (var ch in label)
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                    (ch >= '0' && ch <= '9') || ch == '_' || ch == '-')
                    sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                    sb.Append('-');
            }

            var outp = sb.ToString().Trim('-');
            if (outp.Length > 40) outp = outp.Substring(0, 40).Trim('-');
            return outp;
        }

        /// <summary>The canonical artifact string for a media file: forward-slash, trace-relative.</summary>
        public static string TraceRelativeMediaPath(string fileName) =>
            MediaDirName + "/" + fileName;

        /// <summary>
        /// True only for a safe trace-relative artifact path: non-empty, no
        /// backslash, no drive/scheme (':'), not rooted/absolute, and every
        /// segment is a real name (no empty, '.' or '..' segment). Accepts
        /// "media/foo.png" and safe bare names like "foo.png".
        /// </summary>
        public static bool IsSafeTraceRelativeArtifact(string? rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return false;
            if (rel!.IndexOf('\\') >= 0) return false;   // backslash (Windows sep / escape)
            if (rel.IndexOf(':') >= 0)  return false;    // drive letter or URI scheme
            if (rel[0] == '/')          return false;    // POSIX-rooted
            if (Path.IsPathRooted(rel)) return false;    // absolute on any platform

            foreach (var seg in rel.Split('/'))
            {
                if (seg.Length == 0)            return false;   // leading/trailing slash or "//"
                if (seg == "." || seg == "..") return false;   // traversal / no-op
            }
            return true;
        }

        /// <summary>
        /// Resolve a safe trace-relative artifact to an absolute path under the
        /// trace dir. Returns null when the input is not a safe trace-relative
        /// path (absolute/rooted/backslash/traversal), so callers never resolve
        /// outside the trace dir. "media/foo.png" → <c>&lt;trace&gt;/media/foo.png</c>;
        /// a safe bare "foo.png" → <c>&lt;trace&gt;/foo.png</c>.
        /// </summary>
        public static string? ResolveTraceRelative(string projectRoot, string? rel)
        {
            if (!IsSafeTraceRelativeArtifact(rel)) return null;

            var native   = rel!.Replace('/', Path.DirectorySeparatorChar);
            var combined = Path.Combine(AthTraceEmitter.TraceDir(projectRoot), native);
            return Path.GetFullPath(combined);
        }

        /// <summary>
        /// Read a PNG's pixel dimensions from its IHDR header without decoding
        /// the image. Returns true with width/height and status="" on success.
        /// A file that is missing, locked by the writer, or not yet fully
        /// written yields status="pending" and false (the expected case while a
        /// CaptureScreenshot finishes flushing); a present-but-malformed file
        /// yields status="io_error:not_png".
        /// </summary>
        public static bool TryReadPngSize(string absPath, out int width, out int height, out string status)
        {
            width = 0; height = 0; status = "";
            try
            {
                using (var fs = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var head = new byte[24];
                    int read = 0;
                    while (read < head.Length)
                    {
                        int n = fs.Read(head, read, head.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < 24) { status = "pending"; return false; }   // still being written

                    // 8-byte PNG signature.
                    if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47 ||
                        head[4] != 0x0D || head[5] != 0x0A || head[6] != 0x1A || head[7] != 0x0A)
                    { status = "io_error:not_png"; return false; }

                    // First chunk type (bytes 12..15) must be "IHDR".
                    if (head[12] != (byte)'I' || head[13] != (byte)'H' ||
                        head[14] != (byte)'D' || head[15] != (byte)'R')
                    { status = "io_error:not_png"; return false; }

                    // IHDR width/height are big-endian uint32 at offsets 16 and 20.
                    width  = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                    height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                    if (width <= 0 || height <= 0) { status = "pending"; return false; }
                    return true;
                }
            }
            catch (FileNotFoundException)       { status = "pending"; return false; }
            catch (DirectoryNotFoundException)  { status = "pending"; return false; }
            catch (IOException)                 { status = "pending"; return false; }   // sharing violation / partial write
            catch (System.UnauthorizedAccessException) { status = "pending"; return false; }
        }
    }
}
