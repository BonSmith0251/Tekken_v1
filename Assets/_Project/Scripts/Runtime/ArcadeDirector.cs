using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Versus.Remaster.Arcade
{
    // Boot-scene entry point. The keyboard profile is deliberately the existing desktop layout.
    // Device-specific readers/remapping and commercial deployment are later cabinet steps.
    [DefaultExecutionOrder(-200)]
    public sealed partial class ArcadeDirector : MonoBehaviour
    {
        public const string BootScene = "Assets/_Project/Scenes/ArcadeBoot.unity";
        public const string MatchScene = "Assets/_Project/Scenes/LocalVersus2.unity";
        public static ArcadeDirector Instance { get; private set; }
        public static readonly string[] Fighters = { "Jin", "Kazuya", "Xiaoyu", "Alisa", "Katarina", "Lidia", "Bob", "Leo" };
        static readonly string[] Codes = { "JIN", "KAZ", "XIA", "ASA", "LTN", "NSD", "BOB", "LEO" };
        public ArcadeFlow Flow { get; private set; }
        public ArcadeLedger Ledger { get; private set; }
        public ArcadeRanking Rankings { get; private set; }
        public ArcadeInput CabinetInput { get; private set; }
        public string DataDirectory { get; private set; }
        public bool AutomatedTest { get; private set; }
#if UNITY_EDITOR
        public ArcadeControls TestControls { get; set; }
        public string[] TestKeys { get; set; }
#endif
        ArcadeSettings settings;
        string[] stagePool;
        ArcadeMatchRequest request;
        AsyncOperation sceneLoad;
        Camera bootCamera;
        AudioListener bootListener;
        ArcadeControls previous;
        ArcadeFlowState previousState;
        readonly Queue<ArcadeInputEvent> unacknowledged = new Queue<ArcadeInputEvent>();
        string fatalError;
        bool oldCursorVisible, continueArmed;
        CursorLockMode oldCursorLock;
        GUIStyle title, body, detail, card;
        double Now => Time.realtimeSinceStartupAsDouble;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() => Instance = null;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            oldCursorVisible = Cursor.visible; oldCursorLock = Cursor.lockState;
            Cursor.visible = false; Cursor.lockState = CursorLockMode.None;
            Application.runInBackground = true;
            var cameraObject = new GameObject("Arcade title camera"); cameraObject.transform.SetParent(transform);
            bootCamera = cameraObject.AddComponent<Camera>(); bootCamera.clearFlags = CameraClearFlags.SolidColor;
            bootCamera.backgroundColor = new Color(.018f, .023f, .04f); bootCamera.depth = -100;
            bootListener = cameraObject.AddComponent<AudioListener>();
            try
            {
#if UNITY_EDITOR
                AutomatedTest = UnityEditor.SessionState.GetBool("ArcadeAutomatedTest", false);
                DataDirectory = AutomatedTest ? UnityEditor.SessionState.GetString("ArcadeTestData", "") :
                    Path.GetFullPath("Validation/Arcade/KeyboardTest");
                if (string.IsNullOrEmpty(DataDirectory)) throw new InvalidOperationException("Missing isolated arcade test directory.");
#else
                DataDirectory = Path.Combine(Application.persistentDataPath, "Arcade");
#endif
                Directory.CreateDirectory(DataDirectory);
                string settingsPath = Path.Combine(DataDirectory, "arcade-settings.json");
                settings = ArcadeSettingsFile.Load(settingsPath, out string diagnostic);
                if (diagnostic != null)
                {
                    if (File.Exists(settingsPath) || File.Exists(settingsPath + ".bak") || File.Exists(settingsPath + ".tmp"))
                        throw new InvalidDataException(diagnostic);
                    ArcadeSettingsFile.Save(settingsPath, settings);
                }
                if (AutomatedTest)
                { settings.ladderStages = 2; settings.roundsToWin = settings.bossRoundsToWin = 1; settings.continueSeconds = 2; settings.victoryMaxSeconds = 1; settings.selectSeconds = 2; settings.loadTimeoutSeconds = 120; }
#if UNITY_EDITOR
                if (AutomatedTest && UnityEditor.SessionState.GetBool("ArcadeRankingTest", false)) settings.nameEntrySeconds = 6;
#endif
                foreach (string code in Codes)
                    if (!Resources.Load<TextAsset>("CommandGraph/" + code.ToLowerInvariant())) throw new InvalidDataException("Missing fighter data: " + code);
                var selected = EnvironmentSelection.Load();
                var stages = (selected.environments ?? Array.Empty<string>()).Where(s => s == EnvironmentSelection.Arena || StageEnvironment.Exists(s)).ToArray();
                if (AutomatedTest || stages.Length == 0) stages = new[] { EnvironmentSelection.Arena };
                Ledger = new ArcadeLedger(Path.Combine(DataDirectory, "arcade-ledger.json"), settings);
                settings = Ledger.Settings; stagePool = stages;
                LoadBindings();
                Ledger.RecoverInterruptedSessions(); CabinetInput = new ArcadeInput(settings, Ledger.Snapshot.lastEventId);
                Rankings = new ArcadeRanking(Path.Combine(DataDirectory, "arcade-ranking.json"));
                Flow = new ArcadeFlow(settings, Ledger, Fighters, stages, Environment.TickCount, Now, Rankings);
                previousState = Flow.State;
                ArchiveResolvedError();
            }
            catch (Exception error) { Fault(error); }
        }

        void Update()
        {
            if (Flow == null || fatalError != null) return;
            double now = Now;
            try
            {
                var controls = ReadKeyboard();
#if UNITY_EDITOR
                if (AutomatedTest) controls = TestKeys == null ? TestControls : bindings.Read(key => TestKeys.Contains(key));
#endif
                CabinetInput.Sample(controls, now);
                if (sceneLoad != null && sceneLoad.isDone) sceneLoad = null;
                bool menuWasOpen = OperatorOpen;
                if (!OperatorOpen) ObserveMatch(now);
                foreach (var item in CabinetInput.DrainEvents()) unacknowledged.Enqueue(item);
                while (unacknowledged.Count > 0)
                {
                    if (Flow.State == ArcadeFlowState.Fault) break;
                    var item = unacknowledged.Peek();
                    if (item.kind == ArcadeEventKind.Test) ToggleOperator(now);
                    // Discard Starts on either opening or closing frame, while coins remain live.
                    if (item.kind != ArcadeEventKind.Start || !menuWasOpen && !OperatorOpen) Flow.Process(item, now);
                    unacknowledged.Dequeue();
                }
                var rising = controls & ~previous;
                if (!OperatorOpen && !menuWasOpen && Flow.State == ArcadeFlowState.Select)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        bool p1 = side == 0;
                        if ((rising & (p1 ? ArcadeControls.P1Left : ArcadeControls.P2Left)) != 0) Flow.MoveSelection(side, -1);
                        if ((rising & (p1 ? ArcadeControls.P1Right : ArcadeControls.P2Right)) != 0) Flow.MoveSelection(side, 1);
                        if ((rising & (p1 ? ArcadeControls.P1B1 : ArcadeControls.P2B1)) != 0) Flow.ConfirmSelection(side, now);
                    }
                }
                if (!OperatorOpen && !menuWasOpen && Flow.State == ArcadeFlowState.Continue)
                {
                    uint buttons = ((uint)controls >> (Flow.ContinueSide * 8 + 4)) & 15;
                    if (previousState != ArcadeFlowState.Continue) continueArmed = buttons == 0;
                    if (buttons == 0) continueArmed = true;
                    else if (continueArmed) Flow.AccelerateContinue(Time.unscaledDeltaTime * 3);
                }
                if (!OperatorOpen && !menuWasOpen && previousState == ArcadeFlowState.NameEntry && Flow.State == ArcadeFlowState.NameEntry)
                {
                    bool p1 = Flow.NameSide == 0;
                    int change = (rising & (p1 ? ArcadeControls.P1Up : ArcadeControls.P2Up)) != 0 ? 1 :
                        (rising & (p1 ? ArcadeControls.P1Down : ArcadeControls.P2Down)) != 0 ? -1 : 0;
                    int move = (rising & (p1 ? ArcadeControls.P1Right : ArcadeControls.P2Right)) != 0 ? 1 :
                        (rising & (p1 ? ArcadeControls.P1Left : ArcadeControls.P2Left)) != 0 ? -1 : 0;
                    if ((rising & (p1 ? ArcadeControls.P1B2 : ArcadeControls.P2B2)) != 0) move = -1;
                    Flow.EditName(Flow.NameSide, change, move);
                    if ((rising & (p1 ? ArcadeControls.P1B1 : ArcadeControls.P2B1)) != 0) Flow.ConfirmName(Flow.NameSide, now);
                }
                if (OperatorOpen && menuWasOpen) UpdateOperator(controls, rising, now);
                previous = controls; RememberRawKeys(); Flow.Advance(now);
                if (Flow.State != previousState)
                {
                    CabinetInput.ResetGameplayForTransition(); previousState = Flow.State;
                    // BeginLoad opens the original VS screen; close it if the match it covers is abandoned.
                    if (Flow.State != ArcadeFlowState.Loading && Flow.State != ArcadeFlowState.LoadingDemo &&
                        Flow.State != ArcadeFlowState.Fight && Flow.State != ArcadeFlowState.Demo) VersusLoadingScreen.Hide();
                }
                var game = VersusGame.ArcadeCurrent;
                if (game && game.ArcadeRequest.State == ArcadeRequestState.Ready)
                {
                    bool playing = !OperatorOpen && Flow.Match != null && game.ArcadeRequest.Setup.MatchId == Flow.Match.MatchId &&
                        (Flow.State == ArcadeFlowState.Fight || Flow.State == ArcadeFlowState.Victory || Flow.State == ArcadeFlowState.Demo);
                    game.SuspendArcadeMatch(!playing);
                    // Continue, game over and the other full-screen arcade pages draw over a dimmed fight:
                    // its K.O./PERFECT calls and their announcer lines must not show or play through them.
                    game.ArcadeCallsHidden = !playing;
                }
                if (!OperatorOpen && Flow.Match != null && sceneLoad == null && (request == null || request.Setup.MatchId != Flow.Match.MatchId)) BeginLoad(Flow.Match);
                StartPendingLoad();
                if (Flow.State == ArcadeFlowState.Fault && fatalError == null) Fault(new InvalidOperationException(Flow.Error));
            }
            catch (Exception error) { Fault(error); }
        }

        void ObserveMatch(double now)
        {
            Flow.ObserveChallengeEligibility(null, false);
            if (request == null) return;
            if (request.State == ArcadeRequestState.Failed) { Flow.Fail("The match could not initialize. See the arcade error log.", now); throw new InvalidOperationException(request.Failure); }
            if (request.State != ArcadeRequestState.Ready || sceneLoad != null) return;
            Flow.MatchReady(request.Setup.MatchId, now);
            var game = VersusGame.ArcadeCurrent;
            if (!game || !ReferenceEquals(game.ArcadeRequest, request)) return;
            Flow.ObserveChallengeEligibility(request.Setup.MatchId, game.battle.phase == MatchPhase.Fight &&
                game.battle.ticksLeft > 0 && game.battle.fighters[0].health > 0 && game.battle.fighters[1].health > 0 && !VersusLoadingScreen.Holding);
            Flow.ObserveFightTicks(request.Setup.MatchId, game.ArcadeFightTicks);
            if (game.ArcadeResult != null) Flow.MatchFinished(game.ArcadeResult.MatchId, game.ArcadeResult.Winner, now,
                game.ArcadeResult.FightTicks, game.ArcadeResult.Winner == 0 ? game.ArcadeResult.PlayerTwoRounds : game.ArcadeResult.PlayerOneRounds);
            if (Flow.State == ArcadeFlowState.Victory && game.ArcadeVictoryComplete) Flow.FinishVictory(now);
        }

        void BeginLoad(ArcadeMatchSetup setup)
        {
            if (request != null && request.State == ArcadeRequestState.Pending) VersusGame.CancelArcadeMatch(request);
            request = VersusGame.PrepareArcadeMatch(setup, CabinetInput, MatchScene);
            bootCamera.enabled = false; bootListener.enabled = false;
            VersusLoadingScreen.Show(Code(setup.PlayerOne), Code(setup.PlayerTwo));
            // Activating the match scene stalls rendering for seconds. Start it once the VS screen has
            // finished its intro, so the stall holds a complete VS frame, not its dim first frame.
            loadAt = Time.unscaledTime + VersusIntroSeconds;
        }

        const float VersusIntroSeconds = .6f; // VersusLoadingScreen: panels fade 0.5 s, VS pops by 0.5 s
        float loadAt = -1;

        void StartPendingLoad()
        {
            if (loadAt < 0 || Time.unscaledTime < loadAt) return;
            loadAt = -1;
#if UNITY_EDITOR
            sceneLoad = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MatchScene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            sceneLoad = SceneManager.LoadSceneAsync(MatchScene, LoadSceneMode.Single);
#endif
            if (sceneLoad == null) throw new InvalidOperationException("The match scene could not be loaded.");
        }

        // A fault report from an earlier run stays as arcade-error.previous.txt once boot succeeds,
        // so arcade-error.txt only ever describes a failure that is still current.
        void ArchiveResolvedError()
        {
            try
            {
                string current = Path.Combine(DataDirectory, "arcade-error.txt");
                if (File.Exists(current)) File.Copy(current, Path.Combine(DataDirectory, "arcade-error.previous.txt"), true);
                if (File.Exists(current)) File.Delete(current);
            }
            catch (Exception error) { Debug.LogWarning("Could not archive the previous arcade error: " + error.Message); }
        }

        void Fault(Exception error)
        {
            fatalError = error.Message;
            try { Flow?.Fail(error.Message, Now); } catch { /* Keep the original failure and ledger evidence. */ }
            try
            {
                if (!string.IsNullOrEmpty(DataDirectory)) File.WriteAllText(Path.Combine(DataDirectory, "arcade-error.txt"),
                    error + "\nUnacknowledged events: " + string.Join(", ", unacknowledged.Select(e => e.id + ":" + e.kind)));
            }
            catch { }
            var game = VersusGame.ArcadeCurrent;
            if (game && game.ArcadeRequest.State == ArcadeRequestState.Ready) game.SuspendArcadeMatch(true);
            Debug.LogException(error, this);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null; Rankings?.Dispose(); Ledger?.Dispose();
            if (request != null) VersusGame.CancelArcadeMatch(request);
            Cursor.visible = oldCursorVisible; Cursor.lockState = oldCursorLock; Time.timeScale = 1;
        }

        static string Code(string fighter)
        { int index = Array.IndexOf(Fighters, fighter); return index < 0 ? "JIN" : Codes[index]; }

        ArcadeControls ReadKeyboard() => bindings == null ? ArcadeControls.None : bindings.Read(KeyHeld);
        static bool KeyHeld(string key) => Input.GetKey((KeyCode)Enum.Parse(typeof(KeyCode), key));

        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                body = new GUIStyle(title) { fontSize = 27 }; detail = new GUIStyle(body) { fontSize = 17, wordWrap = true };
                card = new GUIStyle(body) { fontSize = 17 };
            }
            var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth; GUI.depth = -200;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            try
            {
                var state = Flow?.State ?? ArcadeFlowState.Fault;
                // The original VS screen (VersusLoadingScreen, opened by BeginLoad) covers a match while it loads.
                bool versusScreen = (state == ArcadeFlowState.Loading || state == ArcadeFlowState.LoadingDemo) && VersusLoadingScreen.Showing;
                bool full = state != ArcadeFlowState.Fight && state != ArcadeFlowState.Demo && state != ArcadeFlowState.Victory && !versusScreen;
                if (full) Fill(new Rect(0, 0, 1280, 720), new Color(.018f, .023f, .04f, state == ArcadeFlowState.Continue ? .85f : 1));
                if (full && state != ArcadeFlowState.Continue && state != ArcadeFlowState.Fault && !OperatorOpen) DrawBackdrop();
                if (fatalError != null || state == ArcadeFlowState.Fault)
                { Text(150, "ARCADE STOPPED", title); Text(270, fatalError ?? Flow?.Error, detail, 140); Text(520, "Stop Play, then check the arcade error report before restarting.", detail); return; }
                if (OperatorOpen) { DrawOperator(); return; }
                int remaining = double.IsInfinity(Flow.Deadline) ? 0 : Math.Max(0, (int)Math.Ceiling(Flow.Deadline - Now));
                switch (state)
                {
                    case ArcadeFlowState.Attract: DrawTitle(); break;
                    case ArcadeFlowState.Challenger:
                        Shadowed(new Rect(0, 230, 1280, 90), "A NEW CHALLENGER", 64); Shadowed(new Rect(0, 330, 1280, 50), "GET READY FOR THE NEXT BATTLE", 27); break;
                    case ArcadeFlowState.Select: DrawArcadeSelection(remaining); break;
                    case ArcadeFlowState.Loading: case ArcadeFlowState.LoadingDemo:
                        if (!versusScreen) Shadowed(new Rect(0, 300, 1280, 90), "GET READY", 64); break;
                    case ArcadeFlowState.Demo: DrawMessage(OperationMessage(), 610); break;
                    case ArcadeFlowState.Continue: DrawContinue(remaining); break;
                    case ArcadeFlowState.NameEntry: DrawNameEntry(remaining); break;
                    case ArcadeFlowState.Ranking: DrawRankings(); DrawMessage(OperationMessage(), 655, .6f); break;
                    case ArcadeFlowState.GameOver: Shadowed(new Rect(0, 280, 1280, 90), "GAME OVER", 72); break;
                    case ArcadeFlowState.Ending:
                        { var logo = ArcadeArt("Logo"); if (logo) Centred(logo, new Vector2(640, 170), 1); }
                        Shadowed(new Rect(0, 300, 1280, 90), "CONGRATULATIONS", 64); Shadowed(new Rect(0, 395, 1280, 50), "YOU CLEARED THE ARCADE LADDER", 27); break;
                }
                if (!Rankings.Available) Text(615, "RANKINGS UNAVAILABLE - SEE OPERATOR DIAGNOSTICS", detail, 35);
                if (!versusScreen) DrawCreditBar(state);
            }
            finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
        }

        static void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        static void Text(float y, string value, GUIStyle style, float height = 90) => GUI.Label(new Rect(40, y, 1200, height), value, style);
    }
}
