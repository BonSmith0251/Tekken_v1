using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Versus.Remaster.Editor
{
    public static class SecondaryMotionSetup
    {
        [Serializable] class Weight
        {
            public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;
            public float weight0,weight1,weight2,weight3;
            public BoneWeight Value => new BoneWeight { boneIndex0=boneIndex0,boneIndex1=boneIndex1,boneIndex2=boneIndex2,boneIndex3=boneIndex3,weight0=weight0,weight1=weight1,weight2=weight2,weight3=weight3 };
        }
        [Serializable] class Part { public Weight[] weights; }
        [MenuItem("Remaster/Apply Restored Kazuya Secondary Weights")]
        public static void ApplyWeights()
        {
            int count=0;
            foreach(string path in Directory.GetFiles("Assets/_Project/Characters/Kazuya/Source/Base", "kaz_*.json"))
            {
                string meshPath="Assets/_Project/Characters/Kazuya/Costumes/Meshes/"+Path.GetFileNameWithoutExtension(path)+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(!mesh)throw new InvalidOperationException("Missing mesh: "+meshPath);
                var part=JsonUtility.FromJson<Part>(File.ReadAllText(path));
                if(part.weights.Length!=mesh.vertexCount)throw new InvalidOperationException("Weight/vertex mismatch: "+meshPath);
                mesh.boneWeights=part.weights.Select(w=>w.Value).ToArray();EditorUtility.SetDirty(mesh);count++;
            }
            AssetDatabase.SaveAssets();Debug.Log("Restored secondary skin weights on "+count+" Kazuya meshes.");
        }
    }
}
