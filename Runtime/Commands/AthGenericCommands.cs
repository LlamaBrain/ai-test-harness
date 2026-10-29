#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
using System;
using System.Collections.Generic;
using System.Text;
using IngameDebugConsole;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Commands
{
    public static class AthGenericCommands
    {
        [ConsoleMethod("harness.version", "ATH protocol and engine version")]
        public static void Version() => Version(NewId());
        [ConsoleMethod("harness.version", "Version; correlation-id form", "correlationId")]
        public static void Version(string correlationId)
        {
            AthLog.Cmd("harness.version", correlationId);
            AthLog.Ok("harness.version", correlationId,
                $"value=\"{AthLog.Esc(JsonUtility.ToJson(GetCapabilities()))}\"");
        }

        [ConsoleMethod("harness.capabilities", "Discover engine-independent ATH features")]
        public static void Capabilities() => Capabilities(NewId());
        [ConsoleMethod("harness.capabilities", "Capabilities; correlation-id form", "correlationId")]
        public static void Capabilities(string correlationId)
        {
            AthLog.Cmd("harness.capabilities", correlationId);
            AthLog.Ok("harness.capabilities", correlationId,
                $"value=\"{AthLog.Esc(JsonUtility.ToJson(GetCapabilities()))}\"");
        }

        public static AthHostCapabilities GetCapabilities()
        {
            var host = AthServices.Host;
            var declared = host != null && host.IsValid ? host.GetCapabilities() : new AthHostCapabilities();
            var result = new AthHostCapabilities
            {
                capabilities = declared.capabilities,
                stateKeys = declared.stateKeys,
                commands = declared.commands
            };
            result.protocolVersion = 1;
            result.engine = "unity";
            result.engineVersion = Application.unityVersion;
            result.buildMode = Application.isEditor ? "editor" : Debug.isDebugBuild ? "development" : "instrumented";
            result.version = AthRuntimeFlag.PackageVersion;
            result.athVersion = AthRuntimeFlag.PackageVersion;
            result.hostName = host?.HostName ?? "<none>";
            var features = new HashSet<string>(result.capabilities ?? Array.Empty<string>())
                { "state", "commands", "events", "async", "reset" };
            if (!Application.isBatchMode) features.Add("screenshot");
            var commands = new HashSet<string>(result.commands ?? Array.Empty<string>())
                { "harness.ping", "harness.state", "harness.reset", "harness.capabilities", "harness.version", "harness.execute" };
            if (features.Contains("screenshot")) commands.Add("harness.snap");
            result.capabilities = Sorted(features);
            result.commands = Sorted(commands);
            var states = new HashSet<string>(result.stateKeys ?? Array.Empty<string>())
                { "engine_name", "package_version", "host_name", "game_ready", "adapter_ready", "bridge_ready", "scene_name", "event:<name>", "async:<id>", "async_success:<id>", "last_async" };
            result.stateKeys = Sorted(states);
            return result;
        }

        [ConsoleMethod("harness.execute", "Execute registered host command with base64 UTF-8 JSON object", "command", "argumentsBase64")]
        public static void Execute(string command, string argumentsBase64) => Execute(command, argumentsBase64, NewId());
        [ConsoleMethod("harness.execute", "Execute; correlation-id form", "command", "argumentsBase64", "correlationId")]
        public static void Execute(string command, string argumentsBase64, string correlationId)
        {
            AthLog.Cmd("harness.execute", correlationId, command);
            var host = AthServices.Host;
            if (host == null || !host.IsValid) { AthLog.Err("harness.execute", correlationId, "not_ready"); return; }
            try
            {
                if (Array.IndexOf(host.GetCapabilities().commands ?? Array.Empty<string>(), command) < 0)
                { AthLog.Err("harness.execute", correlationId, "unknown_command"); return; }
                if (argumentsBase64.Length > 8192) { AthLog.Err("harness.execute", correlationId, "bad_arguments"); return; }
                var json = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(argumentsBase64)).Trim();
                // Host owns schema validation; ATH rejects non-object framing before dispatch.
                if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal))
                { AthLog.Err("harness.execute", correlationId, "bad_arguments"); return; }
                try { JsonUtility.FromJson<JsonObjectProbe>(json); }
                catch (ArgumentException) { AthLog.Err("harness.execute", correlationId, "bad_arguments"); return; }
                var result = host.Execute(command, json, correlationId);
                if (result.Status == "failed")
                    AthLog.Err("harness.execute", correlationId, $"\"{AthLog.Esc(result.Error)}\"");
                else
                    AthLog.Ok("harness.execute", correlationId,
                        $"value=\"{AthLog.Esc(result.Value)}\"{(result.Status == "dispatched" ? " [dispatched]" : "")}");
            }
            catch (FormatException) { AthLog.Err("harness.execute", correlationId, "bad_arguments"); }
            catch (DecoderFallbackException) { AthLog.Err("harness.execute", correlationId, "bad_arguments"); }
            catch (Exception ex) { AthLog.ErrException("harness.execute", correlationId, ex); }
        }

        private static string[] Sorted(HashSet<string> values)
        {
            var result = new string[values.Count]; values.CopyTo(result); Array.Sort(result, StringComparer.Ordinal); return result;
        }
        [Serializable] private sealed class JsonObjectProbe { }
        private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
#endif
