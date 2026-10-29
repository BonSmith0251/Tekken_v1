using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster
{
    // env_scene: browse the imported original stages with two fighters standing in them, tick the
    // ones versus may use, and save the choice to EnvironmentSelection.asset.
    public sealed class EnvironmentScene : MonoBehaviour
    {
        [Tooltip("Fighters shown on the stage: player one's side, then player two's.")]
        public MuseumCharacter[] fighters = new MuseumCharacter[0];
        public Camera Camera { get; private set; }
        public string Previewed { get; private set; } = EnvironmentSelection.Arena;
        public GameObject[] FighterModels => stances.Select(s => s.model).ToArray();

        sealed class Stance
        {
            public GameObject model; public Transform[] bones; public Vector3[] positions; public Quaternion[,] rotations;
            public void Pose(float frame)
            {
                int frames = positions.Length; frame = Mathf.Repeat(frame, frames - 1);
                int a = (int)frame, b = Mathf.Min(a + 1, frames - 1); float t = frame - a;
                var root = Vector3.Lerp(positions[a], positions[b], t); root.x = 0; root.y = 0;
                bones[0].localPosition = root;
                for (int i = 0; i < bones.Length; i++) bones[i].localRotation = Quaternion.Slerp(rotations[a, i], rotations[b, i], t);
            }
        }

        readonly List<Stance> stances = new List<Stance>();
        EnvironmentSelection selection;
        HashSet<string> chosen;
        string[] names;
        GameObject stage, arena;
        StagingRenderSettings renderSettings;
        float frame, yaw, pitch = 8, distance = 5.2f;
        Vector2 scroll;
        string status = "";
        bool dirty;

        void Start()
        {
            try { Build(StagingRenderSettings.FindOrCreate(transform)); }
            catch (Exception e) { status = e.Message; Debug.LogException(e); }
        }

        // Also called by the editor capture, inside a preview scene, with that scene's render settings.
        public void Build(StagingRenderSettings settings)
        {
            renderSettings = settings;
            selection = EnvironmentSelection.Load();
            chosen = new HashSet<string>(selection.environments ?? new string[0]);
            names = new[] { EnvironmentSelection.Arena }.Concat(StageEnvironment.Names).ToArray();
            Camera = new GameObject("Environment camera").AddComponent<Camera>(); Camera.transform.SetParent(transform, false);
            Camera.tag = "MainCamera"; Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.fieldOfView = 35; Camera.nearClipPlane = .05f; Camera.farClipPlane = 2000;
            if (renderSettings) renderSettings.Apply(Camera, null);
            arena = BuildArena();
            var random = new System.Random();
            for (int i = 0; i < fighters.Length; i++) if (fighters[i]) stances.Add(Spawn(fighters[i], i, random));
            Preview(chosen.Count > 0 ? names.First(chosen.Contains) : EnvironmentSelection.Arena);
            Pose();
        }

        // A plain floor standing in for the versus scene's generated arena.
        GameObject BuildArena()
        {
            var root = new GameObject("Arena"); root.transform.SetParent(transform, false);
            FighterView.Piece("Arena platform", PrimitiveType.Cube, root.transform, new Vector3(0, -.3f, 0), new Vector3(15, .6f, 9),
                FighterView.Surface(renderSettings ? renderSettings.floor : new Color(.09f, .12f, .17f), .55f));
            return root;
        }

        Stance Spawn(MuseumCharacter character, int side, System.Random random)
        {
            var prefab = character.costumes != null && character.costumes.Length > 0 ? character.costumes[0].prefab : character.prefab;
            var model = Instantiate(prefab, transform);
            model.name = character.displayName;
            // Versus places player one at -X facing +X; the camera looks along +Z.
            model.transform.localPosition = new Vector3(side == 0 ? -1.1f : 1.1f, 0, 0);
            model.transform.localRotation = Quaternion.LookRotation(side == 0 ? Vector3.right : Vector3.left);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            FightWardrobe.DressSaved(model, character, random);
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in model.GetComponentsInChildren<Transform>()) map[bone.name] = bone;
            var stance = new Stance { model = model, bones = character.boneNames.Select(n => map.TryGetValue(n, out var b) ? b : null).ToArray() };
            if (stance.bones.Any(b => !b)) throw new InvalidOperationException(character.displayName + " is missing a museum bone.");
            var action = character.actions.FirstOrDefault(a => a.moveId == character.stanceMoveId) ?? character.actions[0];
            byte[] data = action.motion.bytes;
            if (data.Length < 16 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "JRT1") throw new InvalidDataException("Invalid stance motion for " + character.displayName);
            int frames = BitConverter.ToInt32(data, 4), count = BitConverter.ToInt32(data, 8);
            if (count != stance.bones.Length) throw new InvalidDataException(character.displayName + " stance does not match the rig");
            stance.positions = new Vector3[frames]; stance.rotations = new Quaternion[frames, count];
            for (int f = 0; f < frames; f++)
            {
                int offset = 16 + f * (3 + count * 4) * 4;
                stance.positions[f] = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));
                for (int c = 0; c < count; c++)
                {
                    int p = offset + 12 + c * 16;
                    stance.rotations[f, c] = new Quaternion(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8), BitConverter.ToSingle(data, p + 12));
                }
            }
            return stance;
        }

        public void Preview(string name)
        {
            if (stage) { StageEnvironment.Despawn(stage); stage.SetActive(false); DestroyImmediate(stage); }
            Previewed = name;
            arena.SetActive(name == EnvironmentSelection.Arena);
            if (name != EnvironmentSelection.Arena) stage = StageEnvironment.Spawn(name, transform);
            if (Application.isPlaying) Resources.UnloadUnusedAssets();
        }

        public void SetView(float yaw, float pitch, float distance) { this.yaw = yaw; this.pitch = pitch; this.distance = distance; PlaceCamera(); }

        public void Pose(float at = 0){ frame = at; foreach (var s in stances) s.Pose(frame); PlaceCamera(); }

        void PlaceCamera()
        {
            var target = new Vector3(0, 1, 0);
            Camera.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * distance;
            Camera.transform.LookAt(target);
        }

        public void Save()
        {
            selection.environments = names.Where(chosen.Contains).ToArray();
            selection.Save(); dirty = false;
            status = selection.environments.Length == 0 ? "Saved: versus uses the built-in arena" :
                "Saved " + selection.environments.Length + " environment" + (selection.environments.Length == 1 ? "" : "s") + " for versus";
        }

        void Update()
        {
            if (stances.Count == 0) return;
            frame += Time.deltaTime * 60;
            foreach (var s in stances) s.Pose(frame);
            if (Input.mousePosition.x > 330)
            {
                if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X") * 4; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3, -10, 70); }
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * .4f, 1.5f, 30);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                int i = Array.IndexOf(names, Previewed) + (Input.GetKeyDown(KeyCode.DownArrow) ? 1 : -1);
                Preview(names[(i + names.Length) % names.Length]);
            }
            if (Input.GetKeyDown(KeyCode.Space)) Toggle(Previewed);
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.S)) Save();
            PlaceCamera();
            if (renderSettings) renderSettings.Apply(Camera, null);
        }

        void Toggle(string name) { if (!chosen.Remove(name)) chosen.Add(name); dirty = true; status = ""; }

        static string Label(string name) => name == EnvironmentSelection.Arena ? "Remaster arena (built-in)" : name;

        void OnGUI()
        {
            if (names == null) { GUI.Label(new Rect(20, 20, 600, 30), status); return; }
            GUI.Box(new Rect(10, 10, 310, Screen.height - 20), "");
            GUILayout.BeginArea(new Rect(20, 18, 290, Screen.height - 36));
            GUILayout.Label("<b>ENVIRONMENTS</b>  " + chosen.Count + " selected for versus", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All")) { chosen = new HashSet<string>(names); dirty = true; }
            if (GUILayout.Button("None")) { chosen.Clear(); dirty = true; }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var name in names)
            {
                GUILayout.BeginHorizontal();
                bool on = GUILayout.Toggle(chosen.Contains(name), "", GUILayout.Width(20));
                if (on != chosen.Contains(name)) Toggle(name);
                var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = name == Previewed ? FontStyle.Bold : FontStyle.Normal };
                if (GUILayout.Button((name == Previewed ? "▶ " : "") + Label(name), style)) Preview(name);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(dirty ? "Save selection *" : "Save selection", GUILayout.Height(34))) Save();
            GUILayout.Label(status);
            GUILayout.EndArea();
            GUI.Label(new Rect(340, Screen.height - 54, 900, 22), "Previewing " + Label(Previewed) + (Previewed == EnvironmentSelection.Arena ? "" : "  •  original textures; shader effects and baked lighting are not converted"));
            GUI.Label(new Rect(340, Screen.height - 30, 900, 22), "Click a name to preview • tick to use in versus • ↑/↓ browse • Space tick • Ctrl+S save • right-drag orbit • scroll zoom");
        }
    }
}
