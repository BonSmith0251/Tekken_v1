using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Renders face close-ups in an isolated preview scene, leaving the open scene untouched.
    [InitializeOnLoad]
    public static class EyeCloseupCapture
    {
        static EyeCloseupCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/capture-eyes") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/capture-eyes");
            try { Run(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/capture-eyes-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Rendering/Capture eye close-ups")]
        public static void Run()
        {
            Directory.CreateDirectory("Validation/Eyes");
            Capture("Assets/_Project/Characters/Jin/Costumes/Prefabs/Default_1P.prefab", "jin-museum");
            Capture("Assets/_Project/Resources/JinPlayable.prefab", "jin-versus");
            Capture("Assets/_Project/Characters/Xiaoyu/Costumes/Prefabs/Default_1P.prefab", "xia-default");
            Capture("Assets/_Project/Characters/Lidia/Costumes/Prefabs/Default_1P.prefab", "lidia-default");
            Debug.Log("Eye close-ups written to Validation/Eyes");
        }

        static void Capture(string prefabPath, string name)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var root = preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                var eyes = root.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .First(r => r.sharedMaterials.Any(m => m && m.name.Contains("_Eyes")));
                var mesh = eyes.sharedMesh;
                int sub = Array.FindIndex(eyes.sharedMaterials, m => m && m.name.Contains("_Eyes"));
                var baked = new Mesh(); eyes.BakeMesh(baked, true);
                var points = baked.GetTriangles(sub).Select(i => eyes.transform.TransformPoint(baked.vertices[i])).ToArray();
                UnityEngine.Object.DestroyImmediate(baked);
                var center = points.Aggregate(Vector3.zero, (a, p) => a + p) / points.Length;
                // The eyes sit forward of the head bone, which fixes the camera side for any rig.
                var head = root.GetComponentsInChildren<Transform>().First(t => string.Equals(t.name, "Head", StringComparison.OrdinalIgnoreCase));
                var forward = root.transform.forward;
                if (Vector3.Dot(center - head.position, forward) < 0) forward = -forward;
                for (int view = 0; view < 2; view++)
                {
                    float distance = view == 0 ? .16f : .32f;
                    var cam = preview.camera;
                    cam.transform.position = center + forward * distance + Vector3.up * .005f;
                    cam.transform.LookAt(center);
                    cam.fieldOfView = 30; cam.nearClipPlane = .01f; cam.farClipPlane = 10;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .2f, .24f);
                    preview.lights[0].intensity = 1.2f; preview.lights[0].transform.rotation = Quaternion.LookRotation(-forward + Vector3.down * .5f + Vector3.right * .3f);
                    preview.lights[1].intensity = .6f;
                    preview.ambientColor = new Color(.35f, .35f, .35f);
                    preview.BeginStaticPreview(new Rect(0, 0, 900, 600));
                    preview.Render(true);
                    var image = preview.EndStaticPreview();
                    File.WriteAllBytes("Validation/Eyes/" + name + (view == 0 ? "-close" : "-face") + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { preview.Cleanup(); }
        }
    }
}
