using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Versus.Remaster.Arcade;

namespace Versus.Remaster.Editor
{
    public static class ArcadeRankingCheck
    {
        const string Output = "Validation/Arcade";
        static readonly Queue<ArcadeControls> presses = new Queue<ArcadeControls>();
        static int phase, frame;
        static bool inputActive, wonRun, lostRun, pausedName, stored, cleared;
        static double waitUntil;
        [Serializable] sealed class Report
        {
            public bool passed, endingNameObserved, gameOverNameObserved, namePauseAndCreditIsolation, persistedEntriesObserved, confirmedClearObserved;
            public string error, utc, dataDirectory;
            public string scope = "Actual Unity: two-stage ladder ending and later game-over name entry; logical initials, timeout initials, F2 pause, coins/ignored Starts, saved ranked scores and confirmed operator table clear. KOs forced for testing; physical devices not simulated.";
        }
        public static void Reset()
        { phase = 0; presses.Clear(); inputActive = wonRun = lostRun = pausedName = stored = cleared = false; }
        static void Press(params ArcadeControls[] controls)
        { foreach (var c in controls) { presses.Enqueue(c); presses.Enqueue(0); } }
        static bool Pump(ArcadeDirector director)
        {
            if (inputActive && Time.frameCount - frame < 3) return true;
            inputActive = false;
            if (presses.Count == 0) return false;
            director.TestControls = presses.Dequeue(); frame = Time.frameCount; inputActive = true; return true;
        }
        public static void Tick(ArcadeDirector director)
        {
            if (Pump(director)) return;
            var flow = director.Flow; var game = VersusGame.ArcadeCurrent;
            bool fighting = flow.State == ArcadeFlowState.Fight && game && game.battle.phase == MatchPhase.Fight;
            switch (phase)
            {
                case 0:
                    if (flow.State != ArcadeFlowState.Attract) return;
                    Press(ArcadeControls.Coin1 | ArcadeControls.Start1); phase++; break;
                case 1:
                    if (!fighting) return;
                    game.battle.fighters[1].health = 0; phase++; break;
                case 2:
                    if (!fighting || flow.StageIndex != 1) return;
                    game.battle.fighters[1].health = 0; phase++; break;
                case 3:
                    if (flow.State != ArcadeFlowState.NameEntry) return;
                    wonRun = flow.NameSide == 0 && director.Ledger.Snapshot.sessions[0] == null;
                    Require(wonRun, "Ending did not close entitlement before name entry");
                    Press(ArcadeControls.Test | ArcadeControls.Coin2 | ArcadeControls.Start2); phase++; break;
                case 4:
                    Require(director.OperatorOpen, "Name Test did not open"); waitUntil = Time.realtimeSinceStartupAsDouble + 7; phase++; break;
                case 5:
                    if (Time.realtimeSinceStartupAsDouble < waitUntil) return;
                    pausedName = flow.State == ArcadeFlowState.NameEntry && director.Ledger.Snapshot.starts == 1 && director.Ledger.VisibleCredits(0) == 1;
                    Require(pausedName, "Operator time or Start changed pending score"); Press(ArcadeControls.Test); phase++; break;
                case 6:
                    Require(!director.OperatorOpen && flow.State == ArcadeFlowState.NameEntry, "Name did not resume");
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-name-entry.png"); frame = Time.frameCount; phase++; break;
                case 7:
                    if (Time.frameCount < frame + 2) return;
                    Press(ArcadeControls.P1Up, ArcadeControls.P1B1, ArcadeControls.P1B1, ArcadeControls.P1B1); phase++; break;
                case 8:
                    Require(flow.State == ArcadeFlowState.Ranking, "Initials did not submit");
                    var first = director.Rankings.Table(flow.RankingRules);
                    Require(first.Length == 1 && first[0].initials == "BAA" && first[0].stagesCleared == 2 && first[0].fightTicks > 0 && first[0].cleanWins == 2, "Ending score metrics/name");
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-rankings.png"); phase++; break;
                case 9:
                    if (flow.State != ArcadeFlowState.Attract) return;
                    Press(ArcadeControls.Start1); phase++; break;
                case 10:
                    if (!fighting) return;
                    game.battle.fighters[1].health = 0; phase++; break;
                case 11:
                    if (!fighting || flow.StageIndex != 1) return;
                    game.battle.fighters[0].health = 0; phase++; break;
                case 12:
                    if (flow.State != ArcadeFlowState.NameEntry) return;
                    lostRun = flow.NameSide == 0 && director.Ledger.Snapshot.sessions[0] == null;
                    Require(lostRun, "Game over did not enter initials");
                    Press(ArcadeControls.Coin1 | ArcadeControls.Start1 | ArcadeControls.P2B1); phase++; break;
                case 13:
                    if (flow.State != ArcadeFlowState.Ranking) return;
                    var table = director.Rankings.Table(flow.RankingRules);
                    Require(table.Length == 2 && table[0].initials == "BAA" && table[1].initials == "AAA" && table[1].stagesCleared == 1, "Timeout score/order");
                    var envelope = ArcadeJson.Decode<ArcadeAtomicFile<ArcadeRankingState>.Envelope>(File.ReadAllText(Path.Combine(director.DataDirectory, "arcade-ranking.json")));
                    var saved = ArcadeJson.Decode<ArcadeRankingState>(envelope.payload); saved.Validate();
                    stored = saved.entries.Length == 2 && director.Ledger.VisibleCredits(0) == 1 && director.Ledger.Snapshot.starts == 2;
                    Require(stored, "Disk ranking / name-screen payment isolation");
                    Press(ArcadeControls.Test, ArcadeControls.P1Up, ArcadeControls.P1B1); phase++; break;
                case 14:
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-operator-rankings.png"); frame = Time.frameCount; phase++; break;
                case 15:
                    if (Time.frameCount < frame + 2) return;
                    Press(ArcadeControls.P1Up, ArcadeControls.P1B1); phase++; break;
                case 16:
                    Require(director.Rankings.Table(flow.RankingRules).Length == 2, "Rankings cleared before confirmation");
                    Press(ArcadeControls.P1Down, ArcadeControls.P1B1); phase++; break;
                case 17:
                    cleared = director.Rankings.Table(flow.RankingRules).Length == 0 && director.Ledger.VisibleCredits(0) == 1;
                    Require(cleared, "Confirmed table clear damaged credit"); Press(ArcadeControls.Test); phase++; break;
                case 18: Require(!director.OperatorOpen, "Test did not close"); Finish(null, director); break;
            }
        }
        static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        public static void Finish(Exception error, ArcadeDirector director)
        {
            File.WriteAllText(Output + "/ranking-play-validation.json", JsonUtility.ToJson(new Report {
                passed = error == null && wonRun && lostRun && pausedName && stored && cleared,
                endingNameObserved = wonRun, gameOverNameObserved = lostRun, namePauseAndCreditIsolation = pausedName,
                persistedEntriesObserved = stored, confirmedClearObserved = cleared, error = error?.ToString(),
                utc = DateTime.UtcNow.ToString("O"), dataDirectory = director ? director.DataDirectory : null }, true));
            SessionState.SetBool("ArcadeRankingTest", false); SessionState.SetBool("ArcadeAutomatedTest", false); EditorApplication.isPlaying = false;
        }
    }
}
