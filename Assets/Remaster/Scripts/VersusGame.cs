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
        public Camera sceneCamera;
        public bool automaticFight = true;
        public bool playEntrance;
        public BattleModel battle { get; private set; }
        public bool paused { get; private set; }
        readonly PlayerCommand[] commands = { PlayerCommand.Neutral, PlayerCommand.Neutral };
        readonly int[] pending = { -1, -1 };
        readonly float[] pendingAt = { 0, 0 };
        IFighterPresentation[] views;
        Camera cameraRig;
        EntranceCameraLens gameplayLens;
        // Read from the original game camera +0x39C during a user-confirmed normal fight.
        const float GameplayHorizontalFov = 65;
        LocalVersusEntrance entrance;
        bool menu, help, diagnostic;
        readonly int[] aiCooldown = new int[2];
        FighterActionCatalog[] catalogs;
        readonly int[] aiSequence = new int[2];
        int rematchTicks;
        int hitsResolved;
        string hitLabel = "";
        float hitLabelTime, shake, visualTime;
        AudioSource audioSource;
        AudioClip strikeSound, guardSound;
        Material impactMaterial, blockMaterial;
        GUIStyle text, title, small, button;
        readonly List<GameObject> hitEffects = new List<GameObject>();
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
#if UNITY_EDITOR
            if (!jinActions) jinActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/Remaster/Museum/Jin.asset");
            if (!kazuyaActions) kazuyaActions = UnityEditor.AssetDatabase.LoadAssetAtPath<MuseumCharacter>("Assets/Remaster/Museum/Kazuya.asset");
#endif
            if (!jinActions || !kazuyaActions) throw new InvalidOperationException("Assign both character move catalogs in LocalVersus.");
            catalogs = new[] { new FighterActionCatalog(jinActions, "Jz_"), new FighterActionCatalog(kazuyaActions, "Kz_") };
            battle = new BattleModel(config.rules, new[] { catalogs[0].moves, catalogs[1].moves }); battle.Hit += OnHit;
            Time.fixedDeltaTime = 1f / 60;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            BuildArena();
            var jinObject = sceneJin ? sceneJin : Instantiate(Resources.Load<GameObject>("JinPlayable"), transform);
            var jin = jinObject.GetComponent<JinRig>();
            jin.fighterMoves = catalogs[0].moves;
            jin.actionMotion = jin.gameObject.AddComponent<FighterActionMotion>();
            jin.actionMotion.Initialize(catalogs[0]);
            // An editor session can retain an older in-memory scene after the scene file changes.
            // Resolve the imported prefab for that session as well as for the serialized scene.
#if UNITY_EDITOR
            if (!sceneKazuya && !kazuyaPrefab)
                kazuyaPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Remaster/Kazuya/Prefabs/Karate_Gi.prefab");
#endif
            if (!sceneKazuya && !kazuyaPrefab) throw new InvalidOperationException("Assign the Kazuya Karate Gi prefab in LocalVersus.");
            var kazuyaObject = sceneKazuya ? sceneKazuya : Instantiate(kazuyaPrefab, transform);
            var kazuya = kazuyaObject.GetComponent<JinRig>();
            if (!kazuya) kazuya = kazuyaObject.AddComponent<JinRig>();
            kazuya.fighterMoves = catalogs[1].moves;
            kazuya.actionMotion = kazuya.gameObject.AddComponent<FighterActionMotion>();
            kazuya.actionMotion.Initialize(catalogs[1]);
            config.playerOne = "JIN KAZAMA";
            config.playerTwo = "KAZUYA MISHIMA";
            views = new IFighterPresentation[] { jin, kazuya };
            if (playEntrance)
                entrance = new LocalVersusEntrance(jinObject.transform, kazuyaObject.transform,
                    cameraRig, battle, jin.actionMotion, jinActions, kazuya.actionMotion, kazuyaActions, gameplayLens);
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
        void SmokeCapture()
        {
            ScreenCaptureFallback();
            var jin = views[0] as JinRig;
            var kazuya = views[1] as JinRig;
            bool retargeted = jin && jin.actionMotion && jin.actionMotion.Ready;
            bool kazuyaReady = kazuya && kazuya.actionMotion && kazuya.actionMotion.Ready;
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../runtime-smoke.json"),
                "{\"sceneLoaded\":true,\"fighters\":2,\"phase\":\"" + battle.phase +
                "\",\"automaticFight\":" + (automaticFight ? "true" : "false") +
                ",\"jinRetargetedStance\":" + (retargeted ? "true" : "false") +
                ",\"kazuyaActionsReady\":" + (kazuyaReady ? "true" : "false") +
                ",\"jinActionsPlayed\":" + (jin && jin.actionMotion ? jin.actionMotion.PlayedActions : 0) +
                ",\"kazuyaActionsPlayed\":" + (kazuyaReady ? kazuya.actionMotion.PlayedActions : 0) +
                ",\"jinMoveCount\":" + catalogs[0].moves.Length +
                ",\"kazuyaMoveCount\":" + catalogs[1].moves.Length +
                ",\"hitsResolved\":" + hitsResolved +
                ",\"jinHealth\":" + battle.fighters[0].health +
                ",\"kazuyaHealth\":" + battle.fighters[1].health + "}");
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
            RenderSettings.ambientLight = new Color(.38f, .43f, .54f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(.025f, .04f, .07f); RenderSettings.fogDensity = .022f;
            var floor = FighterView.Surface(new Color(.09f, .12f, .17f), .55f);
            var edge = FighterView.Surface(new Color(.18f, .28f, .36f), .5f);
            var neon = FighterView.Surface(new Color(.1f, .5f, .65f));
            neon.EnableKeyword("_EMISSION"); neon.SetColor("_EmissionColor", new Color(.05f, .5f, .7f) * 1.6f);
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
            var light = new GameObject("Key light").AddComponent<Light>(); light.transform.SetParent(transform);
            light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var fill = new GameObject("Cool rim light").AddComponent<Light>(); fill.transform.SetParent(transform);
            fill.type = LightType.Directional; fill.color = new Color(.35f, .6f, 1); fill.intensity = .65f;
            fill.transform.rotation = Quaternion.Euler(30, 140, 0);
            cameraRig = sceneCamera ? sceneCamera : new GameObject("Versus camera").AddComponent<Camera>();
            if (!sceneCamera) cameraRig.transform.SetParent(transform);
            cameraRig.tag = "MainCamera"; cameraRig.backgroundColor = new Color(.025f, .04f, .07f);
            cameraRig.clearFlags = CameraClearFlags.SolidColor;
            gameplayLens = new EntranceCameraLens(cameraRig);
            gameplayLens.SetHorizontalFov(GameplayHorizontalFov);
            cameraRig.nearClipPlane = .1f; cameraRig.farClipPlane = 100;
            if (!cameraRig.GetComponent<AudioListener>()) cameraRig.gameObject.AddComponent<AudioListener>();
        }
        void Update()
        {
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
            for (int i = 0; i < 2; i++)
            {
                commands[i] = diagnostic ? PlayerCommand.Neutral : ReadInput(i);
                if (commands[i].attack >= 0) { pending[i] = commands[i].attack; pendingAt[i] = Time.unscaledTime; }
            }
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
                    if (++rematchTicks >= 180) { rematchTicks = 0; battle.Restart(); }
                    return;
                }
                commands[0] = AutoCommand(0);
                commands[1] = AutoCommand(1);
                battle.Tick(commands[0], commands[1]);
                return;
            }
            for (int i = 0; i < 2; i++)
            { commands[i].attack = Time.unscaledTime - pendingAt[i] < .14f ? pending[i] : -1; pending[i] = -1; }
            battle.Tick(commands[0], commands[1]);
        }
        PlayerCommand AutoCommand(int player)
        {
            var command = PlayerCommand.Neutral;
            if (battle.phase != MatchPhase.Fight) return command;
            var self = battle.fighters[player]; var foe = battle.fighters[1 - player];
            float gap = Mathf.Abs(foe.x - self.x), lane = foe.z - self.z;
            if (Mathf.Abs(lane) > .3f) command.z = Mathf.Sign(lane);
            if (gap > 1.45f) command.x = self.facing;
            else if (gap < .9f && self.stun == 0) command.x = -self.facing * .45f;
            if (foe.attack >= 0 && gap < 2.15f && self.attack < 0 &&
                (battle.ticksLeft / 53 + player) % 3 == 0)
            {
                var threat = battle.Move(1 - player, foe.attack);
                command.guard = true;
                command.crouch = threat.height == HitHeight.Low;
            }
            if (aiCooldown[player] > 0) aiCooldown[player]--;
            if (gap <= 1.7f && Mathf.Abs(lane) < .55f && self.attack < 0 && self.stun == 0 && !command.guard && aiCooldown[player] == 0)
            {
                int[] pool = catalogs[player].autoActions;
                int choice = pool[(aiSequence[player]++ * 37 + player * 11) % pool.Length];
                command.attack = choice;
                var move = battle.Move(player, choice);
                aiCooldown[player] = move.startup + move.active + move.recovery + 12;
            }
            return command;
        }
        void LateUpdate()
        {
            if (views == null) return;
            if (entrance != null && entrance.Active) entrance.PrepareMotion();
            for (int i = 0; i < 2; i++) views[i].Render(battle.fighters[i], config.rules, visualTime,
                !menu && !paused && battle.phase == MatchPhase.Fight && Mathf.Abs(commands[i].x) + Mathf.Abs(commands[i].z) > .1f);
            if (entrance != null && entrance.Active) { entrance.Present(); return; }
            gameplayLens.SetHorizontalFov(GameplayHorizontalFov);
            var a = battle.fighters[0]; var b = battle.fighters[1];
            float distance = Mathf.Clamp(9 + Mathf.Abs(a.x - b.x) * .6f, 11, 17);
            Vector3 target = new Vector3((a.x + b.x) / 2, 1, (a.z + b.z) / 2);
            Vector3 position = target + new Vector3(0, distance * .27f, -distance);
            position += new Vector3(Mathf.Sin(visualTime * 95) * shake * .15f, 0, 0);
            cameraRig.transform.position = Vector3.Lerp(cameraRig.transform.position == Vector3.zero ? position : cameraRig.transform.position,
                position, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 7));
            cameraRig.transform.LookAt(target);
        }
        PlayerCommand ReadInput(int player)
        {
            bool one = player == 0;
            var c = PlayerCommand.Neutral;
            c.x = (Input.GetKey(one ? KeyCode.D : KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(one ? KeyCode.A : KeyCode.LeftArrow) ? 1 : 0);
            c.z = (Input.GetKey(one ? KeyCode.W : KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(one ? KeyCode.S : KeyCode.DownArrow) ? 1 : 0);
            c.guard = Input.GetKey(one ? KeyCode.R : KeyCode.RightShift);
            c.crouch = Input.GetKey(one ? KeyCode.T : KeyCode.RightControl);
            KeyCode[] keys = one ? new[] { KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J } :
                                   new[] { KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad4, KeyCode.Keypad5 };
            // Alternate P2 attack keys for keyboards without a number pad.
            KeyCode[] alternate = { KeyCode.Comma, KeyCode.Period, KeyCode.Slash, KeyCode.Semicolon };
            int joystickBase = (int)(one ? KeyCode.Joystick1Button0 : KeyCode.Joystick2Button0);
            for (int i = 0; i < 4; i++)
                if (Input.GetKeyDown(keys[i]) || (!one && Input.GetKeyDown(alternate[i])) || Input.GetKeyDown((KeyCode)(joystickBase + i))) c.attack = i;
            c.x = Mathf.Clamp(c.x + Input.GetAxisRaw(one ? "P1StickX" : "P2StickX"), -1, 1);
            c.z = Mathf.Clamp(c.z - Input.GetAxisRaw(one ? "P1StickY" : "P2StickY"), -1, 1);
            c.guard |= Input.GetKey((KeyCode)(joystickBase + 4)); c.crouch |= Input.GetKey((KeyCode)(joystickBase + 5));
            return c;
        }
        void OnApplicationFocus(bool focus) { if (!focus && !menu && !diagnostic) { paused = true; ClearInputs(); } }
        void ClearInputs() { for (int i = 0; i < 2; i++) { commands[i] = PlayerCommand.Neutral; pending[i] = -1; } }
        void StartMatch() { menu = false; paused = false; ClearInputs(); battle.Restart(); }
        void OnHit(HitEvent hit)
        {
            hitsResolved++;
            hitLabel = hit.blocked ? "GUARD" : "P" + (hit.attacker + 1) + "   /   " + hit.damage + " DAMAGE";
            hitLabelTime = .65f; shake = hit.blocked ? .12f : .4f;
            audioSource.PlayOneShot(hit.blocked ? guardSound : strikeSound);
            var f = battle.fighters[hit.defender];
            var spark = FighterView.Piece("Impact", PrimitiveType.Sphere, transform, new Vector3(f.x, 1.3f, f.z - .1f),
                Vector3.one * .2f, hit.blocked ? blockMaterial : impactMaterial).gameObject;
            hitEffects.Add(spark); Destroy(spark, .08f);
            hitEffects.RemoveAll(x => !x);
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
            if (entrance != null && entrance.Active) { entrance.Draw(); return; }
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
            try { DrawGameplayGUI(); }
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
                float width = 500f * f.health / config.rules.health;
                Panel(new Rect(i == 0 ? x : x + 500 - width, 50, width, 18), i == 0 ? config.colorOne : config.colorTwo);
                Label(new Rect(x, 73, 360, 25), f.health + " HP    /    " + f.wins + " ROUND" + (f.wins == 1 ? "" : "S"), small);
            }
            Label(new Rect(550, 20, 180, 60), Mathf.CeilToInt(battle.ticksLeft / 60f).ToString("00"), title, Accent);
            Label(new Rect(581, 78, 150, 25), "FIRST TO " + config.rules.roundsToWin, small);
            Label(new Rect(40, 128, 650, 25), (automaticFight ? "AUTO FIGHT" : "LOCAL VERSUS") + "   /   ROUND " + battle.round + "   /   NEON DECK", small);
            if (hitLabelTime > 0) Label(new Rect(440, 150, 400, 40), hitLabel, new GUIStyle(text) { alignment = TextAnchor.MiddleCenter }, Accent);
            if (battle.phase == MatchPhase.Intro)
                Label(new Rect(250, 240, 780, 90), battle.phaseTicks > 45 ? "ROUND " + battle.round : "FIGHT", title);
            if (battle.phase == MatchPhase.RoundOver || battle.phase == MatchPhase.MatchOver)
            {
                Panel(new Rect(260, 220, 760, 250), Ink);
                Label(new Rect(280, 235, 720, 80), battle.result, title, Accent);
                string result = battle.winner < 0 ? "DRAW - ROUND REPLAY" : "PLAYER " + (battle.winner + 1) + (battle.phase == MatchPhase.MatchOver ? " WINS THE MATCH" : " WINS THE ROUND");
                Label(new Rect(280, 315, 720, 45), result, new GUIStyle(text) { alignment = TextAnchor.MiddleCenter });
                if (battle.phase == MatchPhase.MatchOver && GUI.Button(new Rect(470, 390, 340, 52), "REMATCH   /   ENTER", button)) StartMatch();
            }
            Panel(new Rect(0, 650, 1280, 70), Ink);
            Label(new Rect(30, 657, 1200, 23), "P1  WASD  /  F G H J attacks  /  R guard  /  T crouch       P2  ARROWS  /  NUM 1 2 4 5 attacks  /  R-SHIFT guard  /  R-CTRL crouch", small);
            Label(new Rect(30, 682, 1200, 23), "F1 controls   /   F2 auto/manual   /   ESC pause       Jin and Kazuya / prototype combat rules.", small);
            if (help) DrawHelp();
            if (paused)
            {
                Panel(new Rect(0, 0, 1280, 720), new Color(0, 0, 0, .65f));
                Panel(new Rect(440, 180, 400, 340), Ink); Label(new Rect(440, 195, 400, 80), "PAUSED", title);
                if (GUI.Button(new Rect(490, 290, 300, 48), "RESUME", button)) paused = false;
                if (GUI.Button(new Rect(490, 350, 300, 48), "RESTART MATCH", button)) StartMatch();
                if (GUI.Button(new Rect(490, 410, 300, 48), "MAIN MENU", button)) { menu = true; paused = false; ClearInputs(); }
            }
        }
        void DrawMenu()
        {
            Panel(new Rect(0, 0, 1280, 720), new Color(.015f, .025f, .045f, .72f));
            Label(new Rect(80, 100, 700, 35), "TEKKEN   /   UNITY REMASTER WORKSPACE", small, Accent);
            Label(new Rect(70, 150, 720, 100), "VERSUS", new GUIStyle(title) { fontSize = 94, alignment = TextAnchor.MiddleLeft });
            Label(new Rect(80, 270, 700, 60), "TWO PLAYERS. ONE ARENA.", text);
            Label(new Rect(80, 330, 700, 65), "Play as JIN KAZAMA against Player 2.\nOriginal combat clips / local versus", small);
            if (GUI.Button(new Rect(80, 440, 400, 60), "LOCAL TWO-PLAYER   /   ENTER", button)) StartMatch();
            if (GUI.Button(new Rect(80, 515, 195, 45), "CONTROLS", button)) help = !help;
            if (GUI.Button(new Rect(285, 515, 195, 45), "QUIT", button)) Application.Quit();
            Label(new Rect(80, 642, 1100, 35), "KEYBOARD SHARING + TWO GAMEPADS    /    FIRST TO TWO    /    60-SECOND ROUNDS", small);
            if (help) DrawHelp();
        }
        void DrawHelp()
        {
            Panel(new Rect(225, 145, 830, 440), Ink);
            Label(new Rect(255, 165, 760, 55), "LOCAL CONTROLS", title);
            string controls = "PLAYER 1:  WASD move / sidestep.  F jab, G cross, H sweep, J kick.  R guard, T crouch.\n\n" +
                "PLAYER 2:  Arrow keys move.  Numpad 1, 2, 4, 5 attack (or , . / ;).  Right Shift guard, Right Ctrl crouch.\n\n" +
                "GAMEPADS:  Controller 1 = P1; controller 2 = P2. Left stick move. Buttons 0-3 attack. Bumpers guard / crouch.\n\n" +
                "Hold back to guard. Crouch + guard stops sweeps; mids beat crouching guard. High jabs miss crouching targets.\n\n" +
                "Keyboard rollover varies. Two controllers are useful when many keys are held. Controller mappings use the legacy Input Manager.";
            Label(new Rect(260, 235, 760, 265), controls, new GUIStyle(small) { wordWrap = true, fontSize = 16 });
            if (GUI.Button(new Rect(485, 515, 310, 45), "CLOSE   /   F1", button)) help = false;
        }
    }
}
