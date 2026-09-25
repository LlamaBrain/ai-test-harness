#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE || SMOKE_HARNESS
// AthAsyncOpRecord — entry shape for AthBridge's ring buffer of fire-and-forget
// async ops. Diagnostic tooling can inspect records by correlation id via
// /ath-wait async_done:<id> and /ath-state last_async. These are diagnostic
// operations, not RuntimeSmoke acceptance steps. Mirrors dirigible's
// DirigibleTestBridge.AsyncOpRecord.

namespace LlamaBrainLabs.Ath
{
    public readonly struct AthAsyncOpRecord
    {
        public readonly string Name;
        public readonly string CorrelationId;
        public readonly bool   Success;
        public readonly string Error;

        public AthAsyncOpRecord(string name, string correlationId, bool success, string error)
        {
            Name = name;
            CorrelationId = correlationId;
            Success = success;
            Error = error;
        }
    }
}
#endif
