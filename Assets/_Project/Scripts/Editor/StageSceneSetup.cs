using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Versus.Remaster.Editor
{
    // One scene per original stage, Scenes/Stages/<stg>.unity, for editing and lightmap baking. Each holds a
    // linked instance of Resources/MuseumStages/<stg>.prefab (so re-imports update it), placed on its match
    // start as in versus, plus the stage's own sky/fog/post volume, a static lighting sky, every light the
    // original only baked (switched to Unity baked lights), light and reflection probes around the fight
    // area, a camera on the match start and LightingSettings for the bake. Scenes are built additively;
    // the open scenes are not touched.
    //   Temp/create-stage-scenes  stage names one per line (empty = all); "force" rebuilds existing scenes
    //   Temp/bake-stage-scene     bakes the open stage scene; it must be the only scene open
    [InitializeOnLoad]
    public static class StageSceneSetup
    {
        const string Folder = "Assets/_Project/Scenes/Stages";
        // Lightmap texels per metre. Stages run to a few hundred metres; 2 keeps one stage within a few
        // 2048 maps. Raise it per scene in Lighting > Lightmapping Settings for close-up quality.
        const float TexelsPerMetre = 2;
        // Bounce tuning (set by eye on stg01, 2026-10-26: with the defaults the sun's bounce barely reached the
        // shadows, which stayed near-black). Not recovered from the original, which baked with Lightmass.
        const float IndirectScale = 2.5f, AlbedoBoost = 1.5f, SunBounce = 2;
        const int Bounces = 4;
        // Lightmapped surfaces within this radius of the match start get ArenaScale x the texel density.
        const float ArenaRadius = 30, ArenaScale = 4;

        static StageSceneSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
            if (File.Exists("Temp/create-stage-scenes"))
            {
                var lines = File.ReadAllLines("Temp/create-stage-scenes").Select(l => l.Trim()).Where(l => l != "").ToList();
                File.Delete("Temp/create-stage-scenes");
                bool force = lines.Remove("force");
                try { CreateAll(lines.Count > 0 ? lines.ToArray() : StageEnvironment.Names, force); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/create-stage-scenes-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/capture-open-stage"))
            {
                var views = File.ReadAllLines("Temp/capture-open-stage").Select(l => l.Trim()).Where(l => l != "").ToArray();
                File.Delete("Temp/capture-open-stage");
                try { CaptureOpenStageScene(views); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/capture-open-stage-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/add-stage-navigator"))
            {
                // "save-open": the user confirmed an open stage scene's unsaved change is harmless; save it first.
                bool saveOpen = File.ReadAllText("Temp/add-stage-navigator").Contains("save-open");
                File.Delete("Temp/add-stage-navigator");
                try { AddNavigatorToAll(saveOpen); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/add-stage-navigator-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/retune-open-stage"))
            {
                // "save-first" in the flag: the user confirmed the scene's unsaved change is harmless; save it first.
                bool saveFirst = File.ReadAllText("Temp/retune-open-stage").Contains("save-first");
                File.Delete("Temp/retune-open-stage");
                try { RetuneOpenStageScene(saveFirst); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/retune-open-stage-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/bake-stage-scene"))
            {
                File.Delete("Temp/bake-stage-scene");
                try { BakeOpenStageScene(); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/bake-stage-scene-error.txt", e.ToString()); }
            }
        }

        [MenuItem("Remaster/Stages/Create stage scenes (missing only)")]
        static void CreateMissing() => CreateAll(StageEnvironment.Names, false);

        public static void CreateAll(string[] stages, bool force)
        {
            Directory.CreateDirectory(Folder);
            var report = new List<string>();
            foreach (var stage in stages)
            {
                string path = Folder + "/" + stage + ".unity";
                if (File.Exists(path) && !force) { report.Add("{\"stage\":\"" + stage + "\",\"skipped\":\"exists\"}"); continue; }
                var open = EditorSceneManager.GetSceneByPath(path);
                if (open.isLoaded) { report.Add("{\"stage\":\"" + stage + "\",\"skipped\":\"open in the editor\"}"); continue; }
                report.Add(Create(stage, path));
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            File.WriteAllText("Validation/Stages/scenes.json", "[\n" + string.Join(",\n", report) + "\n]");
            Debug.Log("Stage scenes: " + report.Count + " processed into " + Folder);
        }

        static string Create(string stage, string path)
        {
            var prefab = Resources.Load<GameObject>("MuseumStages/" + stage);
            if (!prefab) throw new InvalidOperationException(stage + " is not imported.");
            string assets = Folder + "/" + stage;
            Directory.CreateDirectory(assets);
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("Stage Scene");
                SceneManager.MoveGameObjectToScene(root, scene);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = stage;
                StageEnvironment.Place(instance, null);
                var marker = root.AddComponent<StageScene>();
                marker.stage = stage; marker.stageInstance = instance;

                int baked = 0, mixed = 0;
                foreach (var b in instance.GetComponentsInChildren<StageBakedLight>(true))
                {
                    // Every light the original baked: on, baked into the lightmaps only, with soft shadows.
                    b.gameObject.SetActive(true);
                    var light = b.GetComponent<Light>(); var hd = b.GetComponent<HDAdditionalLightData>();
                    light.lightmapBakeType = LightmapBakeType.Baked;
                    light.shadows = LightShadows.Soft;
                    hd.SetIntensity(b.bakeIntensity, b.unit);
                    Record(b.gameObject, light, hd);
                    baked++;
                }
                foreach (var light in instance.GetComponentsInChildren<Light>(true))
                {
                    if (light.GetComponent<StageBakedLight>() || light.type != LightType.Directional) continue;
                    // Suns stay realtime for the fighters and bake their bounce light (Mixed, indirect only).
                    light.lightmapBakeType = LightmapBakeType.Mixed;
                    Record(light);
                    mixed++;
                }

                var lighting = instance.GetComponent<StageLighting>();
                var volumeGo = new GameObject("Stage Volume");
                SceneManager.MoveGameObjectToScene(volumeGo, scene);
                volumeGo.transform.SetParent(root.transform, false);
                VolumeProfile profile = null;
                if (lighting && lighting.profile)
                {
                    string profilePath = assets + "/Volume.asset";
                    AssetDatabase.DeleteAsset(profilePath);
                    profile = lighting.PlacedProfile();
                    AssetDatabase.CreateAsset(profile, profilePath);
                    foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                    var volume = volumeGo.AddComponent<Volume>();
                    volume.isGlobal = true; volume.priority = 50; volume.sharedProfile = profile;
                    // HDRP bakes ambient and reflections from the "static lighting sky", not the volume.
                    var staticSky = volumeGo.AddComponent<StaticLightingSky>();
                    staticSky.profile = profile;
                    if (profile.TryGet<VisualEnvironment>(out var env)) staticSky.staticLightingSkyUniqueID = env.skyType.value;
                }

                // Probes over the fight area: a 3 m grid 36 m across and 7.5 m high, and one baked
                // reflection probe for the same space.
                var probes = new GameObject("Light Probes").AddComponent<LightProbeGroup>();
                SceneManager.MoveGameObjectToScene(probes.gameObject, scene);
                probes.transform.SetParent(root.transform, false);
                var positions = new List<Vector3>();
                for (int x = -6; x <= 6; x++) for (int z = -6; z <= 6; z++) for (int y = 0; y < 4; y++)
                    positions.Add(new Vector3(x * 3, .5f + y * 2.3f, z * 3));
                probes.probePositions = positions.ToArray();
                var reflection = new GameObject("Reflection Probe");
                SceneManager.MoveGameObjectToScene(reflection, scene);
                reflection.transform.SetParent(root.transform, false);
                reflection.transform.localPosition = new Vector3(0, 2, 0);
                reflection.AddComponent<ReflectionProbe>();
                var probeData = reflection.AddComponent<HDAdditionalReflectionData>();
                probeData.mode = ProbeSettings.Mode.Baked;
                probeData.influenceVolume.shape = InfluenceShape.Box;
                probeData.influenceVolume.boxSize = new Vector3(40, 15, 40);

                // The env_scene view: behind player one's side of the fight axis, looking along +Z.
                var cam = new GameObject("Stage Camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
                cam.transform.SetParent(root.transform, false);
                cam.transform.position = new Vector3(0, 1.7f, -5.2f);
                cam.transform.LookAt(new Vector3(0, 1, 0));
                cam.fieldOfView = 35; cam.nearClipPlane = .05f; cam.farClipPlane = 2000;
                cam.gameObject.AddComponent<HDAdditionalCameraData>();
                cam.tag = "MainCamera";

                AddNavigator(root);

                string settingsPath = assets + "/Lighting.lighting";
                var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
                if (!settings) { settings = new LightingSettings { name = stage + " lighting" }; AssetDatabase.CreateAsset(settings, settingsPath); }
                settings.bakedGI = true; settings.realtimeGI = false;
                settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
                settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
                settings.lightmapResolution = TexelsPerMetre; settings.lightmapMaxSize = 2048; settings.lightmapPadding = 2;
                settings.directionalityMode = LightmapsMode.NonDirectional;
                settings.lightmapCompression = LightmapCompression.NormalQuality;
                settings.ao = true; settings.aoMaxDistance = 1;
                Lightmapping.SetLightingSettingsForScene(scene, settings);
                Tune(instance, settings);

                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                int contributing = renderers.Count(r => GameObjectUtility.GetStaticEditorFlags(r.gameObject).HasFlag(StaticEditorFlags.ContributeGI));
                int lightmapped = renderers.Count(r => r.receiveGI == ReceiveGI.Lightmaps && GameObjectUtility.GetStaticEditorFlags(r.gameObject).HasFlag(StaticEditorFlags.ContributeGI));
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save " + path);
                return "{\"stage\":\"" + stage + "\",\"scene\":\"" + path + "\",\"renderers\":" + renderers.Length + ",\"contributeGI\":" + contributing +
                       ",\"lightmapped\":" + lightmapped + ",\"bakedLights\":" + baked + ",\"mixedSuns\":" + mixed +
                       ",\"probes\":" + positions.Count + ",\"volume\":" + (profile ? "true" : "false") + "}";
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // Bounce strength, sun indirect and arena texel density (see the constants). Create applies it; the
        // Temp/retune-open-stage flag applies it to the open stage scene and saves it, ready to rebake.
        static string Tune(GameObject instance, LightingSettings settings)
        {
            settings.indirectScale = IndirectScale; settings.albedoBoost = AlbedoBoost; settings.maxBounces = Bounces;
            settings.environmentSampleCount = 512;
            EditorUtility.SetDirty(settings);
            int suns = 0, near = 0;
            foreach (var light in instance.GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Directional && light.lightmapBakeType == LightmapBakeType.Mixed)
                { light.bounceIntensity = SunBounce; Record(light); suns++; }
            var start = instance.transform.parent ? instance.transform.parent.position : Vector3.zero;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.receiveGI != ReceiveGI.Lightmaps) continue;
                var b = r.bounds; var p = b.ClosestPoint(start); p.y = start.y;
                float scale = (new Vector2(p.x - start.x, p.z - start.z)).magnitude <= ArenaRadius ? ArenaScale : 1;
                var so = new SerializedObject(r);
                var prop = so.FindProperty("m_ScaleInLightmap");
                if (Mathf.Approximately(prop.floatValue, scale)) continue;
                prop.floatValue = scale; so.ApplyModifiedPropertiesWithoutUndo(); Record(r);
                if (scale > 1) near++;
            }
            return "{\"suns\":" + suns + ",\"arenaRenderers\":" + near + ",\"indirectScale\":" + IndirectScale + ",\"albedoBoost\":" + AlbedoBoost + ",\"bounces\":" + Bounces + "}";
        }

        public static void RetuneOpenStageScene(bool saveFirst = false)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.path.StartsWith(Folder + "/")) throw new InvalidOperationException(scene.path + " is not a stage scene.");
            if (saveFirst && scene.isDirty) EditorSceneManager.SaveScene(scene);
            // Retuning saves the scene; never save someone's unsaved edits along with it.
            if (scene.isDirty) throw new InvalidOperationException("Save or revert " + scene.name + " first; it has unsaved changes.");
            var marker = UnityEngine.Object.FindObjectsByType<StageScene>(FindObjectsSortMode.None).FirstOrDefault(m => m.gameObject.scene == scene);
            if (!marker || !marker.stageInstance) throw new InvalidOperationException(scene.name + " has no StageScene root.");
            var settings = Lightmapping.GetLightingSettingsForScene(scene);
            string result = Tune(marker.stageInstance, settings);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Validation/Stages/retune-" + scene.name + ".json", result);
        }

        // The fly navigator (StageNavigator): flies the scene camera in Play. Renames the earlier
        // "Third-Person Navigator" object (same component) to the current name.
        const string NavigatorName = "Fly Navigator";
        static bool AddNavigator(GameObject root)
        {
            var existing = root.GetComponentInChildren<StageNavigator>(true);
            if (existing) { if (existing.name == NavigatorName) return false; existing.name = NavigatorName; return true; }
            var go = new GameObject(NavigatorName);
            SceneManager.MoveGameObjectToScene(go, root.scene);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<StageNavigator>();
            return true;
        }

        // Temp/add-stage-navigator: adds the navigator to every existing stage scene without rebuilding it
        // (bakes and edits stay). Open scenes are updated only when they have no unsaved changes.
        public static void AddNavigatorToAll(bool saveOpen = false)
        {
            var report = new List<string>();
            foreach (var path in Directory.GetFiles(Folder, "*.unity").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')).OrderBy(p => p))
            {
                var scene = EditorSceneManager.GetSceneByPath(path);
                bool wasOpen = scene.isLoaded;
                if (wasOpen && scene.isDirty && saveOpen) EditorSceneManager.SaveScene(scene);
                if (wasOpen && scene.isDirty) { report.Add(path + ": skipped, open with unsaved changes"); continue; }
                if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var root = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<StageScene>());
                    if (!root) { report.Add(path + ": no StageScene root"); continue; }
                    bool added = AddNavigator(root);
                    if (added) EditorSceneManager.SaveScene(scene);
                    report.Add(path + (added ? ": added or renamed" : ": already up to date"));
                }
                finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
            }
            File.WriteAllText("Validation/Stages/navigator.txt", string.Join(Environment.NewLine, report));
        }

        static void Record(params UnityEngine.Object[] objects)
        {
            foreach (var o in objects) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        // Temp/capture-open-stage: renders the open stage scene as it is (lightmaps included), through a
        // temporary unsaved copy of its Stage Camera orbiting (0, 1, 0). Lines "label yaw pitch distance";
        // default views "fight 0 8 5.2" and "wide 25 14 14". Writes Validation/Stages/scene-<stg>-<label>.png.
        public static void CaptureOpenStageScene(string[] views)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.path.StartsWith(Folder + "/")) throw new InvalidOperationException(scene.path + " is not a stage scene.");
            string stage = Path.GetFileNameWithoutExtension(scene.path);
            var source = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.name == "Stage Camera" && c.gameObject.scene == scene);
            if (!source) throw new InvalidOperationException(stage + " has no Stage Camera.");
            if (views.Length == 0) views = new[] { "fight 0 8 5.2", "wide 25 14 14" };
            var go = new GameObject("Stage capture") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(source);
                go.AddComponent<HDAdditionalCameraData>();
                cam.targetTexture = rt;
                foreach (var view in views)
                {
                    var f = view.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    float yaw = float.Parse(f[1]), pitch = float.Parse(f[2]), distance = float.Parse(f[3]);
                    // "baked-only" as a fifth word: realtime lights off for this render, to see the lightmaps alone.
                    var realtime = f.Length > 4 && f[4] == "baked-only"
                        ? UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled && l.gameObject.scene == scene && l.lightmapBakeType != LightmapBakeType.Baked).ToList()
                        : new List<Light>();
                    foreach (var l in realtime) l.enabled = false;
                    var target = new Vector3(0, 1, 0);
                    cam.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * distance;
                    cam.transform.LookAt(target);
                    // A few frames so exposure and the sky settle on this new camera.
                    for (int i = 0; i < 4; i++) cam.Render();
                    var previous = RenderTexture.active; RenderTexture.active = rt;
                    var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                    RenderTexture.active = previous;
                    foreach (var l in realtime) l.enabled = true;
                    File.WriteAllBytes("Validation/Stages/scene-" + stage + "-" + f[0] + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            File.WriteAllText("Validation/Stages/scene-" + stage + "-capture.json", "{\"stage\":\"" + stage + "\",\"views\":" + views.Length +
                ",\"lightmaps\":" + LightmapSettings.lightmaps.Length + "}");
        }

        [MenuItem("Remaster/Stages/Bake open stage scene")]
        public static void BakeOpenStageScene()
        {
            if (SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open one stage scene on its own (File > Open Scene " + Folder + "/<stg>.unity); " +
                                                    SceneManager.sceneCount + " scenes are open, and a bake writes lighting into every open scene.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.path.StartsWith(Folder + "/")) throw new InvalidOperationException(scene.path + " is not a stage scene in " + Folder + ".");
            if (scene.isDirty) throw new InvalidOperationException("Save " + scene.name + " before baking.");
            string stage = Path.GetFileNameWithoutExtension(scene.path);
            var started = DateTime.Now;
            void Done()
            {
                Lightmapping.bakeCompleted -= Done;
                var maps = LightmapSettings.lightmaps;
                File.WriteAllText("Validation/Stages/bake-" + stage + ".json",
                    "{\"stage\":\"" + stage + "\",\"seconds\":" + (int)(DateTime.Now - started).TotalSeconds + ",\"lightmaps\":" + maps.Length +
                    ",\"size\":\"" + (maps.Length > 0 && maps[0].lightmapColor ? maps[0].lightmapColor.width + "x" + maps[0].lightmapColor.height : "") + "\"}");
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Baked " + stage + ": " + maps.Length + " lightmaps");
            }
            Lightmapping.bakeCompleted += Done;
            if (!Lightmapping.BakeAsync()) { Lightmapping.bakeCompleted -= Done; throw new InvalidOperationException("The lightmapper did not start."); }
        }
    }
}
