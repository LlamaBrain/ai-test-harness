#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthLog — minimal helpers for emitting the CMD:/OK:/ERR: sentinels that
// Tool_AthCmd's correlation-id log capture keys on. Commands are free to
// call UnityEngine.Debug.Log directly with their own formatting; AthLog
// just centralizes the format so a future change to the sentinel scheme
// only touches one file.

using UnityEngine;

namespace LlamaBrainLabs.Ath
{
    public static class AthLog
    {
        public static void Cmd(string command, string correlationId, string args = null)
        {
            if (string.IsNullOrEmpty(args))
                Debug.Log($"CMD:{command} id={correlationId}");
            else
                Debug.Log($"CMD:{command} id={correlationId} args=\"{args}\"");
        }

        public static void Ok(string command, string correlationId, string payload = null)
        {
            if (string.IsNullOrEmpty(payload))
                Debug.Log($"OK:{command} id={correlationId}");
            else
                Debug.Log($"OK:{command} id={correlationId} {payload}");
        }

        public static void OkDispatched(string command, string correlationId)
        {
            // The literal "[dispatched]" marker is what Tool_AthCmd keys on
            // for fire-and-forget async dispatches. Do not change without
            // also updating Tool_AthCmd's parser.
            Debug.Log($"OK:{command} id={correlationId} [dispatched]");
        }

        public static void Err(string command, string correlationId, string reason)
        {
            Debug.Log($"ERR:{command} id={correlationId} reason={reason}");
        }

        public static void ErrException(string command, string correlationId, System.Exception ex)
        {
            var type = ex?.GetType().Name ?? "unknown";
            var msg  = Esc(ex?.Message ?? "");
            Debug.Log($"ERR:{command} id={correlationId} reason=exception type={type} msg=\"{msg}\"");
        }

        /// <summary>
        /// Escape a value for the inside of a key="..." sentinel field: single
        /// line, with backslash, quote and newline escaped. The remote client's
        /// protocol.js parser is the consumer — keep the two in lockstep.
        /// </summary>
        public static string Esc(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "")
                .Replace("\n", "\\n");
        }
    }
}
#endif
