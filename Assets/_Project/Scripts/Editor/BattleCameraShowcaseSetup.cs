using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class BattleCameraShowcaseSetup
    {
        const string ScenePath = "Assets/_Project/Scenes/BattleCameraShowcase.unity";
        const string Output = "Validation/CameraEffects";
        static BattleCameraShowcaseSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (File.Exists("Temp/stop-battle-camera-editor"))
            {
                File.Delete("Temp/stop-battle-camera-editor");
                EditorApplication.isPlaying = false;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Job("Temp/setup-battle-camera-showcase", Create);
            Job("Temp/check-battle-camera-showcase", Validate);
            Job("Temp/capture-battle-camera-showcase", Capture);
            Job("Temp/play-battle-camera-showcase", Play);
        }
        static void Job(string path, Action work)
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            try { work(); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText(path + "-error.txt", e.ToString()); }
        }
        static MuseumCharacter[] Characters() => new[] { "Jin", "Kazuya", "Xiaoyu", "Alisa", "Katarina" }
            .Select(n => AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/" + n + ".asset")).ToArray();

        [MenuItem("Remaster/Camera Effects/Create demonstration scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before creating the camera scene.");
            if (File.Exists(ScenePath)) return;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("Versus camera demonstration"); SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<BattleCameraShowcase>().characters = Characters();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save camera scene");
            }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/scene-created.txt", ScenePath);
        }

        [MenuItem("Remaster/Camera Effects/Play demonstration")]
        public static void Play()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene edits preserved; open " + ScenePath + " after saving.");
            Create(); EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.ExecuteMenuItem("Window/General/Game"); EditorApplication.isPlaying = true;
        }

        [MenuItem("Remaster/Camera Effects/Validate original tracks")]
        public static void Validate()
        {
            int samples = 0, rejected = 0;
            foreach (var row in BattleCameraEffects.Data.clips)
            {
                var track = new FightCameraTrack(row.resource);
                if (track.Frames != row.frames) throw new InvalidDataException("Frame count differs: " + row.resource);
                foreach (float frame in new[] { 0f, .5f, row.frames / 2f, row.frames - 1f })
                {
                    track.Sample(frame, Vector3.zero, Vector3.right, out var p, out var t, out float fov, out float blend);
                    track.Sample(frame, Vector3.zero, Vector3.left, out var q, out var u, out _, out _);
                    if (Vector3.Distance(q, new Vector3(-p.x, p.y, -p.z)) > .00001f ||
                        Vector3.Distance(u, new Vector3(-t.x, t.y, -t.z)) > .00001f ||
                        !float.IsFinite(fov) || fov < 5 || fov > 120 || blend < 0 || blend > 1 ||
                        (p - t).sqrMagnitude < 1e-8f) throw new InvalidDataException("Invalid camera sample: " + row.resource);
                    samples++;
                }
            }
            byte[] original = Resources.Load<TextAsset>("FightCameras/jin_96").bytes;
            foreach (string kind in new[] { "short", "descriptor", "zero frames", "nonfinite" })
            {
                var data = (byte[])original.Clone();
                if (kind == "short") data = new byte[12];
                else if (kind == "descriptor") BitConverter.GetBytes(99).CopyTo(data, 28);
                else if (kind == "zero frames") BitConverter.GetBytes(0).CopyTo(data, 4);
                else BitConverter.GetBytes(float.NaN).CopyTo(data, 32);
                try { new FightCameraTrack(data); }
                catch (InvalidOperationException) { rejected++; continue; }
                throw new InvalidDataException("Accepted bad track: " + kind);
            }
            foreach (var row in BattleCameraEffects.Data.shakeDispatch)
                if (!BattleCameraEffects.TryShakePreset(row.propertyId, out int preset) || preset != row.preset)
                    throw new InvalidDataException("Shake preset binding mismatch");
            if (BattleCameraEffects.TryShakePreset(0x802E, out _)) throw new InvalidDataException("Character particle property routed to camera");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.json", "{\"passed\":true,\"tracks\":" + BattleCameraEffects.Data.clips.Length +
                ",\"samples\":" + samples + ",\"malformedTracksRejected\":" + rejected + ",\"originalVisualComparison\":false}");
            Debug.Log("Original camera track validation passed.");
        }

        [MenuItem("Remaster/Camera Effects/Capture demonstration")]
        public static void Capture()
        {
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Camera capture"); SceneManager.MoveGameObjectToScene(root, scene);
            var demo = root.AddComponent<BattleCameraShowcase>(); demo.characters = Characters(); demo.showInterface = false;
            try
            {
                demo.Initialize(); demo.Playing = false;
                foreach (int frame in new[] { 0, 40, 90 })
                {
                    demo.Seek(frame);
                    // Synchronous editor sampling needs baked skinning snapshots, otherwise Unity
                    // can retain the first pose's matrices for the entire capture pass.
                    var renderers = demo.Model.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var baked = renderers.Select(r => { var mesh = new Mesh(); r.BakeMesh(mesh); return mesh; }).ToArray();
                    var snapshots = new GameObject[renderers.Length];
                    var rt = new RenderTexture(1280, 720, 24);
                    var before = RenderTexture.active;
                    try
                    {
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            var r = renderers[i]; var g = snapshots[i] = new GameObject("Capture skin");
                            g.transform.SetParent(r.transform, false); g.AddComponent<MeshFilter>().sharedMesh = baked[i];
                            g.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials; r.enabled = false;
                        }
                        demo.View.targetTexture = rt;
                        // Capture the complete shot, independently of the docked Game view's
                        // panel/letterbox dimensions and physical gate configuration.
                        demo.View.rect = new Rect(0, 0, 1, 1);
                        demo.View.usePhysicalProperties = false;
                        demo.View.aspect = 16f / 9f;
                        demo.View.fieldOfView = Camera.HorizontalToVerticalFieldOfView(demo.HorizontalFov, 16f / 9f);
                        demo.View.scene = scene;
                        demo.View.Render(); RenderTexture.active = rt;
                        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                        File.WriteAllBytes(Output + "/jin-camera-" + frame.ToString("000") + ".png", image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    finally
                    {
                        demo.View.targetTexture = null; RenderTexture.active = before;
                        UnityEngine.Object.DestroyImmediate(rt);
                        for (int i = 0; i < renderers.Length; i++)
                        { renderers[i].enabled = true; UnityEngine.Object.DestroyImmediate(snapshots[i]); UnityEngine.Object.DestroyImmediate(baked[i]); }
                    }
                }
                File.WriteAllText(Output + "/capture-complete.txt", "Three Jin original camera samples; no original-game visual comparison.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
