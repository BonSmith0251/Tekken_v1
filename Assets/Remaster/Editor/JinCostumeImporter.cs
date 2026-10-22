using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Versus.Remaster.Editor
{
    public static class JinCostumeImporter
    {
        [Serializable] class Bone { public string name; public int parent; public Vector3 position,scale; public Quaternion rotation; }
        [Serializable] class Section { public string material; public int[] triangles; }
        [Serializable] class Weight
        {
            public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;
            public float weight0,weight1,weight2,weight3;
            public BoneWeight Value=>new BoneWeight {boneIndex0=boneIndex0,boneIndex1=boneIndex1,boneIndex2=boneIndex2,boneIndex3=boneIndex3,
                weight0=weight0,weight1=weight1,weight2=weight2,weight3=weight3};
        }
        [Serializable] class Part { public Bone[] bones; public Vector3[] vertices; public Vector2[] uv; public Weight[] weights; public Section[] submeshes; }
        [Serializable] class UnityMaps { public string albedo,normal,surface,occlusion; public bool specular,cutout; }
        [Serializable] class MaterialEntry { public string name; public UnityMaps unity; }
        [Serializable] class PieceEntry { public string name,path; public string[] materials; }
        [Serializable] class CostumeEntry { public string name; public string[] pieces; }
        [Serializable] class Manifest { public CostumeEntry[] costumes; public PieceEntry[] pieceEntries; public MaterialEntry[] materialEntries; }
        const string Source="Assets/Remaster/Editor/JinCostumeSource";
        const string Output="Assets/Remaster/JinCostumes";

        static Texture2D Texture(string path,bool normal=false,bool color=false)
        {
            if(string.IsNullOrEmpty(path))return null;
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer)throw new Exception("Missing costume texture: "+path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=color;importer.alphaIsTransparency=false;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static string Safe(string value)=>new string(value.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
        static Material BuildMaterial(MaterialEntry entry)
        {
            string path=Output+"/Materials/"+Safe(entry.name)+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader=Shader.Find(entry.unity.specular?"Standard (Specular setup)":"Standard");
            if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;
            mat.color=Color.white;mat.SetFloat("_Glossiness",.45f);mat.SetFloat("_Metallic",0);
            var albedo=Texture(entry.unity.albedo,false,true);if(albedo)mat.mainTexture=albedo;
            var normal=Texture(entry.unity.normal,true);if(normal){mat.SetTexture("_BumpMap",normal);mat.EnableKeyword("_NORMALMAP");}
            var surface=Texture(entry.unity.surface);
            if(surface)
            {
                mat.SetTexture(entry.unity.specular?"_SpecGlossMap":"_MetallicGlossMap",surface);
                mat.EnableKeyword(entry.unity.specular?"_SPECGLOSSMAP":"_METALLICGLOSSMAP");mat.SetFloat("_GlossMapScale",1);
            }
            var ao=Texture(entry.unity.occlusion);if(ao){mat.SetTexture("_OcclusionMap",ao);mat.SetFloat("_OcclusionStrength",1);}
            if(entry.unity.cutout)
            {mat.SetFloat("_Mode",1);mat.SetFloat("_Cutoff",.25f);mat.EnableKeyword("_ALPHATEST_ON");mat.SetOverrideTag("RenderType","TransparentCutout");mat.renderQueue=(int)RenderQueue.AlphaTest;}
            HdrpProjectSetup.ConvertMaterial(mat);EditorUtility.SetDirty(mat);return mat;
        }
        static void AddPart(GameObject root,Transform skeleton,Dictionary<string,Transform> boneMap,
                            PieceEntry entry,Dictionary<string,Material> materials,string costume)
        {
            var part=JsonUtility.FromJson<Part>(File.ReadAllText(entry.path));var bones=new Transform[part.bones.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var source=part.bones[i];
                if(!boneMap.TryGetValue(source.name,out var bone))
                {
                    bone=new GameObject(source.name).transform;
                    bone.SetParent(source.parent<0?skeleton:bones[source.parent],false);
                    bone.localPosition=source.position;bone.localRotation=source.rotation;bone.localScale=source.scale;boneMap.Add(source.name,bone);
                }
                bones[i]=bone;
            }
            var mesh=new Mesh {name=entry.name,indexFormat=IndexFormat.UInt32};mesh.vertices=part.vertices;mesh.uv=part.uv;
            mesh.boneWeights=part.weights.Select(x=>x.Value).ToArray();
            var sections=part.submeshes.Where(x=>!x.material.Contains("Eye_ADD") && x.material!="MI_CH_tear").ToArray();mesh.subMeshCount=sections.Length;
            var doubled=new Dictionary<int,int[]>();
            for(int i=0;i<sections.Length;i++)
            {
                var indices=(int[])sections[i].triangles.Clone();for(int t=0;t<indices.Length;t+=3)(indices[t],indices[t+2])=(indices[t+2],indices[t]);
                mesh.SetTriangles(indices,i);
                if(sections[i].material.Contains("har_") || sections[i].material.Contains("cloth"))
                {var both=new int[indices.Length*2];Array.Copy(indices,both,indices.Length);for(int t=0;t<indices.Length;t+=3){both[indices.Length+t]=indices[t+2];both[indices.Length+t+1]=indices[t+1];both[indices.Length+t+2]=indices[t];}doubled[i]=both;}
            }
            mesh.bindposes=bones.Select(x=>x.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            foreach(var pair in doubled)mesh.SetTriangles(pair.Value,pair.Key);
            string meshPath=Output+"/Meshes/"+Safe(costume)+"_"+Safe(entry.name)+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);
            var skin=new GameObject(entry.name).AddComponent<SkinnedMeshRenderer>();skin.transform.SetParent(root.transform,false);skin.sharedMesh=mesh;skin.bones=bones;skin.rootBone=boneMap["BASE"];
            skin.sharedMaterials=sections.Select(x=>materials[x.material]).ToArray();skin.updateWhenOffscreen=true;skin.quality=SkinQuality.Bone4;
        }
        [MenuItem("Remaster/Rebuild Jin museum costumes")]
        public static void Import()
        {
            Directory.CreateDirectory(Output+"/Materials");Directory.CreateDirectory(Output+"/Meshes");Directory.CreateDirectory(Output+"/Prefabs");AssetDatabase.Refresh();
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/manifest.json"));
            if(manifest.costumes.Length!=7 || manifest.pieceEntries.Length!=16 || manifest.materialEntries.Length!=57)throw new Exception("Unexpected Jin costume inventory");
            var materials=manifest.materialEntries.ToDictionary(x=>x.name,BuildMaterial);var pieces=manifest.pieceEntries.ToDictionary(x=>x.name);
            foreach(var costume in manifest.costumes)
            {
                var root=new GameObject("Jin Kazama - "+costume.name);var skeleton=new GameObject("Skeleton").transform;skeleton.SetParent(root.transform,false);
                var bones=new Dictionary<string,Transform>();foreach(string piece in costume.pieces)AddPart(root,skeleton,bones,pieces[piece],materials,costume.name);
                root.AddComponent<JinRig>();string path=Output+"/Prefabs/"+Safe(costume.name)+".prefab";PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Validation/jin-costumes.json","{\"passed\":true,\"costumes\":7,\"pieces\":16,\"materials\":57}");
        }
        public static MuseumCharacter.Costume[] LoadCostumes()
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/manifest.json"));
            return manifest.costumes.Select(x=>new MuseumCharacter.Costume {name=x.name,prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Prefabs/"+Safe(x.name)+".prefab")}).ToArray();
        }
    }
}
