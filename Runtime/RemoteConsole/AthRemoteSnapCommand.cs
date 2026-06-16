#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// harness.snap — capture a PNG from inside the running player to the remote
// media dir. Remote-console ONLY: the coroutine host is the active
// AthRemoteConsoleServer (not AthBridge), so it ERRs remote_inactive when the
// socket server isn't running. In the editor / a dev PlayMode session without
// the socket, use ath-snap instead. Two-phase: this emits the path + status=pending;
// the Node client polls the PNG path until it is written and stable.

using System;
using IngameDebugConsole;

namespace LlamaBrainLabs.Ath.RemoteConsole
{
    public static class AthRemoteSnapCommand
    {
        [ConsoleMethod("harness.snap", "Capture a PNG to the remote media dir (remote console only)", "label")]
        public static void Snap(string label) => Snap(label, NewId());

        [ConsoleMethod("harness.snap", "Snap; correlation-id form", "label", "correlationId")]
        public static void Snap(string label, string correlationId)
        {
            AthLog.Cmd("harness.snap", correlationId, label);
            var server = AthRemoteConsoleServer.Active;
            if (server == null)
            {
                AthLog.Err("harness.snap", correlationId, "remote_inactive");
                return;
            }
            server.StartRemoteSnap(label, correlationId);
        }

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
#endif
