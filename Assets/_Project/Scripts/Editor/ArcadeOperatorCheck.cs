using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Versus.Remaster.Arcade;

namespace Versus.Remaster.Editor
{
    public static class ArcadeOperatorCheck
    {
        const string Output = "Validation/Arcade";
        sealed class Step { public ArcadeControls controls; public string[] keys; }
        static readonly Queue<Step> steps = new Queue<Step>();
        static Step active;
        static int frame, phase, ticks;
        static double waitUntil, remaining;
        static long session;
        static string matchId;
        static bool countdown, coins, remapped, fightPaused, resumed, applied, cleared;
        [Serializable] sealed class Report
        {
            public bool passed, countdownPreserved, coinStartIsolation, remappingPersisted, fightPauseResume, settingsApplied, bookkeepingPreservedCredit;
            public string error, utc, dataDirectory;
            public string scope = "Actual Unity operator menu via logical controls, injected key names through the remap adapter, paused select/fight, live coin and ignored Start, saved binding, settings Apply and bookkeeping confirmation. Test KOs; no physical keyboard/cabinet verification.";
        }
        public static void Reset()
        { phase = 0; active = null; steps.Clear(); countdown = coins = remapped = fightPaused = resumed = applied = cleared = false; }
        static void Press(params ArcadeControls[] values)
        { foreach (var value in values) { steps.Enqueue(new Step { controls = value }); steps.Enqueue(new Step()); } }
        static void Keys(params string[] keys)
        { steps.Enqueue(new Step { keys = keys }); steps.Enqueue(new Step { keys = Array.Empty<string>() }); }
        static bool Pump(ArcadeDirector director)
        {
            if (active != null)
            { if (Time.frameCount - frame < 3) return true; active = null; }
            if (steps.Count == 0) return false;
            active = steps.Dequeue(); director.TestControls = active.controls; director.TestKeys = active.keys; frame = Time.frameCount; return true;
        }
        public static void Tick(ArcadeDirector director)
        {
            if (Pump(director)) return;
            var flow = director.Flow; var game = VersusGame.ArcadeCurrent;
            switch (phase)
            {
                case 0:
                    if (flow.State != ArcadeFlowState.Attract) return;
                    Press(ArcadeControls.Coin1 | ArcadeControls.Start1); phase++; break;
                case 1:
                    if (flow.State != ArcadeFlowState.Select) return;
                    session = flow.SessionId; remaining = flow.Deadline - Time.realtimeSinceStartupAsDouble;
                    Press(ArcadeControls.Test | ArcadeControls.Coin2 | ArcadeControls.Start2); phase++; break;
                case 2:
                    Require(director.OperatorOpen, "Test did not open operator menu");
                    waitUntil = Time.realtimeSinceStartupAsDouble + 3;
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-operator-home.png"); phase++; break;
                case 3:
                    if (Time.realtimeSinceStartupAsDouble < waitUntil) return;
                    countdown = flow.State == ArcadeFlowState.Select && flow.SessionId == session;
                    coins = director.Ledger.VisibleCredits(0) == 1 && director.Ledger.Snapshot.starts == 1 && !flow.TwoPlayers;
                    Require(countdown && coins, "Paused countdown or Test/coin/Start arbitration");
                    Press(ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1B1,
                        ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1B1);
                    Keys("Q"); phase++; break;
                case 4:
                    Require(director.Bindings.Label(ArcadeControls.P1B1) == "Q", "Menu capture did not bind Q");
                    using (var file = new ArcadeAtomicFile<ArcadeBindings>(Path.Combine(director.DataDirectory, "arcade-input.json"), b => b.Validate()))
                        remapped = file.Load().Label(ArcadeControls.P1B1) == "Q";
                    Require(remapped, "Binding not persisted");
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-operator-input.png");
                    Press(ArcadeControls.P1B2, ArcadeControls.P1B1); phase++; break;
                case 5:
                    Require(!director.OperatorOpen && flow.State == ArcadeFlowState.Select && flow.Deadline > Time.realtimeSinceStartupAsDouble, "Selection failed to resume");
                    Keys("Q"); phase++; break;
                case 6:
                    if (flow.State != ArcadeFlowState.Fight || !game || game.battle.phase != MatchPhase.Fight) return;
                    matchId = flow.Match.MatchId; Press(ArcadeControls.Test); phase++; break;
                case 7:
                    Require(director.OperatorOpen, "Fight Test not opened"); ticks = game.battle.ticksLeft;
                    waitUntil = Time.realtimeSinceStartupAsDouble + 2;
                    Press(ArcadeControls.Coin1 | ArcadeControls.Start2); phase++; break;
                case 8:
                    if (Time.realtimeSinceStartupAsDouble < waitUntil) return;
                    fightPaused = game.battle.ticksLeft == ticks && flow.SessionId == session && flow.Match.MatchId == matchId && !flow.TwoPlayers;
                    Require(fightPaused && director.Ledger.VisibleCredits(0) == 2, "Fight advanced or Start charged in menu");
                    Press(ArcadeControls.Test); phase++; break;
                case 9:
                    if (game.battle.ticksLeft >= ticks - 10) return;
                    resumed = !director.OperatorOpen && flow.Match.MatchId == matchId && flow.SessionId == session;
                    Require(resumed, "Fight did not resume its paid session"); game.battle.fighters[0].health = 0; phase++; break;
                case 10:
                    if (flow.State != ArcadeFlowState.Attract) return;
                    Press(ArcadeControls.Test); phase++; break;
                case 11:
                    // Home -> Coin settings -> toggle Free play -> Home -> Apply -> Confirm.
                    Press(ArcadeControls.P1Down, ArcadeControls.P1B1, ArcadeControls.P1Right, ArcadeControls.P1B2,
                        ArcadeControls.P1Up, ArcadeControls.P1Up, ArcadeControls.P1B1, ArcadeControls.P1Down, ArcadeControls.P1B1); phase++; break;
                case 12:
                    applied = !director.OperatorOpen && director.CurrentSettings.freePlay && director.Ledger.Snapshot.operatorSettings.freePlay && director.Ledger.VisibleCredits(0) == 2;
                    Require(applied, "Settings Apply did not preserve credit / close menu");
                    Press(ArcadeControls.Test); phase++; break;
                case 13:
                    // Home -> Bookkeeping. Up twice wraps from 0 to Clear bookkeeping (row 10).
                    Press(ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1Down, ArcadeControls.P1B1);
                    phase++; break;
                case 14:
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-operator-bookkeeping.png");
                    Press(ArcadeControls.P1Up, ArcadeControls.P1Up, ArcadeControls.P1B1); phase++; break;
                case 15:
                    Require(director.Ledger.Snapshot.starts == 1, "Counter reset before confirmation");
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-operator-confirm.png");
                    Press(ArcadeControls.P1Down, ArcadeControls.P1B1); phase++; break;
                case 16:
                    cleared = director.Ledger.Snapshot.starts == 0 && director.Ledger.Snapshot.bookkeepingClears == 1 && director.Ledger.VisibleCredits(0) == 2;
                    Require(cleared, "Bookkeeping reset consumed credits");
                    Press(ArcadeControls.Test); phase++; break;
                case 17: Require(!director.OperatorOpen, "Operator menu failed to close"); Finish(null, director); break;
            }
        }
        static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        public static void Finish(Exception error, ArcadeDirector director)
        {
            File.WriteAllText(Output + "/operator-play-validation.json", JsonUtility.ToJson(new Report {
                passed = error == null && countdown && coins && remapped && fightPaused && resumed && applied && cleared,
                countdownPreserved = countdown, coinStartIsolation = coins, remappingPersisted = remapped,
                fightPauseResume = fightPaused && resumed, settingsApplied = applied, bookkeepingPreservedCredit = cleared,
                error = error?.ToString(), utc = DateTime.UtcNow.ToString("O"), dataDirectory = director ? director.DataDirectory : null }, true));
            SessionState.SetBool("ArcadeAutomatedTest", false); SessionState.SetBool("ArcadeOperatorTest", false); EditorApplication.isPlaying = false;
        }
    }
}
