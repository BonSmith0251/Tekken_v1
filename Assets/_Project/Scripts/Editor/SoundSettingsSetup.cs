using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // sound_settings: Temp/create-sound-settings-scene builds Assets/_Project/Scenes/sound_settings.unity (additively, so
    // the open scenes stay as they are) and the settings asset Resources/SoundSettings.asset.
    [InitializeOnLoad]
    public static class SoundSettingsSetup
    {
        const string ScenePath = "Assets/_Project/Scenes/sound_settings.unity";
        const string SettingsPath = "Assets/_Project/Resources/SoundSettings.asset";

        static SoundSettingsSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/create-sound-settings-scene") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/create-sound-settings-scene");
            try { Create(); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/create-sound-settings-scene-error.txt", e.ToString()); }
        }

        static SoundSettings Settings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SoundSettings>(SettingsPath);
            if (settings) return settings;
            settings = ScriptableObject.CreateInstance<SoundSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath); AssetDatabase.SaveAssets();
            return settings;
        }

        [MenuItem("Remaster/Sound/Create sound_settings scene")]
        public static void Create()
        {
            Settings();
            var active = EditorSceneManager.GetActiveScene();
            var existing = EditorSceneManager.GetSceneByPath(ScenePath);
            if (existing.isLoaded && existing.isDirty) throw new InvalidOperationException("sound_settings is open with unsaved changes; save or close it first.");
            if (existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("Sound Settings");
                var cam = new GameObject("Sound settings camera").AddComponent<Camera>();
                cam.transform.SetParent(root.transform, false); cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.05f, .06f, .09f);
                root.AddComponent<AudioListener>();
                root.AddComponent<SoundSettingsScene>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) EditorSceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            File.WriteAllText("Temp/create-sound-settings-scene.log", "created " + ScenePath);
            Debug.Log("Created " + ScenePath);
        }

        [MenuItem("Remaster/Sound/Open sound_settings scene")]
        static void Open()
        {
            if (!File.Exists(ScenePath)) Create();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
