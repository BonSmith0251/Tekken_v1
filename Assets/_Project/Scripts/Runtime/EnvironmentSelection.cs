using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // The environments chosen in env_scene (Resources/EnvironmentSelection.asset). Versus scenes read it
    // and play each match on one of them, picked at random; with none chosen they use the built-in arena.
    // In the editor Save writes the asset; a player build keeps its copy in persistentDataPath.
    [CreateAssetMenu(menuName = "Remaster/Environment selection")]
    public sealed class EnvironmentSelection : ScriptableObject
    {
        // The versus scene's own generated arena, listed alongside the original stages.
        public const string Arena = "arena";
        [Tooltip("Stage names (stg00…) and/or \"arena\". Empty uses the arena.")]
        public string[] environments = new string[0];

        static string PlayerFile => Path.Combine(Application.persistentDataPath, "environment-selection.json");

        public static EnvironmentSelection Load()
        {
            var asset = Resources.Load<EnvironmentSelection>("EnvironmentSelection");
            var selection = asset ? asset : CreateInstance<EnvironmentSelection>();
#if !UNITY_EDITOR
            try { if (File.Exists(PlayerFile)) { selection = Instantiate(selection); JsonUtility.FromJsonOverwrite(File.ReadAllText(PlayerFile), selection); } }
            catch (Exception e) { Debug.LogWarning("Environment selection not read: " + e.Message); }
#endif
            return selection;
        }

        public void Save()
        {
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.Contains(this))
            {
                UnityEditor.AssetDatabase.CreateAsset(this, "Assets/_Project/Resources/EnvironmentSelection.asset");
            }
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
            File.WriteAllText(PlayerFile, JsonUtility.ToJson(this));
#endif
        }

        // The environment for one match: a random saved choice that is still imported.
        public string PickForMatch(System.Random random)
        {
            var usable = (environments ?? new string[0]).Where(e => e == Arena || StageEnvironment.Exists(e)).ToArray();
            return usable.Length == 0 ? Arena : usable[random.Next(usable.Length)];
        }
    }
}
