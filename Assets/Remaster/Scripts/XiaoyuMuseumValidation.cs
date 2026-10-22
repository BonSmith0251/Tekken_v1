using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Versus.Remaster
{
    public sealed class XiaoyuMuseumValidation:MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--xiaoyu-smoke")<0)return;
            new GameObject("Xiaoyu validation").AddComponent<XiaoyuMuseumValidation>();
        }
        void Start(){Application.runInBackground=true;StartCoroutine(SafeRun());}
        IEnumerator SafeRun()
        {
            var runner=Run();
            while(true)
            {
                Exception error=null;bool next=false;
                try{next=runner.MoveNext();}catch(Exception exception){error=exception;}
                if(error!=null){Debug.LogException(error);Application.Quit(1);yield break;}
                if(!next)yield break;
                yield return runner.Current;
            }
        }
        IEnumerator Run()
        {
            yield return null;yield return null;
            var museum=FindFirstObjectByType<CharacterMuseum>();
            int index=Array.FindIndex(museum.characters,c=>c.characterCode=="xia");if(index<0)throw new Exception("Xiaoyu is missing");
            museum.SelectCharacter(index);museum.PausePreview();var character=museum.characters[index];
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","XiaoyuValidation"));Directory.CreateDirectory(folder);
            int stance=Array.FindIndex(character.actions,a=>a.moveId==character.stanceMoveId);
            for(int costume=0;costume<character.costumes.Length;costume++)
            {
                museum.SelectCostume(costume);museum.SelectAction(stance);museum.SetFrame(0);
                yield return null;yield return new WaitForEndOfFrame();
                museum.Capture(Path.Combine(folder,"costume-"+costume+".png"),960,540);
                foreach(var renderer in museum.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(!renderer.gameObject.activeInHierarchy)continue;
                    if(renderer.sharedMaterials.Any(m=>!m || m.shader.name!="HDRP/Lit"))throw new Exception("Xiaoyu material is missing or uses the wrong pipeline");
                    var mesh=new Mesh();renderer.BakeMesh(mesh);
                    if(mesh.vertices.Any(v=>float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||v.magnitude>10))throw new Exception("Invalid skinned Xiaoyu mesh: "+renderer.name);
                    Destroy(mesh);
                }
            }
            museum.SelectCostume(0);
            foreach(int move in new[]{1428,1504,1512,1541,1600,1700})
            {
                int action=Array.FindIndex(character.actions,a=>a.moveId==move);museum.SelectAction(action);
                foreach(float fraction in new[]{0f,.5f,1f})
                {
                    museum.SetFrame(fraction*(museum.FrameCount-1));yield return null;yield return new WaitForEndOfFrame();
                    museum.Capture(Path.Combine(folder,"body-"+move+"-"+(int)(fraction*100)+".png"),960,540);
                }
            }
            museum.SelectAction(stance);yield return null;
            var actor=museum.transform.Cast<Transform>().Single(t=>t.gameObject.activeSelf && t.GetComponentInChildren<SkinnedMeshRenderer>());
            var head=actor.GetComponentsInChildren<Transform>().Single(t=>string.Equals(t.name,"Head",StringComparison.OrdinalIgnoreCase));
            var preview=Camera.allCameras.Single(c=>c.name=="Museum camera");
            var rest=actor.GetComponentsInChildren<Transform>().Where(t=>t.name.IndexOf("Lip",StringComparison.OrdinalIgnoreCase)>=0||t.name.Contains("Thumb")||t.name.Contains("Bero")).ToDictionary(t=>t,t=>(t.localPosition,t.localRotation,t.localScale));
            int takes=0,cameras=0,checkedFrames=0,offscreen=0;
            foreach(int action in Enumerable.Range(0,character.actions.Length).Where(i=>!string.IsNullOrEmpty(character.actions[i].secondaryResource)))
            {
                museum.SelectAction(action);museum.SetOriginalCamera(true);takes++;
                if(!string.IsNullOrEmpty(character.actions[action].cameraResource))cameras++;
                for(int frame=0;frame<museum.FrameCount;frame++)
                {
                    museum.SetFrame(frame);museum.RefreshPreviewCamera();checkedFrames++;
                    var point=preview.WorldToViewportPoint(head.position+Vector3.up*.1f);
                    if(point.z<=preview.nearClipPlane || point.x<0 || point.x>1 || point.y<0 || point.y>1)offscreen++;
                    if(frame==0 || frame==museum.FrameCount/2 || frame==museum.FrameCount-1)
                    {
                        yield return null;yield return new WaitForEndOfFrame();
                        museum.Capture(Path.Combine(folder,"staging-"+character.actions[action].moveId+"-"+frame+".png"),960,540);
                    }
                }
            }
            museum.SetOriginalCamera(false);museum.SelectAction(stance);
            foreach(var pair in rest)
                if(Vector3.Distance(pair.Key.localPosition,pair.Value.Item1)>1e-5f || Quaternion.Angle(pair.Key.localRotation,pair.Value.Item2)>.01f || Vector3.Distance(pair.Key.localScale,pair.Value.Item3)>1e-5f)throw new Exception("Secondary pose was not restored: "+pair.Key.name);
            if(offscreen>0)throw new Exception("Xiaoyu staging head outside camera on "+offscreen+" frames");
            File.WriteAllText(Path.Combine(folder,"xiaoyu.json"),"{\"passed\":true,\"costumes\":"+character.costumes.Length+",\"moves\":"+character.actions.Length+",\"staging\":"+takes+",\"nativeCameras\":"+cameras+",\"stagingFrames\":"+checkedFrames+",\"offscreenFrames\":"+offscreen+",\"secondaryRestored\":true}");
            Application.Quit(0);
        }
    }
}
