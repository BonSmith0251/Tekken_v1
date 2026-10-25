using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Renders each converted battle effect beside Jin at several moments, in an isolated preview
    // scene (the open scene is untouched). Output: Validation/HitEffects/<role>-<ms>.png.
    [InitializeOnLoad]
    public static class HitEffectsCapture
    {
        static readonly float[] Moments = { .03f, .08f, .16f, .3f, .55f };
        static HitEffectsCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/capture-hit-effects") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/capture-hit-effects");
            try { Run(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/capture-hit-effects-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Rendering/Capture battle effects")]
        public static void Run()
        {
            Directory.CreateDirectory("Validation/HitEffects");
            foreach (var role in new[] { CascadeEffects.Hit, CascadeEffects.Counter, CascadeEffects.Guard, CascadeEffects.Pierce,
                                         CascadeEffects.Blast, CascadeEffects.Smoke, CascadeEffects.Rage, CascadeEffects.RageAura,
                                         CascadeEffects.PunishMid, CascadeEffects.KO })
                Capture(role);
            Debug.Log("Battle effect captures written to Validation/HitEffects");
        }

        static void Capture(string role)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var jin = preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/JinPlayable.prefab"));
                jin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.left));
                var host = new GameObject("Effects host");
                preview.AddSingleGO(host);
                var effects = CascadeEffects.Create(host.transform);
                Shader.SetGlobalFloat("_CascadeExposure", 1 / 1.2f);
                var bones = jin.GetComponentsInChildren<Transform>().Where(t => new[] { "Hip", "Spine1", "Spine2", "Head", "L_Arm", "R_Arm",
                    "L_Hand", "R_Hand", "L_Leg", "R_Leg", "L_Foot", "R_Foot" }.Contains(t.name)).ToArray();
                bool floor = role == CascadeEffects.Blast || role == CascadeEffects.Smoke;
                bool body = role == CascadeEffects.Rage || role == CascadeEffects.RageAura;
                // A blow arriving from screen left into Jin's chest; floor effects under his hips.
                var at = floor ? new Vector3(0, 0, 0) : body ? new Vector3(0, 1.2f, 0) : new Vector3(-.12f, 1.3f, 0);
                // Hits face along the blow (screen right, into Jin); Rage faces along Jin (screen left).
                var instance = effects.Play(role, at, Quaternion.LookRotation(body ? Vector3.left : Vector3.right), 1, body ? bones : null);
                if (!instance) { File.WriteAllText("Validation/HitEffects/" + role + "-missing.txt", "role not exported"); return; }
                float clock = 0; const float step = 1 / 120f;
                // The aura spawns at 1-4 particles a second, so it is sampled over its first seconds.
                foreach (float moment in role == CascadeEffects.RageAura ? new[] { .5f, 1f, 1.5f, 2f, 3f } : Moments)
                {
                    while (clock < moment) { instance.SimulateStep(step); clock += step; }
                    var cam = preview.camera;
                    cam.transform.position = new Vector3(0, 1.15f, -2.6f);
                    cam.transform.LookAt(new Vector3(0, floor ? .5f : 1.05f, 0));
                    cam.fieldOfView = 40; cam.nearClipPlane = .05f; cam.farClipPlane = 20;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.06f, .07f, .1f);
                    preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
                    preview.lights[1].intensity = .5f;
                    preview.ambientColor = new Color(.3f, .3f, .32f);
                    preview.BeginStaticPreview(new Rect(0, 0, 640, 480));
                    preview.Render(true);
                    var image = preview.EndStaticPreview();
                    File.WriteAllBytes($"Validation/HitEffects/{role}-{Mathf.RoundToInt(moment * 1000):000}.png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { preview.Cleanup(); }
        }
    }
}
