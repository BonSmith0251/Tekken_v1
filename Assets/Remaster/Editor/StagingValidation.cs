using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class StagingValidation
    {
        [Serializable] class Result {public int moveId,frames,headOutsideViewport;public string character,animation;public float maxCameraStep,maxAngleStep,maxFovStep;}
        [Serializable] class Report {public bool passed;public int entries;public Result[] actions;}
        static int index,waitFrame;
        static GameObject actor;
        static Camera camera;
        static RenderTexture target;
        static readonly List<Result> results=new List<Result>();
        static StagingValidation(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(File.Exists("Temp/validate-staging") && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete("Temp/validate-staging");
                if(!UnityEngine.Object.FindFirstObjectByType<VersusGame>())throw new InvalidOperationException("Open LocalVersus2 for staging validation.");
                SessionState.SetBool("StagingValidation",true);EditorApplication.isPlaying=true;index=0;results.Clear();
            }
            if(!SessionState.GetBool("StagingValidation",false)||!EditorApplication.isPlaying)return;
            Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate();
            if(Time.time<.1f)return;
            Directory.CreateDirectory("Validation/Staging");
            try
            {
                var game=UnityEngine.Object.FindFirstObjectByType<VersusGame>();game.enabled=false;
                if(target)
                {
                    if(Time.frameCount<waitFrame+3)return;
                    var previous=RenderTexture.active;RenderTexture.active=target;
                    var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
                    File.WriteAllBytes("Validation/Staging/"+StagingCatalog.Load().entries[index].secondary+".png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=previous;camera.targetTexture=null;
                    target.Release();UnityEngine.Object.DestroyImmediate(target);target=null;UnityEngine.Object.DestroyImmediate(actor);index++;
                }
                var entries=StagingCatalog.Load().entries;
                if(index==entries.Length)
                {
                    File.WriteAllText("Validation/Staging/playback.json",JsonUtility.ToJson(new Report{passed=true,entries=entries.Length,actions=results.ToArray()},true));
                    SessionState.SetBool("StagingValidation",false);EditorApplication.isPlaying=false;return;
                }
                foreach(var existing in UnityEngine.Object.FindObjectsByType<JinRig>(FindObjectsSortMode.None))
                    foreach(var renderer in existing.GetComponentsInChildren<Renderer>())renderer.enabled=false;
                var e=entries[index];var character=e.character=="jin"?game.jinActions:game.kazuyaActions;
                actor=UnityEngine.Object.Instantiate(character.costumes[0].prefab);
                actor.transform.position=Vector3.zero;actor.transform.rotation=Quaternion.identity;
                var rig=actor.GetComponent<JinRig>();if(!rig)rig=actor.AddComponent<JinRig>();rig.Initialize();
                var catalog=new FighterActionCatalog(character,e.character=="jin"?"Jz_":"Kz_");
                rig.fighterMoves=catalog.moves;rig.actionMotion=actor.AddComponent<FighterActionMotion>();rig.actionMotion.Initialize(catalog);
                var secondary=new SecondaryMotionTrack(actor.transform,e.secondary,e.frames);
                var track=string.IsNullOrEmpty(e.camera)?null:new EntranceCameraTrack(e.camera,e.moveId,e.frames);
                camera=Camera.main;var lens=new EntranceCameraLens(camera);
                var bones=actor.GetComponentsInChildren<Transform>();var head=bones.First(b=>b.name=="Head");
                var face=head.InverseTransformDirection(actor.transform.forward);
                var landmarks=bones.Where(b=>b.name=="L_Hand"||b.name=="R_Hand"||b.name=="L_Foot"||b.name=="R_Foot").ToArray();
                foreach(var renderer in actor.GetComponentsInChildren<Renderer>())renderer.renderingLayerMask=3;
                int action=Array.FindIndex(character.actions,a=>a.moveId==e.moveId);if(action<0)throw new Exception("Missing staging body "+e.moveId);
                var state=new FighterState{health=100,facing=1,attack=-1};
                var result=new Result{moveId=e.moveId,frames=e.frames,character=e.character,animation=e.animation};
                Vector3 last=Vector3.zero;Quaternion rotation=Quaternion.identity;float fov=0;
                int frames=e.frames-(e.landingStitch?KazuyaEntranceTimeline.RemovedFrames:0);
                for(int frame=0;frame<frames;frame++)
                {
                    Func<int,int> map=e.landingStitch?KazuyaEntranceTimeline.SourceFrame:(Func<int,int>)null;
                    rig.actionMotion.SetPresentation(action,frame,false,true,map);rig.Render(state,game.config.rules,0,false);secondary.Apply(frame,map);
                    var bounds=new Bounds(head.position+Vector3.up*.1f,Vector3.one*.44f);
                    foreach(var bone in landmarks)bounds.Encapsulate(new Bounds(bone.position,Vector3.one*.2f));
                    if(track!=null)track.Apply(camera,actor.transform,frame,lens,bounds,head.TransformDirection(face),map,head.position);
                    else {lens.SetHorizontalFov(65);camera.transform.position=bounds.center+actor.transform.forward*5;camera.transform.LookAt(bounds.center);}
                    if(float.IsNaN(camera.transform.position.x)||camera.fieldOfView<=0||camera.fieldOfView>=179)throw new Exception("Invalid camera "+e.moveId);
                    if(frame>0){result.maxCameraStep=Mathf.Max(result.maxCameraStep,Vector3.Distance(last,camera.transform.position));result.maxAngleStep=Mathf.Max(result.maxAngleStep,Quaternion.Angle(rotation,camera.transform.rotation));result.maxFovStep=Mathf.Max(result.maxFovStep,Mathf.Abs(fov-camera.fieldOfView));}
                    var projected=camera.WorldToViewportPoint(head.position);
                    if(projected.z<=0 || projected.x<0 || projected.x>1 || projected.y<0 || projected.y>1)result.headOutsideViewport++;
                    last=camera.transform.position;rotation=camera.transform.rotation;fov=camera.fieldOfView;
                }
                results.Add(result);
                target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;waitFrame=Time.frameCount;
            }
            catch(Exception e){File.WriteAllText("Validation/Staging/error.txt",e.ToString());SessionState.SetBool("StagingValidation",false);EditorApplication.isPlaying=false;Debug.LogException(e);}
        }
    }
}
