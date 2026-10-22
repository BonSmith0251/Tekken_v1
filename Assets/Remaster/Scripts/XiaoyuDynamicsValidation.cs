using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Versus.Remaster
{
    public sealed class XiaoyuDynamicsValidation:MonoBehaviour
    {
        [Serializable] class Result {public string costume;public int springs,garments,freeVertices,checkedMotionFrames;public float hairDegrees,clothMovement;}
        [Serializable] class Report {public bool passed=true;public Result[] costumes;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"--xiaoyu-dynamics-smoke")>=0)new GameObject("Xiaoyu dynamics validation").AddComponent<XiaoyuDynamicsValidation>();}
        void Start(){Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;StartCoroutine(Safe());}
        IEnumerator Safe()
        {
            var run=Run();while(true){Exception problem=null;bool next=false;try{next=run.MoveNext();}catch(Exception error){problem=error;}
                if(problem!=null){Debug.LogException(problem);Application.Quit(1);yield break;}if(!next)yield break;yield return run.Current;}
        }
        IEnumerator Run()
        {
            yield return null;yield return null;
            var museum=FindFirstObjectByType<CharacterMuseum>();int index=Array.FindIndex(museum.characters,c=>c.characterCode=="xia");
            museum.SelectCharacter(index);museum.PausePreview();museum.SetOriginalCamera(false);
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","XiaoyuDynamicsValidation"));Directory.CreateDirectory(folder);
            var character=museum.characters[index];var results=new List<Result>();
            for(int costume=0;costume<character.costumes.Length;costume++)
            {
                museum.SelectCostume(costume);museum.SelectAction(Array.FindIndex(character.actions,a=>a.moveId==character.stanceMoveId));
                museum.SetFrame(0);yield return null;yield return null;
                var actor=museum.transform.Cast<Transform>().Single(t=>t.gameObject.activeSelf&&t.GetComponentInChildren<SkinnedMeshRenderer>());
                var hair=actor.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Hair_",StringComparison.OrdinalIgnoreCase)&&t.name.Contains("_OPT")&&!t.name.EndsWith("end",StringComparison.OrdinalIgnoreCase)).ToArray();
                var rest=hair.Select(t=>t.localRotation).ToArray();var cloth=actor.GetComponentsInChildren<Cloth>();
                foreach(var garment in cloth)garment.enabled=false;
                yield return null;yield return new WaitForEndOfFrame();
                museum.Capture(Path.Combine(folder,"skinned-"+costume+".png"),1440,1080);
                foreach(var garment in cloth)garment.enabled=true;
                var dynamics=museum.FlexibleMotion;
                if(dynamics==null||dynamics.SpringCount<10||dynamics.GarmentCount<1||dynamics.FreeClothVertices<10)throw new Exception("Missing active hair/cloth simulation for "+character.costumes[costume].name);
                // Let Unity initialize its skinned particle coordinates before
                // measuring motion; bind-to-posed initialization is not flex.
                for(int frame=0;frame<25;frame++)yield return null;
                var initial=cloth.Select(c=>c.vertices).ToArray();
                for(int frame=0;frame<100;frame++)yield return null;
                float bend=hair.Select((t,i)=>Quaternion.Angle(rest[i],t.localRotation)).Max();float movement=0;
                for(int i=0;i<cloth.Length;i++)
                {
                    var vertices=cloth[i].vertices;var coefficients=cloth[i].coefficients;
                    for(int j=0;j<vertices.Length;j++)
                    {
                        var v=vertices[j];if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||v.magnitude>5)throw new Exception("Unstable cloth simulation");
                        if(coefficients[j].maxDistance>.001f)movement=Mathf.Max(movement,Vector3.Distance(v,initial[i][j]));
                    }
                }
                if(bend<.5f)throw new Exception("Hair remains stiff: "+character.costumes[costume].name);
                if(movement<.0001f)throw new Exception("Clothing remains stiff: "+character.costumes[costume].name);
                yield return new WaitForEndOfFrame();museum.Capture(Path.Combine(folder,"settled-"+costume+".png"),1440,1080);
                foreach(int move in new[]{1504,1512,1541})
                {
                    museum.SelectAction(Array.FindIndex(character.actions,a=>a.moveId==move));
                    for(int frame=0;frame<70;frame++){museum.SetFrame(frame%museum.FrameCount);yield return null;ValidateCloth(cloth);}
                    yield return new WaitForEndOfFrame();museum.Capture(Path.Combine(folder,"motion-"+costume+"-"+move+".png"),1440,1080);
                }
                museum.SetFrame(0);yield return null;museum.SetFrame(museum.FrameCount-1);yield return null;museum.SetFrame(0);
                for(int frame=0;frame<30;frame++){yield return null;ValidateCloth(cloth);}
                results.Add(new Result {costume=character.costumes[costume].name,springs=dynamics.SpringCount,garments=dynamics.GarmentCount,freeVertices=dynamics.FreeClothVertices,hairDegrees=bend,clothMovement=movement,checkedMotionFrames=240});
            }
            File.WriteAllText(Path.Combine(folder,"dynamics.json"),JsonUtility.ToJson(new Report {costumes=results.ToArray()},true));Application.Quit(0);
        }
        static void ValidateCloth(Cloth[] garments)
        {
            foreach(var garment in garments)foreach(var v in garment.vertices)
                if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)||v.magnitude>5)throw new Exception("Unstable garment during attack or seek");
        }
    }
}
