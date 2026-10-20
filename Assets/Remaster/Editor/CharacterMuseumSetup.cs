using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    public static class CharacterMuseumSetup
    {
        [Serializable] class Catalog {public Clip[] clips;public Move[] moves;public int[] aliases;}
        [Serializable] class Clip {public string key,path;}
        [Serializable] class Move {public int id,transition;public string name,key,animation,details;public Cancel[] cancels;}
        [Serializable] class Cancel {public string input;public int move;public int[] window;}
        const string Scene="Assets/Remaster/Scenes/CharacterMuseum.unity";
        [MenuItem("Remaster/Open character museum")]
        public static void Open(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(Scene);}
        public static void Build()
        {
            AssetDatabase.Refresh();
            JinCostumeImporter.Import();
            KazuyaImporter.Import();
            const string folder="Assets/Remaster/Museum";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var jin=BuildCharacter(folder+"/Jin.asset","Jin Kazama",folder+"/JinFull/catalog.json",JinCostumeImporter.LoadCostumes());
            var kazuya=BuildCharacter(folder+"/Kazuya.asset","Kazuya Mishima",folder+"/KazuyaFull/catalog.json",KazuyaImporter.LoadCostumes());
            // SaveAssets can replace newly created ScriptableObject handles; reload both
            // exhibits before exercising the scene so validation uses serialized data.
            jin=AssetDatabase.LoadAssetAtPath<MuseumCharacter>(folder+"/Jin.asset");
            kazuya=AssetDatabase.LoadAssetAtPath<MuseumCharacter>(folder+"/Kazuya.asset");
            var characters=new[]{jin,kazuya};
            foreach(var value in characters)
                if(!value || value.actions==null || value.actions.Length==0 || value.costumes==null || value.costumes.Length==0 || !value.costumes[0].prefab)
                    throw new Exception("Invalid serialized museum exhibit: "+(value?value.displayName:"null")+" actions="+(value&&value.actions!=null?value.actions.Length:0)+" costumes="+(value&&value.costumes!=null?value.costumes.Length:0));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // NewScene unloads assets that are not yet referenced by scene objects.
            characters=new[]{AssetDatabase.LoadAssetAtPath<MuseumCharacter>(folder+"/Jin.asset"),AssetDatabase.LoadAssetAtPath<MuseumCharacter>(folder+"/Kazuya.asset")};
            var museum=new GameObject("Character Museum").AddComponent<CharacterMuseum>();museum.characters=characters;
            int frames=0,actions=0,costumes=0;
            for(int c=0;c<characters.Length;c++){museum.SelectCharacter(c);costumes+=characters[c].costumes.Length;actions+=characters[c].actions.Length;for(int a=0;a<characters[c].actions.Length;a++){museum.SelectAction(a);for(int f=0;f<museum.FrameCount;f++){museum.SetFrame(f);frames++;}}}
            while(museum.transform.childCount>0)UnityEngine.Object.DestroyImmediate(museum.transform.GetChild(museum.transform.childCount-1).gameObject);
            EditorSceneManager.SaveScene(scene,Scene);Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/museum.json","{\"passed\":true,\"characters\":2,\"costumes\":"+costumes+",\"actions\":"+actions+",\"frames\":"+frames+"}");
            Directory.CreateDirectory("Builds/Museum");var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{Scene},locationPathName="Builds/Museum/CharacterMuseum.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Museum build failed");File.WriteAllText("Validation/museum-build.json","{\"succeeded\":true,\"errors\":"+report.summary.totalErrors+"}");
        }
        static MuseumCharacter BuildCharacter(string assetPath,string displayName,string catalogPath,MuseumCharacter.Costume[] costumes)
        {
            var character=AssetDatabase.LoadAssetAtPath<MuseumCharacter>(assetPath);if(!character){character=ScriptableObject.CreateInstance<MuseumCharacter>();AssetDatabase.CreateAsset(character,assetPath);}
            character.displayName=displayName;character.prefab=costumes[0].prefab;character.costumes=costumes;
            character.boneNames=new[]{"BASE","Spine1","Hip","Neck","Head","R_Shoulder","R_Arm","R_ForeArm","R_Hand","L_Shoulder","L_Arm","L_ForeArm","L_Hand","R_UpLeg","R_Leg","R_Foot","L_UpLeg","L_Leg","L_Foot"};
            var catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText(catalogPath));
            var clips=new System.Collections.Generic.Dictionary<string,TextAsset>();
            foreach(var clip in catalog.clips)
            {var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(clip.path);if(!asset)throw new Exception("Missing clip: "+clip.key);clips.Add(clip.key,asset);}
            character.actions=new MuseumCharacter.Action[catalog.moves.Length];
            for(int i=0;i<catalog.moves.Length;i++)
            {
                var move=catalog.moves[i];
                var branches=new MuseumCharacter.Branch[move.cancels.Length];
                for(int j=0;j<branches.Length;j++)
                {
                    var c=move.cancels[j];int target=c.move;
                    if(target>=32768 && target-32768<catalog.aliases.Length)target=catalog.aliases[target-32768];
                    branches[j]=new MuseumCharacter.Branch {target=target,label=(string.IsNullOrEmpty(c.input)?"No input":c.input)+"  ["+string.Join("-",c.window)+"] -> "+target};
                }
                character.actions[i]=new MuseumCharacter.Action {name=move.name,motion=clips[move.key],moveId=move.id,transition=move.transition,animation=move.animation,details=move.details,branches=branches};
            }
            EditorUtility.SetDirty(character);AssetDatabase.SaveAssets();
            return character;
        }
    }
}
