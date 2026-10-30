using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Versus.Remaster.Arcade;

namespace Versus.Remaster.Editor
{
    [InitializeOnLoad]
    public static class ArcadePlaytest
    {
        const string Output = "Validation/Arcade";
        static int phase, startTicks;
        static float startX;
        static long firstSession;
        static string firstOpponent, firstStage;
        static bool moved, continued, gameOver;
        [Serializable] sealed class Report
        {
            public bool passed, movementObserved, continueObserved, gameOverObserved;
            public string error, utc, dataDirectory;
            public string scope = "Unity Play: boot, coin/start, timed select, actual match readiness, human movement, forced test KO, paid continue, game over and title return. Keyboard device delivery and physical cabinet not simulated.";
        }

        static ArcadePlaytest()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool("ArcadeRestoreStartScene", false)) return;
                string previous = SessionState.GetString("ArcadePreviousStartScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
                SessionState.SetBool("ArcadeRestoreStartScene", false); SessionState.SetBool("ArcadeAutomatedTest", false);
            };
        }

        [MenuItem("Remaster/Arcade/Play keyboard test")]
        public static void PlayKeyboard() => Start(false);
        [MenuItem("Remaster/Arcade/Run automated Play check")]
        public static void PlayCheck() => Start(true);
        [MenuItem("Remaster/Arcade/Run challenger Play check")]
        public static void ChallengerCheck() => Start(true, true);
        [MenuItem("Remaster/Arcade/Run operator Play check")]
        public static void OperatorCheck() => Start(true, false, true);
        [MenuItem("Remaster/Arcade/Run saved ledger Play check")]
        public static void LegacyCheck() => Start(true, false, false, true);
        [MenuItem("Remaster/Arcade/Run ranking Play check")]
        public static void RankingCheck() => Start(true, false, false, false, true);
        [MenuItem("Remaster/Arcade/Create Boot scene")]
        public static void EnsureBoot()
        {
            if (File.Exists(ArcadeDirector.BootScene)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Create the Boot scene outside Play mode.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var host = new GameObject("Arcade director"); SceneManager.MoveGameObjectToScene(host, scene); host.AddComponent<ArcadeDirector>();
                if (!EditorSceneManager.SaveScene(scene, ArcadeDirector.BootScene)) throw new IOException("Could not save ArcadeBoot.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (previous.IsValid()) SceneManager.SetActiveScene(previous); }
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/boot-scene.txt", ArcadeDirector.BootScene);
        }

        static void Start(bool automated, bool challenger = false, bool operatorCheck = false, bool legacyCheck = false, bool rankingCheck = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play and let Unity finish compiling first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scene changes before starting the arcade test.");
            EnsureBoot(); Directory.CreateDirectory(Output);
            SessionState.SetString("ArcadePreviousStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool("ArcadeRestoreStartScene", true);
            SessionState.SetBool("ArcadeAutomatedTest", automated);
            SessionState.SetBool("ArcadeChallengerTest", challenger);
            ArcadeChallengerCheck.Reset();
            SessionState.SetBool("ArcadeOperatorTest", operatorCheck);
            ArcadeOperatorCheck.Reset();
            SessionState.SetBool("ArcadeLegacyTest", legacyCheck);
            SessionState.SetBool("ArcadeRankingTest", rankingCheck);
            ArcadeRankingCheck.Reset();
            SessionState.SetString("ArcadeTestData", Path.GetFullPath(Output + "/PlayChecks/" + Guid.NewGuid().ToString("N")));
            if (legacyCheck)
            {
                string data = SessionState.GetString("ArcadeTestData", ""); Directory.CreateDirectory(data);
                File.Copy("Tools/ArcadeHarness/Fixtures/ledger-before-operator.json", Path.Combine(data, "arcade-ledger.json"));
            }
            SessionState.SetFloat("ArcadePlayCheckStart", (float)EditorApplication.timeSinceStartup);
            phase = 0; moved = continued = gameOver = false;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ArcadeDirector.BootScene);
            EditorApplication.isPlaying = true;
        }

        static void Poll()
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                foreach (string flag in new[] { "Temp/setup-arcade", "Temp/check-arcade-play", "Temp/play-arcade", "Temp/check-arcade-challenger", "Temp/check-arcade-operator", "Temp/check-arcade-legacy", "Temp/check-arcade-ranking" })
                {
                    if (!File.Exists(flag)) continue;
                    File.Delete(flag);
                    try { if (flag.EndsWith("setup-arcade")) EnsureBoot(); else Start(flag.Contains("check-arcade-"), flag.EndsWith("check-arcade-challenger"), flag.EndsWith("check-arcade-operator"), flag.EndsWith("check-arcade-legacy"), flag.EndsWith("check-arcade-ranking")); }
                    catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/editor-action-error.txt", e.ToString()); Debug.LogException(e); }
                }
            }
            if (!EditorApplication.isPlaying || !SessionState.GetBool("ArcadeAutomatedTest", false)) return;
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat("ArcadePlayCheckStart", 0) > 600) throw new TimeoutException("Arcade Play check exceeded ten minutes.");
                var director = ArcadeDirector.Instance; if (!director) return;
                var flow = director.Flow; if (flow == null || flow.State == ArcadeFlowState.Fault) throw new Exception(flow?.Error ?? "Boot failed; inspect arcade-error.txt.");
                if (SessionState.GetBool("ArcadeRankingTest", false)) { ArcadeRankingCheck.Tick(director); return; }
                if (SessionState.GetBool("ArcadeLegacyTest", false)) { TickLegacy(director); return; }
                if (SessionState.GetBool("ArcadeOperatorTest", false)) { ArcadeOperatorCheck.Tick(director); return; }
                if (SessionState.GetBool("ArcadeChallengerTest", false)) { ArcadeChallengerCheck.Tick(director); return; }
                var game = VersusGame.ArcadeCurrent;
                switch (phase)
                {
                    case 0:
                        if (flow.State != ArcadeFlowState.Attract) return;
                        director.TestControls = ArcadeControls.Coin1 | ArcadeControls.Start1; phase = 1; break;
                    case 1:
                        if (flow.State != ArcadeFlowState.Select) return;
                        director.TestControls = 0; firstSession = flow.SessionId;
                        ScreenCapture.CaptureScreenshot(Output + "/arcade-select.png"); phase = 2; break;
                    case 2:
                        if (flow.State != ArcadeFlowState.Fight || !game || game.battle.phase != MatchPhase.Fight) return;
                        if (game.ArcadeRequest.State != ArcadeRequestState.Ready || director.Ledger.VisibleCredits(0) != 0) throw new Exception("Ready/debit contract failed.");
                        firstOpponent = flow.Match.PlayerTwo; firstStage = game.StageName;
                        startX = game.battle.fighters[0].x; startTicks = game.battle.ticksLeft;
                        director.TestControls = ArcadeControls.P1Right; phase = 3; break;
                    case 3:
                        if (game.battle.ticksLeft > startTicks - 15) return;
                        moved = Math.Abs(game.battle.fighters[0].x - startX) > .01f;
                        if (!moved) throw new Exception("Human movement did not reach the actual match.");
                        ScreenCapture.CaptureScreenshot(Output + "/arcade-fight.png");
                        director.TestControls = 0; game.battle.fighters[0].health = 0; phase = 4; break;
                    case 4:
                        if (flow.State != ArcadeFlowState.Continue) return;
                        if (flow.SessionId != 0 || director.Ledger.Snapshot.sessions[0] != null) throw new Exception("Lost entitlement was not closed.");
                        director.TestControls = ArcadeControls.Coin1 | ArcadeControls.Start1; phase = 5; break;
                    case 5:
                        if (flow.State != ArcadeFlowState.Select) return;
                        director.TestControls = ArcadeControls.P1B1; phase = 6; break;
                    case 6:
                        director.TestControls = 0;
                        if (flow.State != ArcadeFlowState.Fight || !game || game.battle.phase != MatchPhase.Fight) return;
                        continued = flow.SessionId != firstSession && flow.Match.PlayerTwo == firstOpponent && game.StageName == firstStage && director.Ledger.Snapshot.continues == 1;
                        if (!continued) throw new Exception("Paid continue changed the stage/opponent or reused the debit.");
                        game.battle.fighters[0].health = 0; phase = 7; break;
                    case 7: if (flow.State == ArcadeFlowState.Continue) { ScreenCapture.CaptureScreenshot(Output + "/arcade-continue.png"); phase = 8; } break;
                    case 8: if (flow.State == ArcadeFlowState.GameOver) { gameOver = true; phase = 9; } break;
                    case 9: if (flow.State == ArcadeFlowState.Attract) Finish(null, director); break;
                }
            }
            catch (Exception e) { Finish(e, ArcadeDirector.Instance); }
        }

        [Serializable] sealed class LegacyReport
        {
            public bool passed;
            public string error, utc, dataDirectory;
            public string scope = "Actual Unity Boot and F2 operator screen loaded an unchanged pre-M5 ledger fixture: 13 credits, coin/service/start counters, generation and event ID preserved.";
        }
        static void TickLegacy(ArcadeDirector director)
        {
            if (phase == 0)
            {
                var state = director.Ledger.Snapshot;
                if (director.Flow.State != ArcadeFlowState.Attract || state.credits[0] != 13 || state.credits[1] != 0 ||
                    state.coins[0] != 6 || state.coins[1] != 1 || state.serviceCredits != 8 || state.starts != 2 ||
                    state.generation != 21 || state.lastEventId != 17 || state.operatorSettings != null ||
                    state.bookkeepingClears != 0 || state.creditClears != 0)
                    throw new Exception("Legacy ledger did not boot with its original values.");
                if (File.ReadAllText(Path.Combine(director.DataDirectory, "arcade-ledger.json")) != File.ReadAllText("Tools/ArcadeHarness/Fixtures/ledger-before-operator.json"))
                    throw new Exception("Loading rewrote the legacy ledger.");
                director.TestControls = ArcadeControls.Test; phase = 1; return;
            }
            if (phase == 1)
            {
                if (!director.OperatorOpen) return;
                director.TestControls = 0; ScreenCapture.CaptureScreenshot(Output + "/arcade-legacy-operator.png");
                startTicks = Time.frameCount; phase = 2; return;
            }
            if (Time.frameCount >= startTicks + 2) FinishLegacy(null, director);
        }
        static void FinishLegacy(Exception error, ArcadeDirector director)
        {
            File.WriteAllText(Output + "/legacy-ledger-play-validation.json", JsonUtility.ToJson(new LegacyReport {
                passed = error == null && phase == 2, error = error?.ToString(), utc = DateTime.UtcNow.ToString("O"),
                dataDirectory = director ? director.DataDirectory : null }, true));
            SessionState.SetBool("ArcadeLegacyTest", false); SessionState.SetBool("ArcadeAutomatedTest", false); EditorApplication.isPlaying = false;
        }

        static void Finish(Exception error, ArcadeDirector director)
        {
            if (SessionState.GetBool("ArcadeRankingTest", false)) { ArcadeRankingCheck.Finish(error, director); return; }
            if (SessionState.GetBool("ArcadeLegacyTest", false)) { FinishLegacy(error, director); return; }
            if (SessionState.GetBool("ArcadeOperatorTest", false)) { ArcadeOperatorCheck.Finish(error, director); return; }
            if (SessionState.GetBool("ArcadeChallengerTest", false)) { ArcadeChallengerCheck.Finish(error, director); return; }
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/play-validation.json", JsonUtility.ToJson(new Report {
                passed = error == null && moved && continued && gameOver, error = error?.ToString(), utc = DateTime.UtcNow.ToString("O"),
                movementObserved = moved, continueObserved = continued, gameOverObserved = gameOver, dataDirectory = director ? director.DataDirectory : null }, true));
            SessionState.SetBool("ArcadeAutomatedTest", false); EditorApplication.isPlaying = false;
        }
    }
}
