using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    public partial class VersusGame : MonoBehaviour
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
        public MuseumCharacter alisaActions;
        public GameObject alisaPrefab;
        public MuseumCharacter katarinaActions;
        public GameObject katarinaPrefab;
        public MuseumCharacter lidiaActions;
        public GameObject lidiaPrefab;
        public MuseumCharacter bobActions;
        public GameObject bobPrefab;
        public MuseumCharacter leoActions;
        public GameObject leoPrefab;
        public enum Fighter { Kazuya, Jin, Xiaoyu, Alisa, Katarina, Lidia, Bob, Leo }
        [Tooltip("Fighters for this match. Each runs its original moveset, entrance and victory takes.")]
        public Fighter playerOneFighter = Fighter.Jin, playerTwoFighter = Fighter.Xiaoyu;
        // Show the character select before the match; off, the fighters above play directly.
        public bool characterSelect = true;
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
        readonly int[] previousCuts = new int[2];
        // Smoothed gameplay camera rig: fight-line angle, framing centre and distance.
        bool cameraInitialized;
        float cameraYaw, cameraDistance;
        Vector3 cameraTarget;
        // Critically damped spring velocities: motion starts and settles without jolts.
        float yawVelocity, distanceVelocity;
        // Bones that must stay in shot during the fight (head, hands, feet, hips) per fighter.
        Transform[][] framingBones;
        static readonly string[] FramingBoneNames = { "Head", "L_Hand", "R_Hand", "L_Foot", "R_Foot", "BASE" };
        // Horizontal room for reaching limbs; vertically the calibrated shot already runs close to the
        // feet, so only bodies that rise or drop beyond it (launches, juggles) pull the camera back.
        const float FramingPadX = .18f, FramingMarginX = .86f, FramingPadY = .05f, FramingMarginY = .95f;
        Vector3 targetVelocity;
        // Original slow-down with a held view (camera mode 7), started by a KO or a trade: camera frames since it
        // started (-1: none), its game speed (percent) and length, the held view's blend frames and FOV, and the view it
        // holds (the last shown view before it, without shake).
        float holdFrame = -1;
        int slowPercent = 100, slowFrames, holdIn, holdLength, holdOut;
        float holdFov;
        // Eased slow-down and close-up (the trade with smoothTrade, remaster tuning); the KO keeps the original's steps.
        bool holdSmooth;
        // The slow-down slows the whole game (Time.timeScale), as the original's game speed; off when the KO slows only
        // the winner in the model (BattleEffectTuning.koFreezeWinnerOnly).
        bool holdGlobalSlow = true;
        // Held view aimed at both fighters as they move (the trade with centreTrade, remaster tuning); the KO holds the
        // original's fixed view.
        bool holdCentred;
        // Rage Drive KO shot (camera mode 8): its random variant (-1: the held view instead) and its centre fighter.
        int koDriveVariant = -1, koCentre;
        Vector3 holdPosition, shownPosition;
        Quaternion holdRotation, shownRotation = Quaternion.identity;
        // Damage each fighter took from a clean hit this battle tick (-1: none): both struck on one tick is a trade.
        readonly int[] struckDamage = { -1, -1 };
        // Rage Drive active per fighter (0x801B, the "_EX" moves): a KO by one slows the game longer.
        readonly bool[] rageDrive = new bool[2];
        // Camera effects (shake, follow rate, KO camera) step once per 60 Hz frame of real time: the original
        // camera keeps updating through the KO's near-freeze. Fraction of the next camera frame elapsed.
        float cameraFrameClock;
        // Impact presentation: shakes, the follow rate, Rage Art cameras and the KO come from the original data
        // and code (Docs/BATTLE_CAMERA_EFFECTS.txt).
        string[] codes;
        // Original life gauges, timer, round counters and centre calls.
        BattleHud hud;
        readonly XiaoyuSecondaryDynamics[] flexibleMotion = new XiaoyuSecondaryDynamics[2];
        // Breasts of a female fighter without a wardrobe (the wardrobe's dynamics include them).
        readonly BustDynamics[] breastMotion = new BustDynamics[2];
        readonly WardrobeDynamics[] wardrobeMotion = new WardrobeDynamics[2];
        // Original fight-camera follow rate (0x84CC camera requests): each fighter's latest request and the
        // camera's current per-frame rate, which steps toward the rate the requests select.
        readonly int[] cameraRequest = new int[2];
        float followRate = -1;
        // Original camera shake: the active preset (0 = none) and its battle frame; a new shake replaces it.
        int shakePreset, shakeFrame;
        bool shakeStarted, shakeFromHit;
        // Real seconds since the round ended: the K.O. call shows first, then the result panel;
        // the match winner's victory take starts once the loser's death fall has played.
        float roundEndClock;
        const int ReadyTick = 68, FightTick = ReadyTick + 40;
        // KO_mc, WKO_mc and TimeUp_mc are 120-frame clips (2 s at 60 fps), shown whole as in the original.
        // The victory take starts once the loser's death fall has played: this long at full speed after the KO's
        // near-freeze ends.
        const float KoCallSeconds = 2f, VictoryFallSeconds = 1.2f;
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
        int hitsResolved, trades;
        float hitLabelTime, visualTime;
        AudioSource audioSource;
        AudioClip strikeSound, guardSound, armourSound;
        // Original battle sounds (Resources/BattleSound); the tones above only stand in when they are missing.
        BattleSound sound;
        // The round call last announced (call, round), so each call speaks once; the shot the entrance last voiced.
        string announced;
        LocalVersusEntrance.Shot voicedShot = LocalVersusEntrance.Shot.Complete;
        // The winner's name and "wins" follow the K.O. (or TIME UP) call once it has finished, as in the original.
        float winnerCallAt = -1;
        Material impactMaterial, blockMaterial;
        GUIStyle text, title, small, button;
        readonly List<GameObject> hitEffects = new List<GameObject>();
        // Every tunable choice on top of the original effect data (common effects on/off, hit shake, follow-rate
        // influence, camera smoothing, trade and KO timing) lives in the shared Resources/BattleEffectTuning.asset,
        // read live by every battle and tuned in the BattleEffectTuning scene (user request 2026-10-28). The common
        // effects (BP_CommonEffectManager roles) play alongside each fighter's own wired effects
        // (CharacterBattleEffects, original property 0x802E), which always play; the switch is followed live in Play
        // (SyncCommonEffects). "Original" means the inspected executable's behaviour.
        static BattleEffectTuning Tuning => BattleEffectTuning.Current;
        [Tooltip("Show the battle effect tuning panel (F6 toggles it): sliders for Resources/BattleEffectTuning.asset and buttons that play each effect on demand.")]
        public bool effectTuningPanel;
        [Tooltip("Fighters stand idle and only act when the tuning panel's effect buttons direct them (the BattleEffectTuning scene). Off: the CPU fights as usual.")]
        public bool effectDemosOnly;
        // Directs both fighters through one exchange per tuning-panel button (BattleEffectDemo).
        BattleEffectDemo effectDemo;
        CascadeEffects effects;
        readonly CharacterBattleEffects[] characterEffects = new CharacterBattleEffects[2];
        readonly Queue<ActedMovePropertyEvent> pendingCharacterEffects = new Queue<ActedMovePropertyEvent>();
        // Striking points (hands, elbows, knees, feet, head), the struck body line (hip to head) and the
        // bones Rage effects gather around, per fighter.
        Transform[][] strikeBones, bodyBones, auraBones;
        static readonly string[] StrikeBoneNames = { "L_Hand", "R_Hand", "L_ForeArm", "R_ForeArm", "L_Leg", "R_Leg", "L_Foot", "R_Foot", "Head" };
        static readonly string[] BodyBoneNames = { "Hip", "Spine2", "Head" };
        static readonly string[] AuraBoneNames = { "Hip", "Spine1", "Spine2", "Head", "L_Arm", "R_Arm", "L_ForeArm", "R_ForeArm",
            "L_Hand", "R_Hand", "L_UpLeg", "R_UpLeg", "L_Leg", "R_Leg", "L_Foot", "R_Foot" };
        // Not recovered: the original picks PierceFX natively; heavy clean hits use it here.
        readonly bool[] wasRage = new bool[2];
        readonly CascadeInstance[] rageAura = new CascadeInstance[2], rageStart = new CascadeInstance[2];
        // The Rage aura the original actually shows: a red rim glow on the fighter (see RageRim.shader).
        readonly RageRim[] rageRims = new RageRim[2];
        GameObject[] fighterObjects;
        // The last blow's contact, for the knockout flash (BP_EF_KO plays it where the KO hit landed).
        static readonly Color Ink = new Color(.025f, .035f, .055f, .96f);
        static readonly Color Muted = new Color(.58f, .66f, .75f);
        static readonly Color Accent = new Color(.95f, .78f, .35f);
        void InitializeMatch()
        {
            diagnostic = !ArcadeActive && (Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0 ||
                         Array.IndexOf(Environment.GetCommandLineArgs(), "--capture-preview") >= 0);
            if (!ArcadeActive && Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0) automaticFight = true;
            // The original character select comes first; it reloads the scene with both picks.
            if (characterSelect && !diagnostic && !CharacterSelect.Chosen)
            {
                enabled = false;
                gameObject.AddComponent<CharacterSelect>();
                return;
            }
            if (!ArcadeActive && CharacterSelect.Chosen) { playerOneFighter = CharacterSelect.PlayerOne; playerTwoFighter = CharacterSelect.PlayerTwo; }
            if (!config) config = ScriptableObject.CreateInstance<RemasterConfig>();
            var setups = new[] { Setup(playerOneFighter), Setup(playerTwoFighter) };
            codes = new[] { setups[0].code, setups[1].code };
            hud = new BattleHud(codes[0], codes[1]);
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
            if (ArcadeActive) { rules = arcadeRequest.Setup.BuildRules(rules, TekkenHealth); config.rules = rules; }
            battle = new BattleModel(rules, graphs); battle.Hit += OnHit; battle.MoveProperty += OnMoveProperty;
            battle.ActedMoveProperty += OnActedMoveProperty;
            for (int i = 0; i < 2; i++) cpu[i] = new CpuPlayer(graphs[i], 31 + i * 17, ArcadeActive ? arcadeRequest.Setup.CpuLevel(i) : 0);
            if (ArcadeActive) arcadeController = new Arcade.ArcadeMatchController(arcadeRequest.Setup, arcadeRequest.Input, cpu, IdleGetupTicks);
            effectDemo = new BattleEffectDemo(battle, graphs, cpu);
            battle.immediateKoCollapse = Tuning.koCollapseAtOnce;
            Time.fixedDeltaTime = 1f / 60;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            BuildArena();
            // Both fighters wear random items saved in the wardrobe scene, not their preset costumes.
            var outfitRandom = new System.Random();
            var objects = fighterObjects = new GameObject[2];
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
                if (!wardrobe) breastMotion[i] = BustDynamics.Create(objects[i].transform, setups[i].actions, BustDynamics.DefaultFlexibility);
                objects[i].name = setups[i].actions.displayName + " - Player " + (i + 1);
            }
            framingBones = new Transform[2][];
            for (int i = 0; i < 2; i++)
                framingBones[i] = Array.FindAll(objects[i].GetComponentsInChildren<Transform>(),
                    t => Array.Exists(FramingBoneNames, n => string.Equals(n, t.name, StringComparison.OrdinalIgnoreCase)));
            effects = Tuning.commonBattleEffects ? CascadeEffects.Create(transform) : null;
            for (int i = 0; i < 2; i++) characterEffects[i] = CharacterBattleEffects.Create(transform, codes[i], objects[i].transform);
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
            strikeSound = Tone("Strike", 110, .1f); guardSound = Tone("Guard", 420, .06f); armourSound = Tone("Armour", 230, .14f);
            sound = BattleSound.Create(transform);
            // The stage's own music (generated arena: remaster choice in BattleMusic), Normal until the final round.
            BattleMusic.Instance.PlayStage(StageName);
            impactMaterial = FighterView.Surface(new Color(1, .7f, .25f));
            blockMaterial = FighterView.Surface(Color.cyan);
            if (!ArcadeActive && Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0)
            { menu = false; float introTime = entrance != null ? entrance.DurationSeconds : 0;
                Invoke(nameof(SmokeCapture), introTime + 8); Invoke(nameof(SmokeExit), introTime + 10); }
            if (!ArcadeActive && Array.IndexOf(Environment.GetCommandLineArgs(), "--capture-preview") >= 0)
            { menu = false; Invoke(nameof(PreviewCapture), 4); }
            VersusLoadingScreen.MatchReady();
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
            if (!jinActions) jinActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Jin.asset");
            if (!kazuyaActions) kazuyaActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Kazuya.asset");
            if (!xiaoyuActions) xiaoyuActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Xiaoyu.asset");
            if (!katarinaActions) katarinaActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Katarina.asset");
            if (!lidiaActions) lidiaActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Lidia.asset");
            if (!bobActions) bobActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Bob.asset");
            if (!leoActions) leoActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Leo.asset");
            if (!alisaActions) alisaActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/_Project/Museum/Alisa.asset");
#endif
            var setup = fighter == Fighter.Jin ? new FighterSetup { code = "jin", prefix = "Jz_", actions = jinActions } :
                        fighter == Fighter.Kazuya ? new FighterSetup { code = "kaz", prefix = "Kz_", actions = kazuyaActions } :
                        fighter == Fighter.Alisa ? new FighterSetup { code = "asa", prefix = "Aa_", actions = alisaActions } :
                        fighter == Fighter.Katarina ? new FighterSetup { code = "ltn", prefix = "Ln_", actions = katarinaActions } :
                        fighter == Fighter.Lidia ? new FighterSetup { code = "nsd", prefix = "Nd_", actions = lidiaActions } :
                        fighter == Fighter.Bob ? new FighterSetup { code = "bob", prefix = "Ft_", actions = bobActions } :
                        fighter == Fighter.Leo ? new FighterSetup { code = "leo", prefix = "Un_", actions = leoActions } :
                        new FighterSetup { code = "xia", prefix = "Fu_", actions = xiaoyuActions };
            setup.fighter = fighter;
            if (!setup.actions) throw new InvalidOperationException("Assign the " + fighter + " move catalog in LocalVersus.");
            return setup;
        }

        // Scene-placed fighters are used once; a second copy (mirror match), Xiaoyu, Alisa or Katarina comes from the prefab.
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
                if (!kazuyaPrefab) kazuyaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Kazuya/Costumes/Prefabs/Karate_Gi.prefab");
#endif
                if (!kazuyaPrefab) throw new InvalidOperationException("Assign the Kazuya Karate Gi prefab in LocalVersus.");
                return Instantiate(kazuyaPrefab, transform);
            }
            if (setup.fighter == Fighter.Bob)
            {
#if UNITY_EDITOR
                if (!bobPrefab) bobPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Bob/Costumes/Prefabs/Default_1P.prefab");
#endif
                if (!bobPrefab) throw new InvalidOperationException("Assign the Bob Default 1P prefab in LocalVersus.");
                return Instantiate(bobPrefab, transform);
            }
            if (setup.fighter == Fighter.Leo)
            {
#if UNITY_EDITOR
                if (!leoPrefab) leoPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Leo/Costumes/Prefabs/Default_1P.prefab");
#endif
                if (!leoPrefab) throw new InvalidOperationException("Assign the Leo Default 1P prefab in LocalVersus.");
                return Instantiate(leoPrefab, transform);
            }
            if (setup.fighter == Fighter.Lidia)
            {
#if UNITY_EDITOR
                if (!lidiaPrefab) lidiaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Lidia/Costumes/Prefabs/Default_1P.prefab");
#endif
                if (!lidiaPrefab) throw new InvalidOperationException("Assign the Lidia Default 1P prefab in LocalVersus.");
                return Instantiate(lidiaPrefab, transform);
            }
            if (setup.fighter == Fighter.Katarina)
            {
#if UNITY_EDITOR
                if (!katarinaPrefab) katarinaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Katarina/Costumes/Prefabs/Default_1P.prefab");
#endif
                if (!katarinaPrefab) throw new InvalidOperationException("Assign the Katarina Default 1P prefab in LocalVersus.");
                return Instantiate(katarinaPrefab, transform);
            }
            if (setup.fighter == Fighter.Alisa)
            {
#if UNITY_EDITOR
                if (!alisaPrefab) alisaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Alisa/Costumes/Prefabs/Default_1P.prefab");
#endif
                if (!alisaPrefab) throw new InvalidOperationException("Assign the Alisa Default 1P prefab in LocalVersus.");
                return Instantiate(alisaPrefab, transform);
            }
#if UNITY_EDITOR
            if (!xiaoyuPrefab) xiaoyuPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Characters/Xiaoyu/Costumes/Prefabs/Default_1P.prefab");
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
                ",\"hitsResolved\":" + hitsResolved + ",\"trades\":" + trades +
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
        // The environment saved in env_scene (EnvironmentSelection.asset): an original stage, or this arena.
        public string StageName { get; private set; } = EnvironmentSelection.Arena;
        GameObject stageModel;
        void BuildArena()
        {
            StageName = ArcadeActive && !string.IsNullOrEmpty(arcadeRequest.Setup.Stage) ? arcadeRequest.Setup.Stage :
                EnvironmentSelection.Load().PickForMatch(new System.Random());
            if (StageName != EnvironmentSelection.Arena)
            {
                try { stageModel = StageEnvironment.Spawn(StageName, transform); }
                catch (Exception e) { Debug.LogException(e); StageName = EnvironmentSelection.Arena; }
            }
            if (!stageModel) BuildGeneratedArena();
            BuildCameraAndLights();
        }
        void BuildGeneratedArena()
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
        }
        void BuildCameraAndLights()
        {
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
            cameraRig.nearClipPlane = .1f; cameraRig.farClipPlane = stageModel ? 2000 : 100;
            if (!cameraRig.GetComponent<AudioListener>()) cameraRig.gameObject.AddComponent<AudioListener>();
        }
        void Update()
        {
            if (arcadeController != null && arcadeController.Suspended) return;
            // Nothing starts behind the VS screen; LateUpdate still poses the fighters for the first shot.
            if (VersusLoadingScreen.Holding) return;
            float realDelta = Time.unscaledDeltaTime;
            if (battle != null && (battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver) && !paused)
                roundEndClock += realDelta;
            if (!paused && !menu && battle != null) AdvanceCameraFrames(realDelta);
            if (victory != null && !paused) victory.Advance(Time.unscaledDeltaTime);
            if (entrance != null && entrance.Active)
            {
                visualTime += Time.unscaledDeltaTime;
                entrance.Advance(Time.unscaledDeltaTime, !ArcadeActive && !diagnostic && Input.GetKeyDown(KeyCode.Return));
                VoiceEntrance();
                return;
            }
            UpdateWinnerCall(realDelta);
            if (ArcadeActive) { visualTime += Time.deltaTime; hitLabelTime -= Time.deltaTime; return; }
            if (Input.GetKeyDown(KeyCode.F1)) help = !help;
            if (Input.GetKeyDown(KeyCode.F2)) { automaticFight = !automaticFight; ClearInputs(); }
            if (automaticFight && Input.GetKeyDown(KeyCode.F3)) TriggerRageArt(0);
            if (automaticFight && Input.GetKeyDown(KeyCode.F4)) TriggerRageArt(1);
            if (Input.GetKeyDown(KeyCode.F5)) ForceTradeEffect();
            if (Input.GetKeyDown(KeyCode.F6)) effectTuningPanel = !effectTuningPanel;
            if (Input.GetKeyDown(KeyCode.Escape) && !menu) { paused = !paused; ClearInputs(); }
            if (Input.GetKeyDown(KeyCode.Return))
            { if (menu || battle.phase == MatchPhase.MatchOver) StartMatch(); else if (paused) paused = false; }
            if (menu || paused) return;
            if (automaticFight) { visualTime += Time.deltaTime; hitLabelTime -= Time.deltaTime; return; }
            if (!diagnostic)
                for (int i = 0; i < 2; i++) pressedSinceTick[i] |= PressedButtons(i);
            visualTime += Time.deltaTime;
            hitLabelTime -= Time.deltaTime;
        }
        void FixedUpdate()
        {
            if (arcadeController != null && arcadeController.Suspended) return;
            if (VersusLoadingScreen.Holding) return;
            if (entrance != null && entrance.Active) return;
            if (menu || paused) return;
            if (ArcadeActive) { TickArcadeMatch(); return; }
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
                ReleaseRageGrants();
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
            // Tuning demonstrations direct both fighters; with effectDemosOnly they otherwise stand idle.
            if (effectDemo != null && (effectDemo.Active || effectDemosOnly))
                return effectDemo.Command(player, battle.fighters[player], battle.fighters[1 - player]);
            return cpu[player].Next(battle.fighters[player], battle.fighters[1 - player]);
        }

        // Rage Art button (auto fight): the CPU walks in and replays its original Rage Art input. The
        // battle model needs Rage for it, so the button grants Rage until that Rage Art is over (a
        // demonstration aid; the model never spends Rage). Alisa's Rage Art has no recovered camera.
        readonly RageArtTrigger[] rageArts = { new RageArtTrigger(), new RageArtTrigger() };
        void TriggerRageArt(int player) => rageArts[player].Press(battle, player, cpu[player]);
        // TRADE EFFECT button (F5, a demonstration aid in either mode): plays the original trade's slow-down, shake and
        // held view at once, as if both fighters had just struck each other; the fight itself is not changed.
        bool CanForceTrade => battle != null && battle.phase == MatchPhase.Fight && !paused && !menu && (entrance == null || !entrance.Active);
        void ForceTradeEffect()
        {
            if (CanForceTrade) StartTrade();
        }
        // The trade's slow-down and held view at the original's settings, as the tuning stretches, eases and re-frames
        // them (BattleEffectTuning: tradeLength, smoothTrade, centreTrade, tradeSlowPercent, tradeFov).
        void StartTrade()
        {
            var trade = BattleCameraEffects.Trade; var t = Tuning; float k = Mathf.Max(.1f, t.tradeLength);
            int Frames(float n) => Mathf.Max(1, Mathf.RoundToInt(n * k));
            StartHold(t.tradeSlowPercent, Frames(trade.slowFrames), trade.shakePreset,
                Frames(trade.blendInFrames * (t.smoothTrade ? 2 : 1)), Frames(trade.holdFrames), Frames(trade.blendOutFrames),
                t.tradeFov, t.smoothTrade);
            holdCentred = t.centreTrade;
        }
        // The KO's near-freeze and held view (or the Rage Drive shot) at the original's settings, as the tuning
        // stretches, eases and re-frames them (koSlowPercent, koLength, koFov, smoothKo, centreKo, rageDriveKoShot).
        void StartKo(bool drive)
        {
            var ko = BattleCameraEffects.Ko; var t = Tuning; float k = Mathf.Max(.1f, t.koLength);
            int Frames(float n) => Mathf.Max(1, Mathf.RoundToInt(n * k));
            StartHold(t.koSlowPercent, Frames(drive ? ko.rageDriveSlowFrames : ko.slowFrames), ko.shakePreset,
                Frames(ko.blendInFrames * (t.smoothKo ? 2 : 1)), Frames(ko.holdFrames), Frames(ko.blendOutFrames), t.koFov, t.smoothKo);
            holdCentred = t.centreKo;
            if (t.koFreezeWinnerOnly && battle.winner >= 0)
            {
                // Remaster choice (user request 2026-10-28): only the winner is held, so the loser's KO action plays at once.
                holdGlobalSlow = false; Time.timeScale = 1;
                battle.SlowFighter(battle.winner, slowFrames, t.koSlowPercent / 100f);
            }
            koDriveVariant = drive && t.rageDriveKoShot ? UnityEngine.Random.Range(0, 2) : -1;
            int first = battle.phase == MatchPhase.MatchOver && battle.winner >= 0 ? battle.winner : 0;
            koCentre = koDriveVariant == 1 ? 1 - first : first;
        }
        void ReleaseRageGrants() { for (int i = 0; i < 2; i++) rageArts[i].Tick(battle, i, cpu[i]); }
        void LateUpdate()
        {
            // Re-applied so color edits on the Render Settings object show live during Play.
            if (renderSettings && cameraRig) renderSettings.Apply(cameraRig, null);
            if (views == null) return;
            if (battle.phase == MatchPhase.MatchOver && battle.winner >= 0 && victory == null &&
                (!battle.UsesCommandGraphs || holdFrame < 0 && roundEndClock >= slowFrames / 60f + VictoryFallSeconds))
            {
                victory = victoryStaging[battle.winner];
                // Only takes the winner's own moveset reaches (paired "_y" partner takes are never
                // played by the winner); a button held at the end picks its take, as in the original.
                var winnerGraph = graphs[battle.winner];
                var reachable = VictoryTargets(winnerGraph);
                int held = ArcadeActive ? arcadeController.VictoryButtons(battle.winner) : automaticFight || diagnostic ? 0 : ReadInput(battle.winner).buttons;
                victory.Begin(e => reachable.ContainsKey(e.moveId), PreferredVictory(reachable, held, codes[battle.winner]));
                SayTake(battle.winner, victory.Animation);
                victoryPlayer = battle.winner;
                victoryAnchor = battle.fighters[victoryPlayer].Snapshot();
                var loser = battle.fighters[1 - victoryPlayer];
                float dx = loser.x - victoryAnchor.x, dz = loser.z - victoryAnchor.z, length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length > 1e-3f) { victoryAnchor.dirX = dx / length; victoryAnchor.dirZ = dz / length; }
                victoryAnchor.turn = 0; victoryAnchor.backturned = false;
                FitVictoryAnchor();
            }
            if (victory != null) victory.Prepare();
            if (entrance != null && entrance.Active) entrance.PrepareMotion();
            var shown = new FighterState[2];
            for (int i = 0; i < 2; i++)
            {
                shown[i] = victory != null && i == victoryPlayer && victoryAnchor != null ? victoryAnchor : Interpolated(i);
                views[i].Render(shown[i], config.rules, visualTime,
                    !menu && !paused && battle.phase == MatchPhase.Fight && commands[i].stick != 5);
                if (victory != null && i == victoryPlayer) KeepOnFloor(i);
                if (!paused) { flexibleMotion[i]?.Tick(Time.deltaTime); wardrobeMotion[i]?.Tick(Time.deltaTime); breastMotion[i]?.Tick(Time.deltaTime); }
            }
            // Pose each actual actor first, then resolve its verified native effect bone. The queue
            // retains every source tick when several fixed steps precede a rendered frame.
            while (pendingCharacterEffects.Count > 0)
            {
                var property = pendingCharacterEffects.Dequeue();
                characterEffects[property.player]?.TryPlayProperty(property.id, property.value);
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
            // The original fight camera never zooms on its own: 0x84CC only sets the follow rate below, and the KO
            // close-up is the KO camera held over this view at the end.
            if (!cameraInitialized)
            {
                cameraYaw = goalYaw; cameraDistance = goalDistance; cameraTarget = goalTarget; cameraInitialized = true;
                yawVelocity = distanceVelocity = 0; targetVelocity = Vector3.zero;
            }
            else if (dt > 0)
            {
                // The fight-line angle turns slowest (walk sway never shows), the framing centre
                // follows a little faster, and the distance eases between the two. These times are the remaster's
                // tuning at the original's base rate; the original's follow rate (0x84CC requests, e.g. Kazuya's
                // Rider x3, Jin's shoryu x2, rolls x1.5) scales them all.
                var t = Tuning;
                float ratio = followRate > 0 ? followRate / BattleCameraEffects.BaseFollowRate : 1;
                float lag = 1 / Mathf.Max(.05f, 1 + (ratio - 1) * t.followRateInfluence);
                cameraYaw = Mathf.SmoothDampAngle(cameraYaw, goalYaw, ref yawVelocity, t.yawSmoothing * lag, 240, dt);
                cameraDistance = Mathf.SmoothDamp(cameraDistance, goalDistance, ref distanceVelocity, t.distanceSmoothing * lag, Mathf.Infinity, dt);
                cameraTarget = Vector3.SmoothDamp(cameraTarget, goalTarget, ref targetVelocity, t.targetSmoothing * lag, Mathf.Infinity, dt);
            }
            var orbit = Quaternion.Euler(0, cameraYaw, 0);
            var position = cameraTarget + orbit * Vector3.forward * cameraDistance + Vector3.up * goalOffset.y;
            if (battle.phase == MatchPhase.Fight && holdFrame < 0 && cinematic == null)
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
            var rotation = Quaternion.LookRotation(cameraTarget - position);
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
            // Original camera shake: only the move properties 0x8001-4 / 0x8006-9 start it (landing, bound,
            // wall and Rage Art finish moves, not ordinary hits), and it nods the view's pitch by the preset's
            // per-frame table, one entry per camera frame. It applies to the Rage Art camera as well.
            shownPosition = position; shownRotation = rotation;
            rotation = BattleCameraEffects.Pitched(rotation, BattleCameraEffects.OriginalShakePitch(shakePreset, shakeFrame) * Tuning.shakeStrength);
            if (holdFrame >= 0 && koDriveVariant >= 0)
            {
                // Original Rage Drive KO shot (mode 8): it replaces the live view (and its shake) for its frames, then cuts back.
                if (holdFrame < BattleCameraEffects.Ko.rageDrive.frames)
                {
                    var centre = shown[koCentre]; var opponent = shown[1 - koCentre];
                    BattleCameraEffects.RageDriveKoShot(koDriveVariant, holdFrame + cameraFrameClock,
                        new Vector3(centre.x, 0, centre.z), new Vector3(opponent.x, 0, opponent.z), shownPosition, cameraTarget,
                        shownRotation * Vector3.right, out position, out rotation, out float shotFov);
                    gameplayLens.SetHorizontalFov(shotFov);
                }
            }
            else if (holdFrame >= 0)
            {
                // Original held view (mode 7, KO or trade): the view shown when it started with its own FOV, weighted
                // over the live view (which carries the shake) by its blend-in, hold and blend-out frames.
                float weight = BattleCameraEffects.HoldWeight(holdFrame + cameraFrameClock, holdIn, holdLength, holdOut);
                if (holdSmooth) weight = Mathf.SmoothStep(0, 1, weight);
                var heldRotation = holdRotation; float heldFov = holdFov;
                if (holdCentred) CentreOnFighters(holdPosition, ref heldRotation, ref heldFov);
                position = Vector3.Lerp(position, holdPosition, weight);
                rotation = Quaternion.Slerp(rotation, heldRotation, weight);
                gameplayLens.SetHorizontalFov(Mathf.Lerp(fov, heldFov, weight));
            }
            cameraRig.transform.SetPositionAndRotation(position, rotation);
        }

        // Remaster tuning for the trade close-up: from a held position, aim at the centre of both fighters' framing bones
        // (head, hands, feet, hips) and widen the horizontal FOV past the held one only as far as they need to fit.
        void CentreOnFighters(Vector3 from, ref Quaternion look, ref float horizontalFov)
        {
            if (framingBones == null) return;
            var box = new Bounds(); bool any = false;
            foreach (var bones in framingBones)
                foreach (var bone in bones)
                {
                    if (!bone) continue;
                    if (any) box.Encapsulate(bone.position); else { box = new Bounds(bone.position, Vector3.zero); any = true; }
                }
            if (!any || (box.center - from).sqrMagnitude < 1e-4f) return;
            look = Quaternion.LookRotation(box.center - from);
            var inverse = Quaternion.Inverse(look);
            float needX = 0, needY = 0;
            foreach (var bones in framingBones)
                foreach (var bone in bones)
                {
                    if (!bone) continue;
                    var local = inverse * (bone.position - from);
                    if (local.z < .1f) continue;
                    needX = Mathf.Max(needX, (Mathf.Abs(local.x) + FramingPadX) / local.z / FramingMarginX);
                    needY = Mathf.Max(needY, (Mathf.Abs(local.y) + FramingPadY) / local.z / FramingMarginY);
                }
            float tanHalf = Mathf.Max(needX, needY * cameraRig.aspect);
            horizontalFov = Mathf.Clamp(Mathf.Max(horizontalFov, 2 * Mathf.Atan(tanHalf) * Mathf.Rad2Deg), 1, 100);
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
                previousPosition[i] = new Vector3(f.x, 0, f.z); previousFacing[i] = new Vector3(f.dirX, 0, f.dirZ); previousCuts[i] = f.headingCuts;
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
            // A heading cut this tick (a body on the floor turned to continue its pose) is not swept.
            var d = f.headingCuts != previousCuts[i] ? new Vector3(f.dirX, 0, f.dirZ) : Vector3.Slerp(previousFacing[i], new Vector3(f.dirX, 0, f.dirZ), alpha);
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
        void OnActedMoveProperty(ActedMovePropertyEvent property)
        {
            // Recovered native visual-pass case 0x802E: effect ID 0 on the specified own bone.
            // All observed rows (Jin 143, Katarina 123) use unflagged source frames. Other property flag semantics
            // and manager IDs are not inferred here. Borrowed graph provenance remains on the event.
            if (property.id == 0x802E && property.rawFrame == property.frame && characterEffects[property.player])
                pendingCharacterEffects.Enqueue(property);
        }

        void OnMoveProperty(int player, long id, long value)
        {
            if (BattleCameraEffects.TryShakePreset(id, value, out int preset))
            {
                // As the original setter: the new preset replaces any running shake and restarts at frame 0
                // (preset 0, from 0x8005 / 0x800A, stops it).
                shakePreset = preset; shakeFrame = 0; shakeStarted = preset > 0; shakeFromHit = false;
                return;
            }
            if (BattleCameraEffects.TryCameraRequest(id, value, out int request))
            {
                // The fighter's camera request (walks/steps 18, rolls 13, air reactions 0, ...): it stays until the
                // next one. Not wired: the original keeps a throw escape's 0x8FD while fighter +0x314 is set.
                cameraRequest[player] = request;
                return;
            }
            if (PlayMoveSound(player, id, value)) return;
            // Rage Drive (0x801B): set on the move's first frame, cleared near its end, as fighter +0xC02.
            if (id == BattleCameraEffects.Ko.rageDriveProperty) rageDrive[player] = value != 0;
            switch (id)
            {
                case 0x8439: case 0x843A:                      // cinematic camera, left/right screen side
                    // 0x4000|i: track i in <chr>rotsm.arc; plain i (DLC, e.g. Lidia): in the moveset's own bank.
                    if (((value & 0xC000) == 0x4000 || (value & 0xC000) == 0) && (id == 0x8439) == (battle.fighters[player].facing > 0))
                    {
                        StartCinematic(player, ((value & 0xC000) == 0 ? "m" : "") + (int)(value & 0x3FFF));
                        sound?.Play("SE_RageArts_Start");
                    }
                    break;
            }
        }


        void StartCinematic(int player, string track)
        {
            try { cinematic = new FightCameraTrack(codes[player] + "_" + track); }
            catch (Exception e) { Debug.LogWarning(e.Message); cinematic = null; return; }
            var f = battle.fighters[player];
            cinematicPlayer = player; cinematicFrame = 0;
            cinematicAnchor = new Vector3(f.x, 0, f.z); cinematicForward = new Vector3(f.dirX, 0, f.dirZ);
        }

        void StopCinematic() { cinematic = null; cinematicPlayer = -1; }

        // One step per 60 Hz frame of real time, as the original camera, so the KO camera, shakes and follow rate keep
        // their timing while the KO slows the fight to a near freeze.
        void AdvanceCameraFrames(float realDelta)
        {
            cameraFrameClock += realDelta * 60;
            for (int step = 0; cameraFrameClock >= 1 && step < 8; step++)
            {
                cameraFrameClock -= 1;
                // A shake started since the last frame shows its frame 0 first, as the original evaluates before advancing.
                if (shakePreset > 0 && !shakeStarted && ++shakeFrame >= BattleCameraEffects.ShakeFrames(shakePreset)) { shakePreset = 0; shakeFrame = 0; }
                shakeStarted = false;
                // The original steps its follow rate once per camera frame toward what the requests select.
                if (followRate < 0) followRate = BattleCameraEffects.BaseFollowRate;
                followRate = BattleCameraEffects.StepFollowRate(followRate, BattleCameraEffects.TargetFollowRate(cameraRequest[0], cameraRequest[1]));
                if (holdFrame < 0) continue;
                // The slow-down's game speed holds for its frames, then the fight runs at full speed again.
                holdFrame++;
                if (holdFrame >= Mathf.Max(slowFrames + (holdSmooth ? holdOut : 0), holdIn + holdLength + holdOut)) holdFrame = -1;
            }
            cameraFrameClock = Mathf.Min(cameraFrameClock, 1);
            if (holdFrame >= 0) Time.timeScale = holdGlobalSlow ? HoldSpeed(holdFrame + cameraFrameClock) : 1;
            else if (Time.timeScale < 1 && slowFrames > 0) Time.timeScale = 1;
        }

        // Game speed during a slow-down: the original switches to its speed at once and back after its frames; eased
        // (remaster tuning) it glides in over the close-up's blend-in and back out over its blend-out.
        float HoldSpeed(float frame)
        {
            float slow = slowPercent / 100f;
            if (!holdSmooth) return frame < slowFrames ? slow : 1;
            if (frame < holdIn) return Mathf.Lerp(1, slow, Mathf.SmoothStep(0, 1, frame / holdIn));
            if (frame < slowFrames) return slow;
            return Mathf.Lerp(slow, 1, Mathf.SmoothStep(0, 1, (frame - slowFrames) / holdOut));
        }

        void AfterTick()
        {
            battle.immediateKoCollapse = Tuning.koCollapseAtOnce;
            if (cinematic != null)
            {
                cinematicFrame++;
                // The camera belongs to the attacker's action: it ends with the track, the round,
                // or when the attacker is hit out of it.
                if (cinematicFrame >= cinematic.Frames - 1 || battle.phase != MatchPhase.Fight || battle.fighters[cinematicPlayer].reacting)
                    StopCinematic();
            }
            if (lastPhase == MatchPhase.Fight && battle.phase != MatchPhase.Fight) roundEndClock = 0;
            // The original trade (hit resolution 0x140713DD0): both fighters struck on the same tick, either for at least
            // the threshold, slows the game to 10% for 24 frames, shakes (preset 5) and holds the view (FOV 35: 4 frames
            // in, 20 held, 30 out). A KO on that tick replaces it, as in the original. Not wired: the original also
            // cancels it when a fighter is in a throw and reads a struck flag (+0x754 & 0x20) taken here as a clean hit.
            if (battle.phase == MatchPhase.Fight && struckDamage[0] > 0 && struckDamage[1] > 0 &&
                Mathf.Max(struckDamage[0], struckDamage[1]) >= Tuning.tradeDamageThreshold)
            {
                StartTrade();
                trades++;
            }
            struckDamage[0] = struckDamage[1] = -1;
            if (lastPhase == MatchPhase.Fight && battle.phase != MatchPhase.Fight && battle.result != "TIME UP")
            {
                // The original KO (fight state 0x1406DE720), the same on every round: game speed 1% (a near freeze) for
                // 90 camera frames, 150 when the winner's Rage Drive is active; shake preset 6; and the KO camera, the
                // view shown at the KO narrowed to 35 degrees: 5 frames in, 90 held, 20 back to the live camera. A Rage
                // Drive KO gets the 150-frame mode 8 shot instead, one of two variants at random, centred on P1 (on the
                // match-deciding KO, the match winner) or, in the second variant, that fighter's opponent.
                // No extra KO flash: nine KOs in the reference match (TEKKEN 7 2026-10-23 08-04-08.mp4, 62-456 s) show
                // only the blow's own hit/counter effect, never PS_EF_SpecialKO's radial lines, floor ring or dust.
                StartKo(battle.winner >= 0 && rageDrive[battle.winner]);
            }
            lastPhase = battle.phase;
            effectDemo?.Tick();
            UpdateBattleEffects();
        }

        // Starts an original slow-down with a held view (slow table entry 2 and camera mode 7, as the KO and the trade set
        // them; a new one replaces the running one) and its shake.
        void StartHold(int speedPercent, int frames, int shake, int blendIn, int hold, int blendOut, float fov, bool smooth = false)
        {
            slowPercent = speedPercent; slowFrames = frames; holdIn = blendIn; holdLength = hold; holdOut = blendOut; holdFov = fov;
            holdSmooth = smooth; holdCentred = false; holdGlobalSlow = true;
            holdFrame = 0; cameraFrameClock = 0; holdPosition = shownPosition; holdRotation = shownRotation; koDriveVariant = -1;
            Time.timeScale = HoldSpeed(0);
            shakePreset = shake; shakeFrame = 0; shakeStarted = true; shakeFromHit = false;
        }

        void ResetImpactEffects()
        {
            pendingCharacterEffects.Clear();
            for (int i = 0; i < characterEffects.Length; i++) characterEffects[i]?.Clear();
            for (int i = 0; i < 2; i++) rageArts[i].Release(battle, i);
            // Finite common effects (hit sparks, KO flash, floor dust) must not carry into the next round.
            if (effects) effects.Clear();
            shakePreset = shakeFrame = 0; holdFrame = -1; slowFrames = 0; koDriveVariant = -1; struckDamage[0] = struckDamage[1] = -1; rageDrive[0] = rageDrive[1] = false;
            cameraRequest[0] = cameraRequest[1] = 0; followRate = BattleCameraEffects.BaseFollowRate; Time.timeScale = 1; StopCinematic();
            cameraInitialized = false;
            for (int i = 0; i < 2; i++) { StopAura(i); wasRage[i] = false; }
        }

        void OnDisable() => Time.timeScale = 1;

        // The match keeps running when the window loses focus; only held keys are dropped, since
        // their release would never arrive.
        void OnApplicationFocus(bool focus) { if (!focus) ClearInputs(); }
        void ClearInputs() { if (ArcadeActive) arcadeRequest.Input.ResetGameplayForTransition();
            for (int i = 0; i < 2; i++) { commands[i] = PlayerCommand.Neutral; pressedSinceTick[i] = 0; cpu[i]?.Clear(); } }
        void ClearVictory() { if (victory != null) victory.Clear(); victory = null; victoryPlayer = -1; victoryAnchor = null; }

        // Victory takes keep their source root travel (Jin's jz_w00_n walks 5.8 m, Alisa's aa_w01 6.7 m
        // forward and 3.2 m aside) and the winner faces the loser, so a walk can leave the floor. The
        // original's stages have walls or no edge; here the whole path is fitted inside the arena before
        // the take starts, when the camera cuts to the take's own shot anyway.
        const float FloorMargin = .35f;
        void FitVictoryAnchor()
        {
            if (victory == null || victoryAnchor == null) return;
            var travel = victory.Travel();
            if (travel.Length == 0) return;
            var forward = new Vector3(victoryAnchor.dirX, 0, victoryAnchor.dirZ);
            var left = new Vector3(-forward.z, 0, forward.x);
            float minX = 0, maxX = 0, minZ = 0, maxZ = 0;
            foreach (var t in travel)
            {
                var p = forward * t.x + left * t.y;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            victoryAnchor.x += Shift(victoryAnchor.x + minX, victoryAnchor.x + maxX, config.rules.arenaHalfWidth - FloorMargin);
            victoryAnchor.z += Shift(victoryAnchor.z + minZ, victoryAnchor.z + maxZ, config.rules.arenaHalfDepth - FloorMargin);
        }

        // The shift that brings [low, high] inside [-limit, limit], centring a span that cannot fit.
        static float Shift(float low, float high, float limit)
        {
            if (high - low > 2 * limit) return -(low + high) * .5f;
            if (low < -limit) return -limit - low;
            if (high > limit) return limit - high;
            return 0;
        }

        // Safety net after the fit: feet and hips never pass the floor's edge.
        void KeepOnFloor(int i)
        {
            if (framingBones == null || framingBones[i].Length == 0) return;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var bone in framingBones[i])
            {
                if (bone.name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(bone.name, "Head", StringComparison.OrdinalIgnoreCase)) continue;
                // Fighters are drawn in battle coordinates (JinRig places them at world (x, 0, z)).
                var p = bone.position;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            if (minX > maxX || fighterObjects == null || !fighterObjects[i]) return;
            var shift = new Vector3(Shift(minX, maxX, config.rules.arenaHalfWidth - FloorMargin * .5f), 0,
                                    Shift(minZ, maxZ, config.rules.arenaHalfDepth - FloorMargin * .5f));
            if (shift.sqrMagnitude > 1e-8f) fighterObjects[i].transform.position += shift;
        }
        void StartMatch() { if (ArcadeActive) return; menu = false; paused = false; ClearVictory(); ClearInputs(); ResetImpactEffects(); battle.Restart(); lastPhase = battle.phase; announced = null; winnerCallAt = -1; if (sound) sound.StopAll(); }
        void OnHit(HitEvent hit)
        {
            hitsResolved++;
            if (!hit.blocked) struckDamage[hit.defender] = Math.Max(struckDamage[hit.defender], hit.damage);
            hitLabelTime = .65f;
            // Labeled hit shake (remaster choice, BattleEffectTuning.hitShake): it never replaces an original data shake.
            var tuning = Tuning;
            if (tuning.hitShake && !hit.blocked && (shakePreset == 0 || shakeFromHit))
            { shakePreset = hit.counterHit ? tuning.counterShakePreset : tuning.hitShakePreset; shakeFrame = 0; shakeStarted = true; shakeFromHit = true; }
            if (!PlayHitSound(hit)) audioSource.PlayOneShot(hit.blocked ? guardSound : hit.absorbed ? armourSound : strikeSound);
            // A power crush that takes the blow (HitEvent.absorbed) shows the original armour flash instead of the hit spark.
            string role = hit.blocked ? CascadeEffects.Guard : hit.absorbed ? CascadeEffects.Armour : hit.counterHit ? CascadeEffects.Counter : CascadeEffects.Hit;
            if (effects && effects.Has(role))
            {
                Contact(hit.attacker, hit.defender, out var point, out var direction);
                var rotation = Quaternion.LookRotation(direction, Vector3.up);
                effects.Play(role, point, rotation, tuning.hitEffectScale);
                // No PierceFX: its old >= 20 damage trigger was a remaster guess and the reference shows
                // no such line on heavy hits (removed 2026-10-27).
                // No punish marker in versus (2026-10-27): PracticePunishNotice* is the practice-mode
                // crosshair. The blue ring with lens-flare needles in the reference match was Hwoarang's
                // own effect (PS_EF_CHR_HWO_nomal, 0x802E on his kicks), not a punish notice.
                return;
            }
            if (!tuning.commonBattleEffects) return;
            var f = battle.fighters[hit.defender];
            var spark = FighterView.Piece("Impact", PrimitiveType.Sphere, transform, new Vector3(f.x, 1.3f, f.z - .1f),
                Vector3.one * .2f, hit.blocked ? blockMaterial : impactMaterial).gameObject;
            hitEffects.Add(spark); Destroy(spark, .08f);
            hitEffects.RemoveAll(x => !x);
        }

        // ---- Sound --------------------------------------------------------------------------------

        // Impact sounds from the original data: an attack's own sound list (the moveset's voiceclip block) is its
        // contact set, played when it connects: swing (6) and impact (7) from the common battle table and a shout
        // (2:n, 999 = any). Whiffs stay silent apart from the move's timed whoosh properties. Guard reactions all
        // list 1:26 (RES_BANK_GRD_MD01), played on a block; the reaction move's own impact properties (category 7
        // on its first frame) play through PlayMoveSound. Remaster choices: moves with no listed impact fall back
        // to a punch or kick (by the striking limb) graded by damage, and the struck fighter cries out.
        static readonly string[] PunchSounds = { "RES_BANK_PCH_WK02", "RES_BANK_PCH_MD01", "RES_BANK_PCH_SG01" };
        static readonly string[] KickSounds = { "RES_BANK_KCK_WK01", "RES_BANK_KCK_LT01", "RES_BANK_KCK_SG01" };
        const int GuardReactionTone = 26;

        bool PlayHitSound(HitEvent hit)
        {
            if (!sound || !sound.Loaded) return false;
            if (hit.blocked) return sound.Tone(BattleSound.GuardTone, GuardReactionTone);
            if (hit.absorbed) return sound.Play("Guard01");
            int tier = hit.damage < 10 ? 0 : hit.damage < 20 ? 1 : 2;
            // The struck fighter cries out (native in the original: its moves name pain cries only for throws);
            // the knock-out blow draws a death cry.
            bool knockOut = battle.fighters[hit.defender].health <= 0;
            sound.Cry(hit.defender, codes[hit.defender], tier, knockOut, .9f);
            var attacker = battle.fighters[hit.attacker];
            bool impact = false;
            foreach (int clip in sound.MoveClips(codes[attacker.moveOwner], attacker.move) ?? Array.Empty<int>())
            {
                int category = clip >> 24 & 0xFF, index = clip & 0xFFFFFF;
                if (category == BattleSound.AttackVoice) { if (sound.Speaking(hit.attacker) <= 0) sound.Voice(hit.attacker, codes[hit.attacker], category, index, .9f); }
                else if (BattleSound.IsTone(category) && sound.Tone(category, index)) impact |= category == BattleSound.ImpactTone;
            }
            return impact || sound.Play((StrikesWithLeg(hit.attacker, hit.defender) ? KickSounds : PunchSounds)[tier]);
        }

        // The attacker's striking bone nearest the defender's body is a leg or foot.
        bool StrikesWithLeg(int attacker, int defender)
        {
            var limbs = strikeBones?[attacker]; var body = bodyBones?[defender];
            if (limbs == null || body == null || body.Length < 2) return false;
            float best = float.MaxValue; string nearest = "";
            foreach (var l in limbs)
                for (int k = 0; k + 1 < body.Length; k++)
                {
                    float dist = (l.position - ClosestOnSegment(l.position, body[k].position, body[k + 1].position)).sqrMagnitude;
                    if (dist < best) { best = dist; nearest = l.name; }
                }
            return nearest.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 || nearest.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Original move sounds: 0x84C4 plays the moving fighter's sound, 0x84C6 its opponent's (value =
        // category << 24 | index). Categories 2/3/4 are the attack / pain / death voices (SE_KIAI_n, SE_YARARE_n,
        // SE_DEAD_n); 1/6/7/8 index the common battle table (whooshes, impacts on reaction moves, bone cracks,
        // sparks) and 15 the battle-system tones (Rage Art, Devil Kazuya), both from the original tables (see
        // BattleSound); 9 is the body hitting the floor (Dw_AIR_pon* on their landing frame; played with the heavy
        // down sounds, our choice). Category 10 is not identified. 0x8193 is a footstep. True when the property
        // is a sound property.
        bool PlayMoveSound(int player, long id, long value)
        {
            if (id == 0x8193) { if (sound) sound.Play("PlayerOne_FootSteps", .3f); return false; }
            if (id != 0x84C4 && id != 0x84C6) return false;
            if (!sound) return true;
            int speaker = id == 0x84C6 ? 1 - player : player;
            int category = (int)(value >> 24) & 0xFF, index = (int)(value & 0xFFFFFF);
            if (category == BattleSound.AttackVoice || category == BattleSound.DamageVoice || category == BattleSound.DeathVoice)
                sound.Voice(speaker, codes[speaker], category, index, .9f);
            else if (BattleSound.IsTone(category)) sound.Tone(category, index);
            else if (category == 9) sound.Play("RES_BANK_DWN_HVY0" + (index % 3 + 1));
            else if (category == BattleSound.ItemCategory) sound.Item(index);
            return true;
        }

        // The announcer speaks each centre call once, as it appears.
        void Announce(BattleHud.Call call, int round, bool finalRound, float seconds)
        {
            string name = call == BattleHud.Call.Round ? (finalRound ? "Play_FinalRound" : "Play_Round" + Mathf.Clamp(round, 1, 9)) :
                call == BattleHud.Call.Fight ? "Play_Fight" : call == BattleHud.Call.KO ? "Play_KO" :
                call == BattleHud.Call.DoubleKO ? "Play_DoubleKO" : call == BattleHud.Call.TimeUp ? "Play_TimeUp" :
                call == BattleHud.Call.Draw ? "Play_Draw" : null;
            if (name != null) Announce(name, call + ":" + round, seconds);
            // The FinalRound state switches the stage music to its Final arrangement with the round call.
            if (call == BattleHud.Call.Round && seconds >= 0 && seconds <= .4f) BattleMusic.Instance.SetFinalRound(finalRound);
        }

        void Announce(string name, string key, float seconds)
        {
            if (!sound || seconds < 0 || seconds > .4f || key == announced) return;
            announced = key;
            sound.Announce(name);
        }

        // Entrance takes speak their own line (jz_s00 ...), named like the take.
        void VoiceEntrance()
        {
            if (entrance.Current == voicedShot) return;
            voicedShot = entrance.Current;
            if (voicedShot == LocalVersusEntrance.Shot.PlayerOne) SayTake(0, entrance.OneStagingAnimation);
            else if (voicedShot == LocalVersusEntrance.Shot.PlayerTwo) SayTake(1, entrance.TwoStagingAnimation);
        }

        // A take's voice line shares its name: "jz_w00_n(DVD)" speaks jz_w00 (the _n/_y pair halves share it).
        void SayTake(int player, string animation)
        {
            if (!sound || string.IsNullOrEmpty(animation)) return;
            string name = animation; int paren = name.IndexOf('(');
            if (paren > 0) name = name.Substring(0, paren);
            if (!sound.Has(name) && (name.EndsWith("_n") || name.EndsWith("_y"))) name = name.Substring(0, name.Length - 2);
            // The game's English subtitle for the line shows while it is spoken.
            if (sound.Say(player, name) && SoundSettings.Active.subtitles && sound.LineText(name) is string text)
            { subtitle = text; subtitleUntil = Time.unscaledTime + sound.Speaking(player); }
        }

        string subtitle;
        float subtitleUntil;

        void DrawSubtitle()
        {
            if (subtitle == null || Time.unscaledTime > subtitleUntil) return;
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height / 30f), wordWrap = true };
            var area = new Rect(Screen.width * .1f, Screen.height * .84f, Screen.width * .8f, Screen.height * .08f);
            style.normal.textColor = new Color(0, 0, 0, .8f);
            GUI.Label(new Rect(area.x + 2, area.y + 2, area.width, area.height), subtitle, style);
            style.normal.textColor = Color.white;
            GUI.Label(area, subtitle, style);
            GUI.matrix = previous;
        }

        // After the match-deciding K.O. call: "<fighter> ... wins".
        void UpdateWinnerCall(float delta)
        {
            if (!sound || battle == null) return;
            if (battle.phase == MatchPhase.MatchOver && battle.winner >= 0 && winnerCallAt < 0 && announced != "Winner" &&
                roundEndClock >= KoCallSeconds)
            {
                announced = "Winner";
                sound.Announce(sound.NameCall(codes[battle.winner]));
                winnerCallAt = .9f;
            }
            else if (winnerCallAt > 0 && (winnerCallAt -= delta) <= 0) { sound.Announce("Play_Wins"); winnerCallAt = -1; }
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

        // Rage, from state changes after each tick: Rage starts with RageFX and keeps RageAuraFX on the
        // fighter until the round ends. Rage is not spent here, since the model has no Rage Art
        // consumption, so the aura stays while health is at a quarter or less. No floor effects: air
        // landings and knockdowns on a solid floor show neither BlastFX's ring nor SmokeFX's dust in the
        // reference match (218 s, 236 s; removed 2026-10-27); the water stage's splash is the stage's own.
        void UpdateBattleEffects()
        {
            SyncCommonEffects();
            if (!effects) return;
            effects.glareGain = Tuning.glareGain;
            for (int i = 0; i < 2; i++)
            {
                var f = battle.fighters[i];
                bool rage = battle.phase == MatchPhase.Fight && f.health > 0 && (f.health * 4 <= battle.rules.health || battle.rageGranted[i]);
                if (rage && !wasRage[i])
                {
                    sound?.Play("se_rage", .8f);
                    var center = bodyBones[i].Length > 1 ? bodyBones[i][1].position : transform.TransformPoint(new Vector3(f.x, 1, f.z));
                    // The source effects are authored in the character's frame: X along its facing.
                    var heading = new Vector3(f.dirX, 0, f.dirZ);
                    var facing = Quaternion.LookRotation(heading.sqrMagnitude > 1e-6f ? heading : Vector3.forward);
                    rageStart[i] = effects.Play(CascadeEffects.Rage, center, facing, 1, auraBones[i]);
                    rageAura[i] = effects.Play(CascadeEffects.RageAura, center, facing, 1, auraBones[i]);
                    if (!rageRims[i]) rageRims[i] = RageRim.Create(fighterObjects[i]);
                    if (rageRims[i]) rageRims[i].On = true;
                }
                else if (!rage && wasRage[i]) StopAura(i);
                wasRage[i] = rage;
                // Both Rage effects are attached to the character in the original: they travel with the body.
                if (bodyBones[i].Length > 1)
                {
                    if (rageAura[i]) rageAura[i].transform.position = bodyBones[i][1].position;
                    if (rageStart[i]) rageStart[i].transform.position = bodyBones[i][1].position;
                }
            }
        }

        // Follows the Inspector switch during Play: ticking it loads the common library, unticking it
        // removes the library and everything it is playing. Rage restarts on the next state change.
        void SyncCommonEffects()
        {
            if (Tuning.commonBattleEffects == (bool)effects) return;
            if (Tuning.commonBattleEffects) { effects = CascadeEffects.Create(transform); return; }
            for (int i = 0; i < 2; i++) { StopAura(i); wasRage[i] = false; }
            effects.Clear(); Destroy(effects.gameObject); effects = null;
        }

        void StopAura(int i)
        {
            if (rageAura[i]) rageAura[i].Stop();
            rageAura[i] = null; rageStart[i] = null;
            if (rageRims[i]) rageRims[i].On = false;
        }
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
            if (Event.current.type == EventType.Repaint && battle != null) hud.Advance(battle, Time.unscaledDeltaTime);
            if (entrance != null && entrance.Active && !entrance.ShowsBattleHud) { entrance.Draw(); DrawSubtitle(); return; }
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
            try { DrawGameplayGUI(); if (entrance != null && entrance.Active) entrance.Draw(); if (!ArcadeActive && effectTuningPanel && !menu) DrawTuningPanel(); }
            finally { GUI.matrix = previousMatrix; }
            DrawSubtitle();
        }
        void DrawGameplayGUI()
        {
            if (!ArcadeActive && menu) { DrawMenu(); return; }
            // The gauges leave the screen while the match winner's victory take plays.
            bool victoryTake = battle.phase == MatchPhase.MatchOver && victory != null && victory.Active;
            if (!victoryTake) hud.DrawGauges(battle, Time.unscaledTime);
            if (ArcadeActive && ArcadeCallsHidden) return;
            bool finalRound = battle.round > 1 && battle.fighters[0].wins == battle.rules.roundsToWin - 1 && battle.fighters[1].wins == battle.rules.roundsToWin - 1;
            if (entrance != null && entrance.Active && entrance.ShowsBattleHud)
            {
                Announce(entrance.Cue, 1, false, entrance.CueSeconds);
                hud.DrawCall(entrance.Cue, 1, false, entrance.CueSeconds);
            }
            else if (battle.phase == MatchPhase.Intro)
            {
                // As in the original capture (TEKKEN 7 2026-10-23 08-04-08, 30 fps): READY 68 ticks after ROUND,
                // FIGHT 40 after READY (the READY clip's own length), then FIGHT runs into the fight.
                int elapsed = 150 - battle.phaseTicks;
                var call = elapsed < ReadyTick ? BattleHud.Call.Round : elapsed < FightTick ? BattleHud.Call.Ready : BattleHud.Call.Fight;
                float seconds = (elapsed - (call == BattleHud.Call.Round ? 0 : call == BattleHud.Call.Ready ? ReadyTick : FightTick)) / 60f;
                Announce(call, battle.round, finalRound, seconds);
                hud.DrawCall(call, battle.round, finalRound, seconds);
            }
            else if (battle.phase == MatchPhase.Fight)
            {
                // The original FIGHT call finishes over the first moments of the fight (it started before control).
                float fightAge = (150 - FightTick + battle.rules.seconds * 60 - battle.ticksLeft) / 60f;
                if (fightAge < hud.FightCallSeconds) hud.DrawCall(BattleHud.Call.Fight, battle.round, finalRound, fightAge);
            }
            if (!ArcadeActive && automaticFight)
            {
                Label(new Rect(24, 684, 300, 24), "AUTO FIGHT   /   F2", small);
                // Rage Art buttons: each auto player performs its original Rage Art (F3 / F4).
                bool ready = battle.phase == MatchPhase.Fight && !paused && (entrance == null || !entrance.Active);
                for (int i = 0; i < 2; i++)
                {
                    bool busy = rageArts[i].Active;
                    GUI.enabled = ready && !busy && cpu[i].CanRageArt;
                    if (GUI.Button(new Rect(24 + i * 214, 638, 204, 40), (busy ? "RAGE ART...  " : "RAGE ART  ") + (i == 0 ? "P1" : "P2") + "  (F" + (3 + i) + ")", button))
                        TriggerRageArt(i);
                }
                GUI.enabled = true;
            }
            if (!ArcadeActive)
            {
                GUI.enabled = CanForceTrade;
                if (GUI.Button(new Rect(24 + 2 * 214, 638, 204, 40), "TRADE EFFECT  (F5)", button)) ForceTradeEffect();
                GUI.enabled = true;
            }
            if ((battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver) && battle.UsesCommandGraphs && roundEndClock < KoCallSeconds)
            {
                // The round call over the slow-motion fall; PERFECT follows when the winner took no damage.
                var call = battle.result == "TIME UP" ? BattleHud.Call.TimeUp : battle.result == "DOUBLE KO" ? BattleHud.Call.DoubleKO : BattleHud.Call.KO;
                Announce(call, battle.round, false, roundEndClock);
                hud.DrawCall(call, battle.round, false, roundEndClock);
                if (battle.winner >= 0 && battle.fighters[battle.winner].health == battle.rules.health)
                {
                    if (roundEndClock >= .5f) Announce("Play_Perfect", "Perfect:" + battle.round, roundEndClock - .5f);
                    hud.DrawPerfect(roundEndClock - .5f);
                }
            }
            else if (battle.phase == MatchPhase.MatchOver && victory != null && !victory.Complete)
            {
                // During the winner's victory take only a lower-third banner, as in the original.
                Panel(new Rect(0, 560, 1280, 70), Ink);
                Label(new Rect(0, 565, 1280, 60), (battle.winner == 0 ? config.playerOne : config.playerTwo) + "  WINS", new GUIStyle(title) { fontSize = 40 }, Accent);
            }
            else if (battle.phase == MatchPhase.RoundOver && battle.winner < 0 && battle.result != "TIME UP")
                // A double KO replays the round without a point.
            {
                Announce(BattleHud.Call.Draw, battle.round, false, roundEndClock - KoCallSeconds);
                hud.DrawCall(BattleHud.Call.Draw, battle.round, false, roundEndClock - KoCallSeconds);
            }
            else if (!ArcadeActive && battle.phase == MatchPhase.MatchOver)
            {
                // After the victory take: the match result and the rematch prompt.
                Panel(new Rect(260, 250, 760, 200), Ink);
                string result = battle.winner < 0 ? "DRAW" : (battle.winner == 0 ? config.playerOne : config.playerTwo) + "  WINS THE MATCH";
                Label(new Rect(280, 270, 720, 60), result, new GUIStyle(title) { fontSize = 36 }, Accent);
                if (GUI.Button(new Rect(300, 365, 330, 52), "REMATCH   /   ENTER", button)) StartMatch();
                if (GUI.Button(new Rect(650, 365, 330, 52), "CHARACTER SELECT", button)) CharacterSelect.Return();
            }
            if (!ArcadeActive && help) DrawHelp();
            if (!ArcadeActive && paused)
            {
                Panel(new Rect(0, 0, 1280, 720), new Color(0, 0, 0, .65f));
                Panel(new Rect(440, 160, 400, 400), Ink); Label(new Rect(440, 175, 400, 80), "PAUSED", title);
                if (GUI.Button(new Rect(490, 270, 300, 48), "RESUME", button)) paused = false;
                if (GUI.Button(new Rect(490, 330, 300, 48), "RESTART MATCH", button)) StartMatch();
                if (GUI.Button(new Rect(490, 390, 300, 48), "CHARACTER SELECT", button)) { paused = false; CharacterSelect.Return(); }
                if (GUI.Button(new Rect(490, 450, 300, 48), "MAIN MENU", button)) { menu = true; paused = false; ClearInputs(); ResetImpactEffects(); }
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
