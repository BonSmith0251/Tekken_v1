using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class CombatHandValidation
    {
        static Camera camera;
        static RenderTexture target;
        static int sample, startFrame;
        static readonly string[] Names={"Kazuya-L","Kazuya-R","Jin-L","Jin-R"};
        static CombatHandValidation(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(File.Exists("Temp/validate-combat-hands") && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete("Temp/validate-combat-hands");sample=0;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Remaster/Scenes/LocalVersus2.unity");
                SessionState.SetBool("CombatHandValidation",true);EditorApplication.isPlaying=true;
            }
            if(!SessionState.GetBool("CombatHandValidation",false)||!EditorApplication.isPlaying)return;
            Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                var game=UnityEngine.Object.FindFirstObjectByType<VersusGame>();
                if(!game || Time.time<.5f)return;
                var entrance=(LocalVersusEntrance)typeof(VersusGame).GetField("entrance",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(game);
                if(entrance!=null && entrance.Active)return;
                Directory.CreateDirectory("Validation/CombatHands");
                if(target)
                {
                    if(Time.frameCount<startFrame+4)return;
                    var previous=RenderTexture.active;RenderTexture.active=target;
                    var image=new Texture2D(960,720,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();
                    File.WriteAllBytes("Validation/CombatHands/"+Names[sample]+".png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=previous;
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);target.Release();UnityEngine.Object.DestroyImmediate(target);target=null;
                    sample++;
                    if(sample==4){Time.timeScale=1;SessionState.SetBool("CombatHandValidation",false);EditorApplication.isPlaying=false;return;}
                }
                Time.timeScale=0;
                var rig=UnityEngine.Object.FindObjectsByType<JinRig>(FindObjectsSortMode.None).First(r=>r.name.StartsWith(sample<2?"Kazuya":"Jin"));
                var hand=rig.GetComponentsInChildren<Transform>().First(t=>t.name==(sample%2==0?"L_Hand":"R_Hand"));
                var index=rig.GetComponentsInChildren<Transform>().First(t=>t.name==(sample%2==0?"L_Index2":"R_Index2"));
                var center=(hand.position+index.position)*.5f;
                camera=new GameObject("Hand validation camera").AddComponent<Camera>();camera.CopyFrom(Camera.main);
                camera.rect=new Rect(0,0,1,1);camera.usePhysicalProperties=false;camera.fieldOfView=32;camera.nearClipPlane=.01f;
                camera.gameObject.AddComponent<HDAdditionalCameraData>();
                camera.transform.position=center+rig.transform.forward*.48f+Vector3.up*.16f;
                camera.transform.LookAt(center);
                target=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;startFrame=Time.frameCount;
            }
            catch(Exception e){File.WriteAllText("Validation/CombatHands/error.txt",e.ToString());Time.timeScale=1;SessionState.SetBool("CombatHandValidation",false);EditorApplication.isPlaying=false;Debug.LogException(e);}
        }
    }
}
