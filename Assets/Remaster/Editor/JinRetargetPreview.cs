using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    public sealed class JinRetargetPreview : EditorWindow
    {
        [Serializable] public class Entry { public string key,name,path; public int frames; public float maxFrameRotationDegrees; }
        [Serializable] public class Catalog { public string[] bones; public Entry[] clips; public int mappingVersion; }
        const string DefaultFolder="Assets/Remaster/Editor/RetargetedMotions";
        [SerializeField] bool combatLab;
        [SerializeField] string customClip;
        string Folder=>!string.IsNullOrEmpty(customClip)?Path.GetDirectoryName(customClip).Replace('\\','/'):
            combatLab?"Assets/Remaster/Editor/CombatPoseLab":DefaultFolder;
        Catalog catalog;
        PreviewRenderUtility preview;
        GameObject model;
        Dictionary<string,Transform> bones;
        SkinnedMeshRenderer[] skins;
        readonly List<Mesh> meshes=new List<Mesh>();
        byte[] data;
        int selected,frame;
        bool playing;
        double lastTime;
        float yaw=25;

        [MenuItem("Remaster/Jin retarget preview")]
        public static void Open()=>GetWindow<JinRetargetPreview>("Jin retarget preview").SetCombat(false);
        [MenuItem("Remaster/Combat pose converter preview")]
        public static void OpenCombat()=>GetWindow<JinRetargetPreview>("Jin retarget preview").SetCombat(true);
        public static void OpenConverted(string path)=>GetWindow<JinRetargetPreview>("Jin retarget preview").SetCombat(true,path);
        void SetCombat(bool value,string path=null)
        {
            if(combatLab==value && customClip==path)return;
            OnDisable();combatLab=value;customClip=path;data=null;catalog=null;playing=false;selected=0;
            OnEnable();Repaint();
        }
        public static void OpenClip(string key)
        {
            var window=GetWindow<JinRetargetPreview>("Jin retarget preview");
            window.SetCombat(false);
            window.Ensure();
            int index=Array.FindIndex(window.catalog.clips,c=>c.key==key);
            if(index>=0)window.Load(index);
        }
        public static bool Available(string key)=>File.Exists(DefaultFolder+"/"+key.Replace('/','_')+".bytes");
        void OnEnable(){EditorApplication.update+=Tick;}
        void OnDisable()
        {
            EditorApplication.update-=Tick;
            foreach(var mesh in meshes)DestroyImmediate(mesh);
            meshes.Clear();
            preview?.Cleanup();preview=null;model=null;
        }
        void Ensure()
        {
            if(preview!=null)return;
            if(string.IsNullOrEmpty(customClip))
                catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText(Folder+"/index.json"));
            else
            {
                var bytes=File.ReadAllBytes(customClip);
                if(bytes.Length<16)throw new InvalidDataException("Truncated converted clip");
                string report=Path.ChangeExtension(customClip,"conversion.json");
                var entry=File.Exists(report)?JsonUtility.FromJson<Entry>(File.ReadAllText(report)):new Entry();
                entry.key=Path.GetFileNameWithoutExtension(customClip);entry.name="Converted combat motion";
                entry.path=customClip;entry.frames=BitConverter.ToInt32(bytes,4);
                catalog=new Catalog {bones=CombatPoseConverterWindow.BoneNames,clips=new[]{entry},mappingVersion=1};
            }
            preview=new PreviewRenderUtility();
            preview.camera.fieldOfView=32;
            preview.camera.clearFlags=CameraClearFlags.SolidColor;
            preview.camera.backgroundColor=new Color(.12f,.15f,.19f);
            preview.lights[0].intensity=1.5f;
            preview.lights[0].transform.rotation=Quaternion.Euler(40,30,0);
            preview.lights[1].intensity=.7f;
            preview.ambientColor=new Color(.45f,.45f,.45f);
            model=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab"));
            foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>())behaviour.enabled=false;
            preview.AddSingleGO(model);
            bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
            skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach(var skin in skins)
            {
                var mesh=new Mesh();meshes.Add(mesh);
                var go=new GameObject("Retarget preview mesh");
                go.transform.SetParent(skin.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                skin.enabled=false;
            }
            if(catalog.clips.Length>0)Load(0);
        }
        void Load(int index)
        {
            selected=index;frame=0;playing=false;
            data=File.ReadAllBytes(catalog.clips[index].path);
            int count=BitConverter.ToInt32(data,8),frames=BitConverter.ToInt32(data,4);
            if(System.Text.Encoding.ASCII.GetString(data,0,4)!="JRT1" || count!=catalog.bones.Length ||
                frames!=catalog.clips[index].frames || data.Length!=16+frames*(3+count*4)*4)
                throw new InvalidDataException("Invalid retarget clip");
            Sample(0);Repaint();
        }
        void Sample(int sample)
        {
            int offset=16+sample*(3+catalog.bones.Length*4)*4;
            bones["BASE"].localPosition=new Vector3(Read(offset),Read(offset+4),Read(offset+8));
            offset+=12;
            foreach(var name in catalog.bones)
            {
                bones[name].localRotation=new Quaternion(Read(offset),Read(offset+4),Read(offset+8),Read(offset+12));
                offset+=16;
            }
            for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(meshes[i]);
        }
        float Read(int offset)=>BitConverter.ToSingle(data,offset);
        void Tick()
        {
            if(!playing || data==null)return;
            double now=EditorApplication.timeSinceStartup;
            int steps=(int)((now-lastTime)*60);
            if(steps<1)return;
            lastTime+=steps/60.0;
            frame=(frame+steps)%catalog.clips[selected].frames;Sample(frame);Repaint();
        }
        void OnGUI()
        {
            Ensure();
            EditorGUILayout.LabelField(combatLab?"Combat pose converter - corrected spherical rotations (v3)":"Jin motion preview - mapping v"+catalog.mappingVersion,EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(combatLab?
                "Compressed rotations are decoded as spherical axis-angle, then retargeted through Jin's reference rig. Reconvert clips exported before v3. Original-game visual comparison is still pending.":
                "Experimental retargeting of compatible C8 clips onto Jin. Compressed combat motion still needs a verified rotation mapping.",combatLab?MessageType.Warning:MessageType.Info);
            if(catalog.clips.Length==0)return;
            int next=EditorGUILayout.Popup("Source clip",selected,catalog.clips.Select(c=>c.key+" — "+c.name).ToArray());
            if(next!=selected)Load(next);
            if(combatLab && catalog.clips[selected].maxFrameRotationDegrees>45)
                EditorGUILayout.HelpBox("Fast joint rotation: "+catalog.clips[selected].maxFrameRotationDegrees.ToString("F1")+" degrees in one frame. Scrub the attack extension to inspect it.",MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(playing?"Pause":"Play",GUILayout.Width(70)))
            {playing=!playing;lastTime=EditorApplication.timeSinceStartup;}
            int nextFrame=EditorGUILayout.IntSlider("Frame",frame,0,catalog.clips[selected].frames-1);
            if(nextFrame!=frame){frame=nextFrame;Sample(frame);}
            EditorGUILayout.EndHorizontal();
            yaw=EditorGUILayout.Slider("View angle",yaw,-180,180);
            if(GUILayout.Button("Create Unity AnimationClip"))ExportClip();
            Rect area=GUILayoutUtility.GetRect(100,100,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
            if(Event.current.type==EventType.Repaint)
            {
                preview.BeginPreview(area,GUIStyle.none);
                PositionCamera();
                preview.Render(true);
                GUI.DrawTexture(area,preview.EndPreview(),ScaleMode.StretchToFill,false);
            }
        }
        void PositionCamera()
        {
            preview.camera.transform.position=Quaternion.Euler(0,yaw,0)*new Vector3(0,1.2f,4.2f);
            preview.camera.transform.LookAt(new Vector3(0,1,0));
            preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=20;
            preview.lights[0].transform.rotation=preview.camera.transform.rotation*Quaternion.Euler(20,-25,0);
        }
        void ExportClip()
        {
            var clip=CreateClip();
            string pathName=AssetDatabase.GenerateUniqueAssetPath(Folder+"/"+clip.name+".anim");
            AssetDatabase.CreateAsset(clip,pathName);AssetDatabase.SaveAssets();Selection.activeObject=clip;
        }
        AnimationClip CreateClip()
        {
            var entry=catalog.clips[selected];
            var clip=new AnimationClip {name=(combatLab?"UnverifiedCombat_":"Experimental_v"+catalog.mappingVersion+"_")+entry.key.Replace('/','_'),frameRate=60};
            string[] axes={"x","y","z","w"};
            foreach(var name in catalog.bones)
            {
                string path=AnimationUtility.CalculateTransformPath(bones[name],model.transform);
                int bone=Array.IndexOf(catalog.bones,name);
                for(int a=0;a<4;a++)
                {
                    var keys=new Keyframe[entry.frames];
                    for(int f=0;f<keys.Length;f++)keys[f]=new Keyframe(f/60f,Read(16+f*(3+catalog.bones.Length*4)*4+12+bone*16+a*4));
                    clip.SetCurve(path,typeof(Transform),"m_LocalRotation."+axes[a],new AnimationCurve(keys));
                }
            }
            for(int a=0;a<3;a++)
            {
                var keys=new Keyframe[entry.frames];
                for(int f=0;f<keys.Length;f++)keys[f]=new Keyframe(f/60f,Read(16+f*(3+catalog.bones.Length*4)*4+a*4));
                clip.SetCurve(AnimationUtility.CalculateTransformPath(bones["BASE"],model.transform),typeof(Transform),"m_LocalPosition."+axes[a],new AnimationCurve(keys));
            }
            clip.EnsureQuaternionContinuity();
            return clip;
        }
        // Offscreen validation uses the same sampler and skin baking as the window.
        public static void ValidateConverted(string path)
        {
            var window=CreateInstance<JinRetargetPreview>();window.combatLab=true;window.customClip=path;
            try
            {
                window.Ensure();
                int f=Math.Min(10,window.catalog.clips[0].frames-1);
                window.Sample(f);
                var expected=window.catalog.bones.ToDictionary(n=>n,n=>window.bones[n].localRotation);
                var position=window.bones["BASE"].localPosition;
                var clip=window.CreateClip();
                try
                {
                    window.Sample(0);clip.SampleAnimation(window.model,f/60f);
                    foreach(var n in window.catalog.bones)
                        if(Quaternion.Angle(expected[n],window.bones[n].localRotation)>.1f)
                            throw new Exception("Converted export mismatch: "+n);
                    if(Vector3.Distance(position,window.bones["BASE"].localPosition)>.0001f)
                        throw new Exception("Converted export root mismatch");
                }
                finally{DestroyImmediate(clip);}
            }
            finally{DestroyImmediate(window);}
        }
        public static void ValidateCombat()
        {
            var window=CreateInstance<JinRetargetPreview>();window.combatLab=true;
            try
            {
                window.Ensure();Directory.CreateDirectory("Validation/CombatPoseLab");
                for(int i=0;i<window.catalog.clips.Length;i++)
                {
                    window.Load(i);
                    foreach(int f in new[]{0,Math.Min(10,window.catalog.clips[i].frames-1)})
                    {
                        window.Sample(f);window.PositionCamera();
                        window.preview.BeginStaticPreview(new Rect(0,0,600,600));window.preview.Render(true);
                        var picture=window.preview.EndStaticPreview();
                        File.WriteAllBytes("Validation/CombatPoseLab/"+window.catalog.clips[i].key+"_"+f+".png",picture.EncodeToPNG());
                        DestroyImmediate(picture);
                    }
                    var clip=window.CreateClip();
                    try
                    {
                        int f=Math.Min(10,window.catalog.clips[i].frames-1);
                        window.Sample(f);
                        var expected=window.catalog.bones.ToDictionary(n=>n,n=>window.bones[n].localRotation);
                        window.Sample(0);clip.SampleAnimation(window.model,f/60f);
                        foreach(var n in window.catalog.bones)
                            if(Quaternion.Angle(expected[n],window.bones[n].localRotation)>.1f)
                                throw new Exception("Combat clip export mismatch: "+n);
                    }
                    finally{DestroyImmediate(clip);}
                }
                File.WriteAllText("Validation/combat-pose-converter.json","{\"toolChecksPassed\":true,\"clips\":8,\"renderedFrames\":16,\"originalGameVerified\":false}");
            }
            finally{DestroyImmediate(window);}
        }
        public static void Validate()
        {
            var window=CreateInstance<JinRetargetPreview>();
            try
            {
                window.Ensure();Directory.CreateDirectory("Validation/Retarget");
                for(int i=0;i<window.catalog.clips.Length;i++)
                {
                    window.Load(i);
                    window.Sample(window.catalog.clips[i].frames-1);
                    for(int offset=16;offset<window.data.Length;offset+=4)
                        if(float.IsNaN(window.Read(offset))||float.IsInfinity(window.Read(offset)))
                            throw new Exception("Non-finite retarget data");
                }
                foreach(string key in new[]{"character/0039","character/0040"})
                {
                    window.Load(Array.FindIndex(window.catalog.clips,c=>c.key==key));
                    foreach(int f in new[]{0,30,60})
                    {
                        window.Sample(f);window.PositionCamera();
                        window.preview.BeginStaticPreview(new Rect(0,0,600,600));
                        window.preview.Render(true);
                        var image=window.preview.EndStaticPreview();
                        File.WriteAllBytes("Validation/Retarget/"+key.Replace('/','_')+"_"+f+".png",image.EncodeToPNG());
                        DestroyImmediate(image);
                        foreach(var mesh in window.meshes)
                            foreach(var v in mesh.vertices)
                                if(float.IsNaN(v.x)||float.IsInfinity(v.x)||v.sqrMagnitude>100)
                                    throw new Exception("Invalid retarget mesh");
                    }
                }
                var exported=window.CreateClip();
                try
                {
                    window.Sample(30);
                    var expected=window.catalog.bones.ToDictionary(n=>n,n=>window.bones[n].localRotation);
                    var position=window.bones["BASE"].localPosition;
                    window.Sample(0);
                    exported.SampleAnimation(window.model,.5f);
                    foreach(var n in window.catalog.bones)
                        if(Quaternion.Angle(expected[n],window.bones[n].localRotation)>.1f)
                            throw new Exception("AnimationClip rotation differs from preview: "+n);
                    if(Vector3.Distance(position,window.bones["BASE"].localPosition)>.0001f)
                        throw new Exception("AnimationClip root differs from preview");
                }
                finally{DestroyImmediate(exported);}
                File.WriteAllText("Validation/retarget-preview.json","{\"passed\":true,\"renderedFrames\":6,\"animationClipMatchesPreview\":true,\"clips\":"+window.catalog.clips.Length+",\"gameplayRetargetingVerified\":false}");
            }
            finally{DestroyImmediate(window);}
        }
    }
}
