using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    public static class JinMaterialSetup
    {
        [Serializable] class Entry {public string name,albedo,normal,surface,occlusion;public bool specular;}
        [Serializable] class Catalog {public Entry[] materials;}
        static Texture2D Texture(string path,bool normal=false,bool color=false)
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer)throw new Exception("Missing texture: "+path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=color;importer.alphaIsTransparency=false;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        [MenuItem("Remaster/Update Jin texture maps")]
        public static void Apply()
        {
            AssetDatabase.Refresh();int normals=0,surfaces=0,occlusions=0,albedos=0;
            var catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText("Assets/Remaster/Editor/JinSource/unity-materials.json"));
            foreach(var e in catalog.materials)
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Remaster/Jin/Materials/"+e.name+".mat");
                if(!m)throw new Exception("Missing material: "+e.name);
                m.shader=Shader.Find(e.specular?"Standard (Specular setup)":"Standard");
                if(!string.IsNullOrEmpty(e.normal))
                {
                    m.shader=Shader.Find(e.specular?"Standard (Specular setup)":"Standard");
                    m.SetTexture("_BumpMap",Texture(e.normal,true));m.SetFloat("_BumpScale",1);m.EnableKeyword("_NORMALMAP");normals++;
                }
                if(!string.IsNullOrEmpty(e.surface))
                {
                    m.SetTexture(e.specular?"_SpecGlossMap":"_MetallicGlossMap",Texture(e.surface));
                    m.EnableKeyword(e.specular?"_SPECGLOSSMAP":"_METALLICGLOSSMAP");
                    m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");m.SetFloat("_SmoothnessTextureChannel",0);m.SetFloat("_GlossMapScale",1);surfaces++;
                }
                if(!string.IsNullOrEmpty(e.occlusion)){m.SetTexture("_OcclusionMap",Texture(e.occlusion));m.SetFloat("_OcclusionStrength",1);occlusions++;}
                if(!string.IsNullOrEmpty(e.albedo)){m.mainTexture=Texture(e.albedo,false,true);m.color=Color.white;albedos++;}
                HdrpProjectSetup.ConvertMaterial(m);if(HdrpProjectSetup.IsEyeShadow(m))HdrpProjectSetup.MakeEyeShadowTransparent(m);EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab");
            foreach(var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(skin.sharedMesh.tangents.Length!=skin.sharedMesh.vertexCount)throw new Exception("Missing tangent basis");
                foreach(var m in skin.sharedMaterials)if(m.GetTexture("_NormalMap") && !m.IsKeywordEnabled("_NORMALMAP_TANGENT_SPACE"))throw new Exception("Normal shader variant disabled");
            }
            File.WriteAllText("Validation/jin-materials.json","{\"passed\":true,\"normalMaterials\":"+normals+",\"surfaceMaterials\":"+surfaces+",\"occlusionMaterials\":"+occlusions+",\"updatedAlbedos\":"+albedos+"}");
        }
        static void Capture(string suffix)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Resources/JinPlayable.prefab"));
            var motion=root.AddComponent<JinRetargetMotion>();motion.Initialize(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Remaster/Resources/JinRetargeted/Combat/idle.bytes"));
            motion.Apply(new FighterState{health=100,facing=1},new MatchRules(),0,false);
            var camera=new GameObject("Material comparison camera").AddComponent<Camera>();camera.transform.position=new Vector3(1.5f,1.5f,3.1f);camera.transform.LookAt(new Vector3(0,1.05f,0));camera.fieldOfView=32;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.14f);
            RenderSettings.ambientLight=new Color(.45f,.45f,.45f);
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(35,-35,0);
            var rt=new RenderTexture(1000,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1000,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();File.WriteAllBytes("Validation/jin-materials-"+suffix+".png",image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(root);
        }
        public static void Build()
        {
            Capture("before");Apply();Capture("after");
            foreach(string scene in new[]{"LocalVersus","CharacterMuseum"})
            {
                string output=scene=="LocalVersus"?"Builds/Windows/VersusRemaster.exe":"Builds/Museum/CharacterMuseum.exe";
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Remaster/Scenes/"+scene+".unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed: "+scene);
            }
            File.WriteAllText("Validation/jin-material-builds.json","{\"passed\":true,\"builds\":2}");
        }
    }
}
