#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    public enum RuntimeSmokeLaunchWatchdogState { Invalid, Running, Passed, Failed }
    public enum RuntimeSmokeLaunchFailureCode
    {
        None,
        EnvelopeInvalid,
        ProvenanceMismatch,
        ProcessIdentityMismatch,
        LogNotFresh,
        ProcessExitedWithoutTerminal,
        WatchdogTimeout
    }

    /// <summary>Immutable-by-convention description of one externally launched smoke process.</summary>
    public sealed class RuntimeSmokeLaunchEnvelope
    {
        public int SchemaVersion { get; set; } = 1;
        public string RunId { get; set; }
        public string CorrelationId { get; set; }
        public string WorkflowId { get; set; }
        public string ScriptHash { get; set; }
        public string HostRoot { get; set; }
        public string RunDirectory { get; set; }
        public string ExecutablePath { get; set; }
        public string LogPath { get; set; }
        public int ProcessId { get; set; }
        public DateTime ProcessStartUtc { get; set; }
        public DateTime StartedUtc { get; set; }
        public int TimeoutSeconds { get; set; }
        public string ExpectedTerminalMarker { get; set; }
    }

    /// <summary>Fresh observation captured by the platform-specific process/log probe.</summary>
    public sealed class RuntimeSmokeProcessObservation
    {
        public bool IsRunning { get; set; }
        public int ProcessId { get; set; }
        public DateTime ProcessStartUtc { get; set; }
        public string ExecutablePath { get; set; }
        public string LogPath { get; set; }
        public long LogLength { get; set; }
        public DateTime LogLastWriteUtc { get; set; }
        public string TerminalMarker { get; set; }
    }

    public sealed class RuntimeSmokeLaunchWatchdogResult
    {
        public RuntimeSmokeLaunchWatchdogState State { get; internal set; }
        public RuntimeSmokeLaunchFailureCode FailureCode { get; internal set; }
        public string Diagnostic { get; internal set; }
        public bool Claimed { get; internal set; }
    }

    /// <summary>Process-local single-use ledger preventing an envelope or correlation being claimed twice.</summary>
    public sealed class RuntimeSmokeLaunchClaimLedger
    {
        private readonly object _gate = new object();
        private readonly HashSet<string> _runIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _correlations = new HashSet<string>(StringComparer.Ordinal);

        public RuntimeSmokeLaunchWatchdogResult Claim(RuntimeSmokeLaunchEnvelope envelope,
            RuntimeSmokeProcessObservation observation, DateTime nowUtc)
        {
            var result = RuntimeSmokeLaunchEnvelopeVerifier.Evaluate(envelope, observation, nowUtc);
            if (result.State != RuntimeSmokeLaunchWatchdogState.Running && result.State != RuntimeSmokeLaunchWatchdogState.Passed)
                return result;
            lock (_gate)
            {
                if (_runIds.Contains(envelope.RunId) || _correlations.Contains(envelope.CorrelationId))
                    return new RuntimeSmokeLaunchWatchdogResult
                    {
                        State = RuntimeSmokeLaunchWatchdogState.Failed,
                        FailureCode = RuntimeSmokeLaunchFailureCode.ProvenanceMismatch,
                        Diagnostic = "Launch run ID or correlation was already claimed."
                    };
                _runIds.Add(envelope.RunId);
                _correlations.Add(envelope.CorrelationId);
            }
            result.Claimed = true;
            return result;
        }
    }

    /// <summary>
    /// Verifies launch provenance before interpreting process outcome. This is deliberately a
    /// pure protocol evaluator; OS-specific launchers supply observations and remain responsible
    /// for capturing process start time and fresh log metadata from the launched process handle.
    /// </summary>
    public static class RuntimeSmokeLaunchEnvelopeVerifier
    {
        public static RuntimeSmokeLaunchWatchdogResult Evaluate(RuntimeSmokeLaunchEnvelope envelope,
            RuntimeSmokeProcessObservation observation, DateTime nowUtc)
        {
            var invalid = ValidateEnvelope(envelope);
            if (invalid != null) return Fail(RuntimeSmokeLaunchFailureCode.EnvelopeInvalid, invalid);
            if (observation == null) return Fail(RuntimeSmokeLaunchFailureCode.ProvenanceMismatch, "No process observation was supplied.");
            if (observation.ProcessId != envelope.ProcessId || observation.ProcessStartUtc.Kind != DateTimeKind.Utc ||
                observation.ProcessStartUtc != envelope.ProcessStartUtc ||
                !PathEquals(observation.ExecutablePath, envelope.ExecutablePath))
                return Fail(RuntimeSmokeLaunchFailureCode.ProcessIdentityMismatch, "Observed process PID, start time, or executable does not match the launch envelope.");
            if (nowUtc.Kind != DateTimeKind.Utc || nowUtc < envelope.StartedUtc)
                return Fail(RuntimeSmokeLaunchFailureCode.EnvelopeInvalid, "Watchdog observation time is invalid or precedes launch.");
            if (!PathEquals(observation.LogPath, envelope.LogPath) || observation.LogLength <= 0 ||
                observation.LogLastWriteUtc.Kind != DateTimeKind.Utc || observation.LogLastWriteUtc < envelope.StartedUtc ||
                observation.LogLastWriteUtc > nowUtc)
                return Fail(RuntimeSmokeLaunchFailureCode.LogNotFresh, "Observed log is missing, stale, empty, or not the envelope-owned log.");

            if (string.Equals(observation.TerminalMarker, envelope.ExpectedTerminalMarker, StringComparison.Ordinal))
            {
                if (observation.IsRunning)
                {
                    if (nowUtc >= envelope.StartedUtc.AddSeconds(envelope.TimeoutSeconds))
                        return Fail(RuntimeSmokeLaunchFailureCode.WatchdogTimeout, "Terminal marker appeared but process did not exit before the watchdog deadline.");
                    return new RuntimeSmokeLaunchWatchdogResult { State = RuntimeSmokeLaunchWatchdogState.Running };
                }
                return new RuntimeSmokeLaunchWatchdogResult { State = RuntimeSmokeLaunchWatchdogState.Passed };
            }
            if (!observation.IsRunning)
                return Fail(RuntimeSmokeLaunchFailureCode.ProcessExitedWithoutTerminal, "Launched process exited without the required terminal marker.");
            if (nowUtc >= envelope.StartedUtc.AddSeconds(envelope.TimeoutSeconds))
                return Fail(RuntimeSmokeLaunchFailureCode.WatchdogTimeout, "Launched process exceeded the envelope watchdog deadline.");
            return new RuntimeSmokeLaunchWatchdogResult { State = RuntimeSmokeLaunchWatchdogState.Running };
        }

        private static string ValidateEnvelope(RuntimeSmokeLaunchEnvelope envelope)
        {
            if (envelope == null || envelope.SchemaVersion != 1 || envelope.ProcessId <= 0 || envelope.TimeoutSeconds <= 0 || envelope.TimeoutSeconds > 86400 ||
                envelope.StartedUtc.Kind != DateTimeKind.Utc || envelope.ProcessStartUtc.Kind != DateTimeKind.Utc ||
                envelope.ProcessStartUtc < envelope.StartedUtc || string.IsNullOrWhiteSpace(envelope.RunId) ||
                string.IsNullOrWhiteSpace(envelope.WorkflowId) || !IsSha256(envelope.ScriptHash) ||
                string.IsNullOrWhiteSpace(envelope.ExpectedTerminalMarker) || !SafeId(envelope.RunId) || !SafeId(envelope.CorrelationId))
                return "Launch envelope is missing required schema, identity, deadline, or terminal-marker fields.";
            if (envelope.StartedUtc > DateTime.MaxValue.AddSeconds(-envelope.TimeoutSeconds))
                return "Launch envelope deadline overflows the UTC timestamp range.";
            try
            {
                if (!Path.IsPathRooted(envelope.HostRoot) || !Path.IsPathRooted(envelope.RunDirectory) ||
                    !Path.IsPathRooted(envelope.ExecutablePath) || !Path.IsPathRooted(envelope.LogPath))
                    return "Launch provenance paths must be absolute.";
                var host = Path.GetFullPath(envelope.HostRoot);
                var run = Path.GetFullPath(envelope.RunDirectory);
                var executable = Path.GetFullPath(envelope.ExecutablePath);
                var log = Path.GetFullPath(envelope.LogPath);
                if (!IsWithin(run, host) || PathEquals(run, host) || !IsWithin(log, run) || PathEquals(log, run) || !IsWithin(executable, host))
                    return "Run directory, executable, or log path escapes its declared host provenance.";
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                return "Launch envelope contains an invalid filesystem path.";
            }
            return null;
        }

        private static bool SafeId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-');
        private static bool IsSha256(string value) => value != null && value.Length == 64 &&
            value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
        private static string Trim(string value) => Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static bool PathEquals(string left, string right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
            string.Equals(Trim(left), Trim(right), Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        private static bool IsWithin(string path, string root)
        {
            var fullPath = Trim(path);
            var fullRoot = Trim(root);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return fullPath.Equals(fullRoot, comparison) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison) ||
                (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar && fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, comparison));
        }
        private static RuntimeSmokeLaunchWatchdogResult Fail(RuntimeSmokeLaunchFailureCode code, string diagnostic) =>
            new RuntimeSmokeLaunchWatchdogResult { State = RuntimeSmokeLaunchWatchdogState.Failed, FailureCode = code, Diagnostic = diagnostic };
    }
}
#endif
