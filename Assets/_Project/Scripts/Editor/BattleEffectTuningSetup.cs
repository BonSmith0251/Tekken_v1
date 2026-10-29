using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Creates the battle effect tuning scene: a copy of LocalVersus2 that skips character select, auto-fights and
    // shows VersusGame's tuning panel, plus the shared Resources/BattleEffectTuning.asset the panel edits and every
    // battle reads. Flag Temp/create-effect-tuning-scene or the menu item. Never saves a scene with unsaved user
    // edits. Report: Validation/effect-tuning-scene.json.
    [InitializeOnLoad]
    public static class BattleEffectTuningSetup
    {
        const string Flag = "Temp/create-effect-tuning-scene";
        const string SourcePath = "Assets/_Project/Scenes/LocalVersus2.unity", ScenePath = "Assets/_Project/Scenes/BattleEffectTuning.unity";
        const string AssetPath = "Assets/_Project/Resources/BattleEffectTuning.asset";

        static BattleEffectTuningSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists(Flag) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Flag);
            try { Create(); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText(Flag + "-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Battle Effects/Create tuning scene")]
        public static void Create()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<BattleEffectTuning>(AssetPath);
            if (!tuning)
            {
                tuning = ScriptableObject.CreateInstance<BattleEffectTuning>();
                AssetDatabase.CreateAsset(tuning, AssetPath);
                AssetDatabase.SaveAssets();
            }
            var existing = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded && existing.isDirty)
                throw new InvalidOperationException("BattleEffectTuning is open with unsaved edits; save or close it first.");
            if (existing.IsValid() && existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
            var source = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SourcePath);
            if (source.IsValid() && source.isLoaded && source.isDirty)
                throw new InvalidOperationException("LocalVersus2 has unsaved edits; save them first so the tuning scene copies the saved scene.");
            if (!AssetDatabase.CopyAsset(SourcePath, ScenePath)) throw new IOException("Could not copy " + SourcePath + " to " + ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<VersusGame>(true)).First(v => v);
                game.characterSelect = false;
                game.automaticFight = true;
                game.effectTuningPanel = true;
                game.effectDemosOnly = true;
                EditorUtility.SetDirty(game);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + ScenePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            File.WriteAllText("Validation/effect-tuning-scene.json", "{\"passed\":true,\"scene\":\"" + ScenePath + "\",\"asset\":\"" + AssetPath + "\"}");
            Debug.Log("Battle effect tuning scene created: " + ScenePath + " (tuning asset " + AssetPath + ")");
        }
    }
}
