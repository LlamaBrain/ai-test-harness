#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using UnityEngine;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    [Serializable]
    public sealed class RuntimeSmokeLaunchDocument
    {
        public int schemaVersion;
        public string state;
        public string runId;
        public string correlationId;
        public string workflowId;
        public string scriptHash;
        public string hostRoot;
        public string outputRoot;
        public string authoredRoot;
        public string runDirectory;
        public string artifactRoot;
        public string executablePath;
        public string executableSha256;
        public string logPath;
        public string buildEvidenceSnapshotPath;
        public int processId;
        public string processStartUtc;
        public string startedUtc;
        public int timeoutSeconds;
        public string target;
        public string mode;
        public string suiteId;
        public string scenario;
        public string seed;
        public string[] requiredStepIds;
        public string buildRevision;
        public string buildVersion;
        public string profileHash;
        public string contentHash;
        public string backend;
        public string stripping;
        public string buildEvidenceHash;
        public string nonce;
        public string buildToken;
    }

    [Serializable]
    public sealed class RuntimeSmokeBuildEvidenceDocument
    {
        public int schemaVersion;
        public string artifactPath;
        public string artifactSha256;
        public string revision;
        public string version;
        public string profileHash;
        public string contentHash;
        public string backend;
        public string stripping;
        public string lane;
    }

    public sealed class RuntimeSmokePlayerProcessIdentity
    {
        public int ProcessId { get; }
        public DateTime ProcessStartUtc { get; }
        public string ExecutablePath { get; }
        public string ExecutableSha256 { get; }

        public RuntimeSmokePlayerProcessIdentity(int processId, DateTime processStartUtc, string executablePath, string executableSha256)
        {
            ProcessId = processId;
            ProcessStartUtc = processStartUtc;
            ExecutablePath = executablePath;
            ExecutableSha256 = executableSha256;
        }

        public static RuntimeSmokePlayerProcessIdentity CaptureCurrent()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var processId = checked((int)GetCurrentProcessId());
                var processStartUtc = GetWindowsCurrentProcessStartUtc();
                var windowsExecutable = GetWindowsCurrentProcessImagePath();
                if (processId <= 0 || string.IsNullOrWhiteSpace(windowsExecutable))
                    throw new InvalidOperationException("Windows could not identify the current player process.");
                string windowsHash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(windowsExecutable))
                    windowsHash = ToHex(sha.ComputeHash(stream));
                return new RuntimeSmokePlayerProcessIdentity(processId, processStartUtc, Path.GetFullPath(windowsExecutable), windowsHash);
            }

            using (var process = Process.GetCurrentProcess())
            {
                var executable = process.MainModule == null ? null : process.MainModule.FileName;
                if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Current player executable path is unavailable.");
                string hash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(executable))
                    hash = ToHex(sha.ComputeHash(stream));
                return new RuntimeSmokePlayerProcessIdentity(process.Id, process.StartTime.ToUniversalTime(), Path.GetFullPath(executable), hash);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public uint LowDateTime;
            public uint HighDateTime;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out NativeFileTime creationTime,
            out NativeFileTime exitTime, out NativeFileTime kernelTime, out NativeFileTime userTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleFileName(IntPtr module, StringBuilder fileName, int size);

        private static DateTime GetWindowsCurrentProcessStartUtc()
        {
            if (!GetProcessTimes(GetCurrentProcess(), out var creation, out _, out _, out _))
                throw new InvalidOperationException("Windows could not read current process start time (error " + Marshal.GetLastWin32Error() + ").");
            var fileTime = ((long)creation.HighDateTime << 32) | creation.LowDateTime;
            return DateTime.FromFileTimeUtc(fileTime);
        }

        private static string GetWindowsCurrentProcessImagePath()
        {
            var buffer = new StringBuilder(32768);
            var length = GetModuleFileName(IntPtr.Zero, buffer, buffer.Capacity);
            if (length == 0 || length >= buffer.Capacity)
                throw new InvalidOperationException("Windows could not identify the current process executable (error " + Marshal.GetLastWin32Error() + ").");
            return buffer.ToString();
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class RuntimeSmokeArmedDocument
    {
        public string state = "armed";
        public string runId;
        public string correlationId;
        public string workflowId;
        public string scriptHash;
        public string scenario;
        public string seed;
        public string nonce;
        public string buildToken;
        public int processId;
        public string processStartUtc;
        public string executableSha256;
        public string buildRevision;
        public string profileHash;
        public string contentHash;
        public string hostRoot;
        public string outputRoot;
        public string authoredRoot;
    }

    /// <summary>Player-side, filesystem-durable single-use claim of the external launch envelope.</summary>
    public static class RuntimeSmokeDurableLaunchClaim
    {
        public static bool TryClaim(RuntimeSmokePlayerLaunchOptions options, RuntimeSmokePlayerProcessIdentity process,
            DateTime nowUtc, out RuntimeSmokeArmedDocument armed, out string failure)
        {
            armed = null;
            failure = null;
            if (options == null || process == null || process.ProcessId <= 0 || process.ProcessStartUtc.Kind != DateTimeKind.Utc || nowUtc.Kind != DateTimeKind.Utc)
                return Reject("RuntimeSmoke launch process identity or UTC clock is invalid.", out failure);

            RuntimeSmokeLaunchDocument launch;
            string root;
            try
            {
                root = Path.GetFullPath(options.ArtifactRoot);
                var launchPath = Path.Combine(root, "runtime-smoke-launch.json");
                EnsureNoReparsePoints(root);
                EnsureNoReparsePoints(launchPath);
                if (!File.Exists(launchPath)) return Reject("RuntimeSmoke launch envelope is missing.", out failure);
                using (var stream = new FileStream(launchPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    launch = JsonUtility.FromJson<RuntimeSmokeLaunchDocument>(reader.ReadToEnd());
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException || exception is InvalidOperationException)
            {
                return Reject("RuntimeSmoke launch envelope could not be read safely.", out failure);
            }

            try
            {
                if (!Matches(options, process, nowUtc, root, launch, out var mismatch))
                    return Reject("RuntimeSmoke launch provenance is invalid (" + mismatch + ").", out failure);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException || exception is InvalidOperationException || exception is OverflowException)
            {
                return Reject("RuntimeSmoke launch provenance could not be validated safely.", out failure);
            }

            armed = new RuntimeSmokeArmedDocument
            {
                runId = launch.runId,
                correlationId = launch.correlationId,
                workflowId = launch.workflowId,
                scriptHash = launch.scriptHash,
                scenario = launch.scenario,
                seed = launch.seed,
                nonce = launch.nonce,
                buildToken = launch.buildToken,
                processId = process.ProcessId,
                processStartUtc = process.ProcessStartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                executableSha256 = process.ExecutableSha256,
                buildRevision = launch.buildRevision,
                profileHash = launch.profileHash,
                contentHash = launch.contentHash,
                hostRoot = launch.hostRoot,
                outputRoot = launch.outputRoot,
                authoredRoot = launch.authoredRoot
            };
            try
            {
                WriteNew(Path.Combine(root, "runtime-smoke-claim.json"), JsonUtility.ToJson(armed) + "\n");
                WriteNew(Path.Combine(root, "runtime-smoke-armed.json"), JsonUtility.ToJson(armed) + "\n");
                WriteNew(Path.Combine(root, "transcript.jsonl"), JsonUtility.ToJson(new RuntimeSmokeTranscriptRecord
                {
                    sequence = 0,
                    type = "armed",
                    timestampUtc = armed.processStartUtc,
                    workflowId = armed.workflowId,
                    scriptHash = armed.scriptHash,
                    correlationId = armed.correlationId,
                    scenario = armed.scenario,
                    seed = armed.seed,
                    status = "armed"
                }) + "\n");
                options.SetClaimedHostPaths(launch.hostRoot, launch.outputRoot, launch.authoredRoot);
                options.MarkDurablyClaimed();
                return true;
            }
            catch (IOException exception)
            {
                armed = null;
                return Reject(exception.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "RuntimeSmoke launch was already claimed or contains pre-seeded claim evidence."
                    : "RuntimeSmoke durable claim could not be written.", out failure);
            }
            catch (UnauthorizedAccessException)
            {
                armed = null;
                return Reject("RuntimeSmoke durable claim could not be written.", out failure);
            }
        }

        private static bool Matches(RuntimeSmokePlayerLaunchOptions options, RuntimeSmokePlayerProcessIdentity process,
            DateTime nowUtc, string root, RuntimeSmokeLaunchDocument launch, out string mismatch)
        {
            mismatch = "launch_header_invalid";
            if (launch == null) return false;
            if (launch.schemaVersion != 1) { mismatch = "schema_version_invalid"; return false; }
            if (launch.state != "awaiting_player") { mismatch = "launch_state_invalid"; return false; }
            if (launch.mode != "fsd") { mismatch = "launch_mode_invalid"; return false; }
            if (launch.target != "development" && launch.target != "smoke_harness") { mismatch = "launch_target_invalid"; return false; }
            if (launch.timeoutSeconds < 1 || launch.timeoutSeconds > 86400) { mismatch = "timeout_invalid"; return false; }
            if (!SafeId(launch.runId)) { mismatch = "run_id_invalid"; return false; }
            if (launch.requiredStepIds == null || launch.requiredStepIds.Length == 0 || !IsSafeSteps(launch.requiredStepIds))
            { mismatch = "required_steps_invalid"; return false; }
            if (launch.processId != 0) { mismatch = "launch_pid_already_set"; return false; }
            if (!string.IsNullOrEmpty(launch.processStartUtc)) { mismatch = "launch_process_start_already_set"; return false; }
            mismatch = "launch_identity_mismatch";
            if (launch.correlationId != options.CorrelationId || launch.workflowId != options.WorkflowId || launch.nonce != options.Nonce ||
                launch.buildToken != options.BuildToken) return false;
            mismatch = "build_provenance_invalid";
            if (!IsSha256(launch.scriptHash) || !IsSha256(launch.executableSha256) || !IsSha256(launch.profileHash) || !IsSha256(launch.contentHash) ||
                string.IsNullOrWhiteSpace(launch.buildRevision) || string.IsNullOrWhiteSpace(launch.buildVersion) ||
                string.IsNullOrWhiteSpace(launch.backend) || string.IsNullOrWhiteSpace(launch.stripping) || !IsSha256(launch.buildEvidenceHash)) return false;
            mismatch = "build_token_mismatch";
            if (!string.Equals(ComputeBuildToken(launch), options.BuildToken, StringComparison.Ordinal)) return false;
            mismatch = "launch_time_invalid";
            DateTime started;
            if (!DateTime.TryParse(launch.startedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started) ||
                started.Kind != DateTimeKind.Utc || process.ProcessStartUtc < started || nowUtc < process.ProcessStartUtc ||
                nowUtc >= started.AddSeconds(launch.timeoutSeconds)) return false;
            mismatch = "process_or_host_path_mismatch";
            if (string.IsNullOrWhiteSpace(launch.hostRoot) || string.IsNullOrWhiteSpace(launch.outputRoot) ||
                string.IsNullOrWhiteSpace(launch.authoredRoot) || !PathEquals(launch.runDirectory, root) || !PathEquals(launch.artifactRoot, root) ||
                !IsWithin(launch.outputRoot, launch.hostRoot) || PathEquals(launch.outputRoot, launch.hostRoot) ||
                !IsWithin(launch.authoredRoot, launch.hostRoot) || PathEquals(launch.authoredRoot, launch.hostRoot) ||
                PathsOverlap(launch.outputRoot, launch.authoredRoot) || !IsWithin(root, launch.outputRoot) ||
                !IsWithin(root, launch.hostRoot) || PathEquals(root, launch.hostRoot) || !IsWithin(launch.executablePath, launch.hostRoot) ||
                !PathEquals(Path.GetFullPath(launch.executablePath), Path.GetFullPath(process.ExecutablePath)) ||
                !string.Equals(process.ExecutableSha256, launch.executableSha256, StringComparison.Ordinal)) return false;
            var expectedLog = Path.Combine(root, "logs", "player.log");
            var expectedBuildEvidence = Path.Combine(root, "build-evidence.json");
            mismatch = "artifact_path_mismatch";
            if (!PathEquals(launch.logPath, expectedLog) || !PathEquals(launch.buildEvidenceSnapshotPath, expectedBuildEvidence)) return false;
            mismatch = "artifact_provenance_mismatch";
            try
            {
                if (!File.Exists(process.ExecutablePath) || !string.Equals(HashFile(process.ExecutablePath), process.ExecutableSha256, StringComparison.Ordinal)) return false;
                EnsureNoReparsePoints(Path.GetFullPath(launch.hostRoot));
                EnsureNoReparsePoints(Path.GetFullPath(launch.executablePath));
                EnsureNoReparsePoints(expectedBuildEvidence);
                if (!File.Exists(expectedBuildEvidence)) return false;
                var build = JsonUtility.FromJson<RuntimeSmokeBuildEvidenceDocument>(File.ReadAllText(expectedBuildEvidence, Encoding.UTF8));
                if (build == null || build.schemaVersion != 1 || !PathEquals(build.artifactPath, launch.executablePath) ||
                    build.artifactSha256 != launch.executableSha256 || build.revision != launch.buildRevision ||
                    build.version != launch.buildVersion || build.profileHash != launch.profileHash ||
                    build.contentHash != launch.contentHash || build.backend != launch.backend || build.stripping != launch.stripping ||
                    build.lane != launch.target || ComputeBuildEvidenceHash(build) != launch.buildEvidenceHash) return false;
                mismatch = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException)
            {
                return false;
            }
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) return ToHex(sha.ComputeHash(stream));
        }

        public static string ComputeBuildToken(RuntimeSmokeLaunchDocument launch)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(CanonicalLaunchJson(launch));
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        public static string ComputeBuildEvidenceHash(RuntimeSmokeBuildEvidenceDocument evidence)
        {
            var json = "{\"artifactPath\":" + Quote(evidence.artifactPath) +
                ",\"artifactSha256\":" + Quote(evidence.artifactSha256) +
                ",\"backend\":" + Quote(evidence.backend) +
                ",\"contentHash\":" + Quote(evidence.contentHash) +
                ",\"lane\":" + Quote(evidence.lane) +
                ",\"profileHash\":" + Quote(evidence.profileHash) +
                ",\"revision\":" + Quote(evidence.revision) +
                ",\"schemaVersion\":" + evidence.schemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"stripping\":" + Quote(evidence.stripping) +
                ",\"version\":" + Quote(evidence.version) + "}";
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(json)));
        }

        private static string CanonicalLaunchJson(RuntimeSmokeLaunchDocument launch)
        {
            return "{\"artifactRoot\":" + Quote(launch.artifactRoot) +
                ",\"authoredRoot\":" + Quote(launch.authoredRoot) +
                ",\"backend\":" + Quote(launch.backend) +
                ",\"buildEvidenceHash\":" + Quote(launch.buildEvidenceHash) +
                ",\"buildEvidenceSnapshotPath\":" + Quote(launch.buildEvidenceSnapshotPath) +
                ",\"buildRevision\":" + Quote(launch.buildRevision) +
                ",\"buildToken\":null" +
                ",\"buildVersion\":" + Quote(launch.buildVersion) +
                ",\"contentHash\":" + Quote(launch.contentHash) +
                ",\"correlationId\":" + Quote(launch.correlationId) +
                ",\"executablePath\":" + Quote(launch.executablePath) +
                ",\"executableSha256\":" + Quote(launch.executableSha256) +
                ",\"hostRoot\":" + Quote(launch.hostRoot) +
                ",\"logPath\":" + Quote(launch.logPath) +
                ",\"mode\":" + Quote(launch.mode) +
                ",\"nonce\":" + Quote(launch.nonce) +
                ",\"outputRoot\":" + Quote(launch.outputRoot) +
                ",\"processId\":" + (launch.processId == 0 ? "null" : launch.processId.ToString(CultureInfo.InvariantCulture)) +
                ",\"processStartUtc\":null" +
                ",\"profileHash\":" + Quote(launch.profileHash) +
                ",\"requiredStepIds\":" + QuoteArray(launch.requiredStepIds) +
                ",\"runDirectory\":" + Quote(launch.runDirectory) +
                ",\"runId\":" + Quote(launch.runId) +
                ",\"scenario\":" + QuoteNullable(launch.scenario) +
                ",\"schemaVersion\":" + launch.schemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"scriptHash\":" + Quote(launch.scriptHash) +
                ",\"seed\":" + QuoteNullable(launch.seed) +
                ",\"startedUtc\":" + Quote(launch.startedUtc) +
                ",\"state\":" + Quote(launch.state) +
                ",\"stripping\":" + Quote(launch.stripping) +
                ",\"suiteId\":" + QuoteNullable(launch.suiteId) +
                ",\"target\":" + Quote(launch.target) +
                ",\"timeoutSeconds\":" + launch.timeoutSeconds.ToString(CultureInfo.InvariantCulture) +
                ",\"workflowId\":" + Quote(launch.workflowId) + "}";
        }

        private static string QuoteArray(string[] values)
        {
            var builder = new StringBuilder("[");
            for (var index = 0; index < values.Length; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append(Quote(values[index]));
            }
            return builder.Append(']').ToString();
        }

        private static string Quote(string value)
        {
            if (value == null) return "null";
            var builder = new StringBuilder("\"");
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }

        private static string QuoteNullable(string value) => string.IsNullOrEmpty(value) ? "null" : Quote(value);

        private static bool IsSafeSteps(string[] values)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
                if (!SafeId(value) || !seen.Add(value)) return false;
            return true;
        }

        private static void WriteNew(string path, string value)
        {
            var bytes = new UTF8Encoding(false).GetBytes(value);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void EnsureNoReparsePoints(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            var current = root;
            foreach (var part in full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("RuntimeSmoke launch paths cannot traverse symbolic links or junctions.");
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool SafeId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && value != "." && value != ".." &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOfAny(new[] { '/', '\\' }) < 0;
        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if ((character < '0' || character > '9') && (character < 'a' || character > 'f')) return false;
            return true;
        }
        private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private static bool IsWithin(string child, string root)
        {
            var fullChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return fullChild.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison) ||
                (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar && fullChild.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, comparison));
        }
        private static bool PathsOverlap(string left, string right) => PathEquals(left, right) || IsWithin(left, right) || IsWithin(right, left);
        private static bool Reject(string message, out string failure) { failure = message; return false; }
    }
}
#endif
