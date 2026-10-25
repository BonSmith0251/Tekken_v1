using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace Versus.Remaster.Editor
{
 public static class MuseumStageImporter
 {
  [Serializable] class Catalog {public Stage[] stages;}
  [Serializable] class Stage {public string name;public Node[] nodes;}
  [Serializable] class Node {public int id,parent;public string name,mesh;public Vector3 position,scale;public Rotation rotation;}
  [Serializable] class Rotation {public float pitch,yaw,roll;}
  const string Source="Assets/_Project/Stages/Source";
  public static void Import()
  {
   if(!File.Exists(Source+"/stages.json"))return;
   const string output="Assets/_Project/Resources/MuseumStages";
   Directory.CreateDirectory(output+"/Meshes");AssetDatabase.Refresh();
   var mat=AssetDatabase.LoadAssetAtPath<Material>(output+"/Geometry.mat");
   if(!mat){mat=FighterView.Surface(new Color(.38f,.42f,.46f));AssetDatabase.CreateAsset(mat,output+"/Geometry.mat");}
   var meshes=new Dictionary<string,Mesh>();
   foreach(var stage in JsonUtility.FromJson<Catalog>(File.ReadAllText(Source+"/stages.json")).stages)
   {
    var root=new GameObject(stage.name);var nodes=new Dictionary<int,Transform>();
    foreach(var n in stage.nodes){var t=new GameObject(n.name).transform;t.SetParent(root.transform,false);nodes[n.id]=t;}
    foreach(var n in stage.nodes)
    {
     var t=nodes[n.id];if(nodes.TryGetValue(n.parent,out var parent))t.SetParent(parent,false);
     t.localPosition=new Vector3(n.position.x,n.position.z,-n.position.y)*.01f;
     t.localScale=new Vector3(n.scale.x,n.scale.z,n.scale.y);
     // Unreal Rotator: yaw about +Z, pitch about -Y, roll about -X.
     // Map directions through (X,Y,Z) -> (X,Z,-Y), matching vertices.
     t.localRotation=Quaternion.AngleAxis(n.rotation.yaw,Vector3.up)*Quaternion.AngleAxis(n.rotation.pitch,Vector3.forward)*Quaternion.AngleAxis(-n.rotation.roll,Vector3.right);
     if(string.IsNullOrEmpty(n.mesh))continue;
     if(!meshes.TryGetValue(n.mesh,out var mesh))
     {
      string path=output+"/Meshes/"+n.mesh+".asset";mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
      if(!mesh){using(var r=new BinaryReader(File.OpenRead(Source+"/"+n.mesh+".bytes"))){if(r.ReadInt32()!=0x31475453)throw new Exception("Invalid stage mesh");int vc=r.ReadInt32(),ic=r.ReadInt32();var vertices=new Vector3[vc];var indices=new int[ic];for(int i=0;i<vc;i++)vertices[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());for(int i=0;i<ic;i++)indices[i]=r.ReadInt32();mesh=new Mesh{name=n.mesh,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}}
      meshes[n.mesh]=mesh;
     }
     t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;
    }
    PrefabUtility.SaveAsPrefabAsset(root,output+"/"+stage.name+".prefab");UnityEngine.Object.DestroyImmediate(root);
   }
   AssetDatabase.SaveAssets();
   var names=new List<string>();foreach(var s in JsonUtility.FromJson<Catalog>(File.ReadAllText(Source+"/stages.json")).stages)names.Add(s.name);
   File.WriteAllText(output+"/StageNames.txt",string.Join("\n",names));AssetDatabase.Refresh();
  }
 }
}
