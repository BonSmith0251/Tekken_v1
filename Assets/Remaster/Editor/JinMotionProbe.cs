using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Versus.Remaster.Editor
{
    // Diagnostic only. Does not replace the playable rig or save a prefab.
    public static class JinMotionProbe
    {
        public static readonly string[] Channels={null,null,null,"BASE","Spine1","Hip",null,"Neck","Head","R_Shoulder","R_Arm","R_ForeArm","R_Hand","L_Shoulder","L_Arm","L_ForeArm","L_Hand","R_UpLeg","R_Leg","R_Foot","L_UpLeg","L_Leg","L_Foot"};
        public static Quaternion EulerXYZ(float x,float y,float z) => Quaternion.AngleAxis(z*Mathf.Rad2Deg,Vector3.forward)*Quaternion.AngleAxis(y*Mathf.Rad2Deg,Vector3.up)*Quaternion.AngleAxis(x*Mathf.Rad2Deg,Vector3.right);
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab"));
            var bones=root.GetComponentsInChildren<Transform>().ToDictionary(b=>b.name);
            var rest=bones.ToDictionary(p=>p.Key,p=>p.Value.localRotation);
            var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.position=new Vector3(2.5f,1.45f,3.7f);camera.transform.LookAt(new Vector3(0,1,0));camera.fieldOfView=31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.24f);RenderSettings.ambientLight=new Color(.5f,.5f,.5f);
            var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,-35,0);
            Directory.CreateDirectory("Validation/MotionProbe");
            byte[] data=File.ReadAllBytes("Assets/Remaster/Editor/JinMotionSource/0127.bytes");
            for(int mode=0;mode<4;mode++)
            {
                foreach(var p in bones)p.Value.localRotation=rest[p.Key];
                for(int c=3;c<23;c++)
                {
                    if(Channels[c]==null)continue;
                    float x=BitConverter.ToSingle(data,100+c*12), y=BitConverter.ToSingle(data,104+c*12), z=BitConverter.ToSingle(data,108+c*12);
                    Quaternion q=mode==0?EulerXYZ(x,y,z):mode==1?EulerXYZ(-y,x,-z):mode==2?EulerXYZ(y,-x,-z):EulerXYZ(-x,-y,-z);
                    bones[Channels[c]].localRotation=new Quaternion(q.x,-q.z,q.y,q.w);
                }
                bones["BASE"].localPosition=new Vector3(0,0,-BitConverter.ToSingle(data,116)/1000);
                var bakedObjects=new List<GameObject>();
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh=new Mesh();skin.BakeMesh(mesh);
                    var go=new GameObject("Baked");go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;bakedObjects.Add(go);
                }
                var rt=new RenderTexture(600,600,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(600,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,600,600),0,0);image.Apply();File.WriteAllBytes("Validation/MotionProbe/mode"+mode+".png",image.EncodeToPNG());
                camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
                foreach(var go in bakedObjects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}
            }
        }
    }
}
