// AthTraceEmitter — IO half of the trace emitter. Resolves the consuming
// project's .captain-sdlc/trace/ directory, lazily protects it with a
// .gitignore (trace/ and side-store/ are always-local state per
// captain-sdlc-conventions.md in the captain-sdlc repo), and appends one JSON line to
// the current UTC day's file.
//
// Every path is derived from the projectRoot argument so a test can point it
// at a temp directory; the only Unity coupling is the caller passing
// Application.dataPath into ResolveProjectRoot. The JSON line is built by
// AthTraceWriter; this file never touches the envelope shape.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    internal static class AthTraceEmitter
    {
        public const string StateDirName = ".captain-sdlc";
        public const string TraceDirName = "trace";

        /// <summary>
        /// The consuming Unity project root — the parent of
        /// <c>Application.dataPath</c> (which is <c>&lt;root&gt;/Assets</c>),
        /// NOT the ATH package directory. Traces belong to the project under
        /// test, so they land here.
        /// </summary>
        public static string ResolveProjectRoot(string dataPath)
        {
            var parent = Directory.GetParent(dataPath);
            return parent != null ? parent.FullName : dataPath;
        }

        public static string TraceDir(string projectRoot) =>
            Path.Combine(projectRoot, StateDirName, TraceDirName);

        /// <summary>YYYY-MM-DD.jsonl from a UTC instant. One file per UTC day.</summary>
        public static string DailyFileName(DateTime utc) =>
            utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";

        /// <summary>
        /// Append one already-serialized JSON line. Ensures the trace
        /// directory and the .captain-sdlc/.gitignore exist. Returns the full
        /// path written to. Append-only, LF line endings (JSONL convention),
        /// UTF-8 without BOM.
        /// </summary>
        public static string AppendLine(string projectRoot, string fileName, string jsonLine)
        {
            var dir = TraceDir(projectRoot);
            Directory.CreateDirectory(dir);
            EnsureGitignore(projectRoot);

            var path = Path.Combine(dir, fileName);
            File.AppendAllText(path, jsonLine + "\n", new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Ensure <c>.captain-sdlc/.gitignore</c> excludes the always-local
        /// trace/ and side-store/ subtrees, so the tool guarantees traces — and
        /// captured media under trace/media/ — never get committed by accident.
        /// Idempotent: creates the file with both lines when absent; when it
        /// already exists, preserves its content verbatim and appends only the
        /// missing exact lines (adding a trailing newline first if the file
        /// lacks one). An exact-line match is required — a commented
        /// <c># trace/</c> does not count. Config files alongside the ignored
        /// subtrees stay tracked. Internal so AthMediaUtil can reuse it.
        /// </summary>
        internal static void EnsureGitignore(string projectRoot)
        {
            var stateDir  = Path.Combine(projectRoot, StateDirName);
            var gitignore = Path.Combine(stateDir, ".gitignore");
            Directory.CreateDirectory(stateDir);

            var encoding = new UTF8Encoding(false);
            var required = new[] { "trace/", "side-store/" };

            if (!File.Exists(gitignore))
            {
                File.WriteAllText(
                    gitignore,
                    "# Captain SDLC local state — never committed\n" +
                    "trace/\n" +
                    "side-store/\n",
                    encoding);
                return;
            }

            var existing = File.ReadAllText(gitignore);

            // Exact-line membership: split on LF, strip a trailing CR (CRLF
            // files) and surrounding whitespace, then compare.
            var present = new HashSet<string>();
            foreach (var raw in existing.Split('\n'))
                present.Add(raw.TrimEnd('\r').Trim());

            var missing = new StringBuilder();
            foreach (var line in required)
                if (!present.Contains(line))
                    missing.Append(line).Append('\n');

            if (missing.Length == 0) return;   // both lines already present

            var sb = new StringBuilder(existing);
            if (existing.Length > 0 && existing[existing.Length - 1] != '\n')
                sb.Append('\n');               // clean boundary before appending
            sb.Append(missing);

            File.WriteAllText(gitignore, sb.ToString(), encoding);
        }
    }
}
