#if UNITY_EDITOR || DEVELOPMENT_BUILD
// AthAsyncOpRecord — entry shape for AthBridge's ring buffer of fire-and-forget
// async ops. Skill authors locate records by correlation id via
// /ath-wait async_done:<id> and /ath-state last_async. Mirrors dirigible's
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
