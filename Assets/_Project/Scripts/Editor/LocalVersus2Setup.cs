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
        static LocalVersus2Setup() { EditorApplication.update += Poll; }
        static void Poll()
        {
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
