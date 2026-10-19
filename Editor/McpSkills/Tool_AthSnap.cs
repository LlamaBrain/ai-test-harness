// LlamaBrainLabs.Ath.Editor — MCP tool for Tier-1 still capture (HITL-validation
// evidence). Snaps a Game-view PNG under the consuming project's
// .captain-sdlc/trace/media/ and hands back a trace-relative path the SKILL can
// attach to ath-trace-emit's `artifacts`.
//
// Capture is NON-BLOCKING by design: ScreenCapture.CaptureScreenshot defers the
// grab to end-of-frame and writes the file asynchronously, so a synchronous MCP
// body must never spin waiting for it — that would starve the very frame loop
// that fires end-of-frame (the tool runs inside MainThread.Instance.Run, on the
// main thread). So `capture` registers the expected path and returns
// immediately with Status=pending + a CaptureId; the caller confirms the file
// landed via action="query", which polls the disk (size + PNG header) without
// blocking. Path resolution + safety + the PNG header read live in the pure
// AthMediaUtil so they are unit-testable without a live Editor.
//
// PENDING LIVE-VERIFY: that ScreenCapture.CaptureScreenshot honors an ABSOLUTE
// output path and targets the Game view in PlayMode. If it does not, the
// documented fallback is to route the grab through AthBridge as a
// WaitForEndOfFrame coroutine using CaptureScreenshotAsTexture → EncodeToPNG →
// File.WriteAllBytes (which also yields dimensions directly).

#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEditor;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthSnap
    {
        public sealed class Result
        {
            public string Status       = "ok";   // ok | pending | not_in_playmode | not_found | unsafe_path | bad_action:<a> | io_error:<type>:<msg>
            public string Action       = "";      // capture | query (echoed)
            public string CaptureId    = "";
            public string Path         = "";      // trace-relative, e.g. media/snap_<ts>.png
            public string AbsolutePath = "";
            public int    Width;
            public int    Height;
            public long   SizeBytes;
        }

        // CaptureId -> (absolute path, trace-relative path), registered at
        // capture and polled at query. Concurrent because MCP calls can overlap
        // before MainThread marshals them. Cleared on domain reload — query
        // accepts a trace-relative `path` as a fallback for that case.
        static readonly ConcurrentDictionary<string, (string abs, string rel)> _pending =
            new ConcurrentDictionary<string, (string abs, string rel)>();

        [McpPluginTool(
            "ath-snap",
            Title = "AI Test Harness / Snap"
        )]
        [Description(
            "Capture a Game-view screenshot as PNG evidence for HITL validation, written under the " +
            "consuming project's .captain-sdlc/trace/media/ and referenced — trace-relative, e.g. " +
            "'media/snap_<ts>.png' — from ath-trace-emit's `artifacts`. Two actions. `capture` (default) " +
            "requests the screenshot and returns IMMEDIATELY with Status=pending and a CaptureId: Unity " +
            "writes the PNG at end-of-frame asynchronously, so the tool never blocks. `query` reports " +
            "whether a prior capture has landed — Status=ok with Width/Height/SizeBytes once the file " +
            "exists and its PNG header is readable, otherwise pending. Pass the CaptureId (preferred) or " +
            "the trace-relative Path (fallback, e.g. after a domain reload); `path` is never absolute. " +
            "`capture` requires PlayMode (Status=not_in_playmode otherwise). " +
            "Statuses: ok | pending | not_in_playmode | not_found | unsafe_path | io_error:<type>:<msg>.")]
        public Result Snap(
            [Description("'capture' (default) requests a Game-view screenshot; 'query' checks a prior capture's status.")]
            string action = "capture",
            [Description("Optional label folded into the filename (sanitized to [A-Za-z0-9_-]). Capture only.")]
            string label = "",
            [Description("CaptureId returned by a prior capture. Query only; preferred over `path`.")]
            string captureId = "",
            [Description("Trace-relative path of a prior capture, e.g. 'media/snap_<ts>.png'. Query fallback when the CaptureId is unknown. Never absolute.")]
            string path = "")
        {
            var act = (action ?? "").Trim().ToLowerInvariant();
            if (act.Length == 0) act = "capture";

            return MainThread.Instance.Run(() =>
            {
                var res = new Result { Action = act };
                try
                {
                    switch (act)
                    {
                        case "capture": return Capture(res, label);
                        case "query":   return Query(res, captureId, path);
                        default:
                            res.Status = "bad_action:" + act;
                            return res;
                    }
                }
                catch (Exception ex)
                {
                    res.Status = "io_error:" + ex.GetType().Name + ":" + ex.Message;
                    Debug.LogWarning($"[AthSnap] {act} failed: {ex.GetType().Name}: {ex.Message}");
                    return res;
                }
            });
        }

        static Result Capture(Result res, string label)
        {
            if (!EditorApplication.isPlaying)
            {
                res.Status = "not_in_playmode";
                return res;
            }

            var root     = AthTraceEmitter.ResolveProjectRoot(Application.dataPath);
            var mediaDir = AthMediaUtil.EnsureMediaDir(root);

            var stamp    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fff", CultureInfo.InvariantCulture);
            var safe     = AthMediaUtil.SanitizeLabel(label);
            var fileName = safe.Length > 0 ? $"snap_{stamp}_{safe}.png" : $"snap_{stamp}.png";

            var rel = AthMediaUtil.TraceRelativeMediaPath(fileName);
            if (!AthMediaUtil.IsSafeTraceRelativeArtifact(rel))
            {
                res.Status = "unsafe_path";   // defensive — sanitized name should always pass
                return res;
            }
            var abs = Path.Combine(mediaDir, fileName);

            // Fire-and-return: Unity grabs the Game view at end-of-frame and
            // writes the PNG asynchronously. Confirm via action="query".
            ScreenCapture.CaptureScreenshot(abs);

            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            _pending[id] = (abs, rel);

            res.Status       = "pending";
            res.CaptureId    = id;
            res.Path         = rel;
            res.AbsolutePath = abs;
            return res;
        }

        static Result Query(Result res, string captureId, string path)
        {
            captureId = (captureId ?? "").Trim();
            path      = (path ?? "").Trim();

            string abs;
            string rel;

            if (captureId.Length > 0 && _pending.TryGetValue(captureId, out var entry))
            {
                abs = entry.abs;
                rel = entry.rel;
                res.CaptureId = captureId;
            }
            else if (path.Length > 0)
            {
                // Registry-loss fallback (e.g. domain reload). Trace-relative
                // only — ResolveTraceRelative returns null for absolute/unsafe.
                var root     = AthTraceEmitter.ResolveProjectRoot(Application.dataPath);
                var resolved = AthMediaUtil.ResolveTraceRelative(root, path);
                if (resolved == null)
                {
                    res.Status = "unsafe_path";
                    return res;
                }
                abs = resolved;
                rel = path;
            }
            else
            {
                res.Status = "not_found";
                return res;
            }

            res.Path         = rel;
            res.AbsolutePath = abs;

            if (!File.Exists(abs))
            {
                res.Status = "pending";   // not flushed yet
                return res;
            }

            res.SizeBytes = new FileInfo(abs).Length;
            if (res.SizeBytes <= 0)
            {
                res.Status = "pending";
                return res;
            }

            if (AthMediaUtil.TryReadPngSize(abs, out var w, out var h, out var pngStatus))
            {
                res.Width  = w;
                res.Height = h;
                res.Status = "ok";
            }
            else
            {
                res.Status = pngStatus.Length > 0 ? pngStatus : "pending";
            }
            return res;
        }
    }
}
