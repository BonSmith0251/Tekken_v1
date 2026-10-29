using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Versus.Remaster.Editor
{
    // env_scene: Temp/create-env-scene builds Assets/_Project/Scenes/env_scene.unity (additively, so the
    // open scenes stay as they are) with Alisa and Jin, the Render Settings object and the selection asset.
    // Temp/capture-stages renders the listed stages (one name per line, or a default set) through the
    // same EnvironmentScene code in a preview scene, into Validation/Stages.
    [InitializeOnLoad]
    public static class EnvironmentSceneSetup
    {
        const string ScenePath = "Assets/_Project/Scenes/env_scene.unity";
        const string SelectionPath = "Assets/_Project/Resources/EnvironmentSelection.asset";
        const string ProfilePath = "Assets/_Project/Rendering/StagingTestVolume.asset";
        static readonly string[] DefaultCaptures = { "stg01", "stg04", "stg09", "stg14", "stg19", "stg24", "stg30", "stg32" };

        static EnvironmentSceneSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists("Temp/create-env-scene"))
            {
                File.Delete("Temp/create-env-scene");
                try { Create(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/create-env-scene-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/verify-env-selection"))
            {
                File.Delete("Temp/verify-env-selection");
                try { VerifySelection(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/verify-env-selection-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/verify-stage-wind"))
            {
                var names = File.ReadAllLines("Temp/verify-stage-wind").Select(l => l.Trim()).Where(l => l != "").ToArray();
                File.Delete("Temp/verify-stage-wind");
                try { VerifyWind(names); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/verify-stage-wind-error.txt", e.ToString()); }
            }
            if (File.Exists("Temp/capture-stages"))
            {
                var names = File.ReadAllLines("Temp/capture-stages").Select(l => l.Trim()).Where(l => l != "").ToArray();
                File.Delete("Temp/capture-stages");
                try { Capture(names.Length > 0 ? names : DefaultCaptures); }
                catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/capture-stages-error.txt", e.ToString()); }
            }
        }

        // Saves a test choice through EnvironmentSelection.Save, checks the asset file and the versus
        // pick (EnvironmentSelection.Load().PickForMatch, as VersusGame.BuildArena calls it), then restores it.
        static void VerifySelection()
        {
            var selection = Selection();
            var saved = selection.environments;
            var lines = new List<string>();
            try
            {
                selection.environments = new[] { "stg04", "stg24", "stg99-missing" };
                selection.Save();
                string yaml = File.ReadAllText(SelectionPath);
                lines.Add("assetHasStg04=" + yaml.Contains("stg04") + " assetHasStg24=" + yaml.Contains("stg24"));
                var random = new System.Random(7);
                var picks = Enumerable.Range(0, 200).Select(_ => EnvironmentSelection.Load().PickForMatch(random)).GroupBy(p => p).Select(g => g.Key + ":" + g.Count());
                lines.Add("picks " + string.Join(" ", picks));
                selection.environments = new string[0];
                lines.Add("empty -> " + selection.PickForMatch(random));
            }
            finally { selection.environments = saved; selection.Save(); }
            File.WriteAllText("Validation/Stages/selection.txt", string.Join("\n", lines));
        }

        // Poses each stage's foliage wind at two times and records how far the vertices moved, since a
        // still capture can't show the sway. Writes Validation/Stages/wind.json.
        static void VerifyWind(string[] stages)
        {
            var lines = new List<string>();
            foreach (var name in stages)
            {
                var prefab = Resources.Load<GameObject>("MuseumStages/" + name);
                if (!prefab) { lines.Add("{\"stage\":\"" + name + "\",\"error\":\"not imported\"}"); continue; }
                var preview = new PreviewRenderUtility();
                try
                {
                    var stage = preview.InstantiatePrefabInScene(prefab);
                    var wind = stage.GetComponent<StageWind>();
                    if (!wind) { lines.Add("{\"stage\":\"" + name + "\",\"foliage\":0}"); continue; }
                    var probe = wind.foliage.Where(f => f).ToArray();
                    wind.Prepare();
                    var before = probe.Select(f => f.sharedMesh.vertices).ToArray();
                    float peak = 0;
                    for (int k = 1; k <= 30; k++) peak = Mathf.Max(peak, wind.Step(k * .1f));
                    float moved = 0;
                    for (int i = 0; i < probe.Length; i++)
                    {
                        var after = probe[i].sharedMesh.vertices; float s = probe[i].transform.lossyScale.magnitude / Mathf.Sqrt(3);
                        for (int v = 0; v < after.Length; v++) moved = Mathf.Max(moved, (after[v] - before[i][v]).magnitude * s);
                    }
                    lines.Add("{\"stage\":\"" + name + "\",\"foliage\":" + probe.Length + ",\"hanging\":" + wind.hanging.Count(h => h) +
                              ",\"vertices\":" + probe.Sum(f => f.sharedMesh.vertexCount) + ",\"peakOffsetMetres\":" + peak.ToString("0.000") +
                              ",\"measuredOffsetMetres\":" + moved.ToString("0.000") + ",\"strength\":" + wind.strength + ",\"speed\":" + wind.speed + "}");
                }
                finally { preview.Cleanup(); EditorUtility.UnloadUnusedAssetsImmediate(); }
            }
            Directory.CreateDirectory("Validation/Stages");
            File.WriteAllText("Validation/Stages/wind.json", "[\n" + string.Join(",\n", lines) + "\n]");
        }

        static MuseumCharacter[] Fighters() => new[] {
            AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Alisa.asset"),
            AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Jin.asset") };

        static EnvironmentSelection Selection()
        {
            var selection = AssetDatabase.LoadAssetAtPath<EnvironmentSelection>(SelectionPath);
            if (selection) return selection;
            selection = ScriptableObject.CreateInstance<EnvironmentSelection>();
            AssetDatabase.CreateAsset(selection, SelectionPath); AssetDatabase.SaveAssets();
            return selection;
        }

        [MenuItem("Remaster/Stages/Create env_scene")]
        public static void Create()
        {
            var fighters = Fighters();
            if (fighters.Any(f => !f)) throw new InvalidOperationException("Alisa or Jin museum asset is missing.");
            Selection();
            var active = EditorSceneManager.GetActiveScene();
            var existing = EditorSceneManager.GetSceneByPath(ScenePath);
            if (existing.isLoaded && existing.isDirty) throw new InvalidOperationException("env_scene is open with unsaved changes; save or close it first.");
            if (existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var controller = new GameObject("Environment Select").AddComponent<EnvironmentScene>();
                controller.fighters = fighters;
                SceneManagerMove(controller.gameObject, scene);
                // The env camera looks along +Z like the versus cameras, so the rig takes the versus yaw.
                var settings = StagingRenderSettings.Create(null, AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath));
                settings.transform.localRotation = Quaternion.Euler(0, StagingRenderSettings.VersusYaw, 0);
                SceneManagerMove(settings.gameObject, scene);
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
            File.WriteAllText("Temp/create-env-scene.log", "created " + ScenePath);
            Debug.Log("Created " + ScenePath);
        }

        static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        const int Width = 1280, Height = 720;

        public static void Capture(string[] stages)
        {
            Directory.CreateDirectory("Validation/Stages");
            var report = new List<string>();
            // "stg01 view <yaw> <pitch> <distance> <label>" renders one extra stage-only view to
            // view-<stg>-<label>.png (camera orbits (0,1,0) as in env_scene) instead of the default pair.
            foreach (var line in stages.Where(s => s.Contains(" view ")))
            {
                var f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                report.Add(Shot(f[0], false, float.Parse(f[2]), float.Parse(f[3]), float.Parse(f[4]),
                    "Validation/Stages/view-" + f[0] + "-" + (f.Length > 5 ? f[5] : f[2] + "_" + f[3]) + ".png"));
            }
            stages = stages.Where(s => !s.Contains(" view ")).ToArray();
            if (stages.Length == 0) { File.WriteAllText("Validation/Stages/capture.json", "[\n" + string.Join(",\n", report) + "\n]"); return; }
            foreach (var stage in stages.Contains(EnvironmentSelection.Arena) ? stages : new[] { EnvironmentSelection.Arena }.Concat(stages).ToArray())
            {
                // The env_scene view with both fighters, then a wide shot of the stage alone.
                report.Add(Shot(stage, true, 0, 8, 5.2f, "Validation/Stages/env_scene-" + stage + ".png"));
                if (stage != EnvironmentSelection.Arena) Shot(stage, false, 25, 14, 14, "Validation/Stages/" + stage + ".png");
            }
            File.WriteAllText("Validation/Stages/capture.json", "[\n" + string.Join(",\n", report) + "\n]");
            Debug.Log("Captured " + stages.Length + " stages into Validation/Stages");
        }

        // One preview per shot: a reused preview camera keeps HDRP's exposure history, and every
        // render after its first comes out white.
        static string Shot(string stage, bool fighters, float yaw, float pitch, float distance, string path)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                foreach (var light in preview.lights) light.enabled = false;
                var root = new GameObject("env_scene capture");
                preview.AddSingleGO(root);
                // At runtime HdrpSceneLighting installs this global volume (fixed exposure).
                var global = root.AddComponent<Volume>(); global.isGlobal = true;
                global.sharedProfile = Resources.Load<VolumeProfile>("RemasterEnvironment");
                var settings = StagingRenderSettings.Create(root.transform, AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath));
                settings.transform.localRotation = Quaternion.Euler(0, StagingRenderSettings.VersusYaw, 0);
                var env = root.AddComponent<EnvironmentScene>(); env.fighters = Fighters();
                env.Build(settings);
                env.Camera.enabled = false;
                preview.ambientColor = settings.ambientLight;
                env.Preview(stage);
                foreach (var model in env.FighterModels) model.SetActive(fighters);
                env.SetView(yaw, pitch, distance); env.Pose(0);
                // Stage particle systems: start them and run 3 s so fires and sand streams are full.
                var stageObject = root.transform.Find(stage);
                if (stageObject)
                {
                    var effects = StageEnvironment.StartEffects(stageObject.gameObject);
                    for (int i = 0; i < 90; i++) foreach (var fx in effects) if (fx) fx.SimulateStep(1f / 30);
                }
                var renderers = (root.transform.Find(stage) ?? root.transform.Find("Arena")).GetComponentsInChildren<MeshRenderer>();
                var slots = renderers.SelectMany(r => r.sharedMaterials).ToArray();
                int textured = slots.Count(m => m && m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap"));
                Save(preview, env, settings, path);
                return "{\"stage\":\"" + stage + "\",\"renderers\":" + renderers.Length + ",\"submeshes\":" + slots.Length + ",\"textured\":" + textured + ",\"alignment\":\"" + (stage == EnvironmentSelection.Arena ? "" : StageEnvironment.LastAlignment) + "\"}";
            }
            finally
            {
                preview.Cleanup();
                // Each stage loads hundreds of meshes and textures; without this the GPU runs out of
                // memory partway through a full capture (d3d11 E_OUTOFMEMORY, editor crash at stg30).
                EditorUtility.UnloadUnusedAssetsImmediate();
                GC.Collect();
            }
        }

        static void Save(PreviewRenderUtility preview, EnvironmentScene env, StagingRenderSettings settings, string path)
        {
            var cam = preview.camera;
            cam.transform.SetPositionAndRotation(env.Camera.transform.position, env.Camera.transform.rotation);
            cam.fieldOfView = env.Camera.fieldOfView; cam.nearClipPlane = env.Camera.nearClipPlane; cam.farClipPlane = env.Camera.farClipPlane;
            cam.clearFlags = CameraClearFlags.SolidColor; settings.Apply(cam, null);
            preview.BeginStaticPreview(new Rect(0, 0, Width, Height)); preview.Render(true);
            var image = preview.EndStaticPreview();
            var target = RenderTexture.GetTemporary(Width, Height, 0); var previous = RenderTexture.active;
            Graphics.Blit(image, target); RenderTexture.active = target;
            var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false); frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); frame.Apply();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, frame.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(frame); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
