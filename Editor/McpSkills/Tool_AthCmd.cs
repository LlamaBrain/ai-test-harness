// LlamaBrainLabs.Ath.Editor — typed MCP wrapper around DebugLogConsole.ExecuteCommand.
// Port of dirigible's Tool_DirigibleCmd.cs. Appends a correlation id to the
// command, subscribes to Application.logMessageReceived during the
// synchronous ExecuteCommand, filters captured lines by id, parses the
// OK:/ERR: sentinel, returns a structured result. Dispatched-async commands
// return Status=dispatched and the caller should follow with
// /ath-wait { async_done:<id> }.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using IngameDebugConsole;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LlamaBrainLabs.Ath.Editor.McpSkills
{
    [McpPluginToolType]
    public partial class Tool_AthCmd
    {
        public sealed class Result
        {
            public string   Command = "";
            public string   CorrelationId = "";
            public string   Status = "";          // success | dispatched | failed
            public string   FailReason = "";      // when Status==failed: empty_command | not_in_playmode | exception:<type>:<msg> | command_error | no_response
            public string[] OutputLines = Array.Empty<string>();
            public long     ElapsedMs;
        }

        [McpPluginTool(
            "ath-cmd",
            Title = "AI Test Harness / Cmd"
        )]
        [Description(
            "Fires an in-game IngameDebugConsole command as if a user typed it. " +
            "Appends a correlation id to the command line and captures only log " +
            "lines tagged with that id, so concurrent ath-cmd calls do not " +
            "interfere. Returns Status=success for synchronous commands, " +
            "Status=dispatched for async-dispatch commands (use " +
            "/ath-wait { async_done:<id> } to observe completion), or " +
            "Status=failed. Pass tagId=false for fire-and-forget commands that " +
            "do NOT define a correlation-id overload — the id won't be appended, " +
            "no log capture is attempted, and Status=success means the command " +
            "was dispatched without throwing. " +
            "On Status=failed with FailReason=no_response or command_error — or " +
            "whenever the bridge appears stuck — fetch /console-get-logs before " +
            "retrying: dispatched-command exceptions land in the Unity console " +
            "even when the correlation-id capture misses them. (FailReason " +
            "starting with `exception:` already carries the root cause; no log " +
            "fetch needed for that path.)")]
        public Result Execute(
            [Description("Console command with arguments. Example: 'test.echo hello' or 'player.tp 0 1'.")]
            string command,
            [Description("Append a correlation id to the command line and capture tagged log lines. Default true (the dual-overload pattern this harness uses). Pass false only for commands that don't define a correlation-id overload — otherwise the appended id is parsed as an extra arg and the parser refuses to dispatch.")]
            bool tagId = true)
        {
            var sw = Stopwatch.StartNew();
            var result = new Result
            {
                Command = command ?? "",
                CorrelationId = Guid.NewGuid().ToString("N").Substring(0, 8),
            };

            if (string.IsNullOrWhiteSpace(command))
            {
                result.Status = "failed";
                result.FailReason = "empty_command";
                result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            return MainThread.Instance.Run(() =>
            {
                if (!UnityEditor.EditorApplication.isPlaying)
                {
                    result.Status = "failed";
                    result.FailReason = "not_in_playmode";
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }

                if (!tagId)
                {
                    // Fire-and-forget path: command doesn't take a correlation-id
                    // overload, so we MUST NOT append the id (the parser would
                    // treat it as an extra arg and refuse to dispatch). No log
                    // capture is attempted; success just means we got through
                    // ExecuteCommand without an exception.
                    try
                    {
                        DebugLogConsole.ExecuteCommand(command);
                        result.Status = "success";
                    }
                    catch (Exception ex)
                    {
                        var root = ex;
                        while (root is TargetInvocationException tex && tex.InnerException != null)
                            root = tex.InnerException;
                        result.Status = "failed";
                        result.FailReason = "exception:" + root.GetType().Name + ":" + root.Message;
                    }
                    result.CorrelationId = "";
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }

                var captured = new ConcurrentQueue<string>();
                Application.LogCallback handler = (msg, _, _) =>
                {
                    if (!string.IsNullOrEmpty(msg) && msg.Contains($"id={result.CorrelationId}"))
                        captured.Enqueue(msg);
                };
                Application.logMessageReceived += handler;
                try
                {
                    DebugLogConsole.ExecuteCommand($"{command} {result.CorrelationId}");
                    // Spin-wait briefly for log-callback delivery. Debug.Log
                    // paths queue their handlers, so we may not see CMD/OK
                    // lines until a few frames after ExecuteCommand returns.
                    var spin = Stopwatch.StartNew();
                    while (spin.ElapsedMilliseconds < 250)
                    {
                        if (captured.Any(l => l.StartsWith("OK:") || l.StartsWith("ERR:")))
                            break;
                        System.Threading.Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    // ExecuteCommand uses MethodInfo.Invoke, which wraps body
                    // throws in TargetInvocationException. Unwrap so the real
                    // cause surfaces in FailReason.
                    var root = ex;
                    while (root is TargetInvocationException tex && tex.InnerException != null)
                        root = tex.InnerException;
                    result.Status = "failed";
                    result.FailReason = "exception:" + root.GetType().Name + ":" + root.Message;
                    result.OutputLines = captured.ToArray();
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }
                finally
                {
                    Application.logMessageReceived -= handler;
                }

                result.OutputLines = captured.ToArray();
                var okLine  = result.OutputLines.FirstOrDefault(l => l.StartsWith("OK:")  || l.Contains(" OK:"));
                var errLine = result.OutputLines.FirstOrDefault(l => l.StartsWith("ERR:") || l.Contains(" ERR:"));

                if (errLine != null)
                {
                    result.Status = "failed";
                    result.FailReason = "command_error";
                }
                else if (okLine != null && okLine.Contains("[dispatched]"))
                {
                    // The literal "[dispatched]" marker is emitted by AthLog.OkDispatched
                    // for fire-and-forget async-tracked dispatches. Callers should follow
                    // up with /ath-wait { async_done:<id> } to observe completion via
                    // the bridge ring buffer.
                    result.Status = "dispatched";
                }
                else if (okLine != null)
                {
                    result.Status = "success";
                }
                else
                {
                    result.Status = "failed";
                    result.FailReason = "no_response";
                }

                result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            });
        }
    }
}
