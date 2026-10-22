using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Builds the standalone wardrobe scene (every character with a built wardrobe) in an additive
    // scene, leaving the open scenes untouched.
    [InitializeOnLoad]
    public static class WardrobeSceneSetup
    {
        const string ScenePath="Assets/Remaster/Scenes/Wardrobe.unity";
        static WardrobeSceneSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/create-jin-wardrobe-scene") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/create-jin-wardrobe-scene");
            try { Create(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/create-jin-wardrobe-scene-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Create wardrobe scene")]
        public static void Create()
        {
            var wardrobes=JinCostumeImporter.Wardrobes.Select(c=>AssetDatabase.LoadAssetAtPath<CharacterWardrobe>(c.asset)).Where(w=>w).ToArray();
            if(wardrobes.Length==0)throw new InvalidOperationException("Rebuild at least one character wardrobe first.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                var root=new GameObject("Wardrobe");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
                root.AddComponent<WardrobeViewer>().wardrobes=wardrobes;
                if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Could not save "+ScenePath);
            }
            finally{EditorSceneManager.CloseScene(scene,true);}
            File.WriteAllText("Validation/jin-wardrobe-scene.json","{\"passed\":true,\"scene\":\""+ScenePath+"\",\"characters\":"+wardrobes.Length+"}");
            Debug.Log("Created "+ScenePath);
        }
    }
}
