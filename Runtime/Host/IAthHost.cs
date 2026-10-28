#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
using System;

namespace LlamaBrainLabs.Ath
{
    /// <summary>Game-independent host boundary; calls run on the engine thread except observations.</summary>
    public interface IAthHost : IDisposable
    {
        string HostName { get; }
        bool IsValid { get; }
        bool GameReady { get; }
        AthHostEvents Events { get; }
        AthHostCapabilities GetCapabilities();
        bool TryGetState(string key, out string value);
        AthCommandResult Execute(string command, string argumentsJson, string correlationId);
        AthCommandResult Reset(string scope);
    }

    public interface IAthScreenshotHost
    {
        AthCommandResult CaptureScreenshot(string label, string correlationId);
    }

    [Serializable]
    public sealed class AthHostCapabilities
    {
        public int protocolVersion = 1;
        public string engine = "unity";
        public string engineVersion = "";
        public string buildMode = "";
        public string version = AthRuntimeFlag.PackageVersion;
        public string athVersion = AthRuntimeFlag.PackageVersion;
        public string hostName = "";
        public string[] capabilities = Array.Empty<string>();
        public string[] stateKeys = Array.Empty<string>();
        public string[] commands = Array.Empty<string>();
    }

    public sealed class AthCommandResult
    {
        public string Status { get; }
        public string Value { get; }
        public string Error { get; }
        private AthCommandResult(string status, string value, string error)
        {
            Status = status; Value = value; Error = error;
        }
        public static AthCommandResult Ok(string value = "") => new AthCommandResult("success", value, "");
        public static AthCommandResult Fail(string error) => new AthCommandResult("failed", "", error);
        public static AthCommandResult Dispatched(string value = "") => new AthCommandResult("dispatched", value, "");
    }

    public abstract class AthHostBase : IAthHost
    {
        public abstract string HostName { get; }
        public virtual bool IsValid => true;
        public abstract bool GameReady { get; }
        public AthHostEvents Events { get; } = new AthHostEvents();
        public virtual AthHostCapabilities GetCapabilities() => new AthHostCapabilities
        {
            hostName = HostName, capabilities = new[] { "state", "events", "async" }
        };
        public virtual bool TryGetState(string key, out string value) { value = ""; return false; }
        public virtual AthCommandResult Execute(string command, string argumentsJson, string correlationId)
            => AthCommandResult.Fail("unsupported");
        public virtual AthCommandResult Reset(string scope) => AthCommandResult.Fail("unsupported");
        public virtual void Dispose() { }
    }
}
#endif
