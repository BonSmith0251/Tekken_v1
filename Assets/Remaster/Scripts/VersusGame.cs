using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    public class VersusGame : MonoBehaviour
    {
        public RemasterConfig config;
        public TextAsset jinRetargetedStance;
        public GameObject kazuyaPrefab;
        public MuseumCharacter jinActions;
        public MuseumCharacter kazuyaActions;
        public GameObject sceneJin;
        public GameObject sceneKazuya;
        public MuseumCharacter xiaoyuActions;
        public GameObject xiaoyuPrefab;
        public enum Fighter { Kazuya, Jin, Xiaoyu }
        [Tooltip("Fighters for this match. Each runs its original moveset, entrance and victory takes.")]
        public Fighter playerOneFighter = Fighter.Jin, playerTwoFighter = Fighter.Xiaoyu;
        public Camera sceneCamera;
        public bool automaticFight = true;
        public bool playEntrance;
        public BattleModel battle { get; private set; }
        // Tekken 7 standard health; original damage values use this scale.
        public const int TekkenHealth = 170;
        public const int IdleGetupTicks = 45;
        public bool paused { get; private set; }
        readonly PlayerCommand[] commands = { PlayerCommand.Neutral, PlayerCommand.Neutral };
        // Buttons pressed since the last 60 Hz tick, so a tap shorter than one tick still registers.
        readonly int[] pressedSinceTick = new int[2];
        CommandGraph[] graphs;
        readonly CpuPlayer[] cpu = new CpuPlayer[2];
        IFighterPresentation[] views;
        Camera cameraRig;
        StagingRenderSettings renderSettings;
        EntranceCameraLens gameplayLens;
        // Read from the original game camera +0x39C during a user-confirmed normal fight.
        const float GameplayHorizontalFov = GameplayCameraFraming.HorizontalFov;
        LocalVersusEntrance entrance;
        VictoryStaging[] victoryStaging;
        VictoryStaging victory;
        bool menu, help, diagnostic;
        FighterActionCatalog[] catalogs;
        // Render interpolation: fighter floor positions/facing at the previous 60 Hz tick.
        readonly Vector3[] previousPosition = new Vector3[2], previousFacing = new Vector3[2];
        // Smoothed gameplay camera rig: fight-line angle, framing centre and distance.
        bool cameraInitialized;
        float cameraYaw, cameraDistance, cameraZoom = 1;
        Vector3 cameraTarget;
        // Critically damped spring velocities: motion starts and settles without jolts.
        float yawVelocity, distanceVelocity, zoomVelocity;
        // Bones that must stay in shot during the fight (head, hands, feet, hips) per fighter.
        Transform[][] framingBones;
        static readonly string[] FramingBoneNames = { "Head", "L_Hand", "R_Hand", "L_Foot", "R_Foot", "BASE" };
        // Horizontal room for reaching limbs; vertically the calibrated shot already runs close to the
        // feet, so only bodies that rise or drop beyond it (launches, juggles) pull the camera back.
        const float FramingPadX = .18f, FramingMarginX = .86f, FramingPadY = .05f, FramingMarginY = .95f;
        Vector3 targetVelocity;
        const float KoSlowDuration = 1.2f, KoSlowScale = .3f;
        float koSlowStart;
        // Impact presentation. Shake levels, the airborne preset zoom and Rage Art cameras come from
        // the original move properties; their magnitudes and the KO slow-motion are approximations.
        string[] codes;
        readonly XiaoyuSecondaryDynamics[] flexibleMotion = new XiaoyuSecondaryDynamics[2];
        readonly WardrobeDynamics[] wardrobeMotion = new WardrobeDynamics[2];
        float impactShake, zoomHold, koSlowTime;
        // Real seconds since the round ended: the K.O. call shows first, then the result panel;
        // the match winner's victory take starts once the loser's death fall has played.
        float roundEndClock;
        const float KoCallSeconds = 1.8f, VictoryDelaySeconds = 2.4f;
        int koFocus = -1;
        MatchPhase lastPhase;
        // The winner's pose anchor while the victory take plays: the battle keeps simulating, but the
        // take and its actor-relative camera play from a fixed spot, squarely facing the loser.
        FighterState victoryAnchor;
        int victoryPlayer = -1;
        FightCameraTrack cinematic;
        int cinematicPlayer = -1;
        float cinematicFrame;
        Vector3 cinematicAnchor, cinematicForward;
        int rematchTicks;
        int hitsResolved;
        string hitLabel = "";
        float hitLabelTime, shake, visualTime;
        AudioSource audioSource;
        AudioClip strikeSound, guardSound;
        Material impactMaterial, blockMaterial;
        GUIStyle text, title, small, button;
        readonly List<GameObject> hitEffects = new List<GameObject>();
        // The original common battle effects (BP_CommonEffectManager roles). Which role plays when is
        // native code in the original; here it follows the hit outcome, floor contact and Rage.
        CascadeEffects effects;
        // Striking points (hands, elbows, knees, feet, head), the struck body line (hip to head) and the
        // bones Rage effects gather around, per fighter.
        Transform[][] strikeBones, bodyBones, auraBones;
        static readonly string[] StrikeBoneNames = { "L_Hand", "R_Hand", "L_ForeArm", "R_ForeArm", "L_Leg", "R_Leg", "L_Foot", "R_Foot", "Head" };
        static readonly string[] BodyBoneNames = { "Hip", "Spine2", "Head" };
        static readonly string[] AuraBoneNames = { "Hip", "Spine1", "Spine2", "Head", "L_Arm", "R_Arm", "L_ForeArm", "R_ForeArm",
            "L_Hand", "R_Hand", "L_UpLeg", "R_UpLeg", "L_Leg", "R_Leg", "L_Foot", "R_Foot" };
        // Not recovered: the original picks PierceFX natively; heavy clean hits use it here.
        const int PierceDamage = 20;
        readonly bool[] wasDowned = new bool[2], wasAirborne = new bool[2], wasRage = new bool[2];
        readonly CascadeInstance[] rageAura = new CascadeInstance[2];
        static readonly Color Ink = new Color(.025f, .035f, .055f, .96f);
        static readonly Color Muted = new Color(.58f, .66f, .75f);
        static readonly Color Accent = new Color(.95f, .78f, .35f);
        void Awake()
        {
            diagnostic = Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0 ||
                         Array.IndexOf(Environment.GetCommandLineArgs(), "--capture-preview") >= 0;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0) automaticFight = true;
            if (diagnostic) Application.runInBackground = true;
            if (!config) config = ScriptableObject.CreateInstance<RemasterConfig>();
            var setups = new[] { Setup(playerOneFighter), Setup(playerTwoFighter) };
            codes = new[] { setups[0].code, setups[1].code };
            catalogs = new FighterActionCatalog[2];
            graphs = new CommandGraph[2];
            for (int i = 0; i < 2; i++)
            {
                graphs[i] = CommandGraph.Load(setups[i].code);
                catalogs[i] = new FighterActionCatalog(setups[i].actions, setups[i].prefix, graphs[i].stance);
            }
            var rules = new MatchRules { seconds = config.rules.seconds, roundsToWin = config.rules.roundsToWin,
                arenaHalfWidth = config.rules.arenaHalfWidth, arenaHalfDepth = config.rules.arenaHalfDepth,
                health = TekkenHealth };
            battle = new BattleModel(rules, graphs); battle.Hit += OnHit; battle.MoveProperty += OnMoveProperty;
            for (int i = 0; i < 2; i++) cpu[i] = new CpuPlayer(graphs[i], 31 + i * 17);
            Time.fixedDeltaTime = 1f / 60;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            BuildArena();
            // Both fighters wear random items saved in the wardrobe scene, not their preset costumes.
            var outfitRandom = new System.Random();
            var objects = new GameObject[2];
            var rigs = new JinRig[2];
            for (int i = 0; i < 2; i++)
            {
                objects[i] = Spawn(setups[i], i);
                Dress(objects[i], setups[i].actions, outfitRandom);
                var rig = objects[i].GetComponent<JinRig>();
                if (!rig) rig = objects[i].AddComponent<JinRig>();
                rig.RefreshMaterials();
                rig.fighterMoves = catalogs[i].moves;
                rig.actionMotion = rig.gameObject.AddComponent<FighterActionMotion>();
                rig.actionMotion.Initialize(catalogs[i]);
                rigs[i] = rig;
                // Hair strands, gi flaps and cloth of whatever the fighter wears (the wardrobe's dynamics);
                // a fighter without a wardrobe falls back to the museum's adapted Xiaoyu setup.
                var wardrobe = FightWardrobe.ForCharacter(setups[i].actions);
                if (wardrobe) wardrobeMotion[i] = new WardrobeDynamics(objects[i].transform, wardrobe);
                else if (setups[i].code == "xia") flexibleMotion[i] = new XiaoyuSecondaryDynamics(objects[i].transform);
                objects[i].name = setups[i].actions.displayName + " - Player " + (i + 1);
            }
            framingBones = new Transform[2][];
            for (int i = 0; i < 2; i++)
                framingBones[i] = Array.FindAll(objects[i].GetComponentsInChildren<Transform>(),
                    t => Array.Exists(FramingBoneNames, n => string.Equals(n, t.name, StringComparison.OrdinalIgnoreCase)));
            effects = CascadeEffects.Create(transform);
            strikeBones = new Transform[2][]; bodyBones = new Transform[2][]; auraBones = new Transform[2][];
            for (int i = 0; i < 2; i++)
            {
                strikeBones[i] = Bones(objects[i], StrikeBoneNames);
                bodyBones[i] = Bones(objects[i], BodyBoneNames);
                auraBones[i] = Bones(objects[i], AuraBoneNames);
            }
            // Scene fighters that are not playing this match stay out of view.
            foreach (var unused in new[] { sceneJin, sceneKazuya })
                if (unused && unused != objects[0] && unused != objects[1]) unused.SetActive(false);
            // Hit reactions run in the attacker's moveset and may use the attacker's own clips.
            rigs[0].actionMotion.opponent = catalogs[1]; rigs[1].actionMotion.opponent = catalogs[0];
            config.playerOne = setups[0].actions.displayName.ToUpperInvariant();
            config.playerTwo = setups[1].actions.displayName.ToUpperInvariant();
            views = new IFighterPresentation[] { rigs[0], rigs[1] };
            if (playEntrance)
                entrance = new LocalVersusEntrance(objects[0].transform, objects[1].transform,
                    cameraRig, battle, rigs[1].actionMotion, setups[1].actions, rigs[0].actionMotion, setups[0].actions, gameplayLens,
                    setups[0].code, setups[1].code);
            victoryStaging = new[] {
                new VictoryStaging(objects[0].transform, rigs[0].actionMotion, setups[0].actions, setups[0].code),
                new VictoryStaging(objects[1].transform, rigs[1].actionMotion, setups[1].actions, setups[1].code) };
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.spatialBlend = 0; audioSource.volume = .3f;
            strikeSound = Tone("Strike", 110, .1f); guardSound = Tone("Guard", 420, .06f);
            impactMaterial = FighterView.Surface(new Color(1, .7f, .25f));
            blockMaterial = FighterView.Surface(Color.cyan);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0)
            { menu = false; float introTime = entrance != null ? entrance.DurationSeconds : 0;
                Invoke(nameof(SmokeCapture), introTime + 8); Invoke(nameof(SmokeExit), introTime + 10); }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--capture-preview") >= 0)
            { menu = false; Invoke(nameof(PreviewCapture), 4); }
        }
        struct FighterSetup { public string code, prefix; public MuseumCharacter actions; public Fighter fighter; }

        // Bones in the given order (rigs spell some names differently, so match case-insensitively).
        static Transform[] Bones(GameObject root, string[] names)
        {
            var all = root.GetComponentsInChildren<Transform>();
            var found = new List<Transform>();
            foreach (var n in names)
            {
                var bone = Array.Find(all, t => string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase));
                if (bone) found.Add(bone);
            }
            return found.ToArray();
        }

        FighterSetup Setup(Fighter fighter)
        {
#if UNITY_EDITOR
            if (!jinActions) jinActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/Remaster/Museum/Jin.asset");
            if (!kazuyaActions) kazuyaActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/Remaster/Museum/Kazuya.asset");
            if (!xiaoyuActions) xiaoyuActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/Remaster/Museum/Xiaoyu.asset");
#endif
            var setup = fighter == Fighter.Jin ? new FighterSetup { code = "jin", prefix = "Jz_", actions = jinActions } :
                        fighter == Fighter.Kazuya ? new FighterSetup { code = "kaz", prefix = "Kz_", actions = kazuyaActions } :
                        new FighterSetup { code = "xia", prefix = "Fu_", actions = xiaoyuActions };
            setup.fighter = fighter;
            if (!setup.actions) throw new InvalidOperationException("Assign the " + fighter + " move catalog in LocalVersus.");
            return setup;
        }

        // Scene-placed fighters are used once; a second copy (mirror match) or Xiaoyu comes from the prefab.
        GameObject Spawn(FighterSetup setup, int player)
        {
            if (setup.fighter == Fighter.Jin)
            {
                if (sceneJin && sceneJin.activeSelf && (player == 0 || playerOneFighter != Fighter.Jin)) return sceneJin;
                return Instantiate(Resources.Load<GameObject>("JinPlayable"), transform);
            }
            if (setup.fighter == Fighter.Kazuya)
            {
                if (sceneKazuya && (player == 0 || playerOneFighter != Fighter.Kazuya)) return sceneKazuya;
#if UNITY_EDITOR
                // An editor session can retain an older in-memory scene after the scene file changes.
                if (!kazuyaPrefab) kazuyaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Kazuya/Prefabs/Karate_Gi.prefab");
#endif
                if (!kazuyaPrefab) throw new InvalidOperationException("Assign the Kazuya Karate Gi prefab in LocalVersus.");
                return Instantiate(kazuyaPrefab, transform);
            }
#if UNITY_EDITOR
            if (!xiaoyuPrefab) xiaoyuPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remaster/Xiaoyu/Prefabs/Default_1P.prefab");
#endif
            if (!xiaoyuPrefab) throw new InvalidOperationException("Assign the Xiaoyu Default 1P prefab in LocalVersus.");
            return Instantiate(xiaoyuPrefab, transform);
        }

        static void Dress(GameObject fighter, MuseumCharacter character, System.Random random)
        {
            var outfit = FightWardrobe.DressSaved(fighter, character, random);
            if (outfit != null) Debug.Log(character.displayName + " fight outfit: " + string.Join(", ", outfit));
        }
        void SmokeCapture()
        {
            ScreenCaptureFallback();
            var jin = views[1] as JinRig;
            var kazuya = views[0] as JinRig;
            bool retargeted = jin && jin.actionMotion && jin.actionMotion.Ready;
            bool kazuyaReady = kazuya && kazuya.actionMotion && kazuya.actionMotion.Ready;
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../runtime-smoke.json"),
                "{\"sceneLoaded\":true,\"fighters\":2,\"phase\":\"" + battle.phase +
                "\",\"automaticFight\":" + (automaticFight ? "true" : "false") +
                ",\"jinRetargetedStance\":" + (retargeted ? "true" : "false") +
                ",\"kazuyaActionsReady\":" + (kazuyaReady ? "true" : "false") +
                ",\"jinActionsPlayed\":" + (jin && jin.actionMotion ? jin.actionMotion.PlayedActions : 0) +
                ",\"kazuyaActionsPlayed\":" + (kazuyaReady ? kazuya.actionMotion.PlayedActions : 0) +
                ",\"jinMoveCount\":" + catalogs[1].moves.Length +
                ",\"kazuyaMoveCount\":" + catalogs[0].moves.Length +
                ",\"hitsResolved\":" + hitsResolved +
                ",\"jinHealth\":" + battle.fighters[1].health +
                ",\"kazuyaHealth\":" + battle.fighters[0].health + "}");
        }
        void SmokeExit() => Application.Quit(0);
        void PreviewCapture() => ScreenCaptureFallback();
        void ScreenCaptureFallback()
        {
            // Camera-only proof of the live arena; the in-game HUD uses immediate-mode UI.
            var rt = new RenderTexture(1280, 720, 24); cameraRig.targetTexture = rt; cameraRig.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../arena-preview.png"), image.EncodeToPNG());
            cameraRig.targetTexture = null; RenderTexture.active = null; Destroy(rt); Destroy(image);
        }
        static AudioClip Tone(string name, float frequency, float duration)
        {
            int count = (int)(44100 * duration); var samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = Mathf.Sin(i / 44100f * frequency * Mathf.PI * 2) * Mathf.Pow(1f - (float)i / count, 2);
            var clip = AudioClip.Create(name, count, 1, 44100, false); clip.SetData(samples, 0); return clip;
        }
        void BuildArena()
        {
            var floor = FighterView.Surface(new Color(.09f, .12f, .17f), .55f);
            var edge = FighterView.Surface(new Color(.18f, .28f, .36f), .5f);
            var neon = FighterView.Surface(new Color(.1f, .5f, .65f));
            HdrpSceneLighting.Emission(neon, new Color(.05f, .5f, .7f) * 1.6f);
            FighterView.Piece("Arena platform", PrimitiveType.Cube, transform, new Vector3(0, -.3f, 0), new Vector3(15, .6f, 9), floor);
            for (int i = -6; i <= 6; i++)
                FighterView.Piece("Floor grid", PrimitiveType.Cube, transform, new Vector3(i, .005f, 0), new Vector3(.018f, .012f, 7), edge);
            for (int i = -3; i <= 3; i++)
                FighterView.Piece("Floor grid", PrimitiveType.Cube, transform, new Vector3(0, .006f, i), new Vector3(13, .012f, .018f), edge);
            for (int side = -1; side <= 1; side += 2)
            {
                FighterView.Piece("Arena boundary", PrimitiveType.Cube, transform, new Vector3(0, .025f, side * 3.3f), new Vector3(13, .03f, .055f), neon);
                FighterView.Piece("Arena boundary", PrimitiveType.Cube, transform, new Vector3(side * 6.5f, .025f, 0), new Vector3(.055f, .03f, 6.6f), neon);
            }
            for (int i = 0; i < 16; i++)
            {
                float x = (i - 7.5f) * 2.5f, h = 2 + (i * 7 % 5);
                FighterView.Piece("Distant architecture", PrimitiveType.Cube, transform, new Vector3(x, h / 2 - .3f, 11 + i % 3), new Vector3(1.7f, h, 2), floor);
                FighterView.Piece("Light column", PrimitiveType.Cube, transform, new Vector3(x - .55f, h / 2 - .3f, 9.95f + i % 3), new Vector3(.06f, h * .7f, .04f), neon);
            }
            // A Render Settings object in the scene supplies lights, volume and background (the
            // staging_test look). Without one, these lights stand in until HdrpSceneLighting
            // replaces them with the original stg29 stage lighting.
            renderSettings = FindFirstObjectByType<StagingRenderSettings>();
            if (!renderSettings)
            {
                var light = new GameObject("Key light").AddComponent<Light>(); light.transform.SetParent(transform);
                light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(45, -35, 0);
                var fill = new GameObject("Cool rim light").AddComponent<Light>(); fill.transform.SetParent(transform);
                fill.type = LightType.Directional; fill.color = new Color(.35f, .6f, 1); fill.intensity = .65f;
                fill.transform.rotation = Quaternion.Euler(30, 140, 0);
            }
            cameraRig = sceneCamera ? sceneCamera : new GameObject("Versus camera").AddComponent<Camera>();
            if (!sceneCamera) cameraRig.transform.SetParent(transform);
            cameraRig.tag = "MainCamera"; cameraRig.backgroundColor = new Color(.025f, .04f, .07f);
            cameraRig.clearFlags = CameraClearFlags.SolidColor;
            // The arena keeps its own floor materials.
            if (renderSettings) renderSettings.Apply(cameraRig, null);
            gameplayLens = new EntranceCameraLens(cameraRig);
            gameplayLens.SetHorizontalFov(GameplayHorizontalFov);
            cameraRig.nearClipPlane = .1f; cameraRig.farClipPlane = 100;
            if (!cameraRig.GetComponent<AudioListener>()) cameraRig.gameObject.AddComponent<AudioListener>();
        }
        void Update()
        {
            float realDelta = Time.unscaledDeltaTime;
            if (battle != null && (battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver) && !paused)
                roundEndClock += realDelta;
            impactShake *= Mathf.Exp(-realDelta * 9);
            zoomHold -= realDelta;
            if (koSlowTime > 0)
            {
                // Ease into and out of the final-blow slow motion rather than switching speed.
                koSlowTime -= realDelta;
                float elapsed = koSlowStart - koSlowTime;
                float amount = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .15f)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01(koSlowTime / .35f));
                Time.timeScale = koSlowTime > 0 ? Mathf.Lerp(1, KoSlowScale, amount) : 1;
                if (koSlowTime <= 0) koFocus = -1;
            }
            if (victory != null && !paused) victory.Advance(Time.unscaledDeltaTime);
            if (entrance != null && entrance.Active)
            {
                visualTime += Time.unscaledDeltaTime;
                entrance.Advance(Time.unscaledDeltaTime, !diagnostic && Input.GetKeyDown(KeyCode.Return));
                return;
            }
            if (Input.GetKeyDown(KeyCode.F1)) help = !help;
            if (Input.GetKeyDown(KeyCode.F2)) { automaticFight = !automaticFight; ClearInputs(); }
            if (Input.GetKeyDown(KeyCode.Escape) && !menu) { paused = !paused; ClearInputs(); }
            if (Input.GetKeyDown(KeyCode.Return))
            { if (menu || battle.phase == MatchPhase.MatchOver) StartMatch(); else if (paused) paused = false; }
            if (menu || paused) return;
            if (automaticFight) { visualTime += Time.deltaTime; hitLabelTime -= Time.deltaTime; shake = Mathf.MoveTowards(shake, 0, Time.deltaTime * 1.8f); return; }
            if (!diagnostic)
                for (int i = 0; i < 2; i++) pressedSinceTick[i] |= PressedButtons(i);
            visualTime += Time.deltaTime;
            hitLabelTime -= Time.deltaTime; shake = Mathf.MoveTowards(shake, 0, Time.deltaTime * 1.8f);
        }
        void FixedUpdate()
        {
            if (entrance != null && entrance.Active) return;
            if (menu || paused) return;
            if (automaticFight)
            {
                if (battle.phase == MatchPhase.MatchOver)
                {
                    // Keep the fighters animating: the loser's original death fall plays out.
                    CapturePrevious();
                    battle.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
                    AfterTick();
                    if (battle.winner >= 0 && (victory == null || !victory.Complete)) return;
                    if (++rematchTicks >= 180) { rematchTicks = 0; ClearVictory(); ResetImpactEffects(); battle.Restart(); lastPhase = battle.phase; }
                    return;
                }
                commands[0] = AutoCommand(0);
                commands[1] = AutoCommand(1);
                CapturePrevious();
                battle.Tick(commands[0], commands[1]);
                AfterTick();
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                commands[i] = diagnostic ? PlayerCommand.Neutral : ReadInput(i);
                commands[i].buttons |= pressedSinceTick[i]; pressedSinceTick[i] = 0;
                // Convenience, not original behaviour: a player who stays down without input
                // stands up in place after IdleGetupTicks (any getup input works at once).
                var f = battle.fighters[i];
                if (f.downedTicks >= IdleGetupTicks && commands[i].stick == 5 && commands[i].buttons == 0) commands[i].stick = 8;
            }
            CapturePrevious();
            battle.Tick(commands[0], commands[1]);
            AfterTick();
        }
        PlayerCommand AutoCommand(int player)
        {
            if (battle.phase != MatchPhase.Fight) return PlayerCommand.Neutral;
            return cpu[player].Next(battle.fighters[player], battle.fighters[1 - player]);
        }
        void LateUpdate()
        {
            // Re-applied so color edits on the Render Settings object show live during Play.
            if (renderSettings && cameraRig) renderSettings.Apply(cameraRig, null);
            if (views == null) return;
            if (battle.phase == MatchPhase.MatchOver && battle.winner >= 0 && victory == null &&
                (!battle.UsesCommandGraphs || roundEndClock >= VictoryDelaySeconds))
            {
                victory = victoryStaging[battle.winner];
                // Only takes the winner's own moveset reaches (paired "_y" partner takes are never
                // played by the winner); a button held at the end picks its take, as in the original.
                var winnerGraph = graphs[battle.winner];
                var reachable = VictoryTargets(winnerGraph);
                int held = automaticFight || diagnostic ? 0 : ReadInput(battle.winner).buttons;
                victory.Begin(e => reachable.ContainsKey(e.moveId), PreferredVictory(reachable, held, codes[battle.winner]));
                victoryPlayer = battle.winner;
                victoryAnchor = battle.fighters[victoryPlayer].Snapshot();
                var loser = battle.fighters[1 - victoryPlayer];
                float dx = loser.x - victoryAnchor.x, dz = loser.z - victoryAnchor.z, length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length > 1e-3f) { victoryAnchor.dirX = dx / length; victoryAnchor.dirZ = dz / length; }
                victoryAnchor.turn = 0; victoryAnchor.backturned = false;
            }
            if (victory != null) victory.Prepare();
            if (entrance != null && entrance.Active) entrance.PrepareMotion();
            var shown = new FighterState[2];
            for (int i = 0; i < 2; i++)
            {
                shown[i] = victory != null && i == victoryPlayer && victoryAnchor != null ? victoryAnchor : Interpolated(i);
                views[i].Render(shown[i], config.rules, visualTime,
                    !menu && !paused && battle.phase == MatchPhase.Fight && commands[i].stick != 5);
                if (!paused) { flexibleMotion[i]?.Tick(Time.deltaTime); wardrobeMotion[i]?.Tick(Time.deltaTime); }
            }
            if (entrance != null && entrance.Active) { entrance.Present(); return; }
            if (victory != null) { victory.Present(cameraRig, gameplayLens); return; }
            gameplayLens.SetHorizontalFov(GameplayHorizontalFov);
            var a = shown[0]; var b = shown[1];
            GameplayCameraFraming.Evaluate(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z),
                new Vector3(battle.screenRightX, 0, battle.screenRightZ), out Vector3 goalPosition, out Vector3 goalTarget);
            // Ease the rig as a whole: the fight-line angle turns slowly (ignoring walk-cycle sway),
            // the framing centre and distance follow faster, and the view always aims at the
            // smoothed centre from the smoothed position, so turning and tracking never disagree.
            var goalOffset = goalPosition - goalTarget;
            float goalYaw = Mathf.Atan2(goalOffset.x, goalOffset.z) * Mathf.Rad2Deg;
            float goalDistance = new Vector2(goalOffset.x, goalOffset.z).magnitude;
            float dt = Time.unscaledDeltaTime;
            // Airborne preset zoom and the KO close-up pull the rig in; the KO also centres the downed fighter.
            float zoomGoal = koFocus >= 0 ? .72f : zoomHold > 0 ? .84f : 1;
            if (dt > 0) cameraZoom = Mathf.SmoothDamp(cameraZoom, zoomGoal, ref zoomVelocity, .4f, Mathf.Infinity, dt);
            goalDistance *= cameraZoom;
            if (koFocus >= 0) goalTarget = Vector3.Lerp(goalTarget, new Vector3(shown[koFocus].x, goalTarget.y - .25f, shown[koFocus].z), .6f);
            if (!cameraInitialized)
            {
                cameraYaw = goalYaw; cameraDistance = goalDistance; cameraTarget = goalTarget; cameraInitialized = true;
                yawVelocity = distanceVelocity = 0; targetVelocity = Vector3.zero;
            }
            else if (dt > 0)
            {
                // The fight-line angle turns slowest (walk sway never shows), the framing centre
                // follows a little faster, and the distance eases between the two.
                cameraYaw = Mathf.SmoothDampAngle(cameraYaw, goalYaw, ref yawVelocity, .38f, 240, dt);
                cameraDistance = Mathf.SmoothDamp(cameraDistance, goalDistance, ref distanceVelocity, .32f, Mathf.Infinity, dt);
                cameraTarget = Vector3.SmoothDamp(cameraTarget, goalTarget, ref targetVelocity, .2f, Mathf.Infinity, dt);
            }
            var orbit = Quaternion.Euler(0, cameraYaw, 0);
            var position = cameraTarget + orbit * Vector3.forward * cameraDistance + Vector3.up * goalOffset.y;
            if (battle.phase == MatchPhase.Fight && koFocus < 0 && cinematic == null)
            {
                // Hard framing guarantee after the easing: if any fighter's head, hands, feet or hips
                // (padded for limbs) would leave the frame, pull back at once instead of easing out.
                float required = RequiredFramingDistance(Quaternion.LookRotation(cameraTarget - position), cameraTarget);
                if (cameraDistance < required)
                {
                    cameraDistance = required; distanceVelocity = Mathf.Max(0, distanceVelocity);
                    position = cameraTarget + orbit * Vector3.forward * cameraDistance + Vector3.up * goalOffset.y;
                }
            }
            // Keep the calibrated level angle; hit and impact shake offset the camera without turning it.
            var rotation = Quaternion.LookRotation(cameraTarget - position);
            // Shakes use smooth noise (about 14 Hz) rather than a fast sine, so they read as weight, not jitter.
            float realTime = Time.unscaledTime, amplitude = impactShake + shake * .05f;
            var jolt = (orbit * Vector3.right * (Mathf.PerlinNoise(realTime * 14, .3f) * 2 - 1) +
                        Vector3.up * (Mathf.PerlinNoise(.7f, realTime * 14 + 5) * 2 - 1) * .8f) * amplitude;
            float fov = GameplayHorizontalFov;
            if (cinematic != null)
            {
                // Original Rage Art camera, blended back to the gameplay view by the track's own weight.
                float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                cinematic.Sample(cinematicFrame + alpha, cinematicAnchor, cinematicForward,
                    out Vector3 shotPosition, out Vector3 shotTarget, out float shotFov, out float weight);
                shotPosition.y = Mathf.Max(.25f, shotPosition.y);
                var shotRotation = Quaternion.LookRotation(shotTarget - shotPosition);
                position = Vector3.Lerp(shotPosition, position, weight);
                rotation = Quaternion.Slerp(shotRotation, rotation, weight);
                fov = Mathf.Lerp(shotFov, GameplayHorizontalFov, weight);
            }
            gameplayLens.SetHorizontalFov(fov);
            cameraRig.transform.SetPositionAndRotation(position + jolt, rotation);
        }

        // Distance from the framing centre at which every framing bone fits inside the margins.
        float RequiredFramingDistance(Quaternion look, Vector3 centre)
        {
            if (framingBones == null) return 0;
            float tanY = Mathf.Tan(cameraRig.fieldOfView * .5f * Mathf.Deg2Rad), tanX = tanY * cameraRig.aspect;
            var inverse = Quaternion.Inverse(look);
            float required = 0;
            foreach (var bones in framingBones)
                foreach (var bone in bones)
                {
                    if (!bone) continue;
                    // Camera looks along +z from -distance; a point's depth is distance + local.z.
                    var local = inverse * (bone.position - centre);
                    required = Mathf.Max(required,
                        (Mathf.Abs(local.x) + FramingPadX) / (tanX * FramingMarginX) - local.z,
                        (Mathf.Abs(local.y) + FramingPadY) / (tanY * FramingMarginY) - local.z);
                }
            return required;
        }

        // Victory takes targeted by the moveset's own cancels, with the held button (bits 1, 2, 4, 8 for
        // 1-4) that selects each: those cancels carry the button one byte above the normal button bits.
        static Dictionary<int, int> VictoryTargets(CommandGraph graph)
        {
            var targets = new Dictionary<int, int>();
            foreach (var table in new[] { graph.cancels, graph.groupCancels })
                foreach (var c in table)
                {
                    if ((c.command & 0xFFFFFFFF) == 0x800B || c.target < 0 || c.target >= graph.moves.Length) continue;
                    int held = (int)((c.command >> 40) & 0xF);
                    if (!targets.TryGetValue(c.target, out int known) || known == 0) targets[c.target] = held;
                }
            return targets;
        }

        static int PreferredVictory(Dictionary<int, int> targets, int held, string code)
        {
            if (held == 0) return -1;
            foreach (var e in StagingCatalog.Load().entries)
                if (e.character == code && e.kind == "victory" && targets.TryGetValue(e.moveId, out int button) &&
                    button != 0 && (held & button) == button)
                    return e.moveId;
            return -1;
        }

        void CapturePrevious()
        {
            for (int i = 0; i < 2; i++)
            {
                var f = battle.fighters[i];
                previousPosition[i] = new Vector3(f.x, 0, f.z); previousFacing[i] = new Vector3(f.dirX, 0, f.dirZ);
            }
        }

        // The simulation runs at 60 Hz; draw fighters between their last two ticks so motion stays
        // smooth at any frame rate. Poses still come from the current tick's move and frame.
        FighterState Interpolated(int i)
        {
            var f = battle.fighters[i];
            if (!battle.UsesCommandGraphs || menu || paused) return f;
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            var current = new Vector3(f.x, 0, f.z);
            if ((current - previousPosition[i]).sqrMagnitude > 1) return f;   // round reset: no sweep
            var s = f.Snapshot();
            var p = Vector3.Lerp(previousPosition[i], current, alpha);
            var d = Vector3.Slerp(previousFacing[i], new Vector3(f.dirX, 0, f.dirZ), alpha);
            s.x = p.x; s.z = p.z;
            if (d.sqrMagnitude > 1e-6f) { d.Normalize(); s.dirX = d.x; s.dirZ = d.z; }
            return s;
        }
        // Tekken 7 PC defaults: P1 W/A/S/D with U I J K for 1 2 3 4. P2 uses the arrows and the number
        // pad laid out like the buttons (4 5 over 1 2), or O P L ; without a pad. Gamepads follow
        // the original pad layout: X = 1, Y = 2, A = 3, B = 4.
        static readonly KeyCode[][] ButtonKeys = {
            new[] { KeyCode.U, KeyCode.I, KeyCode.J, KeyCode.K },
            new[] { KeyCode.Keypad4, KeyCode.Keypad5, KeyCode.Keypad1, KeyCode.Keypad2 } };
        static readonly KeyCode[] AlternateP2 = { KeyCode.O, KeyCode.P, KeyCode.L, KeyCode.Semicolon };
        static readonly int[] PadButton = { 2, 3, 0, 1 };
        bool ButtonHeld(int player, int button, bool down)
        {
            bool one = player == 0;
            KeyCode key = ButtonKeys[player][button], pad = (KeyCode)((int)(one ? KeyCode.Joystick1Button0 : KeyCode.Joystick2Button0) + PadButton[button]);
            return down ? Input.GetKeyDown(key) || (!one && Input.GetKeyDown(AlternateP2[button])) || Input.GetKeyDown(pad)
                        : Input.GetKey(key) || (!one && Input.GetKey(AlternateP2[button])) || Input.GetKey(pad);
        }
        int PressedButtons(int player)
        {
            int mask = 0;
            for (int b = 0; b < 4; b++) if (ButtonHeld(player, b, true)) mask |= 1 << b;
            return mask;
        }
        PlayerCommand ReadInput(int player)
        {
            bool one = player == 0;
            var c = PlayerCommand.Neutral;
            float x = (Input.GetKey(one ? KeyCode.D : KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(one ? KeyCode.A : KeyCode.LeftArrow) ? 1 : 0);
            float y = (Input.GetKey(one ? KeyCode.W : KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(one ? KeyCode.S : KeyCode.DownArrow) ? 1 : 0);
            x = Mathf.Clamp(x + Input.GetAxisRaw(one ? "P1StickX" : "P2StickX"), -1, 1);
            y = Mathf.Clamp(y - Input.GetAxisRaw(one ? "P1StickY" : "P2StickY"), -1, 1);
            int column = x > .5f ? 2 : x < -.5f ? 0 : 1, row = y > .5f ? 2 : y < -.5f ? 0 : 1;
            c.stick = row * 3 + column + 1;
            for (int b = 0; b < 4; b++) if (ButtonHeld(player, b, false)) c.buttons |= 1 << b;
            return c;
        }
        void OnMoveProperty(int player, long id, long value)
        {
            switch (id)
            {
                // Two families of four original shake levels; amplitudes are not stored in the data.
                case 0x8001: case 0x8006: Impact(.012f); break;
                case 0x8002: case 0x8007: Impact(.025f); break;
                case 0x8003: case 0x8008: Impact(.045f); break;
                case 0x8004: case 0x8009: Impact(.08f); break;
                case 0x84CC: zoomHold = .45f; break;          // preset camera zoom on airborne reactions
                case 0x8439: case 0x843A:                      // cinematic camera, left/right screen side
                    if ((value & 0xC000) == 0x4000 && (id == 0x8439) == (battle.fighters[player].facing > 0))
                        StartCinematic(player, (int)(value & 0x3FFF));
                    break;
            }
        }

        void Impact(float amplitude) => impactShake = Mathf.Max(impactShake, amplitude);

        void StartCinematic(int player, int track)
        {
            try { cinematic = new FightCameraTrack(codes[player] + "_" + track); }
            catch (Exception e) { Debug.LogWarning(e.Message); cinematic = null; return; }
            var f = battle.fighters[player];
            cinematicPlayer = player; cinematicFrame = 0;
            cinematicAnchor = new Vector3(f.x, 0, f.z); cinematicForward = new Vector3(f.dirX, 0, f.dirZ);
        }

        void StopCinematic() { cinematic = null; cinematicPlayer = -1; }

        void AfterTick()
        {
            if (cinematic != null)
            {
                cinematicFrame++;
                // The camera belongs to the attacker's action: it ends with the track, the round,
                // or when the attacker is hit out of it.
                if (cinematicFrame >= cinematic.Frames - 1 || battle.phase != MatchPhase.Fight || battle.fighters[cinematicPlayer].reacting)
                    StopCinematic();
            }
            if (lastPhase == MatchPhase.Fight && battle.phase != MatchPhase.Fight) roundEndClock = 0;
            if (lastPhase == MatchPhase.Fight && battle.phase != MatchPhase.Fight && battle.result != "TIME UP")
            {
                // Final blow: brief slow motion, closing in on the fighter who went down.
                // Final round: a longer slow motion, as the match-deciding blow in the original.
                koSlowTime = battle.phase == MatchPhase.MatchOver ? KoSlowDuration * 1.6f : KoSlowDuration;
                koSlowStart = koSlowTime;
                koFocus = battle.winner >= 0 ? 1 - battle.winner : 0;
                Impact(.06f);
            }
            lastPhase = battle.phase;
            UpdateBattleEffects();
        }

        void ResetImpactEffects()
        {
            impactShake = zoomHold = koSlowTime = 0; koFocus = -1; Time.timeScale = 1; StopCinematic();
            cameraInitialized = false;
            for (int i = 0; i < 2; i++) { StopAura(i); wasDowned[i] = wasAirborne[i] = wasRage[i] = false; }
        }

        void OnDisable() => Time.timeScale = 1;

        void OnApplicationFocus(bool focus) { if (!focus && !menu && !diagnostic) { paused = true; ClearInputs(); } }
        void ClearInputs() { for (int i = 0; i < 2; i++) { commands[i] = PlayerCommand.Neutral; pressedSinceTick[i] = 0; cpu[i]?.Clear(); } }
        void ClearVictory() { if (victory != null) victory.Clear(); victory = null; victoryPlayer = -1; victoryAnchor = null; }
        void StartMatch() { menu = false; paused = false; ClearVictory(); ClearInputs(); ResetImpactEffects(); battle.Restart(); lastPhase = battle.phase; }
        void OnHit(HitEvent hit)
        {
            hitsResolved++;
            hitLabel = hit.blocked ? "GUARD" : "P" + (hit.attacker + 1) + "   /   " + hit.damage + " DAMAGE";
            hitLabelTime = .65f; shake = hit.blocked ? .12f : .4f;
            audioSource.PlayOneShot(hit.blocked ? guardSound : strikeSound);
            string role = hit.blocked ? CascadeEffects.Guard : hit.counterHit ? CascadeEffects.Counter : CascadeEffects.Hit;
            if (effects && effects.Has(role))
            {
                Contact(hit.attacker, hit.defender, out var point, out var direction);
                var rotation = Quaternion.LookRotation(direction, Vector3.up);
                effects.Play(role, point, rotation);
                if (!hit.blocked && hit.damage >= PierceDamage) effects.Play(CascadeEffects.Pierce, point, rotation);
                return;
            }
            var f = battle.fighters[hit.defender];
            var spark = FighterView.Piece("Impact", PrimitiveType.Sphere, transform, new Vector3(f.x, 1.3f, f.z - .1f),
                Vector3.one * .2f, hit.blocked ? blockMaterial : impactMaterial).gameObject;
            hitEffects.Add(spark); Destroy(spark, .08f);
            hitEffects.RemoveAll(x => !x);
        }

        // The data holds no hitbox positions for the effects: the struck point is taken where the
        // attacker's nearest striking bone meets the defender's body line, and the effect faces along
        // the blow (the source's effect X axis).
        void Contact(int attacker, int defender, out Vector3 point, out Vector3 direction)
        {
            var a = battle.fighters[attacker]; var d = battle.fighters[defender];
            var across = new Vector3(d.x - a.x, 0, d.z - a.z);
            direction = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.forward;
            point = transform.TransformPoint(new Vector3(d.x, 1.2f, d.z));
            var limbs = strikeBones?[attacker]; var body = bodyBones?[defender];
            if (limbs == null || body == null || limbs.Length == 0 || body.Length < 2) return;
            float best = float.MaxValue; Vector3 limb = point, onBody = point;
            foreach (var l in limbs)
                for (int k = 0; k + 1 < body.Length; k++)
                {
                    var q = ClosestOnSegment(l.position, body[k].position, body[k + 1].position);
                    float dist = (l.position - q).sqrMagnitude;
                    if (dist < best) { best = dist; limb = l.position; onBody = q; }
                }
            var toLimb = limb - onBody;
            // On the body surface, toward the striking limb.
            point = onBody + (toLimb.sqrMagnitude > 1e-6f ? toLimb.normalized * Mathf.Min(.12f, toLimb.magnitude * .5f) : Vector3.zero);
            var blow = onBody - limb;
            if (blow.sqrMagnitude > 1e-4f) direction = Vector3.Slerp(direction, blow.normalized, .5f).normalized;
        }

        static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float len = ab.sqrMagnitude;
            return len < 1e-8f ? a : a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
        }

        // Floor impacts and Rage, from state changes after each tick: a body that lands from the air
        // plays BlastFX and SmokeFX, any other fall to the floor SmokeFX; Rage starts with RageFX and
        // keeps RageAuraFX on the fighter until the round ends. Rage is not spent here, since the model
        // has no Rage Art consumption, so the aura stays while health is at a quarter or less.
        void UpdateBattleEffects()
        {
            if (!effects) return;
            for (int i = 0; i < 2; i++)
            {
                var f = battle.fighters[i];
                bool live = battle.phase == MatchPhase.Fight || battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver;
                if (live && f.downed && !wasDowned[i])
                {
                    var hip = bodyBones[i].Length > 0 ? bodyBones[i][0].position : transform.TransformPoint(new Vector3(f.x, 0, f.z));
                    var floor = new Vector3(hip.x, 0, hip.z);
                    var facing = Quaternion.LookRotation(new Vector3(f.dirX, 0, f.dirZ).sqrMagnitude > 1e-6f ? new Vector3(f.dirX, 0, f.dirZ) : Vector3.forward);
                    if (wasAirborne[i]) effects.Play(CascadeEffects.Blast, floor, facing);
                    effects.Play(CascadeEffects.Smoke, floor, facing);
                }
                wasDowned[i] = f.downed; wasAirborne[i] = f.airborne;
                bool rage = battle.phase == MatchPhase.Fight && f.health > 0 && f.health * 4 <= battle.rules.health;
                if (rage && !wasRage[i])
                {
                    var center = bodyBones[i].Length > 1 ? bodyBones[i][1].position : transform.TransformPoint(new Vector3(f.x, 1, f.z));
                    // The source effects are authored in the character's frame: X along its facing.
                    var heading = new Vector3(f.dirX, 0, f.dirZ);
                    var facing = Quaternion.LookRotation(heading.sqrMagnitude > 1e-6f ? heading : Vector3.forward);
                    effects.Play(CascadeEffects.Rage, center, facing, 1, auraBones[i]);
                    rageAura[i] = effects.Play(CascadeEffects.RageAura, center, facing, 1, auraBones[i]);
                }
                else if (!rage && wasRage[i]) StopAura(i);
                wasRage[i] = rage;
                if (rageAura[i] && bodyBones[i].Length > 1) rageAura[i].transform.position = bodyBones[i][1].position;
            }
        }

        void StopAura(int i) { if (rageAura[i]) rageAura[i].Stop(); rageAura[i] = null; }
        void Styles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            text.normal.textColor = Color.white;
            small = new GUIStyle(text) { fontSize = 13 }; small.normal.textColor = Muted;
            title = new GUIStyle(text) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
        }
        static void Panel(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        void Label(Rect rect, string value, GUIStyle style, Color? color = null)
        { var old = GUI.color; GUI.color = color ?? Color.white; GUI.Label(rect, value, style); GUI.color = old; }
        void OnGUI()
        {
            Styles();
            if (entrance != null && entrance.Active && !entrance.ShowsBattleHud) { entrance.Draw(); return; }
            if (!cameraRig) return;
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Rect viewport = cameraRig.pixelRect;
            float top = Screen.height - viewport.yMax;
            Panel(new Rect(0, 0, Screen.width, top), Color.black);
            Panel(new Rect(0, top + viewport.height, Screen.width, viewport.y), Color.black);
            Panel(new Rect(0, top, viewport.x, viewport.height), Color.black);
            Panel(new Rect(viewport.xMax, top, Screen.width - viewport.xMax, viewport.height), Color.black);
            GUI.matrix = Matrix4x4.TRS(new Vector3(viewport.x, top, 0), Quaternion.identity,
                new Vector3(viewport.width / 1280f, viewport.height / 720f, 1));
            try { DrawGameplayGUI(); if (entrance != null && entrance.Active) entrance.Draw(); }
            finally { GUI.matrix = previousMatrix; }
        }
        void DrawGameplayGUI()
        {
            if (menu) { DrawMenu(); return; }
            Panel(new Rect(0, 0, 1280, 115), Ink);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 40 : 740; var f = battle.fighters[i];
                Label(new Rect(x, 14, 470, 30), "P" + (i + 1) + "   " + (i == 0 ? config.playerOne : config.playerTwo), text);
                Panel(new Rect(x, 50, 500, 18), new Color(.2f, .22f, .27f));
                float width = 500f * f.health / battle.rules.health;
                Panel(new Rect(i == 0 ? x : x + 500 - width, 50, width, 18), i == 0 ? config.colorOne : config.colorTwo);
                Label(new Rect(x, 73, 360, 25), f.health + " HP    /    " + f.wins + " ROUND" + (f.wins == 1 ? "" : "S"), small);
            }
            Label(new Rect(550, 20, 180, 60), Mathf.CeilToInt(battle.ticksLeft / 60f).ToString("00"), title, Accent);
            Label(new Rect(581, 78, 150, 25), "FIRST TO " + config.rules.roundsToWin, small);
            Label(new Rect(40, 128, 650, 25), (automaticFight ? "AUTO FIGHT" : "LOCAL VERSUS") + "   /   ROUND " + battle.round + "   /   NEON DECK", small);
            if (hitLabelTime > 0) Label(new Rect(440, 150, 400, 40), hitLabel, new GUIStyle(text) { alignment = TextAnchor.MiddleCenter }, Accent);
            if (battle.phase == MatchPhase.Intro && (entrance == null || !entrance.Active))
                Label(new Rect(250, 240, 780, 90), battle.phaseTicks > 45 ? "ROUND " + battle.round : "FIGHT", title);
            if ((battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver) && battle.UsesCommandGraphs && roundEndClock < KoCallSeconds)
            {
                // The round call first, over the slow-motion fall.
                string call = battle.result == "KNOCKOUT" ? "K.O." : battle.result == "DOUBLE KO" ? "DOUBLE K.O." : battle.result;
                float pop = Mathf.SmoothStep(1.35f, 1, Mathf.Clamp01(roundEndClock / .25f));
                var callStyle = new GUIStyle(title) { fontSize = Mathf.RoundToInt(120 * pop) };
                Label(new Rect(140, 210, 1000, 200), call, callStyle, Accent);
            }
            else if (battle.phase == MatchPhase.MatchOver && victory != null && !victory.Complete)
            {
                // During the winner's victory take only a lower-third banner, as in the original.
                Panel(new Rect(0, 560, 1280, 70), Ink);
                Label(new Rect(0, 565, 1280, 60), (battle.winner == 0 ? config.playerOne : config.playerTwo) + "  WINS", new GUIStyle(title) { fontSize = 40 }, Accent);
            }
            else if (battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver)
            {
                Panel(new Rect(260, 220, 760, 250), Ink);
                Label(new Rect(280, 235, 720, 80), battle.result == "KNOCKOUT" ? "K.O." : battle.result == "DOUBLE KO" ? "DOUBLE K.O." : battle.result, title, Accent);
                string result = battle.winner < 0 ? "DRAW - ROUND REPLAY" : "PLAYER " + (battle.winner + 1) + (battle.phase == MatchPhase.MatchOver ? " WINS THE MATCH" : " WINS THE ROUND");
                Label(new Rect(280, 315, 720, 45), result, new GUIStyle(text) { alignment = TextAnchor.MiddleCenter });
                if (battle.phase == MatchPhase.MatchOver && GUI.Button(new Rect(470, 390, 340, 52), "REMATCH   /   ENTER", button)) StartMatch();
            }
            Panel(new Rect(0, 650, 1280, 70), Ink);
            Label(new Rect(30, 657, 1200, 23), "P1  W A S D  /  U I J K = 1 2 3 4        P2  ARROWS  /  NUM 4 5 1 2 (or O P L ;) = 1 2 3 4        Hold back to guard", small);
            Label(new Rect(30, 682, 1200, 23), "F1 command list   /   F2 auto/manual   /   ESC pause       Original " + config.playerOne + " and " + config.playerTwo + " movesets.", small);
            if (help) DrawHelp();
            if (paused)
            {
                Panel(new Rect(0, 0, 1280, 720), new Color(0, 0, 0, .65f));
                Panel(new Rect(440, 180, 400, 340), Ink); Label(new Rect(440, 195, 400, 80), "PAUSED", title);
                if (GUI.Button(new Rect(490, 290, 300, 48), "RESUME", button)) paused = false;
                if (GUI.Button(new Rect(490, 350, 300, 48), "RESTART MATCH", button)) StartMatch();
                if (GUI.Button(new Rect(490, 410, 300, 48), "MAIN MENU", button)) { menu = true; paused = false; ClearInputs(); ResetImpactEffects(); }
            }
        }
        void DrawMenu()
        {
            Panel(new Rect(0, 0, 1280, 720), new Color(.015f, .025f, .045f, .72f));
            Label(new Rect(80, 100, 700, 35), "TEKKEN   /   UNITY REMASTER WORKSPACE", small, Accent);
            Label(new Rect(70, 150, 720, 100), "VERSUS", new GUIStyle(title) { fontSize = 94, alignment = TextAnchor.MiddleLeft });
            Label(new Rect(80, 270, 700, 60), "TWO PLAYERS. ONE ARENA.", text);
            Label(new Rect(80, 330, 700, 65), "Play as " + config.playerOne + " against Player 2 (" + config.playerTwo + ").\nOriginal movesets / local versus", small);
            if (GUI.Button(new Rect(80, 440, 400, 60), "LOCAL TWO-PLAYER   /   ENTER", button)) StartMatch();
            if (GUI.Button(new Rect(80, 515, 195, 45), "CONTROLS", button)) help = !help;
            if (GUI.Button(new Rect(285, 515, 195, 45), "QUIT", button)) Application.Quit();
            Label(new Rect(80, 642, 1100, 35), "KEYBOARD SHARING + TWO GAMEPADS    /    FIRST TO " + config.rules.roundsToWin + "    /    " + config.rules.seconds + "-SECOND ROUNDS", small);
            if (help) DrawHelp();
        }
        int helpPage;
        void DrawHelp()
        {
            const int perPage = 14;
            int pages = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(graphs[0].demos.Length, graphs[1].demos.Length) / (float)perPage));
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.PageDown) helpPage = (helpPage + 1) % pages;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.PageUp) helpPage = (helpPage + pages - 1) % pages;
            helpPage = Mathf.Clamp(helpPage, 0, pages - 1);
            Panel(new Rect(110, 95, 1060, 540), Ink);
            Label(new Rect(140, 105, 1000, 45), "COMMAND LIST", new GUIStyle(title) { fontSize = 34 });
            Label(new Rect(140, 150, 1000, 44), "Directions are relative to facing: f = toward the opponent, b = away (hold to guard), u/d = up/down.\n" +
                "P1: W A S D, U I J K = 1 2 3 4.   P2: arrows, number pad 4 5 1 2 (or O P L ;) = 1 2 3 4.   Pads: X Y A B = 1 2 3 4.", small);
            var line = new GUIStyle(small) { fontSize = 14 };
            for (int side = 0; side < 2; side++)
            {
                float x = 140 + side * 510;
                Label(new Rect(x, 200, 480, 24), "P" + (side + 1) + "  " + (side == 0 ? config.playerOne : config.playerTwo), text, Accent);
                var demos = graphs[side].demos;
                for (int k = 0; k < perPage; k++)
                {
                    int index = helpPage * perPage + k;
                    if (index >= demos.Length) break;
                    Label(new Rect(x, 232 + k * 24, 250, 22), demos[index].name, line);
                    Label(new Rect(x + 255, 232 + k * 24, 230, 22), demos[index].command.Replace("{U+E034}", ",n,"), line, Color.white);
                }
            }
            Label(new Rect(140, 572, 600, 22), "PAGE " + (helpPage + 1) + " / " + pages + "   (Page Up / Page Down)", small);
            if (GUI.Button(new Rect(860, 568, 280, 45), "CLOSE   /   F1", button)) help = false;
        }
    }
}
