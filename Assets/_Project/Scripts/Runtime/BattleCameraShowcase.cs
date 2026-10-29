using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Versus.Remaster
{
    public sealed class BattleCameraShowcase : MonoBehaviour
    {
        public MuseumCharacter[] characters;
        public string[] codes = { "jin", "kaz", "xia", "asa", "ltn" };
        public bool showInterface = true;
        public Camera View { get; private set; }
        public GameObject Model { get; private set; }
        public float Frame { get; private set; }
        public int Selected { get; private set; }
        public bool Playing = true;
        public bool Flipped;
        public int ShakePreset = -1;
        public bool GameplayView;
        public float HorizontalFov { get; private set; }
        public float GameplayBlend { get; private set; }
        public BattleCameraEffects.Clip Current => BattleCameraEffects.Data.clips[Selected];
        public string BodyStatus { get; private set; }
        FightCameraTrack track;
        StagingRenderSettings settings;
        EntranceCameraLens lens;
        Transform[] bones;
        byte[] motion;
        int motionFrames, motionStart;
        SecondaryMotionTrack hands;
        string failure;
        bool initialized;
        Vector2 scroll;

        void Start() { try { Initialize(); } catch (Exception e) { failure = e.Message; Debug.LogException(e); } }
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            settings = StagingRenderSettings.Create(transform, null);
            settings.transform.rotation = Quaternion.Euler(0, StagingRenderSettings.VersusYaw, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(transform, false); floor.name = "Camera reference floor";
            floor.transform.position = Vector3.down * .025f;
            floor.GetComponent<Renderer>().sharedMaterial = FighterView.Surface(settings.floor);
            for (int i = -4; i <= 4; i++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "Distance reference " + i; line.transform.SetParent(transform, false);
                line.transform.position = new Vector3(i, -.015f, 0);
                line.transform.localScale = new Vector3(.012f, .01f, 10);
                line.GetComponent<Renderer>().sharedMaterial = FighterView.Surface(new Color(.24f, .29f, .34f));
            }
            View = new GameObject("Original battle camera").AddComponent<Camera>();
            View.transform.SetParent(transform, false); View.tag = "MainCamera";
            View.clearFlags = CameraClearFlags.SolidColor; View.nearClipPlane = .05f; View.farClipPlane = 100;
            View.rect = new Rect(.27f, 0, .73f, 1); lens = new EntranceCameraLens(View);
            settings.Apply(View, null);
            Select(0);
        }

        public void Select(int index)
        {
            Selected = (index + BattleCameraEffects.Data.clips.Length) % BattleCameraEffects.Data.clips.Length;
            track = new FightCameraTrack(Current.resource);
            int fighter = Array.IndexOf(codes, Current.code);
            if (fighter < 0 || fighter >= characters.Length || !characters[fighter]) throw new InvalidOperationException("Missing museum character " + Current.code);
            var character = characters[fighter];
            if (Model) { Model.SetActive(false); if (Application.isPlaying) Destroy(Model); else DestroyImmediate(Model); }
            Model = Instantiate(character.prefab, transform);
            foreach (var behavior in Model.GetComponentsInChildren<MonoBehaviour>()) behavior.enabled = false;
            var originals = character.prefab.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var renderer in Model.GetComponentsInChildren<Renderer>(true))
                if (originals.TryGetValue(renderer.name, out var original)) renderer.sharedMaterials = original.sharedMaterials;
            var wardrobe = FightWardrobe.ForCharacter(character);
            if (wardrobe) CostumeColors.Apply(Model, wardrobe, new Dictionary<string, Color?[]>());
            var map = Model.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            bones = character.boneNames.Select(n => map[n]).ToArray();
            var action = character.actions.FirstOrDefault(a => a.moveId == Current.moveId && a.motion);
            BodyStatus = action != null ? "Original action " + Current.moveId : "Original stance; action body unavailable";
            motionStart = action != null ? Math.Max(0, (Current.frame & 0x3fff) - 1) : 0;
            if (action == null) action = character.actions.First(a => a.moveId == character.stanceMoveId && a.motion);
            motion = action.motion.bytes;
            motionFrames = BitConverter.ToInt32(motion, 4);
            if (motion.Length < 16 || System.Text.Encoding.ASCII.GetString(motion, 0, 4) != "JRT1" ||
                BitConverter.ToInt32(motion, 8) != bones.Length || motionFrames < 1 ||
                motion.Length != 16L + motionFrames * (3L + bones.Length * 4L) * 4)
                throw new InvalidDataException("Invalid showcase body motion");
            hands = null;
            var handAsset = Resources.Load<TextAsset>("SecondaryMotion/" + Current.code + "_combat_hands");
            if (handAsset) hands = new SecondaryMotionTrack(Model.transform, Current.code + "_combat_hands", BitConverter.ToInt32(handAsset.bytes, 4));
            Frame = 0; ShakePreset = -1; GameplayView = false; Present();
        }

        public void Seek(float frame) { Frame = Mathf.Clamp(frame, 0, track.Frames - 1); Present(); }
        Quaternion Rotation(int offset) => new Quaternion(BitConverter.ToSingle(motion, offset), BitConverter.ToSingle(motion, offset + 4),
            BitConverter.ToSingle(motion, offset + 8), BitConverter.ToSingle(motion, offset + 12));

        public void Present()
        {
            if (track == null || !Model) return;
            Vector3 forward = Flipped ? Vector3.left : Vector3.right;
            Model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(forward));
            float bodyFrame = Mathf.Clamp(Frame + motionStart, 0, motionFrames - 1);
            int a = (int)bodyFrame, b = Mathf.Min(a + 1, motionFrames - 1);
            float t = bodyFrame - a; int stride = (3 + bones.Length * 4) * 4;
            int offset = 16 + a * stride, next = 16 + b * stride;
            bones[0].localPosition = new Vector3(0, 0, Mathf.Lerp(BitConverter.ToSingle(motion, offset + 8), BitConverter.ToSingle(motion, next + 8), t));
            for (int i = 0; i < bones.Length; i++) bones[i].localRotation = Quaternion.Slerp(Rotation(offset + 12 + i * 16), Rotation(next + 12 + i * 16), t);
            hands?.Apply(0);
            track.Sample(Frame, Vector3.zero, forward, out var position, out var target, out float fov, out float blend);
            var gamePosition = new Vector3(0, 1.35f, -4.4f); var gameTarget = new Vector3(0, 1.05f, 0);
            var rotation = Quaternion.LookRotation(target - position);
            if (GameplayView) blend = 1;
            position = Vector3.Lerp(position, gamePosition, blend);
            rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(gameTarget - gamePosition), blend);
            HorizontalFov = Mathf.Lerp(fov, GameplayCameraFraming.HorizontalFov, blend); GameplayBlend = blend;
            if (ShakePreset >= 0)
            {
                // The original preset table (pitch only), restarted every 1.2 s so it can be watched repeatedly.
                int shakeFrame = (int)Mathf.Repeat(Frame, 72);
                rotation = BattleCameraEffects.Pitched(rotation, BattleCameraEffects.OriginalShakePitch(ShakePreset, shakeFrame));
            }
            lens.SetHorizontalFov(HorizontalFov);
            View.transform.SetPositionAndRotation(position, rotation);
        }

        void Update()
        {
            if (failure != null || track == null) return;
            if (Input.GetKeyDown(KeyCode.Space)) Playing = !Playing;
            if (Input.GetKeyDown(KeyCode.R)) Seek(0);
            if (Input.GetKeyDown(KeyCode.F)) Flipped = !Flipped;
            if (Input.GetKeyDown(KeyCode.RightArrow)) Select(Selected + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Select(Selected - 1);
            if (Playing) Frame = Mathf.Repeat(Frame + Time.unscaledDeltaTime * 60, track.Frames);
            Present();
        }

        void OnGUI()
        {
            if (!showInterface) return;
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            GUI.Box(new Rect(0, 0, 345, 720), "");
            GUILayout.BeginArea(new Rect(16, 14, 312, 690));
            GUILayout.Label("VERSUS / CAMERA LAB", new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold });
            if (failure != null) GUILayout.Label(failure);
            else if (track != null)
            {
                GUILayout.Label("Original tracks / " + BattleCameraEffects.Data.clips.Select(c => c.code).Distinct().Count() + " fighters");
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(228));
                for (int i = 0; i < BattleCameraEffects.Data.clips.Length; i++)
                {
                    var clip = BattleCameraEffects.Data.clips[i];
                    if (GUILayout.Button((i == Selected ? "> " : "") + clip.code.ToUpperInvariant() + "  " + clip.moveName + " / " + clip.track)) Select(i);
                }
                GUILayout.EndScrollView();
                GUILayout.Label("Frame " + Frame.ToString("F1") + " / " + (track.Frames - 1));
                float seek = GUILayout.HorizontalSlider(Frame, 0, track.Frames - 1);
                if (Mathf.Abs(seek - Frame) > .01f) { Playing = false; Seek(seek); }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Playing ? "Pause" : "Play")) Playing = !Playing;
                if (GUILayout.Button("Restart")) Seek(0);
                if (GUILayout.Button("Flip")) { Flipped = !Flipped; Present(); }
                GUILayout.EndHorizontal();
                GameplayView = GUILayout.Toggle(GameplayView, "Wide reference view");
                GUILayout.Label("Horizontal FOV " + HorizontalFov.ToString("F1") + " degrees\nReturn-to-gameplay blend " + GameplayBlend.ToString("F2"));
                GUILayout.Space(8); GUILayout.Label("SHAKE / original presets");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Off")) ShakePreset = -1;
                for (int p = 3; p <= 6; p++) if (GUILayout.Button("" + p)) { ShakePreset = p; GameplayView = true; Seek(0); }
                GUILayout.EndHorizontal();
                var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
                GUILayout.Label("Original preset tables (pitch only, one value per 60 Hz frame) from the executable's shake evaluator. Moves use presets 3-6; the 1.2 s repeat is a demo setting.", wrap);
                GUILayout.Space(8);
                GUILayout.Label(BodyStatus + "\nCamera path/lens samples: original. Unity basis, body pairing and blend interpretation require original-game visual comparison.", wrap);
                GUILayout.Label("Space: pause  R: restart  F: flip\nLeft/right: track  Slider: scrub", wrap);
            }
            GUILayout.EndArea(); GUI.matrix = matrix;
        }
    }
}
