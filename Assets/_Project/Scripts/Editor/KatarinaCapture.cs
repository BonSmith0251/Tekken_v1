using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Poses Katarina's costumes with her own museum clips in an isolated preview scene: full body in
    // her stance, a face close-up, and entrance/victory frames. The open scene is not touched.
    [InitializeOnLoad]
    public static class KatarinaCapture
    {
        static KatarinaCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/capture-katarina") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/capture-katarina");
            try { Run(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/capture-katarina-error.txt", e.ToString()); }
        }

        [MenuItem("Remaster/Rendering/Capture Katarina")]
        public static void Run()
        {
            Directory.CreateDirectory("Validation/Katarina");
            var character = AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Katarina.asset");
            foreach (var costume in character.costumes)
                Render(character, costume.prefab, 1419, 0, costume.name.Replace(' ', '_') + "-stance", new[] { (0f, "front"), (35f, "three-quarter") }, false);
            Render(character, character.costumes[0].prefab, 1419, 0, "face", new[] { (0f, "front"), (25f, "side") }, true);
            Render(character, character.costumes[0].prefab, 2083, .5f, "entrance-2083", new[] { (0f, "front") }, false);
            Render(character, character.costumes[0].prefab, 2088, .7f, "victory-2088", new[] { (0f, "front") }, false);
            // Neck and shoulder seams in a pose that turns the head and bends the arms.
            foreach (var costume in character.costumes)
                Render(character, costume.prefab, 2083, .5f, costume.name.Replace(' ', '_') + "-seams-2083", new[] { (0f, "front"), (60f, "side"), (90f, "arm") }, false, true);
            Debug.Log("Katarina captures written to Validation/Katarina");
        }

        static void Render(MuseumCharacter character, GameObject prefab, int move, float at, string label, (float yaw, string view)[] views, bool face, bool upperBody = false)
        {
            var preview = new PreviewRenderUtility();
            var baked = new List<Mesh>();
            try
            {
                var root = preview.InstantiatePrefabInScene(prefab);
                var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
                foreach (var bone in root.GetComponentsInChildren<Transform>()) map[bone.name] = bone;
                Pose(character, map, move, at);
                Vector3 Bone(string name) => map[name].position;
                var right = Bone("L_UpLeg") - Bone("R_UpLeg"); right.y = 0; right.Normalize();
                var forward = Vector3.Cross(Vector3.up, right);
                var draws = new List<(Mesh mesh, Matrix4x4 matrix, Material[] materials)>();
                var points = new List<Vector3>();
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh(); renderer.BakeMesh(mesh, true); baked.Add(mesh);
                    draws.Add((mesh, renderer.transform.localToWorldMatrix, renderer.sharedMaterials));
                    points.AddRange(mesh.vertices.Select(v => renderer.transform.TransformPoint(v)));
                    renderer.enabled = false;
                }
                var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
                var head = Bone("Head");
                // The eyes sit forward of the head bone; face shots look at them from the front.
                var center = face ? head + Vector3.up * .06f : upperBody ? (Bone("Neck") + Bone("L_Arm") + Bone("R_Arm")) / 3 : (min + max) * .5f;
                float distance = face ? .55f : upperBody ? 1.05f : Mathf.Max(max.y - min.y, 1.2f) * 1.9f;
                foreach (var (yaw, view) in views)
                {
                    var direction = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
                    var cam = preview.camera;
                    cam.transform.position = center + direction * distance;
                    cam.transform.LookAt(center);
                    cam.fieldOfView = 30; cam.nearClipPlane = .02f; cam.farClipPlane = 30;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .2f, .24f);
                    preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * .6f + Vector3.Cross(Vector3.up, direction) * .4f);
                    preview.lights[1].intensity = .5f;
                    preview.ambientColor = new Color(.35f, .35f, .35f);
                    preview.BeginStaticPreview(new Rect(0, 0, face ? 800 : 600, face ? 600 : 900));
                    // DrawMesh submissions last for a single render.
                    foreach (var (mesh, matrix, materials) in draws)
                        for (int i = 0; i < mesh.subMeshCount; i++)
                            preview.DrawMesh(mesh, matrix, materials[Mathf.Min(i, materials.Length - 1)], i);
                    preview.Render(true);
                    var image = preview.EndStaticPreview();
                    File.WriteAllBytes($"Validation/Katarina/{label}-{view}.png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { preview.Cleanup(); foreach (var mesh in baked) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void Pose(MuseumCharacter character, Dictionary<string, Transform> map, int move, float at)
        {
            var action = character.actions.First(a => a.moveId == move && a.motion);
            byte[] data = action.motion.bytes;
            int frames = BitConverter.ToInt32(data, 4), count = BitConverter.ToInt32(data, 8);
            if (count != character.boneNames.Length) throw new InvalidDataException("Action does not match rig: " + move);
            int frame = Mathf.Clamp(Mathf.RoundToInt(at * (frames - 1)), 0, frames - 1);
            int offset = 16 + frame * (3 + count * 4) * 4;
            var bones = character.boneNames.Select(n => map[n]).ToArray();
            bones[0].localPosition = new Vector3(0, 0, BitConverter.ToSingle(data, offset + 8));
            for (int c = 0; c < count; c++)
            {
                int p = offset + 12 + c * 16;
                bones[c].localRotation = new Quaternion(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8), BitConverter.ToSingle(data, p + 12));
            }
        }
    }
}
