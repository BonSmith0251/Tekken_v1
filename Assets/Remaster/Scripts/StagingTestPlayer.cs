using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Versus.Remaster
{
    // Plays the installed staging catalog in source order, one actor and take at a time.
    public sealed class StagingTestPlayer : MonoBehaviour
    {
        public MuseumCharacter[] characters;
        public bool repeat = true;
        public float gapSeconds = .75f;

        sealed class Take
        {
            public MuseumCharacter character;
            public StagingAction action;
            public MuseumCharacter.Action body;
        }

        readonly List<Take> playlist = new List<Take>();
        // One random saved outfit per session, kept across takes.
        readonly int outfitSeed = Environment.TickCount;
        GameObject actor;
        Camera sceneCamera;
        EntranceCameraLens lens;
        Transform[] bones, landmarks;
        Vector3[] positions;
        Quaternion[,] rotations;
        Transform head;
        Vector3 faceAxis;
        SecondaryMotionTrack secondary;
        XiaoyuSecondaryDynamics flexibleMotion;
        EntranceCameraTrack cameraTrack;
        Take current;
        int index, frames;
        float frame, gap;
        bool started, paused;
        string error;

        void Start()
        {
            try
            {
                CreateSet();
                BuildPlaylist();
                if (playlist.Count == 0) throw new InvalidOperationException("No staging takes have matching character, body, camera and secondary assets.");
                Begin(0);
            }
            catch (Exception exception) { Fail(exception); }
        }

        void CreateSet()
        {
            RenderSettings.ambientLight = new Color(.46f, .48f, .54f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Staging test floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = Vector3.one * 2;
            floor.GetComponent<Renderer>().material = FighterView.Surface(new Color(.12f, .16f, .21f));
            foreach (int side in new[] { -1, 1 })
            {
                var light = new GameObject(side < 0 ? "Key light" : "Fill light").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Directional;
                light.color = side < 0 ? Color.white : new Color(.65f, .75f, 1f);
                light.intensity = side < 0 ? 1.35f : .65f;
                light.transform.rotation = Quaternion.Euler(42, side * 45, 0);
                // Without shadows both lights reach the eyeballs and mouth interior through the
                // lids and lips. Contact shadows resolve the millimeter gaps the shadow map cannot.
                light.shadows = side < 0 ? LightShadows.Soft : LightShadows.None;
                var data = light.gameObject.AddComponent<HDAdditionalLightData>();
                data.SetIntensity(light.intensity * Mathf.PI, LightUnit.Lux);
                data.useContactShadow.useOverride = true;
                data.useContactShadow.@override = true;
            }
            // Close-up takes need short shadow ranges; keep this local so other scenes are unchanged.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.value = true; contact.length.value = .04f; contact.thicknessScale.value = .02f;
            contact.maxDistance.value = 20; contact.fadeDistance.value = 2;
            profile.Add<HDShadowSettings>(true).maxShadowDistance.value = 12;
            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10; volume.sharedProfile = profile;
            sceneCamera = new GameObject("Staging action camera").AddComponent<Camera>();
            sceneCamera.transform.SetParent(transform, false);
            sceneCamera.tag = "MainCamera";
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(.025f, .035f, .055f);
            sceneCamera.nearClipPlane = .05f;
            sceneCamera.farClipPlane = 100;
            sceneCamera.gameObject.AddComponent<AudioListener>();
            lens = new EntranceCameraLens(sceneCamera);
            lens.SetHorizontalFov(65);
        }

        void BuildPlaylist()
        {
            foreach (var entry in StagingCatalog.Load().entries.Where(x => x.kind == "entrance" || x.kind == "victory")
                .OrderBy(x => x.character).ThenBy(x => x.kind == "entrance" ? 0 : 1).ThenBy(x => x.moveId))
            {
                var character = Array.Find(characters, c => c && Code(c) == entry.character);
                if (!character) { Debug.LogWarning("Staging test missing Unity character: " + entry.character + " " + entry.moveId); continue; }
                var body = Array.Find(character.actions, a => a.moveId == entry.moveId && a.motion);
                if (body == null || string.IsNullOrEmpty(entry.secondary) ||
                    !Resources.Load<TextAsset>("SecondaryMotion/" + entry.secondary) ||
                    (!string.IsNullOrEmpty(entry.camera) && !Resources.Load<TextAsset>("EntranceCameras/" + entry.camera)))
                {
                    Debug.LogWarning("Staging test missing converted action resource: " + entry.character + " " + entry.moveId);
                    continue;
                }
                playlist.Add(new Take { character = character, action = entry, body = body });
            }
        }

        static string Code(MuseumCharacter character)
        {
            if (!string.IsNullOrEmpty(character.characterCode)) return character.characterCode;
            if (character.displayName.StartsWith("Jin", StringComparison.OrdinalIgnoreCase)) return "jin";
            if (character.displayName.StartsWith("Kazuya", StringComparison.OrdinalIgnoreCase)) return "kaz";
            if (character.displayName.StartsWith("Xiaoyu", StringComparison.OrdinalIgnoreCase)) return "xia";
            return character.name.ToLowerInvariant();
        }

        void Begin(int next)
        {
            if (secondary != null) secondary.Restore();
            secondary = null;
            flexibleMotion = null;
            if (actor) { actor.SetActive(false); Destroy(actor); }
            index = next;
            current = playlist[index];
            var prefab = current.character.prefab ? current.character.prefab : current.character.costumes[0].prefab;
            if (!prefab) throw new InvalidOperationException("Missing prefab for " + current.character.displayName);
            actor = Instantiate(prefab, transform);
            // Characters with a wardrobe wear their saved items, not the preset costume.
            var outfit = FightWardrobe.DressSaved(actor, current.character, new System.Random(outfitSeed));
            if (outfit != null) Debug.Log(current.character.displayName + " staging outfit: " + string.Join(", ", outfit));
            actor.name = current.character.displayName + " - " + current.action.animation;
            actor.transform.localPosition = Vector3.zero;
            actor.transform.localRotation = Quaternion.identity;
            foreach (var behaviour in actor.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in actor.GetComponentsInChildren<Transform>()) map[bone.name] = bone;
            bones = new Transform[current.character.boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!map.TryGetValue(current.character.boneNames[i], out bones[i]))
                    throw new InvalidOperationException("Missing bone " + current.character.boneNames[i] + " on " + current.character.displayName);
            }
            if (!map.TryGetValue("Head", out head)) throw new InvalidOperationException("Missing head on " + current.character.displayName);
            faceAxis = head.InverseTransformDirection(actor.transform.forward);
            landmarks = new[] { "L_Hand", "R_Hand", "L_Foot", "R_Foot" }.Where(map.ContainsKey).Select(name => map[name]).ToArray();
            LoadBody(current.body.motion, current.action);
            secondary = new SecondaryMotionTrack(actor.transform, current.action.secondary, frames);
            cameraTrack = string.IsNullOrEmpty(current.action.camera) ? null :
                new EntranceCameraTrack(current.action.camera, current.action.moveId, frames);
            frame = 0; gap = 0; started = false;
            // Xiaoyu's hair and garments use the museum's adapted springs and cloth.
            if (Code(current.character) == "xia") flexibleMotion = new XiaoyuSecondaryDynamics(actor.transform);
            Present();
            flexibleMotion?.Reset();
            Debug.Log("Staging test playing " + current.character.displayName + " " + current.action.kind + " " + current.action.animation);
        }

        void LoadBody(TextAsset asset, StagingAction entry)
        {
            byte[] data = asset.bytes;
            if (data.Length < 16 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "JRT1")
                throw new InvalidOperationException("Invalid body " + entry.character + " " + entry.moveId);
            frames = BitConverter.ToInt32(data, 4);
            int count = BitConverter.ToInt32(data, 8);
            if (frames != entry.frames || count != bones.Length || BitConverter.ToInt32(data, 12) != 60 ||
                data.Length != 16L + frames * (3L + count * 4L) * 4)
                throw new InvalidOperationException("Body/rig mismatch " + entry.character + " " + entry.moveId);
            positions = new Vector3[frames]; rotations = new Quaternion[frames, count];
            for (int f = 0; f < frames; f++)
            {
                int p = 16 + f * (3 + count * 4) * 4;
                positions[f] = new Vector3(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));
                for (int b = 0; b < count; b++)
                {
                    int q = p + 12 + b * 16;
                    rotations[f, b] = new Quaternion(BitConverter.ToSingle(data, q), BitConverter.ToSingle(data, q + 4),
                        BitConverter.ToSingle(data, q + 8), BitConverter.ToSingle(data, q + 12));
                }
            }
        }

        int SourceFrame(int presentation) => current.action.landingStitch ? KazuyaEntranceTimeline.SourceFrame(presentation) : presentation;
        int Duration => current.action.landingStitch ? frames - KazuyaEntranceTimeline.RemovedFrames : frames;

        void Update()
        {
            if (error != null || current == null) return;
            if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
            if (Input.GetKeyDown(KeyCode.RightArrow)) { Next(); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { Begin((index + playlist.Count - 1) % playlist.Count); return; }
            if (paused) return;
            if (!started) { started = true; return; }
            if (frame >= Duration - 1)
            {
                gap += Time.unscaledDeltaTime;
                if (gap >= gapSeconds) Next();
                return;
            }
            frame = Mathf.Min(Duration - 1, frame + Mathf.Clamp(Time.unscaledDeltaTime, 0, 1f / 15f) * 60);
        }

        void Next()
        {
            int next = index + 1;
            if (next == playlist.Count && !repeat) { paused = true; return; }
            try { Begin(next % playlist.Count); }
            catch (Exception exception) { Fail(exception); }
        }

        void LateUpdate()
        {
            if (error != null || current == null) return;
            try { Present(); if (!paused) flexibleMotion?.Tick(Time.unscaledDeltaTime); }
            catch (Exception exception) { Fail(exception); }
        }

        void Present()
        {
            int a = Mathf.Clamp(SourceFrame((int)frame), 0, frames - 1);
            int b = Mathf.Clamp(SourceFrame(Mathf.Min((int)frame + 1, Duration - 1)), 0, frames - 1);
            float t = frame - (int)frame;
            bones[0].localPosition = Vector3.Lerp(positions[a], positions[b], t);
            int stitchFrom = -1;
            float stitchBlend = 1;
            if (current.action.landingStitch)
            {
                int presentation = (int)frame;
                for (int cut = Mathf.Max(1, presentation - 5); cut <= presentation + 1; cut++)
                    if (SourceFrame(cut) > SourceFrame(cut - 1) + 1)
                    {
                        stitchFrom = Mathf.Clamp(SourceFrame(cut - 1), 0, frames - 1);
                        stitchBlend = Mathf.SmoothStep(0, 1, (frame - (cut - 1)) / 6f);
                    }
            }
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion pose = Quaternion.Slerp(rotations[a, i], rotations[b, i], t);
                bones[i].localRotation = stitchFrom >= 0
                    ? Quaternion.Slerp(rotations[stitchFrom, i], pose, stitchBlend) : pose;
            }
            secondary.Apply(frame, current.action.landingStitch ? (Func<int, int>)KazuyaEntranceTimeline.SourceFrame : null);
            var bounds = new Bounds(head.position + Vector3.up * .1f, Vector3.one * .44f);
            foreach (var bone in landmarks) bounds.Encapsulate(new Bounds(bone.position, Vector3.one * .2f));
            Vector3 front = head.TransformDirection(faceAxis);
            if (cameraTrack != null)
                cameraTrack.Apply(sceneCamera, actor.transform, frame, lens, bounds, front,
                    current.action.landingStitch ? (Func<int, int>)KazuyaEntranceTimeline.SourceFrame : null, head.position);
            else
            {
                // The paired victory record has no standalone source camera.
                lens.SetHorizontalFov(65);
                front = Vector3.ProjectOnPlane(front, Vector3.up).normalized;
                if (front.sqrMagnitude < .01f) front = actor.transform.forward;
                sceneCamera.transform.position = bounds.center + front * Mathf.Max(3.5f, bounds.extents.magnitude * 3);
                sceneCamera.transform.LookAt(bounds.center);
            }
        }

        void Fail(Exception exception)
        {
            error = exception.Message;
            Debug.LogException(exception);
        }

        void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 520, 92), "Staging test");
            if (error != null) { GUI.Label(new Rect(24, 42, 495, 55), error); return; }
            if (current == null) return;
            GUI.Label(new Rect(24, 39, 495, 24), (index + 1) + "/" + playlist.Count + "  " + current.character.displayName +
                "  " + current.action.kind + "  " + current.action.animation + "  " + ((int)frame + 1) + "/" + Duration);
            GUI.Label(new Rect(24, 65, 495, 24), (paused ? "Paused · " : "Playing · ") +
                (cameraTrack == null ? "camera fallback" : "native camera") + " · Space pause · arrows change take");
        }
    }
}
