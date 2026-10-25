using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Versus.Remaster.Editor
{
    // Katarina's costume sets from export_katarina.py / prepare_katarina_materials.py.
    public static class KatarinaImporter
    {
        [Serializable] class Bone { public string name; public int parent; public Vector3 position,scale; public Quaternion rotation; }
        [Serializable] class Section { public string material; public int[] triangles; }
        [Serializable] class Weight
        {
            public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;
            public float weight0,weight1,weight2,weight3;
            public BoneWeight Value => new BoneWeight {boneIndex0=boneIndex0,boneIndex1=boneIndex1,boneIndex2=boneIndex2,boneIndex3=boneIndex3,weight0=weight0,weight1=weight1,weight2=weight2,weight3=weight3};
        }
        [Serializable] class Part { public Bone[] bones; public Vector3[] vertices; public Vector2[] uv; public Weight[] weights; public Section[] submeshes; }
        [Serializable] class Maps { public string albedo,normal,surface,occlusion; public bool specular,cutout; }
        [Serializable] class MaterialEntry { public string name; public Maps unity; }
        [Serializable] class Piece { public string name,path; }
        [Serializable] class Costume { public string name; public string[] pieces; }
        [Serializable] class Manifest { public Costume[] costumes; public Piece[] pieceEntries; public MaterialEntry[] materialEntries; }
        const string Source="Assets/_Project/Characters/Katarina/Source/Base", Output="Assets/_Project/Characters/Katarina/Costumes";
        public const string DisplayName="Katarina Alves";
        static string Safe(string value)=>new string(value.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
        static Texture2D Texture(string path,bool normal=false,bool color=false)
        {
            if(string.IsNullOrEmpty(path))return null;
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer)throw new InvalidDataException("Missing Katarina map: "+path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=color;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Material(MaterialEntry entry)
        {
            string path=Output+"/Materials/"+Safe(entry.name)+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader=Shader.Find(entry.unity.specular?"Standard (Specular setup)":"Standard");if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
            material.color=Color.white;material.SetFloat("_Glossiness",.45f);
            material.mainTexture=Texture(entry.unity.albedo,false,true);
            var normal=Texture(entry.unity.normal,true);if(normal){material.SetTexture("_BumpMap",normal);material.EnableKeyword("_NORMALMAP");}
            var surface=Texture(entry.unity.surface);if(surface){material.SetTexture(entry.unity.specular?"_SpecGlossMap":"_MetallicGlossMap",surface);material.EnableKeyword(entry.unity.specular?"_SPECGLOSSMAP":"_METALLICGLOSSMAP");}
            var occlusion=Texture(entry.unity.occlusion);if(occlusion)material.SetTexture("_OcclusionMap",occlusion);
            if(entry.unity.cutout){material.SetFloat("_Mode",1);material.SetFloat("_Cutoff",.25f);material.EnableKeyword("_ALPHATEST_ON");material.SetOverrideTag("RenderType","TransparentCutout");material.renderQueue=(int)RenderQueue.AlphaTest;}
            HdrpProjectSetup.ConvertMaterial(material);
            // The lip is an alpha overlay on the face skin, as for Xiaoyu and Alisa.
            if(HdrpProjectSetup.IsEyeShadow(material) || entry.name.EndsWith("_lip"))HdrpProjectSetup.MakeEyeShadowTransparent(material);
            EditorUtility.SetDirty(material);return material;
        }
        // Same rule as Xiaoyu's importer: share corners only in sections stored as unshared
        // triangles; double-sided hair cards elsewhere keep their separate vertices.
        static void WeldTriangleSoup(Part part,Section[] sections)
        {
            foreach(var section in sections)
            {
                var triangles=section.triangles;
                if(triangles.Length==0 || triangles.Distinct().Count()<triangles.Length)continue;
                var first=new Dictionary<string,int>();
                for(int i=0;i<triangles.Length;i++)
                {
                    int v=triangles[i];var p=part.vertices[v];var t=part.uv[v];var w=part.weights[v];
                    string key=string.Join(",",new[]{p.x,p.y,p.z,t.x,t.y,w.weight0,w.weight1,w.weight2,w.weight3}.Select(x=>Mathf.RoundToInt(x*100000)))+"|"+w.boneIndex0+","+w.boneIndex1+","+w.boneIndex2+","+w.boneIndex3;
                    if(first.TryGetValue(key,out int shared))triangles[i]=shared;else first.Add(key,v);
                }
            }
        }
        static void AddPart(GameObject root,Transform skeleton,Dictionary<string,Transform> map,Piece entry,Dictionary<string,Material> materials,string costume)
        {
            var part=JsonUtility.FromJson<Part>(File.ReadAllText(entry.path));var bones=new Transform[part.bones.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var bone=part.bones[i];
                // Anonymous DCC helpers are local to a mesh piece.
                bool anonymous=System.Text.RegularExpressions.Regex.IsMatch(bone.name,@"^transform\d+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                Transform target=null;
                if(anonymous || !map.TryGetValue(bone.name,out target))
                {
                    target=new GameObject(anonymous?entry.name+"_"+bone.name:bone.name).transform;target.SetParent(bone.parent<0?skeleton:bones[bone.parent],false);
                    target.localPosition=bone.position;target.localRotation=bone.rotation;target.localScale=bone.scale;if(!anonymous)map.Add(bone.name,target);
                }
                bones[i]=target;
            }
            // Rebuild an existing mesh asset in place; CopySerialized keeps the old vertex buffer.
            string meshPath=Output+"/Meshes/"+Safe(costume+"_"+entry.name)+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var mesh=existing?existing:new Mesh();mesh.Clear();mesh.name=entry.name;mesh.indexFormat=IndexFormat.UInt32;
            mesh.vertices=part.vertices;mesh.uv=part.uv;mesh.boneWeights=part.weights.Select(w=>w.Value).ToArray();
            var sections=part.submeshes.Where(s=>!s.material.Contains("Eye_ADD")&&!s.material.EndsWith("_tear")&&!s.material.StartsWith("MI_unassigned__")).ToArray();mesh.subMeshCount=sections.Length;
            WeldTriangleSoup(part,sections);
            var doubleSided=new Dictionary<int,int[]>();
            for(int i=0;i<sections.Length;i++)
            {
                var indices=(int[])sections[i].triangles.Clone();
                for(int j=0;j<indices.Length;j+=3){int temporary=indices[j];indices[j]=indices[j+2];indices[j+2]=temporary;}
                mesh.SetTriangles(indices,i);
                if(sections[i].material.IndexOf("har_",StringComparison.OrdinalIgnoreCase)>=0 || sections[i].material.IndexOf("eyelash",StringComparison.OrdinalIgnoreCase)>=0)
                {
                    var both=new int[indices.Length*2];Array.Copy(indices,both,indices.Length);
                    for(int j=0;j<indices.Length;j+=3){both[indices.Length+j]=indices[j+2];both[indices.Length+j+1]=indices[j+1];both[indices.Length+j+2]=indices[j];}
                    doubleSided[i]=both;
                }
            }
            mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            foreach(var section in doubleSided)mesh.SetTriangles(section.Value,section.Key);
            if(existing)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,meshPath);
            var renderer=new GameObject(entry.name).AddComponent<SkinnedMeshRenderer>();renderer.transform.SetParent(root.transform,false);
            renderer.sharedMesh=mesh;renderer.bones=bones;renderer.rootBone=map["BASE"];
            renderer.sharedMaterials=sections.Select(s=>materials[s.material]).ToArray();renderer.updateWhenOffscreen=true;renderer.quality=SkinQuality.Bone4;
        }
        [InitializeOnLoadMethod]
        static void WatchFlags()
        {
            EditorApplication.update+=()=>
            {
                if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/import-katarina"))return;
                File.Delete("Temp/import-katarina");
                try{Import();KatarinaMuseumSetup.Finish();}catch(Exception e){Debug.LogException(e);File.WriteAllText("Temp/import-katarina-error.txt",e.ToString());}
            };
        }
        [MenuItem("Remaster/Import Katarina models")]
        public static void Import()
        {
            foreach(var directory in new[]{"Materials","Meshes","Prefabs"})Directory.CreateDirectory(Output+"/"+directory);
            AssetDatabase.Refresh();var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/manifest.json"));
            if(manifest.costumes.Length!=4 || manifest.pieceEntries.Length!=10)throw new InvalidDataException("Unexpected Katarina source inventory");
            var materials=manifest.materialEntries.ToDictionary(m=>m.name,Material);var pieces=manifest.pieceEntries.ToDictionary(p=>p.name);
            foreach(var costume in manifest.costumes)
            {
                var root=new GameObject(DisplayName+" - "+costume.name);
                try
                {
                    var skeleton=new GameObject("Skeleton").transform;skeleton.SetParent(root.transform,false);
                    var bones=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);
                    foreach(string piece in costume.pieces)AddPart(root,skeleton,bones,pieces[piece],materials,costume.name);
                    PrefabUtility.SaveAsPrefabAsset(root,Output+"/Prefabs/"+Safe(costume.name)+".prefab");
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Validation/katarina-model.json","{\"passed\":true,\"costumes\":4,\"pieces\":10,\"materials\":"+materials.Count+",\"originalSkinWeights\":true}");
        }
        public static MuseumCharacter.Costume[] LoadCostumes()
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/manifest.json"));
            return manifest.costumes.Select(c=>new MuseumCharacter.Costume {name=c.name,prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Prefabs/"+Safe(c.name)+".prefab")}).ToArray();
        }
    }
}
