using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
namespace Versus.Remaster.Editor
{
 // Builds Resources/MuseumStages/<stg>.prefab from Tools/import_museum_stages.py output: STG2 meshes
 // with normals, tangents and UVs, one HDRP/Lit material per original material instance (base colour,
 // normal and emissive maps from Stages/Textures), and each component's OverrideMaterials.
 // Temp/import-stages runs it in the open editor.
 [InitializeOnLoad]
 public static class MuseumStageImporter
 {
  [Serializable] class Catalog {public Stage[] stages;public MeshInfo[] meshes;public MaterialInfo[] materials;}
  [Serializable] class Stage {public string name;public Node[] nodes;public bool hasStart;public Vector3 start;public float startYaw=-90;public StageLightingBuilder.PostData post;}
  [Serializable] class Node {public int id,parent;public string name,mesh;public string[] materials;public Vector3 position,scale;public Rotation rotation;public Beam beam;public string effect;
   public StageLightingBuilder.LightData light;public StageLightingBuilder.SkyData sky;public StageLightingBuilder.FogData fog;public StageLightingBuilder.WindData wind;}
  // BP_LightBeam light shaft (cm, from its MaterialInstanceDynamic).
  [Serializable] class Beam {public float width,length,brightness,fadedistance=2048;public float[] color;public string mask;}
  [Serializable] class Rotation {public float pitch,yaw,roll;}
  [Serializable] class MeshInfo {public string key,path;public string[] materials;}
  [Serializable] class MaterialInfo {public string key,path,@base,normal,emissive,blend,baseSource,note;public bool twoSided,hidden;public float[] color,emissiveColor;}
  const string Source="Assets/_Project/Stages/Source",Textures="Assets/_Project/Stages/Textures",Materials="Assets/_Project/Stages/Materials";
  const string Output="Assets/_Project/Resources/MuseumStages";
  static MuseumStageImporter(){EditorApplication.update+=Poll;}
  static void Poll()
  {
   if(File.Exists("Temp/fix-lightmap-uvs") && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
   {File.Delete("Temp/fix-lightmap-uvs");try{FixLightmapUVs();}catch(Exception e){File.WriteAllText("Temp/fix-lightmap-uvs-error.txt",e.ToString());Debug.LogException(e);}}
   if(!File.Exists("Temp/import-stages") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)return;
   File.Delete("Temp/import-stages");
   try{Import();}catch(Exception e){File.WriteAllText("Temp/import-stages-error.txt",e.ToString());Debug.LogException(e);}
  }
  [MenuItem("Remaster/Stages/Import original stages")]
  public static void Import()
  {
   if(!File.Exists(Source+"/stages.json"))return;
   var catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText(Source+"/stages.json"));
   Directory.CreateDirectory(Output+"/Meshes");Directory.CreateDirectory(Materials);AssetDatabase.Refresh();
   ConfigureTextures();
   var fallback=AssetDatabase.LoadAssetAtPath<Material>(Output+"/Geometry.mat");
   if(!fallback){fallback=FighterView.Surface(new Color(.38f,.42f,.46f));AssetDatabase.CreateAsset(fallback,Output+"/Geometry.mat");}
   var materials=new Dictionary<string,Material>();var bakedLux=new List<string>();
   // Editor helper materials (collision, occluders) draw nothing in the game; an invisible material keeps the submesh count.
   var hidden=AssetDatabase.LoadAssetAtPath<Material>(Materials+"/Hidden.mat");
   if(!hidden)
   {
    hidden=new Material(Shader.Find("HDRP/Lit"));hidden.SetFloat("_SurfaceType",1);hidden.SetColor("_BaseColor",new Color(0,0,0,0));
    HDMaterial.ValidateMaterial(hidden);AssetDatabase.CreateAsset(hidden,Materials+"/Hidden.mat");
   }
   if(catalog.materials!=null)foreach(var info in catalog.materials)materials[info.key]=info.hidden?hidden:BuildMaterial(info);
   var meshInfo=(catalog.meshes??new MeshInfo[0]).ToDictionary(m=>m.key);
   var meshes=new Dictionary<string,(Mesh mesh,int[] slots)>();
   int untextured=0;var log=new List<string>();
   foreach(var stage in catalog.stages)
   {
    var root=new GameObject(stage.name);var nodes=new Dictionary<int,Transform>();
    Transform fogNode=null,windNode=null;StageLightingBuilder.SkyData sky=null;StageLightingBuilder.FogData fog=null;StageLightingBuilder.WindData wind=null;
    var stageInfo=root.AddComponent<StageInfo>();stageInfo.hasMatchStart=stage.hasStart;stageInfo.matchStart=stage.start;stageInfo.matchYaw=stage.startYaw;
    foreach(var n in stage.nodes){var t=new GameObject(n.name).transform;t.SetParent(root.transform,false);nodes[n.id]=t;}
    foreach(var n in stage.nodes)
    {
     var t=nodes[n.id];if(nodes.TryGetValue(n.parent,out var parent))t.SetParent(parent,false);
     t.localPosition=new Vector3(n.position.x,n.position.z,-n.position.y)*.01f;
     t.localScale=new Vector3(n.scale.x,n.scale.z,n.scale.y);
     // Unreal Rotator: yaw about +Z, pitch about -Y, roll about -X.
     // Map directions through (X,Y,Z) -> (X,Z,-Y), matching vertices.
     t.localRotation=Quaternion.AngleAxis(n.rotation.yaw,Vector3.up)*Quaternion.AngleAxis(n.rotation.pitch,Vector3.forward)*Quaternion.AngleAxis(-n.rotation.roll,Vector3.right);
     if(n.beam!=null && !string.IsNullOrEmpty(n.beam.mask))AddBeam(t,n.beam);
     if(n.light!=null && !string.IsNullOrEmpty(n.light.type))StageLightingBuilder.AddLight(t,n.light);
     if(n.sky!=null && n.sky.color!=null && n.sky.color.Length>=3 && sky==null)sky=n.sky;
     if(n.fog!=null && n.fog.color!=null && n.fog.color.Length>=3 && (fog==null || n.fog.enabled && !fog.enabled)){fog=n.fog;fogNode=t;}
     if(n.wind!=null && n.wind.strength>0 && windNode==null){wind=n.wind;windNode=t;}
     if(!string.IsNullOrEmpty(n.effect))t.gameObject.AddComponent<StageEffectMarker>().system=n.effect;
     if(string.IsNullOrEmpty(n.mesh))continue;
     if(!meshes.TryGetValue(n.mesh,out var entry)){entry=LoadMesh(n.mesh);meshes[n.mesh]=entry;}
     meshInfo.TryGetValue(n.mesh,out var info);
     // Fog-sheet and god-ray effect planes get their material at runtime from their blueprint;
     // unmaterialed they are grey slabs (fog sheets are 80 km across) and grid cards.
     if(info!=null && (info.path.Contains("/FogSheet/") || info.path.Contains("/LightBeam/Meshes/SM_GodRay")))continue;
     var shared=new Material[entry.slots.Length];
     for(int i=0;i<shared.Length;i++)
     {
      int slot=entry.slots[i];string key="";
      if(n.materials!=null && slot>=0 && slot<n.materials.Length && !string.IsNullOrEmpty(n.materials[slot]))key=n.materials[slot];
      else if(info!=null && info.materials!=null && slot>=0 && slot<info.materials.Length)key=info.materials[slot];
      shared[i]=!string.IsNullOrEmpty(key) && materials.TryGetValue(key,out var m)?m:fallback;
      if(shared[i]!=hidden && (shared[i]==fallback || !shared[i].GetTexture("_BaseColorMap")))untextured++;
     }
     if(shared.All(m=>m==hidden))continue;
     t.gameObject.AddComponent<MeshFilter>().sharedMesh=entry.mesh;var meshRenderer=t.gameObject.AddComponent<MeshRenderer>();meshRenderer.sharedMaterials=shared;
     MarkForBaking(meshRenderer,hidden);
    }
    // The stage's own lighting (lights are already on their nodes), and its foliage wind.
    StageLightingBuilder.TrimBakedLights(root,stage.start);
    bakedLux.Add("\""+stage.name+"\":"+StageLightingBuilder.LastBakedLux.ToString("0.0"));
    StageLightingBuilder.Finish(root,stage.name,StageLightingBuilder.BuildProfile(stage.name,sky,fog,stage.post),fogNode,windNode,wind);
    PrefabUtility.SaveAsPrefabAsset(root,Output+"/"+stage.name+".prefab");UnityEngine.Object.DestroyImmediate(root);
    log.Add(stage.name);
   }
   AssetDatabase.SaveAssets();
   File.WriteAllText(Output+"/StageNames.txt",string.Join("\n",catalog.stages.Select(s=>s.name)));AssetDatabase.Refresh();
   Directory.CreateDirectory("Validation/Stages");
   File.WriteAllText("Validation/Stages/baked-lights.json","{"+string.Join(",",bakedLux)+"}");
   File.WriteAllText("Validation/Stages/import.json","{\"stages\":"+catalog.stages.Length+",\"meshes\":"+meshes.Count+",\"materials\":"+materials.Count+
    ",\"texturedMaterials\":"+materials.Values.Count(m=>m.GetTexture("_BaseColorMap"))+",\"untexturedSubmeshes\":"+untextured+"}");
   Debug.Log("Imported "+catalog.stages.Length+" original stages with "+materials.Count+" materials.");
  }

  // Reuses the mesh asset (and GUID) of the earlier geometry-only import, refilled with the STG2 data.
  static (Mesh,int[]) LoadMesh(string key)
  {
   string path=Output+"/Meshes/"+key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool created=!mesh;
   if(created)mesh=new Mesh{name=key};
   int[] slots;bool lightmapUV;
   using(var r=new BinaryReader(File.OpenRead(Source+"/"+key+".bytes")))
   {
    if(r.ReadInt32()!=0x32475453)throw new Exception("Stage mesh "+key+" is not STG2; re-run Tools/import_museum_stages.py");
    int vc=r.ReadInt32(),sections=r.ReadInt32(),uvCount=r.ReadInt32();
    Vector3[] V3(){var a=new Vector3[vc];for(int i=0;i<vc;i++)a[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());return a;}
    var vertices=V3();var normals=V3();var tangents=new Vector4[vc];
    for(int i=0;i<vc;i++)tangents[i]=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    var uvs=new List<Vector2[]>();
    for(int c=0;c<uvCount;c++){var uv=new Vector2[vc];for(int i=0;i<vc;i++)uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());uvs.Add(uv);}
    mesh.Clear();mesh.indexFormat=vc>65535?IndexFormat.UInt32:IndexFormat.UInt16;
    mesh.vertices=vertices;mesh.normals=normals;mesh.tangents=tangents;
    for(int c=0;c<uvs.Count;c++)mesh.SetUVs(c,uvs[c]);
    lightmapUV=uvs.Count>1 && uvs[1].All(u=>u.x>=-.001f && u.x<=1.001f && u.y>=-.001f && u.y<=1.001f);
    mesh.subMeshCount=sections;slots=new int[sections];
    for(int s=0;s<sections;s++)
    {
     slots[s]=r.ReadInt32();int count=r.ReadInt32();var indices=new int[count];
     // STG2 keeps Unreal's index order, which after the (X,Z,-Y) axis mapping winds against the
     // normals; reverse each triangle so Unity's front faces face out (otherwise stages look see-through).
     for(int i=0;i<count;i+=3){indices[i]=r.ReadInt32();indices[i+2]=r.ReadInt32();indices[i+1]=r.ReadInt32();}
     mesh.SetTriangles(indices,s,false);
    }
    mesh.RecalculateBounds();
   }
   // Unity bakes lightmaps into UV channel 1. Unreal stage meshes carry their lightmap layout there
   // (95% of them); where it is missing or not inside 0..1, Unity unwraps one.
   if(!lightmapUV)Unwrap(mesh);
   if(created)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
   return (mesh,slots);
  }

  // Textures imported before StageTexturePostprocessor existed keep default settings; fix and reimport them.
  static void ConfigureTextures()
  {
   var stale=new List<string>();
   foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{Textures}))
   {
    string path=AssetDatabase.GUIDToAssetPath(guid);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
    if(importer && StageTexturePostprocessor.Configure(path,importer))stale.Add(path);
   }
   if(stale.Count==0)return;
   try{AssetDatabase.StartAssetEditing();foreach(var path in stale)AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);}
   finally{AssetDatabase.StopAssetEditing();}
  }

  // The original shaft is a line that M_GodRay_* widens in its vertex shader (unlit, additive,
  // mask texture along its length). Here it is two crossed quads from the billboard along its
  // forward axis: X = length, Y and Z = width.
  const float BeamGain=.4f;
  static void AddBeam(Transform parent,Beam beam)
  {
   string meshPath=Output+"/Meshes/GodRayBeam.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
   if(!mesh)
   {
    mesh=new Mesh{name="GodRayBeam"};
    mesh.vertices=new[]{new Vector3(0,-.5f,0),new Vector3(0,.5f,0),new Vector3(1,.5f,0),new Vector3(1,-.5f,0),
                        new Vector3(0,0,-.5f),new Vector3(0,0,.5f),new Vector3(1,0,.5f),new Vector3(1,0,-.5f)};
    mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1),new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
    mesh.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7};mesh.RecalculateNormals();mesh.RecalculateBounds();
    AssetDatabase.CreateAsset(mesh,meshPath);
   }
   var c=beam.color!=null && beam.color.Length>=3?new Color(beam.color[0],beam.color[1],beam.color[2]):Color.white;
   string matPath=Materials+"/GodRay_"+beam.mask+"_"+Mathf.RoundToInt(beam.brightness*100)+"_"+ColorUtility.ToHtmlStringRGB(c)+".mat";
   var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);bool created=!material;
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Resources/StageEffects/LightShaft.shader");
   if(created)material=new Material(shader);
   material.shader=shader;material.renderQueue=(int)RenderQueue.Transparent;
   // Not recovered: how bright the shafts read against the original's own stage lighting. This
   // scene is lit far more dimly, so the MID brightness is scaled by eye (0.9 shafts burned white).
   float b=beam.brightness*BeamGain;
   material.SetTexture("_MainTex",Texture(beam.mask));material.SetColor("_Color",new Color(c.r*b,c.g*b,c.b*b,1));
   material.SetFloat("_FadeDistance",beam.fadedistance*.01f);
   if(created)AssetDatabase.CreateAsset(material,matPath);else EditorUtility.SetDirty(material);
   var go=new GameObject("GodRay");go.transform.SetParent(parent,false);
   go.transform.localScale=new Vector3(beam.length*.01f,beam.width*.01f,beam.width*.01f);
   go.AddComponent<MeshFilter>().sharedMesh=mesh;
   var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
  }

  // The unwrapper splits vertices along its seams; past 65535 a 16-bit mesh fails ("too many vertices").
  static void Unwrap(Mesh mesh)
  {
   mesh.indexFormat=IndexFormat.UInt32;
   Unwrapping.GenerateSecondaryUVSet(mesh);
   if(mesh.vertexCount<=65535)mesh.indexFormat=IndexFormat.UInt16;
  }

  // Temp/fix-lightmap-uvs: unwraps stage meshes left without a usable lightmap UV (channel 1 missing or
  // outside 0..1), without a full re-import. Writes Validation/Stages/lightmap-uvs.json.
  [MenuItem("Remaster/Stages/Fix missing lightmap UVs")]
  public static void FixLightmapUVs()
  {
   var fixedMeshes=new List<string>();
   foreach(var guid in AssetDatabase.FindAssets("t:Mesh",new[]{Output+"/Meshes"}))
   {
    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
    if(!mesh || mesh.name=="GodRayBeam" || mesh.triangles.Length==0)continue;
    if(HasLightmapUV(mesh))continue;
    try{Unwrap(mesh);EditorUtility.SetDirty(mesh);fixedMeshes.Add(mesh.name+" ("+mesh.vertexCount+" vertices)");}
    catch(Exception e){fixedMeshes.Add(mesh.name+" FAILED, uses light probes: "+e.Message.Replace("\"","'"));}
   }
   AssetDatabase.SaveAssets();
   // Renderers whose mesh still has no lightmap UV take their light from probes.
   int probed=0;
   foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{Output}))
   {
    string path=AssetDatabase.GUIDToAssetPath(guid);var root=PrefabUtility.LoadPrefabContents(path);bool changed=false;
    foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
     if(r.receiveGI==ReceiveGI.Lightmaps && !HasLightmapUV(r.GetComponent<MeshFilter>().sharedMesh)){r.receiveGI=ReceiveGI.LightProbes;changed=true;probed++;}
    if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
    PrefabUtility.UnloadPrefabContents(root);
   }
   fixedMeshes.Add(probed+" renderers switched to light probes");
   File.WriteAllText("Validation/Stages/lightmap-uvs.json","{\"fixed\":"+fixedMeshes.Count+",\"meshes\":[\""+string.Join("\",\"",fixedMeshes)+"\"]}");
  }

  // Stage scenes (Scenes/Stages/<stg>.unity) bake lightmaps from the prefab instance. Opaque and
  // cut-out surfaces contribute; transparent and helper surfaces, and sky domes (anything over 500 m),
  // do not. Cut-out foliage and small props take their light from light probes rather than lightmap space.
  static void MarkForBaking(MeshRenderer renderer,Material hidden)
  {
   var mats=renderer.sharedMaterials;
   bool solid=mats.Any(m=>m && m!=hidden && (!m.HasProperty("_SurfaceType") || m.GetFloat("_SurfaceType")==0));
   bool sky=renderer.bounds.size.magnitude>500;
   var flags=solid && !sky?StaticEditorFlags.ContributeGI|StaticEditorFlags.ReflectionProbeStatic|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic:0;
   GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,flags);
   bool cutout=mats.All(m=>m && m.HasProperty("_AlphaCutoffEnable") && m.GetFloat("_AlphaCutoffEnable")>0);
   var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
   renderer.receiveGI=solid && !sky && !cutout && renderer.bounds.size.magnitude>1 && HasLightmapUV(mesh)?ReceiveGI.Lightmaps:ReceiveGI.LightProbes;
  }

  // Channel 1 present on every vertex and inside 0..1 (Unity's lightmap UV rule).
  static bool HasLightmapUV(Mesh mesh)
  {
   if(!mesh)return false;
   var uv=new List<Vector2>();mesh.GetUVs(1,uv);
   return uv.Count==mesh.vertexCount && uv.All(u=>u.x>=-.001f && u.x<=1.001f && u.y>=-.001f && u.y<=1.001f);
  }

  static Texture2D Texture(string key)=>string.IsNullOrEmpty(key)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"/"+key+".png");

  static Material BuildMaterial(MaterialInfo info)
  {
   string path=Materials+"/"+info.key+".mat";
   var material=AssetDatabase.LoadAssetAtPath<Material>(path);bool created=!material;
   if(created)material=new Material(Shader.Find("HDRP/Lit"));
   material.shader=Shader.Find("HDRP/Lit");
   var baseMap=Texture(info.@base);var normal=Texture(info.normal);var emissive=Texture(info.emissive);
   var color=info.color!=null && info.color.Length==4?new Color(info.color[0],info.color[1],info.color[2],1):Color.white;
   // Shader-constant surfaces keep the original's colour parameter where there is one.
   if(!baseMap && color==Color.white)color=new Color(.55f,.55f,.55f);
   material.SetTexture("_BaseColorMap",baseMap);material.SetColor("_BaseColor",color);
   material.SetTexture("_NormalMap",normal);material.SetFloat("_NormalScale",1);
   material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.25f);
   bool selfLit=info.baseSource!=null && info.baseSource.StartsWith("emissive");
   if(emissive)
   {
    var e=info.emissiveColor!=null && info.emissiveColor.Length==4 && (info.emissiveColor[0]+info.emissiveColor[1]+info.emissiveColor[2])>0?new Color(info.emissiveColor[0],info.emissiveColor[1],info.emissiveColor[2]):Color.white;
    material.SetFloat("_UseEmissiveIntensity",0);
    material.SetTexture("_EmissiveColorMap",emissive);material.SetColor("_EmissiveColor",e*(selfLit?1.2f:1f));
    if(selfLit)material.SetColor("_BaseColor",Color.black);
   }
   else {material.SetTexture("_EmissiveColorMap",null);material.SetColor("_EmissiveColor",Color.black);}
   bool transparent=info.blend=="translucent" || info.blend=="additive" || info.blend=="modulate";
   material.SetFloat("_SurfaceType",transparent?1:0);
   material.SetFloat("_BlendMode",info.blend=="additive"?1:0);
   material.SetFloat("_AlphaCutoffEnable",info.blend=="masked"?1:0);material.SetFloat("_AlphaCutoff",.5f);
   material.SetFloat("_DoubleSidedEnable",info.twoSided?1:0);
   if(transparent && !baseMap)material.SetColor("_BaseColor",new Color(color.r,color.g,color.b,.45f));
   // Fog, mist and cloud cards fade with distance and animation in the original; at a constant
   // opacity they read as solid columns, so draw them at half strength.
   if(transparent && System.Text.RegularExpressions.Regex.IsMatch(info.path,"(fog|mist|smoke|cloud|unkai|Water_Cliff)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
   {var c=material.GetColor("_BaseColor");c.a*=.5f;material.SetColor("_BaseColor",c);}
   HDMaterial.ValidateMaterial(material);
   if(created)AssetDatabase.CreateAsset(material,path);else EditorUtility.SetDirty(material);
   return material;
  }
 }

 // Stage textures arrive as PNG from the exporter; "_nrm" ones are tangent-space normal maps.
 public sealed class StageTexturePostprocessor : AssetPostprocessor
 {
  void OnPreprocessTexture(){if(assetPath.StartsWith("Assets/_Project/Stages/Textures/"))Configure(assetPath,(TextureImporter)assetImporter);}
  // Applies the stage settings; true when anything changed.
  public static bool Configure(string path,TextureImporter importer)
  {
   bool normal=path.EndsWith("_nrm.png");
   var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
   if(importer.textureType==type && importer.sRGBTexture==!normal && importer.maxTextureSize==2048 && importer.mipmapEnabled && (normal || importer.alphaIsTransparency))return false;
   importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Compressed;
   importer.textureType=type;importer.sRGBTexture=!normal;
   if(!normal)importer.alphaIsTransparency=true;
   return true;
  }
 }
}
