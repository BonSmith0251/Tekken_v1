using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    public static class BobMuseumSetup
    {
        [Serializable] class StagingCatalog {public Staging[] entries;}
        [Serializable] class Staging {public int moveId,frames;public string secondary,camera;}
        const string Asset="Assets/_Project/Museum/Bob.asset", MuseumScene="Assets/_Project/Scenes/CharacterMuseum.unity";
        [MenuItem("Remaster/Import Bob into museum")]
        static void ImportFromMenu(){BobImporter.Import();Finish();}
        public static void Finish()
        {
            var character=CharacterMuseumSetup.BuildCharacter(Asset,BobImporter.DisplayName,"Assets/_Project/Museum/BobFull/catalog.json",BobImporter.LoadCostumes());
            character.characterCode="bob";character.stanceMoveId=1443;
            var staging=JsonUtility.FromJson<StagingCatalog>(File.ReadAllText("Assets/_Project/Characters/Bob/Source/Base/staging.json"));
            foreach(var take in staging.entries)
            {
                var action=character.actions.Single(a=>a.moveId==take.moveId);
                if(BitConverter.ToInt32(action.motion.bytes,4)!=take.frames)throw new InvalidDataException("Bob staging body mismatch: "+take.moveId);
                action.secondaryResource=take.secondary;action.cameraResource=take.camera;action.category="Staging";
            }
            EditorUtility.SetDirty(character);AssetDatabase.SaveAssets();
            AddToMuseumScene(AssetDatabase.LoadAssetAtPath<MuseumCharacter>(Asset));
            File.WriteAllText("Validation/bob-museum.json","{\"passed\":true,\"costumes\":"+character.costumes.Length+",\"actions\":"+character.actions.Length+",\"staging\":"+staging.entries.Length+"}");
        }
        static void AddToMuseumScene(MuseumCharacter character)
        {
            EditScene(MuseumScene,scene=>
            {
                var museum=scene.GetRootGameObjects().Select(g=>g.GetComponentInChildren<CharacterMuseum>(true)).First(m=>m);
                museum.characters=museum.characters.Where(c=>c && c.characterCode!="bob").Append(character).ToArray();
                EditorUtility.SetDirty(museum);
            });
            EditScene("Assets/_Project/Scenes/staging_test.unity",scene=>
            {
                var player=scene.GetRootGameObjects().Select(g=>g.GetComponentInChildren<StagingTestPlayer>(true)).First(p=>p);
                player.characters=player.characters.Where(c=>c && c.characterCode!="bob").Append(character).ToArray();
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
                if(userEdits)Debug.LogWarning("Bob added to the open scene "+path+"; save it to keep her.");
                else EditorSceneManager.SaveScene(scene);
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
    }
}
