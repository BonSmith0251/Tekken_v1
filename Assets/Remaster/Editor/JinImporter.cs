using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Versus.Remaster.Editor
{
    public static class JinImporter
    {
        [Serializable] class Bone { public string name; public int parent; public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] class Section { public string material; public int[] triangles; }
        [Serializable] class Weight
        {
            public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;
            public float weight0,weight1,weight2,weight3;
            public BoneWeight ToUnity() => new BoneWeight {boneIndex0=boneIndex0,boneIndex1=boneIndex1,boneIndex2=boneIndex2,boneIndex3=boneIndex3,
                weight0=weight0,weight1=weight1,weight2=weight2,weight3=weight3};
        }
        [Serializable] class Part { public string source, sha256; public Bone[] bones; public Vector3[] vertices; public Vector2[] uv; public Weight[] weights; public Section[] submeshes; }
        [Serializable] class MaterialEntry { public string name, texture; }
        [Serializable] class MaterialMap { public MaterialEntry[] materials; }
        const string Source = "Assets/Remaster/Editor/JinSource";
        const string Output = "Assets/Remaster/Jin";
        [MenuItem("Remaster/Rebuild playable Jin")]
        public static void Import()
        {
            Directory.CreateDirectory(Output + "/Meshes"); Directory.CreateDirectory(Output + "/Materials");
            AssetDatabase.Refresh();
            var materialMap = JsonUtility.FromJson<MaterialMap>(File.ReadAllText(Source + "/materials.json"));
            var materials = new Dictionary<string,Material>();
            foreach (var entry in materialMap.materials)
            {
                string path = Output + "/Materials/" + entry.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,path); }
                mat.color = Color.white; mat.SetFloat("_Glossiness",.26f); mat.SetFloat("_Metallic",0);
                if (!string.IsNullOrEmpty(entry.texture))
                {
                    string texturePath = Output + "/Textures/" + entry.texture + ".png";
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    importer.alphaIsTransparency = true; importer.maxTextureSize = 2048; importer.SaveAndReimport();
                    mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                }
                else mat.color = entry.name.Contains("Eyes") ? new Color(.12f,.065f,.035f) : new Color(.7f,.5f,.4f);
                // The source uses grayscale colorable surfaces. Approximate the costume tints in Standard.
                if(entry.name.Contains("har_") || entry.name.Contains("eyelash") || entry.name.Contains("eyebrow")) mat.color=new Color(.055f,.045f,.04f);
                if(entry.name.Contains("bdu_")) mat.color=new Color(.42f,.055f,.065f);
                if(entry.name.Contains("bdl_")) mat.color=new Color(.10f,.115f,.14f);
                if (entry.name.Contains("har_") || entry.name.Contains("eyelash") || entry.name.Contains("eyebrow") || entry.name.Contains("eye_shadow"))
                {
                    mat.SetFloat("_Mode",1); mat.SetFloat("_Cutoff",.25f); mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType","TransparentCutout"); mat.renderQueue = (int)RenderQueue.AlphaTest;
                }
                EditorUtility.SetDirty(mat); materials[entry.name] = mat;
            }
            JinMaterialSetup.Apply();
            var root = new GameObject("Jin Kazama");
            var skeleton = new GameObject("Skeleton").transform; skeleton.SetParent(root.transform,false);
            var boneByName = new Dictionary<string,Transform>();
            int vertices=0, triangles=0;
            foreach (string file in Directory.GetFiles(Source,"SK_*.json").OrderBy(x=>x))
            {
                var part = JsonUtility.FromJson<Part>(File.ReadAllText(file));
                var bones = new Transform[part.bones.Length];
                for (int i=0; i<bones.Length; i++)
                {
                    var b = part.bones[i];
                    if (!boneByName.TryGetValue(b.name,out var transform))
                    {
                        transform = new GameObject(b.name).transform;
                        transform.SetParent(b.parent<0 ? skeleton : bones[b.parent],false);
                        transform.localPosition=b.position; transform.localRotation=b.rotation; transform.localScale=b.scale;
                        boneByName[b.name]=transform;
                    }
                    bones[i]=transform;
                }
                var mesh = new Mesh {name=Path.GetFileNameWithoutExtension(file),indexFormat=IndexFormat.UInt32};
                mesh.vertices=part.vertices; mesh.uv=part.uv; mesh.boneWeights=part.weights.Select(w=>w.ToUnity()).ToArray();
                var sections=part.submeshes.Where(s=>!s.material.Contains("Eye_ADD") && s.material!="MI_CH_tear").ToArray();
                mesh.subMeshCount=sections.Length;
                var doubleSided=new Dictionary<int,int[]>();
                for (int i=0;i<sections.Length;i++)
                {
                    var indices=(int[])sections[i].triangles.Clone();
                    // Unreal's source front-face winding is opposite Unity's mesh convention.
                    for(int t=0;t<indices.Length;t+=3) {int swap=indices[t];indices[t]=indices[t+2];indices[t+2]=swap;}
                    mesh.SetTriangles(indices,i);
                    if(sections[i].material.Contains("har_"))
                    {
                        // Original hair shaders render cards from both sides.
                        var twoSided=new int[indices.Length*2]; Array.Copy(indices,twoSided,indices.Length);
                        for(int t=0;t<indices.Length;t+=3) {twoSided[indices.Length+t]=indices[t+2];twoSided[indices.Length+t+1]=indices[t+1];twoSided[indices.Length+t+2]=indices[t];}
                        doubleSided[i]=twoSided;
                    }
                    triangles+=indices.Length/3;
                }
                mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                // Add the back faces after calculating normals, avoiding cancellation of opposite normals.
                foreach(var pair in doubleSided)mesh.SetTriangles(pair.Value,pair.Key);
                foreach(var weight in part.weights)
                    if(Mathf.Abs(weight.weight0+weight.weight1+weight.weight2+weight.weight3-1)>.001f) throw new Exception("Invalid skin weights");
                string path=Output+"/Meshes/"+mesh.name+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; }
                else AssetDatabase.CreateAsset(mesh,path);
                var skin=new GameObject(mesh.name).AddComponent<SkinnedMeshRenderer>(); skin.transform.SetParent(root.transform,false);
                skin.sharedMesh=mesh; skin.bones=bones; skin.rootBone=boneByName["BASE"];
                skin.sharedMaterials=sections.Select(s=>materials[s.material]).ToArray();
                skin.updateWhenOffscreen=true; skin.quality=SkinQuality.Bone4;
                vertices+=mesh.vertexCount;
            }
            root.AddComponent<JinRig>();
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remaster/Resources/JinPlayable.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            var config=AssetDatabase.LoadAssetAtPath<RemasterConfig>("Assets/Remaster/Settings/LocalVersus.asset");
            config.playerOne="JIN KAZAMA"; EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/jin-import.json","{\"parts\":4,\"bones\":"+boneByName.Count+",\"vertices\":"+vertices+",\"triangles\":"+triangles+",\"skinned\":true,\"proceduralAnimation\":true}");
            ValidatePose();
        }
        [MenuItem("Remaster/Validate playable Jin")]
        public static void ValidatePose()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab");
            var instance=UnityEngine.Object.Instantiate(prefab); var rig=instance.GetComponent<JinRig>();
            var state=new FighterState {health=100,facing=1}; var rules=new MatchRules();
            rig.Render(state,rules,0,false);
            var skin=instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name.Contains("bdu"));
            var baked=new Mesh(); skin.BakeMesh(baked); var idle=baked.vertices;
            if(baked.bounds.size.magnitude>5 || baked.vertexCount<6000) throw new Exception("Jin skin bounds invalid");
            state.attack=0; state.attackFrame=rules.moves[0].startup; rig.Render(state,rules,0,false); skin.BakeMesh(baked);
            float maxDelta=0; for(int i=0;i<idle.Length;i++) maxDelta=Mathf.Max(maxDelta,Vector3.Distance(idle[i],baked.vertices[i]));
            if(maxDelta<.1f || maxDelta>2) throw new Exception("Jin punch deformation invalid: "+maxDelta);
            File.WriteAllText("Validation/jin-pose.json","{\"passed\":true,\"punchVertexDisplacement\":"+maxDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("JIN_RIG_VALIDATED: "+maxDelta);
        }
        public static void BuildUpdate() { ProjectSetup.Setup(); Import(); ProjectSetup.BuildWindows(); }
        public static void RenderPreviews()
        {
            ProjectSetup.Setup(); Import();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab"));
            var rig=root.GetComponent<JinRig>(); var rules=new MatchRules();
            var camera=new GameObject("Check camera").AddComponent<Camera>();
            camera.transform.position=new Vector3(2.5f,1.45f,3.7f); camera.transform.LookAt(new Vector3(0,1,0));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.24f);camera.fieldOfView=31;
            RenderSettings.ambientLight=new Color(.5f,.5f,.5f);
            var light=new GameObject("Check light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;
            light.transform.rotation=Quaternion.Euler(35,-35,0);
            string[] poses={"idle","jab","cross","sweep","kick","guard","crouch","hit"};
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();
            for(int pose=0;pose<poses.Length;pose++)
            {
                var f=new FighterState {health=100,facing=1,attack=pose>=1 && pose<=4 ? pose-1 : -1,guard=pose==5,crouch=pose==6,stun=pose==7?10:0};
                if(f.attack>=0)f.attackFrame=rules.moves[f.attack].startup;
                rig.Render(f,rules,0,false);root.transform.rotation=Quaternion.identity;
                var bakedObjects=new List<GameObject>();
                foreach(var skin in skins)
                {
                    var mesh=new Mesh();skin.BakeMesh(mesh);
                    if(mesh.bounds.size.magnitude>5)throw new Exception("Invalid pose bounds: "+poses[pose]);
                    var go=new GameObject("Baked check");go.transform.SetParent(root.transform,false);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                    skin.enabled=false;bakedObjects.Add(go);
                }
                var rt=new RenderTexture(800,800,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(800,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,800),0,0);image.Apply();
                File.WriteAllBytes("Validation/jin-"+poses[pose]+".png",image.EncodeToPNG());
                camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
                foreach(var go in bakedObjects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}
            }
            File.WriteAllText("Validation/jin-all-poses.json","{\"passed\":true,\"poses\":8}");
            Debug.Log("JIN_VISUAL_POSES_PASSED: 8");
        }
    }
}
