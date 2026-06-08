// Tool_AthRecord — REAL implementation (Tier-2 motion capture). Lives in the
// optional LlamaBrainLabs.Ath.Editor.Recorder assembly, which compiles ONLY
// when com.unity.recorder is installed (defineConstraints: ATH_RECORDER,
// UNITY_MCP_READY). The `#if ATH_RECORDER` below is belt-and-suspenders — if
// this file is ever compiled without the define it simply yields nothing, and
// the base assembly's stub (compiled under `#if !ATH_RECORDER`) takes over.
//
// Wraps Unity Recorder's RecorderController to capture the Game view to an mp4
// under <project>/.captain-sdlc/trace/media/, returning a trace-relative path
// for ath-trace-emit's `artifacts`. State persists across the discrete start
// and stop MCP calls via statics (the only thing that survives between calls in
// one editor session). Lifecycle hooks flush a recording on PlayMode exit and
// before a domain reload so a clip is never left orphaned/unfinalized.
//
// PENDING LIVE-VERIFY: the exact Recorder API surface (RecorderController,
// MovieRecorderSettings.OutputFormat / GameViewInputSettings) is the 4.x shape;
// confirm it compiles and records on the Recorder version actually installed
// (4.x on Unity 2022.3, 5.x on Unity 6). If 5.x diverges, gate the divergent
// calls behind a RECORDER_5 versionDefine (see plan / ADR-0014 contingency).

#if ATH_RECORDER
#nullable enable

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthRecord
    {
        public sealed class Result
        {
            public string Status       = "ok";   // ok | already_recording | not_recording | not_in_playmode | unsafe_path | bad_action:<a> | io_error:<type>:<msg>
            public string Action       = "";      // start | stop | query (echoed)
            public string Path         = "";      // trace-relative, e.g. media/rec_<ts>.mp4
            public string AbsolutePath = "";
            public bool   Finalized;
            public long   SizeBytes;
        }

        // Active-session state, persisted across the start and stop MCP calls.
        static RecorderController?         _controller;
        static RecorderControllerSettings? _settings;
        static string?                     _activeAbsNoExt;   // active recording output, no extension
        static string?                     _lastStoppedAbs;   // last finalized .mp4, absolute

        // Size-stability tracking for finalization — no main-thread spin; the
        // stable count accrues across separate skill-issued `query` calls.
        static string? _stablePath;
        static long    _stableLastSize = -1;
        static int     _stableCount;

        [McpPluginTool(
            "ath-record",
            Title = "AI Test Harness / Record"
        )]
        [Description(
            "Tier-2 motion capture (Unity Recorder mp4) for HITL-validation footage: a SKILL brackets " +
            "the steps that demonstrate a feature with start ... stop, then attaches the clip (trace-relative " +
            "'media/<clip>.mp4') to ath-trace-emit's `artifacts`. Actions: `start` (requires PlayMode) begins " +
            "recording the Game view; `stop` ends it and reports the clip; `query` reports recording state, or " +
            "(after stop) whether the mp4 has FINALIZED — Finalized=true only once its size is non-zero and " +
            "unchanged across >=2 queries (no blocking). `query` accepts a trace-relative `path` " +
            "(media/<clip>.mp4; never absolute) as a fallback after a domain reload clears the session state. " +
            "Statuses: ok | already_recording | not_recording | not_in_playmode | io_error:<type>:<msg>.")]
        public Result Record(
            [Description("'start' | 'stop' | 'query'.")]
            string action,
            [Description("Optional clip label folded into the filename (sanitized). Start only.")]
            string label = "",
            [Description("Capture width in px; 0 = current Game-view width. Start only.")]
            int width = 0,
            [Description("Capture height in px; 0 = current Game-view height. Start only.")]
            int height = 0,
            [Description("Capture frame rate. Start only. Default 30.")]
            int frameRate = 30,
            [Description("Trace-relative clip path (e.g. 'media/<clip>.mp4') to query after a domain reload. Query only; never absolute.")]
            string path = "")
        {
            var act = (action ?? "").Trim().ToLowerInvariant();

            return MainThread.Instance.Run(() =>
            {
                var res = new Result { Action = act };
                try
                {
                    switch (act)
                    {
                        case "start": return Start(res, label, width, height, frameRate);
                        case "stop":  return Stop(res);
                        case "query": return Query(res, path);
                        default:
                            res.Status = "bad_action:" + act;
                            return res;
                    }
                }
                catch (Exception ex)
                {
                    res.Status = "io_error:" + ex.GetType().Name + ":" + ex.Message;
                    Debug.LogWarning($"[AthRecord] {act} failed: {ex.GetType().Name}: {ex.Message}");
                    SafeDisposeController();   // never leave a half-started session wedged
                    return res;
                }
            });
        }

        static Result Start(Result res, string label, int width, int height, int frameRate)
        {
            if (!EditorApplication.isPlaying)
            {
                res.Status = "not_in_playmode";
                return res;
            }
            if (_controller != null && _controller.IsRecording())
            {
                res.Status = "already_recording";
                if (_activeAbsNoExt != null)
                {
                    res.AbsolutePath = _activeAbsNoExt + ".mp4";
                    res.Path = RelOf(res.AbsolutePath);
                }
                return res;
            }

            var root     = AthTraceEmitter.ResolveProjectRoot(Application.dataPath);
            var mediaDir = AthMediaUtil.EnsureMediaDir(root);

            var stamp    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fff", CultureInfo.InvariantCulture);
            var safe     = AthMediaUtil.SanitizeLabel(label);
            var baseName = safe.Length > 0 ? $"rec_{stamp}_{safe}" : $"rec_{stamp}";
            var absNoExt = Path.Combine(mediaDir, baseName);

            var w   = width      > 0 ? width      : Mathf.Max(2, Screen.width);
            var h   = height     > 0 ? height     : Mathf.Max(2, Screen.height);
            var fps = frameRate  > 0 ? frameRate  : 30;

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie    = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name         = "AthMovie";
            movie.Enabled      = true;
            movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;   // H.264; Windows-safe
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth  = w,
                OutputHeight = h,
            };
            movie.AudioInputSettings.PreserveAudio = false;
            movie.OutputFile = absNoExt;   // Recorder appends the extension

            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = fps;

            var controller = new RecorderController(settings);
            controller.PrepareRecording();
            controller.StartRecording();

            _settings       = settings;
            _controller     = controller;
            _activeAbsNoExt = absNoExt;
            ResetStability();

            res.Status       = "ok";
            res.AbsolutePath = absNoExt + ".mp4";
            res.Path         = RelOf(res.AbsolutePath);
            return res;
        }

        static Result Stop(Result res)
        {
            // Covers stop-without-start AND stop-after-domain-reload (statics wiped).
            if (_controller == null)
            {
                res.Status = "not_recording";
                return res;
            }

            var absNoExt = _activeAbsNoExt;
            SafeDisposeController();   // stops if recording, disposes settings, nulls statics

            if (absNoExt == null)
            {
                res.Status = "not_recording";
                return res;
            }

            var abs = absNoExt + ".mp4";
            _lastStoppedAbs  = abs;
            res.AbsolutePath = abs;
            res.Path         = RelOf(abs);
            res.Finalized    = EvaluateFinalized(abs, out var size);
            res.SizeBytes    = size;
            res.Status       = "ok";
            return res;
        }

        static Result Query(Result res, string path)
        {
            path = (path ?? "").Trim();

            if (_controller != null && _controller.IsRecording())
            {
                res.Status    = "ok";
                res.Finalized = false;   // still rolling
                if (_activeAbsNoExt != null)
                {
                    res.AbsolutePath = _activeAbsNoExt + ".mp4";
                    res.Path = RelOf(res.AbsolutePath);
                }
                return res;
            }

            string abs;
            if (path.Length > 0)
            {
                var root     = AthTraceEmitter.ResolveProjectRoot(Application.dataPath);
                var resolved = AthMediaUtil.ResolveTraceRelative(root, path);   // trace-relative only; null on absolute/unsafe
                if (resolved == null)
                {
                    res.Status = "unsafe_path";
                    return res;
                }
                abs = resolved;
                res.Path = path;
            }
            else if (_lastStoppedAbs != null)
            {
                abs = _lastStoppedAbs;
                res.Path = RelOf(abs);
            }
            else
            {
                res.Status = "not_recording";
                return res;
            }

            res.AbsolutePath = abs;
            res.Finalized    = EvaluateFinalized(abs, out var size);
            res.SizeBytes    = size;
            res.Status       = "ok";
            return res;
        }

        /// <summary>
        /// The mp4 is finalized only once it exists, is non-empty, and its size
        /// is unchanged across at least two queries — guarding against the
        /// encoder still flushing right after StopRecording. Per-path; no spin.
        /// </summary>
        static bool EvaluateFinalized(string abs, out long size)
        {
            size = 0;
            if (!File.Exists(abs)) { ResetStability(); return false; }
            try { size = new FileInfo(abs).Length; }
            catch { ResetStability(); return false; }
            if (size <= 0) { ResetStability(); return false; }

            if (_stablePath == abs && _stableLastSize == size)
            {
                _stableCount++;
            }
            else
            {
                _stablePath     = abs;
                _stableLastSize = size;
                _stableCount    = 1;
            }
            return _stableCount >= 2;
        }

        static void ResetStability()
        {
            _stablePath     = null;
            _stableLastSize = -1;
            _stableCount    = 0;
        }

        static string RelOf(string abs) =>
            AthMediaUtil.MediaDirName + "/" + Path.GetFileName(abs);

        static void SafeDisposeController()
        {
            try
            {
                if (_controller != null && _controller.IsRecording())
                    _controller.StopRecording();
            }
            catch { /* best-effort flush */ }

            if (_settings != null)
            {
                try { UnityEngine.Object.DestroyImmediate(_settings); }
                catch { /* best-effort */ }
            }

            _controller     = null;
            _settings       = null;
            _activeAbsNoExt = null;
        }

        /// <summary>Flush an in-flight recording (PlayMode exit / domain reload) so
        /// the mp4 is finalized rather than orphaned. Best-effort.</summary>
        internal static void StopIfRecording()
        {
            if (_controller == null) return;
            SafeDisposeController();
        }
    }

    // Wires the cleanup hooks once when the editor assembly loads.
    [InitializeOnLoad]
    static class AthRecordLifecycle
    {
        static AthRecordLifecycle()
        {
            EditorApplication.playModeStateChanged    += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload  += OnBeforeAssemblyReload;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                Tool_AthRecord.StopIfRecording();
        }

        static void OnBeforeAssemblyReload() => Tool_AthRecord.StopIfRecording();
    }
}
#endif
