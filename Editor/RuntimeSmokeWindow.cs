#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using LlamaBrainLabs.Ath.Editor.RuntimeSmokes;
using LlamaBrainLabs.Ath.RuntimeSmokes;
using UnityEditor;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor
{
    public sealed class RuntimeSmokeWindow : EditorWindow
    {
        private const string CorrelationPreference = "LlamaBrainLabs.Ath.RuntimeSmoke.LastCorrelation";
        private RuntimeSmokeTarget _target = RuntimeSmokeTarget.Editor;
        private RuntimeSmokeMode _mode = RuntimeSmokeMode.Fsd;
        private string _filter = string.Empty;
        private string _selectedWorkflow;
        private string _correlationId;
        private string _diagnostic;
        private string _investigationActionInFlight;
        private Vector2 _transcriptScroll;
        private RuntimeSmokeWorkflowListItem[] _workflows = Array.Empty<RuntimeSmokeWorkflowListItem>();
        private RuntimeSmokeRegistrySnapshot _snapshot;
        private RuntimeSmokeEditorWindowState _state;
        private double _nextRefresh;

        [MenuItem("Window/AI Test Harness/RuntimeSmoke")]
        public static void Open() => GetWindow<RuntimeSmokeWindow>("RuntimeSmoke");

        private void OnEnable()
        {
            _correlationId = EditorPrefs.GetString(CorrelationPreference, string.Empty);
            Refresh();
            EditorApplication.update += Tick;
        }

        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + 0.5d;
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null)
            {
                _workflows = Array.Empty<RuntimeSmokeWorkflowListItem>();
                _snapshot = null;
                _state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput { Registered = false, Filter = _filter });
                return;
            }

            try
            {
                _workflows = registry.List(_target, _mode).ToArray();
                if (!string.IsNullOrEmpty(_correlationId))
                {
                    var current = registry.GetStatus(_correlationId);
                    _snapshot = current.Found ? current : null;
                }
                else _snapshot = null;
                if (_selectedWorkflow == null && _workflows.Length > 0) _selectedWorkflow = _workflows[0].Id;
                _state = RuntimeSmokeEditorWindowReducer.Reduce(new RuntimeSmokeEditorWindowInput
                {
                    Registered = true,
                    Workflows = _workflows,
                    Filter = _filter,
                    SelectedWorkflowId = _selectedWorkflow,
                    Snapshot = _snapshot
                });
                _diagnostic = null;
            }
            catch (Exception exception) { _diagnostic = exception.Message; }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                _target = (RuntimeSmokeTarget)EditorGUILayout.EnumPopup(_target, GUILayout.Width(130));
                _mode = (RuntimeSmokeMode)EditorGUILayout.EnumPopup(_mode, GUILayout.Width(130));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(70))) Refresh();
            }

            var registry = RuntimeSmokeRegistryService.Current;
            if (registry == null)
            {
                EditorGUILayout.HelpBox("No RuntimeSmoke registry is registered for this Editor session. The window reads the same host registry used by MCP and command transports.", MessageType.Info);
                return;
            }
            if (!string.IsNullOrEmpty(_diagnostic)) EditorGUILayout.HelpBox(_diagnostic, MessageType.Error);

            _filter = EditorGUILayout.TextField("Filter", _filter);
            Refresh();
            var visible = _state == null ? Array.Empty<RuntimeSmokeWorkflowListItem>() : _state.VisibleWorkflows.ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.45f)))
                {
                    EditorGUILayout.LabelField("Available workflows", EditorStyles.boldLabel);
                    foreach (var workflow in visible)
                    {
                        var label = string.IsNullOrEmpty(workflow.Proposition) ? workflow.Id : workflow.Id + " — " + workflow.Proposition;
                        if (GUILayout.Toggle(_selectedWorkflow == workflow.Id, label, "Button")) _selectedWorkflow = workflow.Id;
                    }
                    if (visible.Length == 0) EditorGUILayout.HelpBox("No workflows match this target, mode, and filter.", MessageType.None);
                }
                using (new EditorGUILayout.VerticalScope()) DrawRunPanel(registry);
            }
        }

        private void DrawRunPanel(RuntimeSmokeRunRegistry registry)
        {
            EditorGUILayout.LabelField("Run controls", EditorStyles.boldLabel);
            var controls = _state == null ? new RuntimeSmokeEditorWindowControls() : _state.Controls;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!controls.CanStart))
                {
                    if (GUILayout.Button("Start"))
                    {
                        _correlationId = Guid.NewGuid().ToString("N");
                        var started = registry.Start(_selectedWorkflow, _correlationId, _target, _mode);
                        EditorPrefs.SetString(CorrelationPreference, _correlationId);
                        _diagnostic = started.Accepted ? null : (started.Diagnostic ?? started.Status);
                        Refresh();
                    }
                }
                using (new EditorGUI.DisabledScope(!controls.CanResume))
                {
                    if (GUILayout.Button("Resume checkpoint"))
                    {
                        var resumed = registry.Resume(_correlationId, _state.ResumeCheckpointId);
                        _diagnostic = resumed.Accepted ? null : (resumed.Diagnostic ?? resumed.Status);
                        Refresh();
                    }
                }
                using (new EditorGUI.DisabledScope(!controls.CanCancel))
                {
                    if (GUILayout.Button("Cancel"))
                    {
                        var cancelled = registry.Cancel(_correlationId);
                        _diagnostic = cancelled.Accepted ? null : (cancelled.Diagnostic ?? cancelled.Status);
                        Refresh();
                    }
                }
            }

            if (_state == null) return;
            var state = _state;
            var selected = state.SelectedWorkflow;
            if (selected != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Selected workflow", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Proposition", selected.Proposition ?? "â€”");
                EditorGUILayout.LabelField("Investigation context", selected.InvestigationSummary ?? "(not authored)");
                EditorGUILayout.LabelField("Target / mode", (selected.Target ?? "â€”") + " / " + (selected.Mode ?? "â€”"));
                EditorGUILayout.LabelField("Scenario", selected.Scenario ?? "(not authored)");
                EditorGUILayout.LabelField("Seed", selected.Seed ?? "(not authored)");
                EditorGUILayout.LabelField("Required capabilities", string.Join(", ", selected.RequiredCapabilities ?? Array.Empty<string>()));
                EditorGUILayout.LabelField("Script hash", selected.ScriptHash ?? "â€”", EditorStyles.miniLabel);
            }
            if (state.Snapshot == null) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Run", state.RunState.ToString());
            EditorGUILayout.LabelField("Correlation", state.CorrelationId ?? "—");
            EditorGUILayout.LabelField("Workflow", state.WorkflowId ?? "—");
            EditorGUILayout.LabelField("Run scenario", state.Scenario ?? "(not authored)");
            EditorGUILayout.LabelField("Run seed", state.Seed ?? "(not authored)");
            if (state.Progress.IsIndeterminate)
            {
                var phase = state.RunState == RuntimeSmokeRegistryRunState.ReviewReady ? "Paused for review" : "Running";
                var fraction = (float)(EditorApplication.timeSinceStartup % 1d);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18), fraction, phase + " · " + state.Progress.CompletedSteps + " completed");
            }
            else EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18), state.Progress.Percent / 100f, state.RunState.ToString());
            EditorGUILayout.LabelField("Active step", state.Progress.ActiveStepId ?? state.ResumeCheckpointId ?? "—");

            if (state.AssertionFailure != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Assertion failure", state.AssertionFailure.AssertionId ?? "unknown", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Classification", state.AssertionFailure.Classification ?? "<unspecified>");
                EditorGUILayout.LabelField("Expected", state.AssertionFailure.Expected ?? "<unavailable>");
                EditorGUILayout.LabelField("Actual", state.AssertionFailure.Actual ?? "<unavailable>");
                if (!string.IsNullOrEmpty(state.AssertionFailure.Diagnostic)) EditorGUILayout.HelpBox(state.AssertionFailure.Diagnostic, MessageType.Error);
            }
            if (state.FailureContext != null)
            {
                if (!string.IsNullOrEmpty(state.FailureContext.hostStateDisplay))
                    EditorGUILayout.SelectableLabel("Host state: " + state.FailureContext.hostStateDisplay +
                        (state.FailureContext.hostStateRedacted ? "  " + state.FailureContext.hostStateDigest : string.Empty),
                        EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
                if (!string.IsNullOrEmpty(state.FailureContext.reproductionCommand))
                    EditorGUILayout.SelectableLabel("Reproduce via " + state.FailureContext.reproductionTransport + ": " +
                        state.FailureContext.reproductionCommand, EditorStyles.wordWrappedMiniLabel,
                        GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
            }

            if (state.CaptureActions.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Declared capture actions", EditorStyles.boldLabel);
                foreach (var action in state.CaptureActions)
                {
                    using (new EditorGUI.DisabledScope(_investigationActionInFlight != null))
                    {
                        if (GUILayout.Button("Capture " + action.id + "  (" + action.captureLabel + ")"))
                            ExecuteCaptureAction(registry, action.id);
                    }
                }
                if (_investigationActionInFlight != null)
                    EditorGUILayout.LabelField("Running " + _investigationActionInFlight + "…", EditorStyles.miniLabel);
            }
            if (state.InvestigationActions.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Declared investigation actions", EditorStyles.boldLabel);
                foreach (var action in state.InvestigationActions)
                {
                    using (new EditorGUI.DisabledScope(_investigationActionInFlight != null))
                    {
                        if (GUILayout.Button(action.id + "  (" + action.kind + ")"))
                            ExecuteInvestigationAction(registry, action.id);
                    }
                }
                if (_investigationActionInFlight != null)
                    EditorGUILayout.LabelField("Running " + _investigationActionInFlight + "…", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Transcript", EditorStyles.boldLabel);
            _transcriptScroll = EditorGUILayout.BeginScrollView(_transcriptScroll, GUILayout.MinHeight(90), GUILayout.MaxHeight(230));
            foreach (var record in state.Transcript.Skip(Math.Max(0, state.Transcript.Count - 200)))
            {
                var detail = string.Join("  ", new[] { record.timestampUtc, record.type, record.stepId, record.status, record.failureCode, record.diagnostic }.Where(value => !string.IsNullOrEmpty(value)));
                EditorGUILayout.SelectableLabel(detail, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
                if (record.type == "assertion_failed")
                    EditorGUILayout.LabelField("Classification: " + (record.assertionClassification ?? record.failureCode ?? "<unspecified>") +
                        "    Expected: " + (record.expectedRedacted ? "<redacted>" : record.expectedDisplay ?? "<unavailable>") +
                        "    Actual: " + (record.actualRedacted ? "<redacted>" : record.actualDisplay ?? "<unavailable>"), EditorStyles.miniLabel);
                if (record.type == "investigation_action" && !string.IsNullOrEmpty(record.investigationValueDisplay))
                    EditorGUILayout.SelectableLabel("Result: " + record.investigationValueDisplay +
                        (record.investigationValueRedacted ? "  " + record.investigationValueDigest : string.Empty),
                        EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("Artifacts", EditorStyles.boldLabel);
            var artifactDirectory = state.ArtifactDirectory;
            foreach (var artifact in state.Artifacts)
            {
                var path = string.IsNullOrEmpty(artifactDirectory) ? null : Path.Combine(artifactDirectory, artifact);
                var exists = path != null && File.Exists(path);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(artifact + (exists ? "  ·  available" : "  ·  not yet available"));
                    using (new EditorGUI.DisabledScope(!exists))
                        if (GUILayout.Button("Reveal", GUILayout.Width(60))) EditorUtility.RevealInFinder(path);
                }
            }
            if (state.Artifacts.Count > 0)
                EditorGUILayout.LabelField("Run directory", string.IsNullOrEmpty(artifactDirectory) ? "Not exposed by this host sink" : artifactDirectory, EditorStyles.miniLabel);
        }

        private async void ExecuteInvestigationAction(RuntimeSmokeRunRegistry registry, string actionId)
        {
            _investigationActionInFlight = actionId;
            try
            {
                var result = await registry.ExecuteInvestigationActionAsync(_correlationId, actionId);
                _diagnostic = result.Accepted ? null : (result.Diagnostic ?? result.Status);
            }
            catch (Exception exception) { _diagnostic = exception.Message; }
            finally
            {
                _investigationActionInFlight = null;
                Refresh();
                Repaint();
            }
        }
        private async void ExecuteCaptureAction(RuntimeSmokeRunRegistry registry, string actionId)
        {
            _investigationActionInFlight = actionId;
            try
            {
                var result = await registry.CaptureAsync(_correlationId, actionId);
                _diagnostic = result.Accepted ? null : (result.Diagnostic ?? result.Status);
            }
            catch (Exception exception) { _diagnostic = exception.Message; }
            finally
            {
                _investigationActionInFlight = null;
                Refresh();
                Repaint();
            }
        }
    }

    [InitializeOnLoad]
    internal static class RuntimeSmokePlayModeLifecycle
    {
        static RuntimeSmokePlayModeLifecycle()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            RuntimeSmokeRegistryService.Current?.CancelActiveRuns(RuntimeSmokeTarget.Editor);
        }
    }
}
#endif
