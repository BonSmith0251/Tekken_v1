using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Versus.Remaster.Editor
{
    // Adds the Render Settings object (key/fill lights, volume, colors) to the open staging_test
    // scene with the scene's established look. An existing object is left untouched.
    [InitializeOnLoad]
    public static class StagingRenderSettingsSetup
    {
        const string ProfilePath = "Assets/_Project/Rendering/StagingTestVolume.asset";
        const string ObsoleteAssetPath = "Assets/_Project/Resources/StagingRenderSettings.asset";

        // Scenes whose controllers read the Render Settings object, with the rig's yaw. In the
        // versus scenes it replaces the extracted original stage lighting.
        static readonly (string path, float yaw)[] Scenes =
        {
            ("Assets/_Project/Scenes/staging_test.unity", 0),
            ("Assets/_Project/Scenes/CharacterMuseum.unity", 0),
            ("Assets/_Project/Scenes/Wardrobe.unity", 0),
            ("Assets/_Project/Scenes/LocalVersus.unity", StagingRenderSettings.VersusYaw),
            ("Assets/_Project/Scenes/LocalVersus2.unity", StagingRenderSettings.VersusYaw),
        };

        static StagingRenderSettingsSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists("Temp/create-staging-render-settings"))
            {
                File.Delete("Temp/create-staging-render-settings");
                try { Create(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/create-staging-render-settings-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/add-render-settings-all-scenes"))
            {
                File.Delete("Temp/add-render-settings-all-scenes");
                try { AddToAllScenes(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/add-render-settings-all-scenes-error.txt", e.ToString()); }
            }
        }

        static VolumeProfile Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile) return profile;
            profile = StagingRenderSettings.CreateDefaultProfile();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // Opens each target scene additively when it isn't loaded, so the user's open scene stays as it is.
        [MenuItem("Remaster/Rendering/Add staging render settings to all scenes")]
        public static void AddToAllScenes()
        {
            var report = new System.Collections.Generic.List<string>();
            foreach (var (path, yaw) in Scenes)
            {
                var scene = EditorSceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                bool wasDirty = scene.isDirty;
                bool present = false;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.GetComponentInChildren<StagingRenderSettings>(true)) present = true;
                string result = "already present";
                if (!present)
                {
                    var settings = StagingRenderSettings.Create(null, Profile());
                    settings.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(settings.gameObject, scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    // Save only when that cannot also commit unrelated unsaved edits.
                    if (!wasDirty) { EditorSceneManager.SaveScene(scene); result = "added and saved"; }
                    else result = "added; scene had unsaved changes, not saved";
                }
                if (opened) EditorSceneManager.CloseScene(scene, true);
                report.Add(path + ": " + result);
            }
            File.WriteAllText("Temp/add-render-settings-all-scenes.log", string.Join("\n", report));
            Debug.Log("Render Settings per scene:\n" + string.Join("\n", report));
        }

        [MenuItem("Remaster/Rendering/Add staging render settings to scene")]
        public static void Create()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "staging_test") throw new InvalidOperationException("Open staging_test first; the active scene is " + scene.name);
            // The settings were briefly a ScriptableObject asset; that asset no longer matches the script.
            if (File.Exists(ObsoleteAssetPath)) AssetDatabase.DeleteAsset(ObsoleteAssetPath);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (!profile)
            {
                profile = StagingRenderSettings.CreateDefaultProfile();
                AssetDatabase.CreateAsset(profile, ProfilePath);
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                AssetDatabase.SaveAssets();
            }
            var existing = UnityEngine.Object.FindFirstObjectByType<StagingRenderSettings>();
            if (existing) { Selection.activeObject = existing.gameObject; Debug.Log("staging_test already has " + existing.name); return; }
            bool wasDirty = scene.isDirty;
            var settings = StagingRenderSettings.Create(null, profile);
            Undo.RegisterCreatedObjectUndo(settings.gameObject, "Add staging render settings");
            EditorSceneManager.MarkSceneDirty(scene);
            // Save only when that cannot also commit unrelated unsaved edits.
            if (!wasDirty) EditorSceneManager.SaveScene(scene);
            Selection.activeObject = settings.gameObject;
            Debug.Log("Added Render Settings to staging_test" + (wasDirty ? " (scene had unsaved changes; not saved)" : " and saved the scene"));
        }
    }
}
