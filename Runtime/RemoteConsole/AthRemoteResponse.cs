#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthRemoteResponse — flat DTO serialized with UnityEngine.JsonUtility into the
// single newline-delimited JSON object that is one TCP response. Field names
// must match what Tools~/ath-exe-client/protocol.js (parseResponse) reads.
// JsonUtility owns the JSON-layer string escaping; the raw sentinel lines in
// `lines` keep their own AthLog key="value" quoting inside.

using System;

namespace LlamaBrainLabs.Ath.RemoteConsole
{
    [Serializable]
    public sealed class AthRemoteResponse
    {
        public string   correlationId = "";
        public string   status        = "";   // success | dispatched | failed
        public string   failReason    = "";   // busy | no_response | command_error | bad_request | exception:<type>:<msg>
        public string[] lines         = Array.Empty<string>();
        public long     elapsedMs;
        public bool     truncated;
    }
}
#endif
