using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Versus.Remaster.Arcade;

namespace Versus.Remaster.Editor
{
    // Runs through ArcadePlaytest's isolated Boot launch and restores its editor start scene.
    public static class ArcadeChallengerCheck
    {
        const string Output = "Validation/Arcade";
        static int phase, ticks, selectionFrame;
        static float x1, x2;
        static string opponent, stage;
        static long incumbent, challenger;
        static VersusGame suspendedGame;
        static bool joined, moved, continued, resumed, direct;
        [Serializable] sealed class Report
        {
            public bool passed, challengerObserved, bothHumansMoved, loserContinueObserved, winnerLadderObserved, directVersusObserved;
            public string error, utc, dataDirectory;
            public string scope = "Actual Unity Boot and match scenes; logical inputs, forced test KOs. Challenger suspends incumbent, both humans move, loser continues, challenger winner inherits ladder, then direct versus winner starts new ladder. Physical keyboard/cabinet not simulated.";
        }
        public static void Reset()
        { phase = 0; joined = moved = continued = resumed = direct = false; suspendedGame = null; }

        public static void Tick(ArcadeDirector director)
        {
            var flow = director.Flow;
            var game = VersusGame.ArcadeCurrent;
            bool fighting = flow.State == ArcadeFlowState.Fight && game && game.battle.phase == MatchPhase.Fight;
            switch (phase)
            {
                case 0:
                    if (flow.State != ArcadeFlowState.Attract) return;
                    director.TestControls = ArcadeControls.Coin1 | ArcadeControls.Start1; phase++; break;
                case 1:
                    if (flow.State != ArcadeFlowState.Select) return;
                    director.TestControls = 0; incumbent = flow.SessionId; phase++; break;
                case 2:
                    if (!fighting) return;
                    opponent = flow.Match.PlayerTwo; stage = game.StageName; suspendedGame = game;
                    director.TestControls = ArcadeControls.Coin2 | ArcadeControls.Start2; phase++; break;
                case 3:
                    if (flow.State != ArcadeFlowState.Challenger) return;
                    director.TestControls = 0; ticks = game.battle.ticksLeft; challenger = flow.SessionFor(1);
                    Require(flow.SessionFor(0) == incumbent && challenger != 0 && director.Ledger.Snapshot.challenges == 1, "Challenge debit/ownership");
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-challenger.png"); phase++; break;
                case 4:
                    Require(suspendedGame.battle.ticksLeft == ticks, "Old fight continued under challenger call");
                    if (flow.State != ArcadeFlowState.Select) return;
                    joined = true; director.TestControls = ArcadeControls.P2Right;
                    selectionFrame = Time.frameCount;
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-two-player-select.png"); phase++; break;
                case 5:
                    if (flow.Fighter(1) != "Kazuya" || Time.frameCount <= selectionFrame + 1) return;
                    director.TestControls = ArcadeControls.P1B1 | ArcadeControls.P2B1;
                    phase++; break;
                case 6:
                    if (flow.State == ArcadeFlowState.Select) return;
                    director.TestControls = 0;
                    if (!fighting) return;
                    Require(!flow.Match.CpuOne && !flow.Match.CpuTwo, "Versus controller routing");
                    x1 = game.battle.fighters[0].x; x2 = game.battle.fighters[1].x; ticks = game.battle.ticksLeft;
                    director.TestControls = ArcadeControls.P1Right | ArcadeControls.P2Left; phase++; break;
                case 7:
                    if (game.battle.ticksLeft > ticks - 15) return;
                    moved = Math.Abs(game.battle.fighters[0].x - x1) > .01f && Math.Abs(game.battle.fighters[1].x - x2) > .01f;
                    Require(moved, "Both humans must move in the actual match");
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-two-player-fight.png");
                    director.TestControls = 0; game.battle.fighters[0].health = 0; phase++; break;
                case 8:
                    if (flow.State != ArcadeFlowState.Continue) return;
                    Require(flow.ContinueSide == 0 && flow.SessionFor(0) == 0 && flow.SessionFor(1) == challenger, "Loser/winner settlement");
                    director.TestControls = ArcadeControls.Coin1 | ArcadeControls.Start1; phase++; break;
                case 9:
                    if (flow.State != ArcadeFlowState.Select) return;
                    director.TestControls = 0; phase++; break;
                case 10:
                    if (!fighting) return;
                    continued = flow.TwoPlayers && !flow.Match.CpuOne && !flow.Match.CpuTwo && director.Ledger.Snapshot.continues == 1 && flow.SessionFor(0) != incumbent;
                    Require(continued, "Loser rematch payment/control");
                    game.battle.fighters[0].health = 0; phase++; break;
                case 11:
                    if (flow.State != ArcadeFlowState.Continue) return;
                    ScreenCapture.CaptureScreenshot(Output + "/arcade-loser-continue.png"); phase++; break;
                case 12:
                    if (!fighting || flow.TwoPlayers) return;
                    resumed = flow.HumanSide == 1 && flow.SessionId == challenger && flow.StageIndex == 0 && flow.Match.PlayerOne == opponent && game.StageName == stage && flow.Match.CpuOne && !flow.Match.CpuTwo;
                    Require(resumed, "Challenger winner did not inherit original ladder");
                    game.battle.fighters[1].health = 0; phase++; break;
                case 13:
                    if (flow.State != ArcadeFlowState.Attract) return;
                    director.TestControls = ArcadeControls.Coin1 | ArcadeControls.Coin2 | ArcadeControls.Start1 | ArcadeControls.Start2; phase++; break;
                case 14:
                    if (flow.State != ArcadeFlowState.Select) return;
                    Require(flow.TwoPlayers && director.Ledger.Snapshot.starts == 3, "Simultaneous direct starts");
                    director.TestControls = 0; phase++; break;
                case 15:
                    if (!fighting) return;
                    Require(!flow.Match.CpuOne && !flow.Match.CpuTwo, "Direct match is not two human");
                    incumbent = flow.SessionFor(0); game.battle.fighters[1].health = 0; phase++; break;
                case 16:
                    if (flow.State != ArcadeFlowState.Continue) return;
                    Require(flow.ContinueSide == 1, "Wrong direct loser continue side"); phase++; break;
                case 17:
                    if (!fighting || flow.TwoPlayers) return;
                    direct = flow.HumanSide == 0 && flow.SessionId == incumbent && flow.StageIndex == 0 && !flow.Match.CpuOne && flow.Match.CpuTwo;
                    Require(direct, "Direct winner new ladder"); Finish(null, director); break;
            }
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Finish(Exception error, ArcadeDirector director)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/challenger-play-validation.json", JsonUtility.ToJson(new Report {
                passed = error == null && joined && moved && continued && resumed && direct,
                challengerObserved = joined, bothHumansMoved = moved, loserContinueObserved = continued,
                winnerLadderObserved = resumed, directVersusObserved = direct, error = error?.ToString(),
                utc = DateTime.UtcNow.ToString("O"), dataDirectory = director ? director.DataDirectory : null }, true));
            SessionState.SetBool("ArcadeAutomatedTest", false); SessionState.SetBool("ArcadeChallengerTest", false);
            EditorApplication.isPlaying = false;
        }
    }
}
