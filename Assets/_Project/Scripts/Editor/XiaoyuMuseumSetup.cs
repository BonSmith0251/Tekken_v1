using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    public static class XiaoyuMuseumSetup
    {
        [Serializable] class StagingCatalog {public Staging[] entries;}
        [Serializable] class Staging {public int moveId,frames;public string secondary,camera;}
        [MenuItem("Remaster/Import Xiaoyu into museum")]
        static void ImportFromMenu(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())Build();}
        public static void Build()
        {
            AssetDatabase.Refresh();XiaoyuImporter.Import();
            Finish();
        }
        public static void Finish()
        {
            const string asset="Assets/_Project/Museum/Xiaoyu.asset";
            var character=CharacterMuseumSetup.BuildCharacter(asset,"Ling Xiaoyu","Assets/_Project/Museum/XiaoyuFull/catalog.json",XiaoyuImporter.LoadCostumes());
            character.characterCode="xia";character.stanceMoveId=1428;
            var staging=JsonUtility.FromJson<StagingCatalog>(File.ReadAllText("Assets/_Project/Characters/Xiaoyu/Source/Base/staging.json"));
            foreach(var take in staging.entries)
            {
                var action=character.actions.Single(a=>a.moveId==take.moveId);
                if(BitConverter.ToInt32(action.motion.bytes,4)!=take.frames)throw new InvalidDataException("Xiaoyu staging body mismatch: "+take.moveId);
                action.secondaryResource=take.secondary;action.cameraResource=take.camera;action.category="Staging";
            }
            EditorUtility.SetDirty(character);AssetDatabase.SaveAssets();
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CharacterMuseum.unity",OpenSceneMode.Single);
            var museum=UnityEngine.Object.FindFirstObjectByType<CharacterMuseum>();
            var characters=museum.characters.Where(c=>c && c.characterCode!="xia" && c.displayName!="Ling Xiaoyu").ToList();
            character=AssetDatabase.LoadAssetAtPath<MuseumCharacter>(asset);
            characters.Add(character);museum.characters=characters.ToArray();
            int frames=0;
            museum.SelectCharacter(characters.Count-1);
            for(int i=0;i<character.actions.Length;i++){museum.SelectAction(i);for(int f=0;f<museum.FrameCount;f++){museum.SetFrame(f);frames++;}}
            while(museum.transform.childCount>0)UnityEngine.Object.DestroyImmediate(museum.transform.GetChild(museum.transform.childCount-1).gameObject);
            EditorUtility.SetDirty(museum);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Validation/xiaoyu-museum.json","{\"passed\":true,\"costumes\":"+character.costumes.Length+",\"actions\":"+character.actions.Length+",\"frames\":"+frames+",\"staging\":"+staging.entries.Length+"}");
            int totalActions=characters.Sum(c=>c.actions.Length),totalCostumes=characters.Sum(c=>c.costumes.Length),totalFrames=characters.Sum(c=>c.actions.Sum(a=>BitConverter.ToInt32(a.motion.bytes,4)));
            File.WriteAllText("Validation/museum.json","{\"passed\":true,\"characters\":"+characters.Count+",\"costumes\":"+totalCostumes+",\"actions\":"+totalActions+",\"frames\":"+totalFrames+"}");
            CharacterMuseumSetup.RebuildPlayer();
        }
    }
}
