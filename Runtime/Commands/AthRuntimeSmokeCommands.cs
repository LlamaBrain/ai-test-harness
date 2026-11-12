#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IngameDebugConsole;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Commands
{
    /// <summary>Development-only console transport into the registered generic RuntimeSmoke registry.</summary>
    public static class AthRuntimeSmokeCommands
    {
        [ConsoleMethod("runtime_smoke.run", "Run one checked-in RuntimeSmoke workflow", "workflowId")]
        public static void Run(string workflowId) => Run(workflowId, Guid.NewGuid().ToString("D"), null);

        [ConsoleMethod("runtime_smoke.run", "Run one checked-in RuntimeSmoke workflow", "workflowId", "runCorrelation")]
        public static void Run(string workflowId, string runCorrelation) => Run(workflowId, runCorrelation, null);

        [ConsoleMethod("runtime_smoke.run", "Run a checked-in RuntimeSmoke; third arg is the console transport correlation", "workflowId", "runCorrelation", "transportCorrelation")]
        public static void Run(string workflowId, string runCorrelation, string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? (runCorrelation ?? "") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.run id={id} workflow={workflowId} run={runCorrelation}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.run id={id} reason=not_registered"); return; }
            if (!TryCurrentTarget(out var target)) { Debug.Log($"ERR:runtime_smoke.run id={id} reason=target_unavailable"); return; }
            var result = registry.Start(workflowId, runCorrelation, target, RuntimeSmokeMode.Fsd);
            Debug.Log(result.Accepted
                ? $"OK:runtime_smoke.run id={id} correlation={result.CorrelationId} status={result.Status}"
                : $"ERR:runtime_smoke.run id={id} reason={result.Status} detail=\"{Escape(result.Diagnostic)}\"");
        }

        [ConsoleMethod("runtime_smoke.status", "Read one RuntimeSmoke's transcript and current state", "runCorrelation")]
        public static void Status(string runCorrelation) => Status(runCorrelation, null);

        [ConsoleMethod("runtime_smoke.status", "Read one RuntimeSmoke; second arg is the console transport correlation", "runCorrelation", "transportCorrelation")]
        public static void Status(string runCorrelation, string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? Guid.NewGuid().ToString("N") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.status id={id} run={runCorrelation}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.status id={id} reason=not_registered"); return; }
            var status = registry.GetStatus(runCorrelation);
            if (!status.Found) { Debug.Log($"ERR:runtime_smoke.status id={id} reason=run_unknown"); return; }
            Debug.Log($"OK:runtime_smoke.status id={id} correlation={status.CorrelationId} workflow={status.WorkflowId} state={status.State.ToString().ToLowerInvariant()} step={status.ActiveStepId ?? ""} hash={status.ScriptHash}");
        }

        [ConsoleMethod("runtime_smoke.export_context", "Export failure context for a terminal failed RuntimeSmoke", "runCorrelation")]
        public static void ExportContext(string runCorrelation) => ExportContext(runCorrelation, null);

        [ConsoleMethod("runtime_smoke.export_context", "Export failure context; second arg is the console transport correlation", "runCorrelation", "transportCorrelation")]
        public static void ExportContext(string runCorrelation, string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? Guid.NewGuid().ToString("N") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.export_context id={id} run={runCorrelation}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.export_context id={id} reason=not_registered"); return; }
            var status = registry.GetStatus(runCorrelation);
            if (!status.Found || status.FailureContext == null)
            {
                Debug.Log($"ERR:runtime_smoke.export_context id={id} reason=no_failure_context");
                return;
            }
            Debug.Log($"RUNTIME_SMOKE_FAILURE_CONTEXT id={id} json={Escape(JsonUtility.ToJson(status.FailureContext))}");
            Debug.Log($"OK:runtime_smoke.export_context id={id} correlation={runCorrelation}");
        }

        [ConsoleMethod("runtime_smoke.investigate", "Execute one declared RuntimeSmoke investigation action", "runCorrelation", "actionId")]
        public static void Investigate(string runCorrelation, string actionId) => Investigate(runCorrelation, actionId, null);

        [ConsoleMethod("runtime_smoke.investigate", "Execute one declared RuntimeSmoke investigation action; third arg is the console transport correlation", "runCorrelation", "actionId", "transportCorrelation")]
        public static async void Investigate(string runCorrelation, string actionId, string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? Guid.NewGuid().ToString("N") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.investigate id={id} run={runCorrelation} action={actionId}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.investigate id={id} reason=not_registered"); return; }

            RuntimeSmokeRegistryCommandResult result;
            try
            {
                result = await registry.ExecuteInvestigationActionAsync(runCorrelation, actionId);
            }
            catch (Exception exception)
            {
                Debug.Log($"ERR:runtime_smoke.investigate id={id} reason=exception detail=\"{Escape(exception.Message)}\"");
                return;
            }

            if (result == null || !result.Accepted)
            {
                Debug.Log($"ERR:runtime_smoke.investigate id={id} reason={(result == null ? "no_result" : result.Status)} detail=\"{Escape(result?.Diagnostic)}\"");
                return;
            }

            Debug.Log($"RUNTIME_SMOKE_INVESTIGATION id={id} run={runCorrelation} action={actionId} kind={result.ValueKind} value=\"{Escape(result.ValueDisplay)}\" digest={result.ValueDigest} redacted={result.ValueRedacted.ToString().ToLowerInvariant()}");
            Debug.Log($"OK:runtime_smoke.investigate id={id} correlation={runCorrelation} action={actionId} status={result.Status}");
        }

        [ConsoleMethod("runtime_smoke.cancel", "Cancel one active RuntimeSmoke", "runCorrelation")]
        public static void Cancel(string runCorrelation) => Cancel(runCorrelation, null);

        [ConsoleMethod("runtime_smoke.cancel", "Cancel one RuntimeSmoke; second arg is the console transport correlation", "runCorrelation", "transportCorrelation")]
        public static void Cancel(string runCorrelation, string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? Guid.NewGuid().ToString("N") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.cancel id={id} run={runCorrelation}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.cancel id={id} reason=not_registered"); return; }
            var result = registry.Cancel(runCorrelation);
            Debug.Log(result.Accepted
                ? $"OK:runtime_smoke.cancel id={id} correlation={runCorrelation} status={result.Status}"
                : $"ERR:runtime_smoke.cancel id={id} reason={result.Status}");
        }

        [ConsoleMethod("runtime_smoke.resume", "Resume a RuntimeSmoke review checkpoint", "runCorrelation", "checkpointId")]
        public static void Resume(string runCorrelation, string checkpointId) => Resume(runCorrelation, checkpointId, null);

        [ConsoleMethod("runtime_smoke.resume", "Resume a RuntimeSmoke review checkpoint; third arg is the console transport correlation", "runCorrelation", "checkpointId", "transportCorrelation")]
        public static void Resume(string runCorrelation, string checkpointId, string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? Guid.NewGuid().ToString("N") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.resume id={id} run={runCorrelation} checkpoint={checkpointId}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.resume id={id} reason=not_registered"); return; }
            var result = registry.Resume(runCorrelation, checkpointId);
            Debug.Log(result.Accepted
                ? $"OK:runtime_smoke.resume id={id} correlation={runCorrelation} checkpoint={checkpointId} status={result.Status}"
                : $"ERR:runtime_smoke.resume id={id} reason={result.Status}");
        }

        [ConsoleMethod("runtime_smoke.list", "List registered RuntimeSmoke FSD workflow IDs")]
        public static void List() => List(Guid.NewGuid().ToString("N"));

        [ConsoleMethod("runtime_smoke.list", "List workflows; argument is the console transport correlation", "transportCorrelation")]
        public static void List(string transportCorrelation)
        {
            var id = string.IsNullOrWhiteSpace(transportCorrelation) ? Guid.NewGuid().ToString("N") : transportCorrelation;
            Debug.Log($"CMD:runtime_smoke.list id={id}");
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null) { Debug.Log($"ERR:runtime_smoke.list id={id} reason=not_registered"); return; }
            if (!TryCurrentTarget(out var target)) { Debug.Log($"ERR:runtime_smoke.list id={id} reason=target_unavailable"); return; }
            foreach (var workflow in registry.List(target, RuntimeSmokeMode.Fsd))
                Debug.Log($"RUNTIME_SMOKE_WORKFLOW id={id} workflow={workflow.Id} hash={workflow.ScriptHash} proposition=\"{Escape(workflow.Proposition)}\"");
            Debug.Log($"OK:runtime_smoke.list id={id}");
        }

        private static bool TryCurrentTarget(out RuntimeSmokeTarget target)
        {
#if UNITY_EDITOR
            target = RuntimeSmokeTarget.Editor;
            return true;
#elif DEVELOPMENT_BUILD
            target = RuntimeSmokeTarget.Development;
            return true;
#else
            target = RuntimeSmokeTarget.Editor;
            return false;
#endif
        }

        private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }
}
#endif
