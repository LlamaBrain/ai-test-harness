#nullable enable
// AthRemoteSettings — the editor half of the two-lock design. A Project
// Settings panel + Tools menu toggle that adds/removes the ATH_REMOTE scripting
// define for the ACTIVE build target. ATH_REMOTE is the compile-time master
// enable for the exe remote-console harness: keep it OFF for ship builds, ON
// only for an RC test build.
//
// The link.xml that preserves the harness from IL2CPP managed stripping ships
// INSIDE the package (Runtime/RemoteConsole/link.xml) — always present, a no-op
// when the harness is compiled out — so there is nothing for this toggle to
// write into the host's Assets/. It manages the define (and authoring config)
// only.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor
{
    internal static class AthRemoteSettings
    {
        private const string Define   = "ATH_REMOTE";
        private const string MenuPath = "Tools/ATH/Remote Console (ATH_REMOTE)";

        private static NamedBuildTarget ActiveTarget =>
            NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

        private static bool IsEnabled(NamedBuildTarget t)
        {
            foreach (var d in PlayerSettings.GetScriptingDefineSymbols(t).Split(';'))
                if (d.Trim() == Define) return true;
            return false;
        }

        private static void SetEnabled(NamedBuildTarget t, bool on)
        {
            var defs = new List<string>(PlayerSettings.GetScriptingDefineSymbols(t).Split(';'));
            defs.RemoveAll(d => d.Trim() == Define || string.IsNullOrWhiteSpace(d));
            if (on) defs.Add(Define);
            PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", defs));
        }

        // ---- Tools menu shortcut ----
        [MenuItem(MenuPath, false)]
        private static void Toggle() => SetEnabled(ActiveTarget, !IsEnabled(ActiveTarget));

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled(ActiveTarget));
            return true;
        }

        // ---- Project Settings ▸ ATH Remote ----
        [SettingsProvider]
        public static SettingsProvider Provider()
        {
            return new SettingsProvider("Project/ATH Remote", SettingsScope.Project)
            {
                label = "ATH Remote",
                keywords = new HashSet<string>(new[] { "ATH", "remote", "console", "harness", "exe", "ATH_REMOTE" }),
                guiHandler = _ =>
                {
                    var t = ActiveTarget;
                    EditorGUILayout.HelpBox(
                        $"Modifying the ACTIVE build target: {t.TargetName}. ATH_REMOTE bakes the " +
                        "exe remote-console harness into this target's builds (dev OR non-dev release). " +
                        "Keep it OFF for ship builds; ON only for an RC test build.",
                        MessageType.Info);

                    bool on = IsEnabled(t);
                    bool newOn = EditorGUILayout.ToggleLeft($"Enable ATH_REMOTE for {t.TargetName}", on);
                    if (newOn != on) SetEnabled(t, newOn);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Authoring defaults (ProjectSettings/AthRemote.json)", EditorStyles.boldLabel);
                    var cfg = AthRemoteConfig.Load();
                    EditorGUI.BeginChangeCheck();
                    cfg.port      = EditorGUILayout.IntField("Port", cfg.port);
                    cfg.mediaDir  = EditorGUILayout.TextField("Media dir (blank = persistentDataPath)", cfg.mediaDir);
                    cfg.timeoutMs = EditorGUILayout.IntField("Response timeout (ms)", cfg.timeoutMs);
                    if (EditorGUI.EndChangeCheck()) cfg.Save();

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        "Stripping preservation ships in the package (Runtime/RemoteConsole/link.xml) — " +
                        "no host setup needed. Host requirement for non-dev RC builds: a host adapter " +
                        "gated on UNITY_EDITOR || DEVELOPMENT_BUILD must also add || ATH_REMOTE, or " +
                        "harness.state reports adapter_present=false.",
                        MessageType.Warning);
                },
            };
        }
    }
}
