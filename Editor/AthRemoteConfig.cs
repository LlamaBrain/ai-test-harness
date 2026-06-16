#nullable enable
// AthRemoteConfig — pure load/save of ProjectSettings/AthRemote.json, the
// shared config the editor toggle authors and the Node client
// (Tools~/ath-exe-client) reads. Editor-side authoring defaults only; the
// built player is configured by launch args/env (the Node launcher passes the
// resolved values in). Standard JSON via JsonUtility so JS JSON.parse reads it.

using System.IO;
using UnityEngine;

namespace LlamaBrainLabs.Ath.Editor
{
    [System.Serializable]
    public sealed class AthRemoteConfig
    {
        public int    port      = 8787;
        public string mediaDir  = "";    // blank => player resolves persistentDataPath
        public int    timeoutMs = 2000;

        private const string RelPath = "ProjectSettings/AthRemote.json";

        public static AthRemoteConfig Load()
        {
            try
            {
                if (File.Exists(RelPath))
                {
                    var json = File.ReadAllText(RelPath);
                    return JsonUtility.FromJson<AthRemoteConfig>(json) ?? new AthRemoteConfig();
                }
            }
            catch { /* fall through to defaults */ }
            return new AthRemoteConfig();
        }

        public void Save() => File.WriteAllText(RelPath, JsonUtility.ToJson(this, true));
    }
}
