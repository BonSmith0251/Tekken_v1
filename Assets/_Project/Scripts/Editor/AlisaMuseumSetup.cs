using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    public static class AlisaMuseumSetup
    {
        [Serializable] class StagingCatalog {public Staging[] entries;}
        [Serializable] class Staging {public int moveId,frames;public string secondary,camera;}
        const string Asset="Assets/_Project/Museum/Alisa.asset", MuseumScene="Assets/_Project/Scenes/CharacterMuseum.unity";
        [MenuItem("Remaster/Import Alisa into museum")]
        static void ImportFromMenu(){AlisaImporter.Import();Finish();}
        public static void Finish()
        {
            var character=CharacterMuseumSetup.BuildCharacter(Asset,AlisaImporter.DisplayName,"Assets/_Project/Museum/AlisaFull/catalog.json",AlisaImporter.LoadCostumes());
            character.characterCode="asa";character.stanceMoveId=1456;
            var staging=JsonUtility.FromJson<StagingCatalog>(File.ReadAllText("Assets/_Project/Characters/Alisa/Source/Base/staging.json"));
            foreach(var take in staging.entries)
            {
                var action=character.actions.Single(a=>a.moveId==take.moveId);
                if(BitConverter.ToInt32(action.motion.bytes,4)!=take.frames)throw new InvalidDataException("Alisa staging body mismatch: "+take.moveId);
                action.secondaryResource=take.secondary;action.cameraResource=take.camera;action.category="Staging";
            }
            EditorUtility.SetDirty(character);AssetDatabase.SaveAssets();
            AddToMuseumScene(AssetDatabase.LoadAssetAtPath<MuseumCharacter>(Asset));
            File.WriteAllText("Validation/alisa-museum.json","{\"passed\":true,\"costumes\":"+character.costumes.Length+",\"actions\":"+character.actions.Length+",\"staging\":"+staging.entries.Length+"}");
        }
        // LocalVersus2 plays Alisa (P1) against Xiaoyu (P2). Edits the scene in place if it is open.
        public static void PrepareLocalVersus2()
        {
            const string path="Assets/_Project/Scenes/LocalVersus2.unity";
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool opened=!scene.isLoaded,userEdits=scene.isLoaded && scene.isDirty;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var game=scene.GetRootGameObjects().Select(g=>g.GetComponentInChildren<VersusGame>(true)).First(v=>v);
                game.playerOneFighter=VersusGame.Fighter.Alisa;game.playerTwoFighter=VersusGame.Fighter.Xiaoyu;
                game.alisaActions=AssetDatabase.LoadAssetAtPath<MuseumCharacter>(Asset);
                game.alisaPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Alisa/Costumes/Prefabs/Default_1P.prefab");
                if(!game.alisaActions || !game.alisaPrefab)throw new InvalidDataException("Import Alisa before preparing LocalVersus2.");
                EditorUtility.SetDirty(game);EditorSceneManager.MarkSceneDirty(scene);
                if(userEdits)Debug.LogWarning("LocalVersus2 set to Alisa vs Xiaoyu; save the scene to keep it.");
                else EditorSceneManager.SaveScene(scene);
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            File.WriteAllText("Validation/alisa-localversus2.json","{\"passed\":true,\"playerOne\":\"Alisa\",\"playerTwo\":\"Xiaoyu\",\"savedByJob\":"+(userEdits?"false":"true")+"}");
        }
        static void AddToMuseumScene(MuseumCharacter character)
        {
            EditScene(MuseumScene,scene=>
            {
                var museum=scene.GetRootGameObjects().Select(g=>g.GetComponentInChildren<CharacterMuseum>(true)).First(m=>m);
                museum.characters=museum.characters.Where(c=>c && c.characterCode!="asa").Append(character).ToArray();
                EditorUtility.SetDirty(museum);
            });
            EditScene("Assets/_Project/Scenes/staging_test.unity",scene=>
            {
                var player=scene.GetRootGameObjects().Select(g=>g.GetComponentInChildren<StagingTestPlayer>(true)).First(p=>p);
                player.characters=player.characters.Where(c=>c && c.characterCode!="asa").Append(character).ToArray();
                EditorUtility.SetDirty(player);
            });
        }
        // Edits a scene without replacing whatever scene the user has open, and never saves
        // the user's own unsaved edits along with ours.
        static void EditScene(string path,Action<UnityEngine.SceneManagement.Scene> edit)
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool opened=!scene.isLoaded,userEdits=scene.isLoaded && scene.isDirty;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                edit(scene);EditorSceneManager.MarkSceneDirty(scene);
                if(userEdits)Debug.LogWarning("Alisa added to the open scene "+path+"; save it to keep her.");
                else EditorSceneManager.SaveScene(scene);
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
    }
}
