using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Versus.Remaster.Editor
{
    // Poses Xiaoyu with original body clips in an isolated preview scene and renders
    // neck and thigh close-ups. The open scene and play state are not touched.
    [InitializeOnLoad]
    public static class XiaoyuRigCapture
    {
        static XiaoyuRigCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists("Temp/capture-xiaoyu-rig") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/capture-xiaoyu-rig");
            try { Run(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/capture-xiaoyu-rig-error.txt", e.ToString()); }
        }

        static readonly (int move, float at)[] Poses = { (-1, 0), (1428, 0), (2428, .5f), (2433, .5f) };

        [MenuItem("Remaster/Rendering/Capture Xiaoyu neck and thighs")]
        public static void Run()
        {
            Directory.CreateDirectory("Validation/Xiaoyu/Rig");
            var character = AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/Remaster/Museum/Xiaoyu.asset");
            var prefab = character.costumes[0].prefab;
            foreach (var (move, at) in Poses)
            {
                var preview = new PreviewRenderUtility();
                var baked = new List<Mesh>();
                try
                {
                    var root = preview.InstantiatePrefabInScene(prefab);
                    foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
                    var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
                    foreach (var bone in root.GetComponentsInChildren<Transform>()) map[bone.name] = bone;
                    if (move >= 0) Pose(character, map, move, at);
                    string label = move < 0 ? "bind" : move.ToString();
                    Vector3 Bone(string name) => map[name].position;
                    var right = Bone("L_UpLeg") - Bone("R_UpLeg"); right.y = 0; right.Normalize();
                    var forward = Vector3.Cross(Vector3.up, right);
                    var neck = (Bone("Neck") + Bone("Head")) * .5f;
                    var thighs = (Bone("L_UpLeg") + Bone("R_UpLeg") + Bone("L_Leg") + Bone("R_Leg")) * .25f;
                    var draws = new List<(Mesh mesh, Matrix4x4 matrix, Material[] materials)>();
                    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh = new Mesh(); renderer.BakeMesh(mesh, true); baked.Add(mesh);
                        draws.Add((mesh, renderer.transform.localToWorldMatrix, renderer.sharedMaterials));
                        renderer.enabled = false;
                    }
                    foreach (var (region, center, distance) in new[] { ("neck", neck, .38f), ("thighs", thighs, .95f) })
                        for (int view = 0; view < 4; view++)
                        {
                            var direction = Quaternion.AngleAxis(view * 90, Vector3.up) * forward;
                            var cam = preview.camera;
                            cam.transform.position = center + direction * distance;
                            cam.transform.LookAt(center);
                            cam.fieldOfView = 30; cam.nearClipPlane = .02f; cam.farClipPlane = 20;
                            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .2f, .24f);
                            preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * .6f + Vector3.Cross(Vector3.up, direction) * .4f);
                            preview.lights[1].intensity = .5f;
                            preview.ambientColor = new Color(.35f, .35f, .35f);
                            preview.BeginStaticPreview(new Rect(0, 0, 700, 700));
                            // DrawMesh submissions last for a single render.
                            foreach (var (mesh, matrix, materials) in draws)
                                for (int i = 0; i < mesh.subMeshCount; i++)
                                    preview.DrawMesh(mesh, matrix, materials[Mathf.Min(i, materials.Length - 1)], i);
                            preview.Render(true);
                            var image = preview.EndStaticPreview();
                            File.WriteAllBytes($"Validation/Xiaoyu/Rig/{label}-{region}-{view * 90}.png", image.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(image);
                        }
                }
                finally { preview.Cleanup(); foreach (var mesh in baked) UnityEngine.Object.DestroyImmediate(mesh); }
            }
            Debug.Log("Xiaoyu rig captures written to Validation/Xiaoyu/Rig");
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
            var root = new Vector3(0, 0, BitConverter.ToSingle(data, offset + 8));
            bones[0].localPosition = root;
            for (int c = 0; c < count; c++)
            {
                int p = offset + 12 + c * 16;
                bones[c].localRotation = new Quaternion(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8), BitConverter.ToSingle(data, p + 12));
            }
        }
    }
}
