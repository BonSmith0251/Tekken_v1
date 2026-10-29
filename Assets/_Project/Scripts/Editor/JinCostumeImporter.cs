using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class JinCostumeImporter
    {
        static JinCostumeImporter() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var character in Wardrobes)
            {
                string flag = "Temp/import-" + character.code + "-wardrobe", tints = "Temp/tints-" + character.code + "-wardrobe";
                if (File.Exists(tints))
                {
                    File.Delete(tints);
                    try { UpdateTints(character); } catch (Exception e) { Debug.LogException(e); File.WriteAllText(tints + "-error.txt", e.ToString()); }
                    return;
                }
                if (!File.Exists(flag)) continue;
                File.Delete(flag);
                try { ImportWardrobe(character); } catch (Exception e) { Debug.LogException(e); File.WriteAllText(flag + "-error.txt", e.ToString()); }
                return;
            }
        }
        // Wardrobe characters: their outfit-set source (face skeleton, reused skin/face/outfit
        // materials), the exported wardrobe source, the built output, and import options that
        // match each character's own costume importer.
        public sealed class WardrobeCharacter
        {
            public string code,costumeSource,costumeMaterials,face,wardrobeSource,output,asset,museum;
            public bool weldTriangleSoup,localAnonymousBones;
            // Alisa: re-hang her ATH_*_Btop detach points (AlisaImporter) so items share her model's bones.
            public bool detachPoints;
            public string[] doubleSided={"har_","cloth"};
        }
        public static readonly WardrobeCharacter[] Wardrobes={
            new WardrobeCharacter {code="jin",costumeSource="Assets/_Project/Characters/Jin/Source/Costumes",costumeMaterials="Assets/_Project/Characters/Jin/Costumes/Materials",face="jin_fac.json",
                wardrobeSource="Assets/_Project/Characters/Jin/Source/Wardrobe",output="Assets/_Project/Characters/Jin/Wardrobe",asset="Assets/_Project/Wardrobe/JinWardrobe.asset",museum="Assets/_Project/Museum/Jin.asset"},
            new WardrobeCharacter {code="kaz",costumeSource="Assets/_Project/Characters/Kazuya/Source/Base",costumeMaterials="Assets/_Project/Characters/Kazuya/Costumes/Materials",face="kaz_fac.json",
                wardrobeSource="Assets/_Project/Characters/Kazuya/Source/Wardrobe",output="Assets/_Project/Characters/Kazuya/Wardrobe",asset="Assets/_Project/Wardrobe/KazuyaWardrobe.asset",museum="Assets/_Project/Museum/Kazuya.asset"},
            // Xiaoyu's importer welds triangle-soup sections, keeps anonymous transformN helpers
            // local to their piece, and renders eyelashes and feathers double-sided.
            new WardrobeCharacter {code="xia",costumeSource="Assets/_Project/Characters/Xiaoyu/Source/Base",costumeMaterials="Assets/_Project/Characters/Xiaoyu/Costumes/Materials",face="xia_fac.json",
                wardrobeSource="Assets/_Project/Characters/Xiaoyu/Source/Wardrobe",output="Assets/_Project/Characters/Xiaoyu/Wardrobe",asset="Assets/_Project/Wardrobe/XiaoyuWardrobe.asset",museum="Assets/_Project/Museum/Xiaoyu.asset",
                weldTriangleSoup=true,localAnonymousBones=true,doubleSided=new[]{"har_","cloth","eyelash","feather"}},
            // Alisa's upper items carry her forearms and hands (export_wardrobe.py merges exp_*_parmL/R in).
            new WardrobeCharacter {code="asa",costumeSource="Assets/_Project/Characters/Alisa/Source/Base",costumeMaterials="Assets/_Project/Characters/Alisa/Costumes/Materials",face="asa_fac.json",
                wardrobeSource="Assets/_Project/Characters/Alisa/Source/Wardrobe",output="Assets/_Project/Characters/Alisa/Wardrobe",asset="Assets/_Project/Wardrobe/AlisaWardrobe.asset",museum="Assets/_Project/Museum/Alisa.asset",
                weldTriangleSoup=true,localAnonymousBones=true,detachPoints=true,doubleSided=new[]{"har_","cloth","eyelash"}},
            // Katarina's own importer welds triangle soup (her Loose shorts) like Xiaoyu's and Alisa's.
            new WardrobeCharacter {code="ltn",costumeSource="Assets/_Project/Characters/Katarina/Source/Base",costumeMaterials="Assets/_Project/Characters/Katarina/Costumes/Materials",face="ltn_face.json",
                wardrobeSource="Assets/_Project/Characters/Katarina/Source/Wardrobe",output="Assets/_Project/Characters/Katarina/Wardrobe",asset="Assets/_Project/Wardrobe/KatarinaWardrobe.asset",museum="Assets/_Project/Museum/Katarina.asset",
                weldTriangleSoup=true,localAnonymousBones=true,doubleSided=new[]{"har_","cloth","eyelash"}},
                    // Lidia (DLC); her importer welds triangle soup like Katarina's.
            new WardrobeCharacter {code="nsd",costumeSource="Assets/_Project/Characters/Lidia/Source/Base",costumeMaterials="Assets/_Project/Characters/Lidia/Costumes/Materials",face="nsd_fac.json",
                wardrobeSource="Assets/_Project/Characters/Lidia/Source/Wardrobe",output="Assets/_Project/Characters/Lidia/Wardrobe",asset="Assets/_Project/Wardrobe/LidiaWardrobe.asset",museum="Assets/_Project/Museum/Lidia.asset",
                weldTriangleSoup=true,localAnonymousBones=true,doubleSided=new[]{"har_","cloth","eyelash"}},
                    new WardrobeCharacter {code="bob",costumeSource="Assets/_Project/Characters/Bob/Source/Base",costumeMaterials="Assets/_Project/Characters/Bob/Costumes/Materials",face="bob_fac.json",
                wardrobeSource="Assets/_Project/Characters/Bob/Source/Wardrobe",output="Assets/_Project/Characters/Bob/Wardrobe",asset="Assets/_Project/Wardrobe/BobWardrobe.asset",museum="Assets/_Project/Museum/Bob.asset",
                weldTriangleSoup=true,localAnonymousBones=true,doubleSided=new[]{"har_","cloth","eyelash"}},
            new WardrobeCharacter {code="leo",costumeSource="Assets/_Project/Characters/Leo/Source/Base",costumeMaterials="Assets/_Project/Characters/Leo/Costumes/Materials",face="leo_fac.json",
                wardrobeSource="Assets/_Project/Characters/Leo/Source/Wardrobe",output="Assets/_Project/Characters/Leo/Wardrobe",asset="Assets/_Project/Wardrobe/LeoWardrobe.asset",museum="Assets/_Project/Museum/Leo.asset",
                weldTriangleSoup=true,localAnonymousBones=true,doubleSided=new[]{"har_","cloth","eyelash"}},
        };
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
        [Serializable] class UnityMaps { public string albedo,normal,surface,occlusion; public bool specular,cutout; public TintEntry tint; }
        // Written by prepare_jin_costume_materials.py for customizable materials; colors are flat linear RGB x4.
        [Serializable] class TintEntry { public string @base,mask; public float[] colors; public bool[] slots; public bool baked,hair,layered; }
        [Serializable] class MaterialEntry { public string name; public UnityMaps unity; }
        [Serializable] class PieceEntry { public string name,path; public string[] materials,cloth; }
        [Serializable] class CostumeEntry { public string name; public string[] pieces; }
        [Serializable] class Manifest { public CostumeEntry[] costumes; public PieceEntry[] pieceEntries; public MaterialEntry[] materialEntries; }
        [Serializable] class WardrobeEntry { public string piece,slot,owner,item,name,dependency,dependencyBase; }
        [Serializable] class WardrobeManifest { public WardrobeEntry[] wardrobe; public PieceEntry[] pieceEntries; public MaterialEntry[] materialEntries; }
        const string Source="Assets/_Project/Characters/Jin/Source/Costumes";
        static readonly string[] WardrobeSlots={"upper","lower","hair","head","glasses"};
        const string Output="Assets/_Project/Characters/Jin/Costumes";

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
        static Material BuildMaterial(MaterialEntry entry,string output=Output)
        {
            string path=output+"/Materials/"+Safe(entry.name)+".mat";
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
            HdrpProjectSetup.ConvertMaterial(mat);if(HdrpProjectSetup.IsEyeShadow(mat))HdrpProjectSetup.MakeEyeShadowTransparent(mat);if(HdrpProjectSetup.IsLashOrBrow(mat))HdrpProjectSetup.MakeLashOrBrowUnlit(mat);EditorUtility.SetDirty(mat);return mat;
        }
        // localPrefix: when set, anonymous DCC helpers (transformN) stay local to this piece under
        // that prefix, because two pieces can share a helper name with different parents.
        static Transform[] AddBones(Transform skeleton,Dictionary<string,Transform> boneMap,Part part,string localPrefix=null)
        {
            var bones=new Transform[part.bones.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var source=part.bones[i];
                bool local=localPrefix!=null && System.Text.RegularExpressions.Regex.IsMatch(source.name,@"^transform\d+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if(local || !boneMap.TryGetValue(source.name,out var bone))
                {
                    bone=new GameObject(local?localPrefix+"_"+source.name:source.name).transform;
                    bone.SetParent(source.parent<0?skeleton:bones[source.parent],false);
                    bone.localPosition=source.position;bone.localRotation=source.rotation;bone.localScale=source.scale;if(!local)boneMap.Add(source.name,bone);
                }
                bones[i]=bone;
            }
            return bones;
        }
        // Some sections store unshared triangles, which RecalculateNormals shades flat. Share
        // corners with identical position, UV and skin weights in those sections only.
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
        static void AddPart(GameObject root,Transform skeleton,Dictionary<string,Transform> boneMap,
                            PieceEntry entry,Dictionary<string,Material> materials,string costume,string output=Output,WardrobeCharacter options=null)
        {
            var part=JsonUtility.FromJson<Part>(File.ReadAllText(entry.path));var bones=AddBones(skeleton,boneMap,part,options!=null && options.localAnonymousBones?entry.name:null);
            // Rebuild an existing mesh asset in place: copying a new mesh over it kept the old vertex
            // buffer (Kazuya's glasses stayed at their first export's position after a re-export).
            string meshPath=output+"/Meshes/"+Safe(costume)+"_"+Safe(entry.name)+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var mesh=existing?existing:new Mesh();mesh.Clear();mesh.name=entry.name;mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=part.vertices;mesh.uv=part.uv;
            mesh.boneWeights=part.weights.Select(x=>x.Value).ToArray();
            // Sections with an empty material slot in the source (exported as MI_unassigned__*) are not drawn in game.
            var sections=part.submeshes.Where(x=>!x.material.Contains("Eye_ADD") && x.material!="MI_CH_tear" && !x.material.StartsWith("MI_unassigned__")).ToArray();mesh.subMeshCount=sections.Length;
            if(options!=null && options.weldTriangleSoup)WeldTriangleSoup(part,sections);
            var doubleSided=options!=null?options.doubleSided:new[]{"har_","cloth"};
            var doubled=new Dictionary<int,int[]>();
            for(int i=0;i<sections.Length;i++)
            {
                var indices=(int[])sections[i].triangles.Clone();for(int t=0;t<indices.Length;t+=3)(indices[t],indices[t+2])=(indices[t+2],indices[t]);
                mesh.SetTriangles(indices,i);
                if(doubleSided.Any(token=>sections[i].material.IndexOf(token,StringComparison.OrdinalIgnoreCase)>=0))
                {var both=new int[indices.Length*2];Array.Copy(indices,both,indices.Length);for(int t=0;t<indices.Length;t+=3){both[indices.Length+t]=indices[t+2];both[indices.Length+t+1]=indices[t+1];both[indices.Length+t+2]=indices[t];}doubled[i]=both;}
            }
            mesh.bindposes=bones.Select(x=>x.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            foreach(var pair in doubled)mesh.SetTriangles(pair.Value,pair.Key);
            if(existing)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,meshPath);
            var skin=new GameObject(entry.name).AddComponent<SkinnedMeshRenderer>();skin.transform.SetParent(root.transform,false);skin.sharedMesh=mesh;skin.bones=bones;skin.rootBone=boneMap["BASE"];
            skin.sharedMaterials=sections.Select(x=>materials[x.material]).ToArray();skin.updateWhenOffscreen=true;skin.quality=SkinQuality.Bone4;
        }
        [MenuItem("Remaster/Rebuild Jin museum costumes")]
        public static void Import()
        {
            Directory.CreateDirectory(Output+"/Materials");Directory.CreateDirectory(Output+"/Meshes");Directory.CreateDirectory(Output+"/Prefabs");AssetDatabase.Refresh();
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/manifest.json"));
            if(manifest.costumes.Length!=7 || manifest.pieceEntries.Length!=16 || manifest.materialEntries.Length!=57)throw new Exception("Unexpected Jin costume inventory");
            var materials=manifest.materialEntries.ToDictionary(x=>x.name,x=>BuildMaterial(x));var pieces=manifest.pieceEntries.ToDictionary(x=>x.name);
            foreach(var costume in manifest.costumes)
            {
                var root=new GameObject("Jin Kazama - "+costume.name);var skeleton=new GameObject("Skeleton").transform;skeleton.SetParent(root.transform,false);
                var bones=new Dictionary<string,Transform>();foreach(string piece in costume.pieces)AddPart(root,skeleton,bones,pieces[piece],materials,costume.name);
                root.AddComponent<JinRig>();string path=Output+"/Prefabs/"+Safe(costume.name)+".prefab";PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Validation/jin-costumes.json","{\"passed\":true,\"costumes\":7,\"pieces\":16,\"materials\":57}");
        }
        [MenuItem("Remaster/Rebuild Jin wardrobe")]static void ImportJinWardrobe()=>ImportWardrobe(Wardrobes[0]);
        [MenuItem("Remaster/Rebuild Kazuya wardrobe")]static void ImportKazuyaWardrobe()=>ImportWardrobe(Wardrobes[1]);
        [MenuItem("Remaster/Rebuild Xiaoyu wardrobe")]static void ImportXiaoyuWardrobe()=>ImportWardrobe(Wardrobes[2]);
        [MenuItem("Remaster/Rebuild Alisa wardrobe")]static void ImportAlisaWardrobe()=>ImportWardrobe(Wardrobes[3]);
        [MenuItem("Remaster/Rebuild Katarina wardrobe")]static void ImportKatarinaWardrobe()=>ImportWardrobe(Wardrobes[4]);
        [MenuItem("Remaster/Rebuild Lidia wardrobe")]static void ImportLidiaWardrobe()=>ImportWardrobe(Wardrobes[5]);
        [MenuItem("Remaster/Rebuild Bob wardrobe")]static void ImportBobWardrobe()=>ImportWardrobe(Wardrobes[6]);
        [MenuItem("Remaster/Rebuild Leo wardrobe")]static void ImportLeoWardrobe()=>ImportWardrobe(Wardrobes[7]);
        // One prefab per wearable piece; the wardrobe scene dresses the skeleton with them by bone name.
        public static void ImportWardrobe(WardrobeCharacter character)
        {
            string WardrobeOutput=character.output,WardrobeAsset=character.asset;
            foreach(string folder in new[]{"/Materials","/Meshes","/Prefabs"})Directory.CreateDirectory(WardrobeOutput+folder);AssetDatabase.Refresh();
            var manifest=JsonUtility.FromJson<WardrobeManifest>(File.ReadAllText(character.wardrobeSource+"/manifest.json"));
            var costumeMaterials=JsonUtility.FromJson<Manifest>(File.ReadAllText(character.costumeSource+"/manifest.json")).materialEntries.Select(x=>x.name);
            var materials=manifest.materialEntries.ToDictionary(x=>x.name,x=>BuildMaterial(x,WardrobeOutput));
            // Glasses lens opacity lives in the source parent materials, not the textures: use a fixed clear or tinted lens.
            foreach(var pair in materials)
            {
                string name=pair.Key.ToLowerInvariant();
                if(name.Contains("gla_") && (name.Contains("_lens") || name.Contains("_trans")))
                    HdrpProjectSetup.MakeTransparent(pair.Value,name.Contains("sun")?new Color(.06f,.06f,.07f,.75f):new Color(.85f,.88f,.9f,.2f));
            }
            // Skin, face and outfit materials are shared with the character's outfit sets.
            foreach(string name in costumeMaterials)if(!materials.ContainsKey(name))
                materials[name]=AssetDatabase.LoadAssetAtPath<Material>(character.costumeMaterials+"/"+Safe(name)+".mat");
            var pieces=manifest.pieceEntries.ToDictionary(x=>x.name);int built=0;
            // Some pieces only carry the bones they skin to; the face skeleton supplies BASE and the full chain.
            var faceSkeleton=JsonUtility.FromJson<Part>(File.ReadAllText(character.costumeSource+"/"+character.face));
            foreach(var stale in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>!t.parent && t.name.EndsWith(" wardrobe piece") ).ToArray())
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            foreach(var item in manifest.wardrobe)
            {
                var piece=pieces[item.piece];var missing=piece.materials.Where(m=>!materials.TryGetValue(m,out var mat)||!mat).ToArray();
                if(missing.Length>0)throw new Exception("Wardrobe piece "+item.piece+" is missing materials: "+string.Join(", ",missing));
                var root=new GameObject(item.piece+" wardrobe piece");
                try
                {
                    var skeleton=new GameObject("Skeleton").transform;skeleton.SetParent(root.transform,false);
                    // Common items spell shared bones in other cases (Base, HEAD); Unreal matches them case-insensitively.
                    var bones=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);AddBones(skeleton,bones,faceSkeleton,character.localAnonymousBones?Path.GetFileNameWithoutExtension(character.face):null);
                    AddPart(root,skeleton,bones,piece,materials,"wardrobe",WardrobeOutput,character);
                    if(character.detachPoints)AlisaImporter.AttachDetachPoints(bones);
                    PrefabUtility.SaveAsPrefabAsset(root,WardrobeOutput+"/Prefabs/"+Safe(item.piece)+".prefab");built++;
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(WardrobeAsset));AssetDatabase.Refresh();
            var wardrobe=AssetDatabase.LoadAssetAtPath<CharacterWardrobe>(WardrobeAsset);
            if(!wardrobe){wardrobe=ScriptableObject.CreateInstance<CharacterWardrobe>();AssetDatabase.CreateAsset(wardrobe,WardrobeAsset);}
            wardrobe.character=AssetDatabase.LoadAssetAtPath<MuseumCharacter>(character.museum);
            // Alisa's outfit sets keep her forearms as separate pieces; her upper items carry their own,
            // so wearing one must also hide those (Directory names match the model's renderer names).
            var forearms=Directory.GetFiles(character.costumeSource,"*.json").Select(Path.GetFileNameWithoutExtension)
                .Where(n=>System.Text.RegularExpressions.Regex.IsMatch(n,@"_exp_.+_parm[LR]$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray();
            wardrobe.slots=LoadWardrobe(manifest,manifest.pieceEntries.ToDictionary(x=>x.name),WardrobeOutput,TintMaterials(character,manifest),forearms);EditorUtility.SetDirty(wardrobe);AssetDatabase.SaveAssets();
            var counts=WardrobeSlots.Concat(new[]{"variant"}).Select(s=>"\""+s+"\":"+manifest.wardrobe.Count(w=>w.slot==s));
            File.WriteAllText("Validation/"+character.code+"-wardrobe.json","{\"passed\":true,\"pieces\":"+built+","+string.Join(",",counts)+",\"materials\":"+manifest.materialEntries.Length+",\"wardrobeAsset\":\""+WardrobeAsset+"\"}");
            Debug.Log(character.code+" wardrobe imported: "+built+" pieces");
        }
        // Customizable materials of the wardrobe and of the character's outfit sets (own items reuse those).
        static Dictionary<string,MaterialEntry> TintMaterials(WardrobeCharacter character,WardrobeManifest manifest)
        {
            var materials=JsonUtility.FromJson<Manifest>(File.ReadAllText(character.costumeSource+"/manifest.json")).materialEntries
                .Where(m=>m.unity?.tint!=null && !string.IsNullOrEmpty(m.unity.tint.@base)).ToDictionary(m=>m.name);
            foreach(var m in manifest.materialEntries)if(m.unity?.tint!=null && !string.IsNullOrEmpty(m.unity.tint.@base))materials[m.name]=m;
            return materials;
        }
        static Texture2D TintTexture(string path,bool color)
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer)throw new Exception("Missing tint texture: "+path);
            // Masks hold weights, not colors: they must stay linear.
            if(importer.sRGBTexture!=color || importer.textureType!=TextureImporterType.Default)
            {importer.textureType=TextureImporterType.Default;importer.sRGBTexture=color;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();}
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static CharacterWardrobe.Tint[] Tints(PieceEntry piece,Dictionary<string,MaterialEntry> materials)=>
            (piece.materials??new string[0]).Where(materials.ContainsKey).Select(name=>{var t=materials[name].unity.tint;return new CharacterWardrobe.Tint {
                material=name,baseMap=TintTexture(t.@base,true),maskMap=string.IsNullOrEmpty(t.mask)?null:TintTexture(t.mask,false),slots=t.slots,baked=t.baked,hair=t.hair,layered=t.layered,
                colors=Enumerable.Range(0,4).Select(i=>new Color(t.colors[i*3],t.colors[i*3+1],t.colors[i*3+2],1)).ToArray()};}).ToArray();

        // Refreshes only the recoloring data of an imported wardrobe (no mesh, material or prefab rebuild).
        public static void UpdateTints(WardrobeCharacter character)
        {
            var manifest=JsonUtility.FromJson<WardrobeManifest>(File.ReadAllText(character.wardrobeSource+"/manifest.json"));
            var wardrobe=AssetDatabase.LoadAssetAtPath<CharacterWardrobe>(character.asset);
            if(!wardrobe)throw new InvalidOperationException("Import the "+character.code+" wardrobe first.");
            var pieces=manifest.pieceEntries.ToDictionary(x=>x.name);var materials=TintMaterials(character,manifest);int count=0;
            foreach(var piece in wardrobe.slots.SelectMany(s=>s.pieces))
            {piece.tints=pieces.TryGetValue(piece.piece,out var entry)?Tints(entry,materials):new CharacterWardrobe.Tint[0];if(piece.tints.Length>0)count++;}
            EditorUtility.SetDirty(wardrobe);AssetDatabase.SaveAssets();
            File.WriteAllText("Validation/"+character.code+"-wardrobe-colors.json","{\"passed\":true,\"recolorablePieces\":"+count+",\"tintMaterials\":"+materials.Count+"}");
            Debug.Log(character.code+" wardrobe colors: "+count+" recolorable pieces");
        }
        static CharacterWardrobe.Slot[] LoadWardrobe(WardrobeManifest manifest,Dictionary<string,PieceEntry> pieces,string WardrobeOutput,Dictionary<string,MaterialEntry> tintMaterials,string[] forearms)
        {
            GameObject Prefab(string piece)=>string.IsNullOrEmpty(piece)?null:AssetDatabase.LoadAssetAtPath<GameObject>(WardrobeOutput+"/Prefabs/"+Safe(piece)+".prefab");
            // The character's own items first, then the common customization items, each alphabetical.
            // Head items and glasses may be left off; variants are only worn under head items.
            return WardrobeSlots.Select(slot=>new CharacterWardrobe.Slot {name=slot,optional=slot=="head"||slot=="glasses",pieces=manifest.wardrobe.Where(w=>w.slot==slot)
                .OrderBy(w=>w.owner=="cmn"?1:0).ThenBy(w=>w.name,StringComparer.OrdinalIgnoreCase)
                .Select(w=>new CharacterWardrobe.Piece {name=w.name,piece=w.piece,common=w.owner=="cmn",prefab=Prefab(w.piece),
                    dependencyBase=w.dependencyBase,dependency=Prefab(w.dependency),cloth=pieces[w.piece].cloth??new string[0],tints=Tints(pieces[w.piece],tintMaterials),
                    replaces=slot=="upper"?forearms:new string[0]}).ToArray()}).ToArray();
        }
        public static MuseumCharacter.Costume[] LoadCostumes()
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Source+"/manifest.json"));
            return manifest.costumes.Select(x=>new MuseumCharacter.Costume {name=x.name,prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Prefabs/"+Safe(x.name)+".prefab")}).ToArray();
        }
    }
}
