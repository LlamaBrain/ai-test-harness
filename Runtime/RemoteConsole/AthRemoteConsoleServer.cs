#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
// AthRemoteConsoleServer — loopback TCP front end for the in-game console.
//
// TRUST BOUNDARY: dev-only debugging surface. Binds 127.0.0.1 only, exists
// only under the ATH_REMOTE/dev gate, and listens only when launched with
// -ath-remote-console. No auth — loopback + opt-in + dev-only IS the boundary.
// Never enable in a shipped release build.
//
// Protocol (contract: Tools~/ath-exe-client/protocol.js):
//   request  = one newline-delimited command string  (server appends its own corrId)
//   response = one newline-delimited JSON object (AthRemoteResponse)
// Commands execute FIFO on the Unity main thread (this component's Update
// pump); the socket thread blocks until the request completes or its deadline
// elapses. We never spin-wait on the main thread — log callbacks may arrive a
// frame or two after ExecuteCommand, so the pump re-checks across Update ticks.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using IngameDebugConsole;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LlamaBrainLabs.Ath.RemoteConsole
{
    public sealed class AthRemoteConsoleServer : MonoBehaviour
    {
        /// <summary>Active instance, or null when the remote console is not running.</summary>
        public static AthRemoteConsoleServer Active { get; private set; }

        // ---- caps / hardening (documented constants) ----
        private const int MaxRequestBytes          = 8 * 1024;
        private const int MaxCapturedLines         = 256;
        private const int MaxCapturedBytes         = 64 * 1024;
        private const int MaxConcurrentConnections = 8;
        private const int SocketReadTimeoutMs      = 5000;
        private const int DoneWaitMarginMs         = 2000;

        private AthRemoteOptions _opts;
        /// <summary>Resolved media directory for harness.snap (Phase 2).</summary>
        public string MediaDir { get; private set; }

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private int _connCount;

        private readonly ConcurrentQueue<Request> _inbox = new ConcurrentQueue<Request>();
        private Request _active;   // touched only on the main thread (Update + OnLog)

        public void Configure(AthRemoteOptions opts)
        {
            _opts = opts;
            MediaDir = string.IsNullOrEmpty(opts.MediaDir)
                ? Path.Combine(Application.persistentDataPath, "ath-media")
                : opts.MediaDir;
        }

        // ---- harness.snap host (called from AthRemoteSnapCommand) ----
        public void StartRemoteSnap(string label, string correlationId)
            => StartCoroutine(CaptureSnap(label, correlationId));

        private IEnumerator CaptureSnap(string label, string correlationId)
        {
            // Capture must run after the frame renders. The yield is outside the
            // try so this stays a legal iterator (no yields inside try/catch).
            yield return new WaitForEndOfFrame();
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                int w = tex.width, h = tex.height;
                var png = tex.EncodeToPNG();
                Destroy(tex);

                string file = $"snap_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Sanitize(label)}.png";
                string abs  = Path.Combine(MediaDir, file);
                File.WriteAllBytes(abs, png);

                AthLog.Ok("harness.snap", correlationId, $"path=\"{AthLog.Esc(abs)}\" w={w} h={h} status=pending");
            }
            catch (Exception ex)
            {
                AthLog.ErrException("harness.snap", correlationId, ex);
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "snap";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        private void Awake()
        {
            if (Active != null && Active != this) { Destroy(this); return; }
            Active = this;
        }

        private void OnEnable()  => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void Start()
        {
            if (_opts == null) { Debug.LogWarning("[AthRemoteConsole] no options; not starting."); return; }
            try { Directory.CreateDirectory(MediaDir); } catch { /* best effort */ }
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _opts.Port);
                _listener.Start();
                _running = true;
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AthRemoteConsoleAccept" };
                _acceptThread.Start();
                Debug.Log($"[AthRemoteConsole] listening on 127.0.0.1:{_opts.Port} media=\"{MediaDir}\"");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AthRemoteConsole] failed to bind 127.0.0.1:{_opts.Port}: {ex.Message}");
            }
        }

        // ---- accept loop (background thread) ----
        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException)       { break; }  // listener stopped
                catch (ObjectDisposedException) { break; }
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            int n = Interlocked.Increment(ref _connCount);
            try
            {
                client.ReceiveTimeout = SocketReadTimeoutMs;
                using var stream = client.GetStream();

                if (n > MaxConcurrentConnections) { WriteResponse(stream, Simple("", "failed", "busy")); return; }

                string line = ReadLine(stream, out bool tooLong);
                if (tooLong || string.IsNullOrEmpty(line)) { WriteResponse(stream, Simple("", "failed", "bad_request")); return; }

                var req = new Request { Command = line, CorrelationId = NewId() };
                _inbox.Enqueue(req);

                int waitMs = (_opts?.ResponseDeadlineMs ?? AthRemoteOptions.DefaultDeadlineMs) + DoneWaitMarginMs;
                if (req.Done.Wait(waitMs) && req.Response != null)
                    WriteResponse(stream, req.Response);
                else
                    WriteResponse(stream, Simple(req.CorrelationId, "failed", "no_response"));
            }
            catch { /* connection died — nothing to do */ }
            finally
            {
                Interlocked.Decrement(ref _connCount);
                try { client.Close(); } catch { }
            }
        }

        // ---- main-thread FIFO pump ----
        private void Update()
        {
            if (_active == null)
            {
                if (!_inbox.TryDequeue(out _active)) return;
                BeginExecute(_active);
            }

            if (_active != null && (HasTerminal(_active) || PastDeadline(_active)))
            {
                Finish(_active);
                _active = null;
            }
        }

        private void BeginExecute(Request req)
        {
            req.Sw = Stopwatch.StartNew();
            try { DebugLogConsole.ExecuteCommand($"{req.Command} {req.CorrelationId}"); }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                req.ExecException = "exception:" + root.GetType().Name + ":" + root.Message;
            }
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            var a = _active;
            if (a != null && !string.IsNullOrEmpty(msg) && msg.Contains("id=" + a.CorrelationId))
                a.Captured.Enqueue(msg);
        }

        private static bool HasTerminal(Request req)
        {
            if (req.ExecException != null) return true;
            foreach (var l in req.Captured)
                if (l.StartsWith("OK:", StringComparison.Ordinal) || l.StartsWith("ERR:", StringComparison.Ordinal)) return true;
            return false;
        }

        private bool PastDeadline(Request req)
            => req.Sw != null && req.Sw.ElapsedMilliseconds >= (_opts?.ResponseDeadlineMs ?? AthRemoteOptions.DefaultDeadlineMs);

        private void Finish(Request req)
        {
            var all = req.Captured.ToArray();   // scan ALL captured before truncating returned lines
            string status, failReason = "";
            if (req.ExecException != null) { status = "failed"; failReason = req.ExecException; }
            else
            {
                var okLine  = all.FirstOrDefault(l => l.StartsWith("OK:",  StringComparison.Ordinal));
                var errLine = all.FirstOrDefault(l => l.StartsWith("ERR:", StringComparison.Ordinal));
                if (errLine != null)                              { status = "failed"; failReason = "command_error"; }
                else if (okLine != null && okLine.Contains("[dispatched]")) status = "dispatched";
                else if (okLine != null)                            status = "success";
                else                                              { status = "failed"; failReason = "no_response"; }
            }

            var lines = Truncate(all, out bool truncated);
            req.Response = new AthRemoteResponse
            {
                correlationId = req.CorrelationId,
                status        = status,
                failReason    = failReason,
                lines         = lines,
                elapsedMs     = req.Sw?.ElapsedMilliseconds ?? 0,
                truncated     = truncated,
            };
            req.Done.Set();
        }

        private static string[] Truncate(string[] all, out bool truncated)
        {
            truncated = false;
            var outList = new List<string>(Math.Min(all.Length, MaxCapturedLines));
            int bytes = 0;
            foreach (var l in all)
            {
                if (outList.Count >= MaxCapturedLines) { truncated = true; break; }
                int b = Encoding.UTF8.GetByteCount(l);
                if (bytes + b > MaxCapturedBytes)      { truncated = true; break; }
                bytes += b;
                outList.Add(l);
            }
            return outList.ToArray();
        }

        // ---- helpers ----
        private static AthRemoteResponse Simple(string corr, string status, string reason)
            => new AthRemoteResponse { correlationId = corr ?? "", status = status, failReason = reason };

        private static void WriteResponse(NetworkStream stream, AthRemoteResponse resp)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(resp) + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ReadLine(NetworkStream stream, out bool tooLong)
        {
            tooLong = false;
            var buf = new List<byte>(256);
            var one = new byte[1];
            while (stream.Read(one, 0, 1) > 0)
            {
                byte b = one[0];
                if (b == (byte)'\n') return Encoding.UTF8.GetString(buf.ToArray());
                if (b == (byte)'\r') continue;
                if (b < 0x20) continue;                 // reject control chars
                if (buf.Count >= MaxRequestBytes) { tooLong = true; return null; }
                buf.Add(b);
            }
            return buf.Count > 0 ? Encoding.UTF8.GetString(buf.ToArray()) : null;
        }

        private static Exception Unwrap(Exception ex)
        {
            var root = ex;
            while (root is TargetInvocationException tex && tex.InnerException != null) root = tex.InnerException;
            return root;
        }

        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        private void OnDestroy()        => Shutdown();
        private void OnApplicationQuit() => Shutdown();

        private void Shutdown()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }   // unblocks AcceptTcpClient
            _listener = null;
            if (Active == this) Active = null;
            // In-flight socket threads time out on Done.Wait and write no_response.
        }
    }

    internal sealed class Request
    {
        public string Command;
        public string CorrelationId;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public readonly ConcurrentQueue<string> Captured = new ConcurrentQueue<string>();
        public Stopwatch Sw;
        public string ExecException;
        public AthRemoteResponse Response;
    }
}
#endif
