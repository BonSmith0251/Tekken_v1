using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>Every fighter's own battle effects (CharacterEffects/<code>): original actions replayed through
    /// the native 0x802E dispatcher, plus a manual browser of all extracted systems. Left/right switch fighter.
    /// Manual placement and the preview clock are not battle dispatch data.</summary>
    public sealed class CharacterEffectsShowcase : MonoBehaviour
    {
        [Serializable] sealed class Library { public CascadeRoleData[] roles; public CascadeSystemData[] systems; public string[] limitations; }
        [Serializable] sealed class Binding { public int effectId; public string role; }
        [Serializable] sealed class Registry { public Binding[] bindings; }
        /// <summary>A fighter in the scene's list: museum model and motions, effect code (older museum assets
        /// store none) and the original actions with recovered effect rows, shown as buttons.</summary>
        [Serializable] public sealed class Fighter { public MuseumCharacter character; public string code; public int[] actions; }
        public Fighter[] fighters = Array.Empty<Fighter>();
        public int FighterIndex { get; private set; }
        int[] Featured => fighters.Length > 0 && fighters[FighterIndex].actions != null ? fighters[FighterIndex].actions : new[] { 1840, 1779 };
        string DisplayName => character ? character.displayName : Code;
        public MuseumCharacter character;
        public bool showInterface = true;
        public const string ResourceFolder = "CharacterEffects/jin";
        public const string DefaultRole = "PS_EF_CHR_JIN_NOMAL";
        // Older museum assets (Jin, Kazuya) store no character code; set it here for those.
        public string characterCode;
        public string Code => !string.IsNullOrEmpty(characterCode) ? characterCode :
            character && !string.IsNullOrEmpty(character.characterCode) ? character.characterCode : "jin";
        string Folder => "CharacterEffects/" + Code;
        string defaultRole = DefaultRole;
        public string[] Roles { get; private set; } = Array.Empty<string>();
        public string CurrentRole => Roles.Length > 0 ? Roles[selected] : "";
        public float Clock { get; private set; }
        public bool Playing { get; private set; } = true;
        public GameObject Model => model;
        public CascadeInstance Instance => actionMove >= 0 ? actionEffects?.LastInstance : instance;
        public bool FacingRight => facingRight;
        public int ActionMove => actionMove;
        public int SourceFrame => Mathf.Clamp(Mathf.FloorToInt(Clock * 60 + .0001f) + 1, 1, stanceFrames);
        public int TriggeredEvents { get; private set; }
        public string PreviewLimitations => warnings.TryGetValue(CurrentRole, out var warning) ? warning : "";
        const float PreviewDuration = 4;
        CascadeEffects effects;
        CharacterBattleEffects actionEffects;
        CommandGraph graph;
        CascadeInstance instance;
        GameObject model;
        Camera viewCamera;
        Transform[] bones;
        Transform[] effectBones;
        SecondaryMotionTrack combatHands;
        int combatHandFrames;
        byte[] stance;
        int stanceFrames, selected;
        int actionMove = -1, dispatchedFrame;
        bool initialized, facingRight, travel;
        string failure = "";
        Vector2 scroll;
        float zoom = 5.3f;
        Vector3 modelOrigin = new Vector3(-.55f, 0, 0);
        readonly Dictionary<string, string> warnings = new Dictionary<string, string>();
        readonly Dictionary<string, float> repeatIntervals = new Dictionary<string, float>();

        void Start()
        {
            try { Initialize(true); }
            catch (Exception error) { failure = error.Message; Debug.LogException(error); }
        }

        public void Initialize(bool buildStage)
        {
            if (initialized) return;
            if (fighters.Length > 0) { character = fighters[FighterIndex].character; characterCode = fighters[FighterIndex].code; }
            var asset = Resources.Load<TextAsset>(Folder + "/effects");
            if (!asset) throw new FileNotFoundException(Code + "'s exported effect library is missing.");
            var library = JsonUtility.FromJson<Library>(asset.text);
            Roles = library.roles.Select(r => r.role).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray();
            if (Roles.Length == 0) throw new InvalidDataException(Code + "'s effect library is empty.");
            // The manager's effect ID 0 record (Jin's NOMAL, Katarina's nomal) is what native dispatch plays.
            var registry = Resources.Load<TextAsset>(Folder + "/registry");
            var zero = registry ? JsonUtility.FromJson<Registry>(registry.text).bindings?.FirstOrDefault(b => b.effectId == 0) : null;
            if (zero != null) defaultRole = zero.role;
            selected = Array.IndexOf(Roles, defaultRole);
            if (selected < 0) selected = 0;
            var prefab = character ? character.prefab : Resources.Load<GameObject>("JinPlayable");
            if (!prefab || !character) throw new InvalidOperationException("The showcase needs the fighter's museum model and stance catalog.");
            model = Instantiate(prefab, transform);
            model.name = (character ? character.displayName : "Jin") + " — original stance";
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            // JinRig.Awake has already cloned skin.materials in Play mode. Its '(Instance)'
            // names no longer match the original palette records. This viewer owns animation
            // and does not use JinRig's flash materials, so restore the exact prefab references
            // before applying source default tints. Edit captures and Play now use the same maps.
            var sourceSkins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var modelSkins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (sourceSkins.Length != modelSkins.Length) throw new InvalidDataException(DisplayName + " prefab renderer layout changed during instantiation.");
            for (int i = 0; i < modelSkins.Length; i++)
            {
                if (modelSkins[i].name != sourceSkins[i].name || modelSkins[i].sharedMesh != sourceSkins[i].sharedMesh)
                    throw new InvalidDataException(DisplayName + " prefab renderer order changed during instantiation.");
                modelSkins[i].sharedMaterials = sourceSkins[i].sharedMaterials;
            }
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in model.GetComponentsInChildren<Transform>()) map[bone.name] = bone;
            effectBones = model.GetComponentsInChildren<Transform>();
            foreach (var role in library.roles)
            {
                var system = library.systems.First(s => s.name == role.system);
                var gaps = new List<string>();
                if (system.emitters.Any(e => e.subImageInterpolation != null && e.subImageInterpolation.IndexOf("Random", StringComparison.OrdinalIgnoreCase) >= 0))
                    gaps.Add("random atlas emitter omitted");
                if (system.emitters.Any(e => e.boneNames != null && e.boneNames.Any(name => !map.ContainsKey(name))))
                    gaps.Add("missing original socket emitter omitted");
                if (system.emitters.Any(e => e.distortion)) gaps.Add("refraction omitted");
                warnings[role.role] = gaps.Count > 0 ? "Partial preview: " + string.Join("; ", gaps) : "Converted preview; shader and intensity adaptations apply";
                float duration = system.emitters.Length == 0 ? PreviewDuration : system.emitters.Max(e => e.loops == 0 ? PreviewDuration :
                    e.delay + Mathf.Max(0, e.duration) * Mathf.Max(1, e.loops) + (e.life != null && e.life.Length > 0 ? e.life.Max() : 0));
                repeatIntervals[role.role] = Mathf.Clamp(duration + .2f, .8f, PreviewDuration);
            }
            bones = character.boneNames.Select(name => map.TryGetValue(name, out var bone) ? bone :
                throw new InvalidDataException("Missing " + DisplayName + " bone " + name)).ToArray();
            // The source default palette is required for the imported untinted hair maps.
            // Keep the original default outfit; leave the user's saved wardrobe choices untouched.
            var wardrobe = FightWardrobe.ForCharacter(character);
            if (wardrobe) CostumeColors.Apply(model, wardrobe, new Dictionary<string, Color?[]>());
            LoadMotion(character.stanceMoveId);
            var handAsset = Resources.Load<TextAsset>("SecondaryMotion/" + Code + "_combat_hands");
            if (handAsset)
            {
                combatHandFrames = BitConverter.ToInt32(handAsset.bytes, 4);
                combatHands = new SecondaryMotionTrack(model.transform, Code + "_combat_hands", combatHandFrames);
            }
            effects = CascadeEffects.Create(transform, Folder, Code + " original effect library");
            graph = CommandGraph.Load(Code);
            actionEffects = CharacterBattleEffects.Create(transform, Code, model.transform);
            if (actionEffects) actionEffects.SetManualSimulation(true);
            if (buildStage) BuildStage();
            initialized = true;
            if (buildStage && actionEffects && Featured.Length > 0) SelectAction(Featured[0]); else Restart();
        }

        /// <summary>Replaces the shown fighter (model, effect library, dispatcher); stage and camera stay.</summary>
        public void SelectFighter(int index)
        {
            if (fighters.Length == 0) return;
            index = (index % fighters.Length + fighters.Length) % fighters.Length;
            if (instance) Retire(instance.gameObject);
            if (actionEffects) Retire(actionEffects.gameObject);
            if (effects) Retire(effects.gameObject);
            if (model) Retire(model);
            instance = null; actionEffects = null; effects = null; model = null; combatHands = null;
            warnings.Clear(); repeatIntervals.Clear();
            actionMove = -1; selected = 0; scroll = Vector2.zero; failure = "";
            initialized = false; FighterIndex = index;
            try
            {
                Initialize(false);
                if (actionEffects && Featured.Length > 0) SelectAction(Featured[0]);
            }
            catch (Exception error) { failure = error.Message; Debug.LogException(error); }
        }

        void LoadMotion(int move)
        {
            var action = character.actions.FirstOrDefault(a => a.moveId == move);
            if (action == null || !action.motion) throw new InvalidDataException(DisplayName + "'s original motion is missing: " + move);
            stance = action.motion.bytes;
            if (stance.Length < 16) throw new InvalidDataException(DisplayName + " motion header is truncated.");
            stanceFrames = BitConverter.ToInt32(stance, 4);
            if (System.Text.Encoding.ASCII.GetString(stance, 0, 4) != "JRT1" || stanceFrames < 1 ||
                BitConverter.ToInt32(stance, 8) != bones.Length || BitConverter.ToInt32(stance, 12) != 60 ||
                stance.Length != 16L + stanceFrames * (3L + bones.Length * 4L) * 4)
                throw new InvalidDataException(DisplayName + " motion has an invalid rig/header: " + move);
        }

        void BuildStage()
        {
            var settings = StagingRenderSettings.Create(transform, null);
            settings.transform.localRotation = Quaternion.Euler(0, StagingRenderSettings.VersusYaw, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview floor"; floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.down * .025f;
            floor.GetComponent<Renderer>().sharedMaterial = FighterView.Surface(settings.floor);
            viewCamera = new GameObject("Character effects camera").AddComponent<Camera>();
            viewCamera.transform.SetParent(transform, false);
            viewCamera.tag = "MainCamera";
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.fieldOfView = 38; viewCamera.nearClipPlane = .05f; viewCamera.farClipPlane = 60;
            viewCamera.rect = new Rect(.28f, .19f, .72f, .81f);
            settings.Apply(viewCamera, floor.GetComponent<Renderer>());
            PositionCamera();
        }

        public void SelectRole(string role)
        {
            int index = Array.IndexOf(Roles, role);
            if (index < 0) throw new ArgumentException("Unexported " + Code + " role: " + role);
            selected = index;
            actionMove = -1;
            LoadMotion(character.stanceMoveId);
            Restart();
        }

        public void SelectAction(int move)
        {
            if (!actionEffects) throw new InvalidOperationException("Verified " + DisplayName + " native dispatch data is unavailable; the manual asset browser remains usable.");
            if (move < 0 || move >= graph.moves.Length) throw new ArgumentOutOfRangeException(nameof(move));
            LoadMotion(move);
            selected = Array.IndexOf(Roles, defaultRole);
            actionMove = move;
            Restart();
        }

        public void SetFacing(bool right)
        {
            facingRight = right;
            Seek(Clock);
        }

        public void SetTravel(bool value) { travel = value; Seek(Clock); }
        public void SetPlaying(bool value) { Playing = value; }
        public void Restart() => Seek(0);

        public void Seek(float time)
        {
            if (!initialized) return;
            if (instance) Retire(instance.gameObject);
            instance = null;
            if (actionEffects) actionEffects.Clear();
            TriggeredEvents = 0; dispatchedFrame = 0;
            Clock = 0;
            Pose();
            if (actionMove >= 0)
            {
                DispatchActionFrames();
                Advance(Mathf.Clamp(time, 0, PreviewDuration));
                return;
            }
            instance = effects.Play(CurrentRole, Anchor(), model.transform.rotation, 1, effectBones);
            if (!instance) throw new InvalidOperationException("No converted system for " + CurrentRole);
            // This browser owns the clock, in both Play mode and isolated editor captures.
            instance.enabled = false;
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Simulate(0, false, true, false);
                ps.Pause(false);
            }
            Advance(Mathf.Clamp(time, 0, PreviewDuration));
        }

        public void Advance(float seconds)
        {
            const float step = 1f / 120;
            while (seconds > .000001f)
            {
                float dt = Mathf.Min(seconds, step);
                Clock += dt; seconds -= dt;
                Pose();
                if (actionMove >= 0)
                {
                    DispatchActionFrames();
                    actionEffects.SimulateStep(dt);
                }
                if (instance)
                {
                    instance.transform.SetPositionAndRotation(Anchor(), model.transform.rotation);
                    instance.SimulateStep(dt);
                }
            }
        }

        void Pose()
        {
            float frame = actionMove >= 0 ? Mathf.Clamp(Clock * 60, 0, stanceFrames - 1) : Mathf.Repeat(Clock * 60, stanceFrames);
            int a = (int)frame, b = actionMove >= 0 ? Mathf.Min(a + 1, stanceFrames - 1) : (a + 1) % stanceFrames;
            float blend = frame - a;
            int stride = (3 + bones.Length * 4) * 4, offset = 16 + a * stride, next = 16 + b * stride;
            bones[0].localPosition = new Vector3(0, 0, Mathf.Lerp(BitConverter.ToSingle(stance, offset + 8), BitConverter.ToSingle(stance, next + 8), blend));
            for (int i = 0; i < bones.Length; i++)
                bones[i].localRotation = Quaternion.Slerp(ReadRotation(offset + 12 + i * 16), ReadRotation(next + 12 + i * 16), blend);
            combatHands?.Apply(actionMove >= 0 ? 0 : Mathf.Repeat(frame, combatHandFrames));
            float x = travel ? Mathf.Sin(Clock * 1.5f) * .65f : 0;
            model.transform.SetPositionAndRotation(modelOrigin + Vector3.right * x,
                Quaternion.LookRotation(facingRight ? Vector3.right : Vector3.left));
        }

        Quaternion ReadRotation(int offset) => new Quaternion(BitConverter.ToSingle(stance, offset), BitConverter.ToSingle(stance, offset + 4),
            BitConverter.ToSingle(stance, offset + 8), BitConverter.ToSingle(stance, offset + 12));
        Vector3 Anchor() => model.transform.position + Vector3.up * 1.15f + model.transform.forward * .65f;
        public int ParticleCount => actionMove >= 0 ? actionEffects.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount) :
            instance ? instance.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount) : 0;

        void DispatchActionFrames()
        {
            int frame = SourceFrame;
            var properties = graph.moves[actionMove].properties;
            if (properties != null)
                for (int f = dispatchedFrame + 1; f <= frame; f++)
                    for (int p = 0; p + 2 < properties.Length; p += 3)
                        if ((properties[p] & 0x3FFF) == f && actionEffects.TryPlayProperty(properties[p + 1], properties[p + 2]))
                            TriggeredEvents++;
            dispatchedFrame = frame;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) SelectFighter(FighterIndex + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SelectFighter(FighterIndex - 1);
            if (!initialized || failure.Length != 0) return;
            if (Input.GetKeyDown(KeyCode.Space)) Playing = !Playing;
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            if (Input.GetKeyDown(KeyCode.F)) SetFacing(!facingRight);
            if (Input.GetKeyDown(KeyCode.DownArrow)) SelectRole(Roles[(selected + 1) % Roles.Length]);
            if (Input.GetKeyDown(KeyCode.UpArrow)) SelectRole(Roles[(selected + Roles.Length - 1) % Roles.Length]);
            if (Playing)
            {
                float repeat = actionMove >= 0 ? stanceFrames / 60f + .3f : repeatIntervals[CurrentRole];
                if (Clock >= repeat) Restart();
                Advance(Mathf.Min(Time.unscaledDeltaTime, .1f));
            }
            if (Input.mousePosition.x > Screen.width * .28f)
            {
                zoom = Mathf.Clamp(zoom - Input.mouseScrollDelta.y * .3f, 2.5f, 9);
                PositionCamera();
            }
        }

        void PositionCamera()
        {
            if (!viewCamera) return;
            viewCamera.transform.position = new Vector3(-.15f, 1.35f, -zoom);
            viewCamera.transform.LookAt(new Vector3(-.15f, 1.05f, 0));
        }

        void OnGUI()
        {
            if (!showInterface) return;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            try
            {
                GUI.Box(new Rect(0, 0, 358, 720), "");
                var title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(18, 12, 325, 36), DisplayName.ToUpperInvariant() + " / BATTLE EFFECTS", title);
                GUI.Label(new Rect(18, 44, 325, 24), "Original package assets · " + Roles.Length + " systems");
                for (int i = 0; i < fighters.Length; i++)
                {
                    string label = fighters[i].character ? fighters[i].character.displayName.Split(' ')[0] : fighters[i].code;
                    if (GUI.Button(new Rect(18 + i * 65, 72, 62, 28), (i == FighterIndex ? "▶" : "") + label)) SelectFighter(i);
                }
                if (failure.Length != 0) { GUI.Label(new Rect(18, 110, 320, 200), failure); return; }
                bool enabled = GUI.enabled;
                GUI.enabled = initialized && actionEffects;
                var featured = Featured;
                float width = (322f - (featured.Length - 1) * 6) / Mathf.Max(1, featured.Length);
                for (int i = 0; i < featured.Length; i++)
                {
                    int move = featured[i];
                    string label = graph != null && move < graph.moves.Length ? graph.moves[move].name : "Action " + move;
                    if (GUI.Button(new Rect(18 + i * (width + 6), 108, width, 32), (actionMove == move ? "▶" : "") + label)) SelectAction(move);
                }
                GUI.enabled = enabled;
                GUI.Label(new Rect(18, 144, 325, 25), "Original move events / manual asset list below");
                scroll = GUI.BeginScrollView(new Rect(12, 172, 334, 478), scroll, new Rect(0, 0, 310, Roles.Length * 45));
                for (int i = 0; i < Roles.Length; i++)
                {
                    string name = Regex.Replace(Roles[i], "^PS_EF_(CHR_)?", "", RegexOptions.IgnoreCase);
                    if (GUI.Button(new Rect(0, i * 45, 307, 40), (selected == i ? "▶  " : "") + name)) SelectRole(Roles[i]);
                }
                GUI.EndScrollView();
                GUI.Label(new Rect(18, 656, 325, 50), "←→ fighter    ↑↓ choose    Space pause\nR restart    F flip");
                GUI.Box(new Rect(358, 574, 922, 146), "");
                GUI.Label(new Rect(380, 580, 875, 25), actionMove >= 0 ? "Original move " + actionMove + " / " + graph.moves[actionMove].name +
                    " / source frame " + SourceFrame + " / dispatched " + TriggeredEvents : CurrentRole);
                GUI.Label(new Rect(380, 602, 875, 23), actionMove >= 0 ? "Original timed properties → shared character effect dispatcher. Converted rendering adaptations apply." : PreviewLimitations);
                if (GUI.Button(new Rect(380, 624, 85, 30), Playing ? "Pause" : "Play")) Playing = !Playing;
                if (GUI.Button(new Rect(475, 624, 85, 30), "Restart")) Restart();
                if (GUI.Button(new Rect(570, 624, 100, 30), facingRight ? "Face left" : "Face right")) SetFacing(!facingRight);
                bool nextTravel = GUI.Toggle(new Rect(685, 631, 100, 25), travel, "Move fighter");
                if (nextTravel != travel) SetTravel(nextTravel);
                float seek = GUI.HorizontalSlider(new Rect(790, 635, 320, 25), Clock, 0, PreviewDuration);
                if (Mathf.Abs(seek - Clock) > .015f) { Playing = false; Seek(seek); }
                GUI.Label(new Rect(1125, 628, 140, 25), Clock.ToString("0.00") + " s");
                var note = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13 };
                GUI.Label(new Rect(380, 663, 870, 47), actionMove >= 0 ?
                    "Replays the imported action and its original effect properties. This preview does not simulate opponent collision or move transitions. Scrubbing restarts particles." :
                    "Manual asset preview. Placement and playback here are preview choices; this view does not validate original attack triggers. Scrubbing restarts the particle randomization.", note);
            }
            finally { GUI.matrix = old; }
        }

        static void Retire(GameObject value)
        {
            value.SetActive(false);
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
