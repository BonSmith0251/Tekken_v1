using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Chooses LocalVersus2's two fighters. Flag Temp/setup-localversus2 holds "<P1> <P2>" using
    // VersusGame.Fighter names (e.g. "Alisa Katarina"). Edits the scene in place when it is open
    // and never saves the user's own unsaved edits along with ours.
    [InitializeOnLoad]
    public static class LocalVersus2Setup
    {
        const string Flag = "Temp/setup-localversus2", ScenePath = "Assets/_Project/Scenes/LocalVersus2.unity";
        const string RetireFlag = "Temp/retire-localversus", OldScenePath = "Assets/_Project/Scenes/LocalVersus.unity";
        const string CommonFlag = "Temp/localversus2-common-effects", AutoFlag = "Temp/localversus2-auto-fight";

        // Flag Temp/localversus2-auto-fight holds "on" or "off": saves VersusGame.automaticFight in
        // LocalVersus2 (auto fight is the default versus mode since 2026-10-27; F2 still toggles it).
        public static void SetAutomaticFight(bool on)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("LocalVersus2 has unsaved edits; save them first.");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<VersusGame>(true)).First(v => v);
                game.automaticFight = on;
                EditorUtility.SetDirty(game);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + ScenePath);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            File.WriteAllText("Validation/localversus2-auto-fight.json", "{\"passed\":true,\"automaticFight\":" + (on ? "true" : "false") + "}");
            Debug.Log("LocalVersus2 automatic fight " + (on ? "on" : "off"));
        }

        // Flag Temp/localversus2-common-effects holds "on" or "off": saves VersusGame.commonBattleEffects in
        // LocalVersus2 (the common hit/counter/guard/punish/KO/floor/Rage set; character effects always play).
        // Never saves a scene with unsaved user edits. Report: Validation/localversus2-common-effects.json.
        public static void SetCommonBattleEffects(bool on)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("LocalVersus2 has unsaved edits; save them first.");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            // Since 2026-10-28 the switch lives in the shared tuning asset every battle reads, not in the scene.
            var tuning = BattleEffectTuning.Current;
            tuning.commonBattleEffects = on;
            tuning.Save();
            File.WriteAllText("Validation/localversus2-common-effects.json", "{\"passed\":true,\"commonBattleEffects\":" + (on ? "true" : "false") + "}");
            Debug.Log("LocalVersus2 common battle effects " + (on ? "on" : "off"));
        }

        // One-off (2026-10-27, user request): LocalVersus2 is the versus scene. Replaces the first
        // LocalVersus scene in Build Settings, moves that scene to the Recycle Bin (it is also in git),
        // and saves LocalVersus2 with its common-effects switch written out (off). Never touches a
        // scene with unsaved edits. Report: Validation/localversus-retired.json.
        public static void RetireLocalVersus()
        {
            var old = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(OldScenePath);
            if (old.IsValid() && old.isLoaded) throw new InvalidOperationException("LocalVersus is open; close it first.");
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != OldScenePath && s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("LocalVersus2 has unsaved edits; save them first.");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            bool common;
            try
            {
                var game = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<VersusGame>(true)).First(v => v);
                common = BattleEffectTuning.Current.commonBattleEffects;
                EditorUtility.SetDirty(game);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + ScenePath);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            bool trashed = !File.Exists(OldScenePath) || AssetDatabase.MoveAssetToTrash(OldScenePath);
            if (!trashed) throw new IOException("Could not move " + OldScenePath + " to the Recycle Bin.");
            File.WriteAllText("Validation/localversus-retired.json", "{\"passed\":true,\"buildScenes\":[" +
                string.Join(",", EditorBuildSettings.scenes.Select(s => "\"" + s.path + "\"")) + "],\"localVersusExists\":" +
                (File.Exists(OldScenePath) ? "true" : "false") + ",\"localVersus2CommonBattleEffects\":" + (common ? "true" : "false") + "}");
            Debug.Log("LocalVersus retired; LocalVersus2 is the versus scene.");
        }
        static LocalVersus2Setup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (File.Exists(AutoFlag) && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                string value = File.ReadAllText(AutoFlag).Trim(); File.Delete(AutoFlag);
                try { SetAutomaticFight(value != "off" && value != "0"); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText(AutoFlag + "-error.txt", e.ToString()); }
            }
            if (File.Exists(CommonFlag) && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                string value = File.ReadAllText(CommonFlag).Trim(); File.Delete(CommonFlag);
                try { SetCommonBattleEffects(value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase)); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText(CommonFlag + "-error.txt", e.ToString()); }
            }
            if (File.Exists(RetireFlag) && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(RetireFlag);
                try { RetireLocalVersus(); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText(RetireFlag + "-error.txt", e.ToString()); }
            }
            if (!File.Exists(Flag) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string text = File.ReadAllText(Flag); File.Delete(Flag);
            try
            {
                var names = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (names.Length != 2) throw new ArgumentException("Expected two fighter names, got: " + text);
                Set((VersusGame.Fighter)Enum.Parse(typeof(VersusGame.Fighter), names[0], true), (VersusGame.Fighter)Enum.Parse(typeof(VersusGame.Fighter), names[1], true));
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText(Flag + "-error.txt", e.ToString()); }
        }

        public static void Set(VersusGame.Fighter playerOne, VersusGame.Fighter playerTwo)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded, userEdits = scene.isLoaded && scene.isDirty;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<VersusGame>(true)).First(v => v);
                game.playerOneFighter = playerOne; game.playerTwoFighter = playerTwo;
                // Prefab-spawned fighters get their move catalog and Default 1P prefab assigned explicitly.
                foreach (var fighter in new[] { playerOne, playerTwo })
                {
                    if (fighter == VersusGame.Fighter.Alisa)
                    {
                        game.alisaActions = Load<MuseumCharacter>("Assets/_Project/Museum/Alisa.asset");
                        game.alisaPrefab = Load<GameObject>("Assets/_Project/Characters/Alisa/Costumes/Prefabs/Default_1P.prefab");
                    }
                    if (fighter == VersusGame.Fighter.Bob)
                    {
                        game.bobActions = Load<MuseumCharacter>("Assets/_Project/Museum/Bob.asset");
                        game.bobPrefab = Load<GameObject>("Assets/_Project/Characters/Bob/Costumes/Prefabs/Default_1P.prefab");
                    }
                    if (fighter == VersusGame.Fighter.Leo)
                    {
                        game.leoActions = Load<MuseumCharacter>("Assets/_Project/Museum/Leo.asset");
                        game.leoPrefab = Load<GameObject>("Assets/_Project/Characters/Leo/Costumes/Prefabs/Default_1P.prefab");
                    }
                    if (fighter == VersusGame.Fighter.Lidia)
                    {
                        game.lidiaActions = Load<MuseumCharacter>("Assets/_Project/Museum/Lidia.asset");
                        game.lidiaPrefab = Load<GameObject>("Assets/_Project/Characters/Lidia/Costumes/Prefabs/Default_1P.prefab");
                    }
                    if (fighter == VersusGame.Fighter.Katarina)
                    {
                        game.katarinaActions = Load<MuseumCharacter>("Assets/_Project/Museum/Katarina.asset");
                        game.katarinaPrefab = Load<GameObject>("Assets/_Project/Characters/Katarina/Costumes/Prefabs/Default_1P.prefab");
                    }
                }
                EditorUtility.SetDirty(game); EditorSceneManager.MarkSceneDirty(scene);
                if (userEdits) Debug.LogWarning("LocalVersus2 set to " + playerOne + " vs " + playerTwo + "; save the scene to keep it.");
                else EditorSceneManager.SaveScene(scene);
                File.WriteAllText("Validation/localversus2-fighters.json", "{\"passed\":true,\"playerOne\":\"" + playerOne + "\",\"playerTwo\":\"" + playerTwo + "\",\"savedByJob\":" + (userEdits ? "false" : "true") + "}");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new FileNotFoundException("Missing " + path);
            return asset;
        }
    }
}
