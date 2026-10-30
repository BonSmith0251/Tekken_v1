using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Versus.Remaster.Arcade;

static class FlowChecks
{
    [Serializable] public sealed class Case { public string name, error; public bool passed; }
    [Serializable] public sealed class Report { public bool passed; public string utc; public Case[] cases; }
    sealed class Fixture : IDisposable
    {
        public readonly ArcadeLedger ledger;
        public readonly ArcadeFlow flow;
        public readonly string LedgerPath;
        public Fixture(ArcadeSettings settings = null)
        {
            settings ??= new ArcadeSettings();
            LedgerPath = Path.Combine(root, Guid.NewGuid().ToString("N"), "ledger.json");
            ledger = new ArcadeLedger(LedgerPath, settings);
            flow = new ArcadeFlow(settings, ledger, new[] { "Jin", "Kazuya", "Xiaoyu", "Alisa" }, new[] { "arena", "stg29" }, 41, 0);
        }
        public void Coin(long id, double now, int side = 0) => flow.Process(new ArcadeInputEvent(id, now, ArcadeEventKind.Coin, side), now);
        public void Start(long id, double now, int side = 0) => flow.Process(new ArcadeInputEvent(id, now, ArcadeEventKind.Start, side), now);
        public void Fight(int side = 0)
        { Coin(1, 0, side); Start(2, 0, side); flow.ConfirmSelection(1); flow.MatchReady(flow.Match.MatchId, 2); }
        public void Lose()
        { Fight(); flow.MatchFinished(flow.Match.MatchId, 1, 3); flow.FinishVictory(4); }
        public void Dispose() => ledger.Dispose();
    }
    static Fixture Direct()
    {
        var f = new Fixture(); f.Coin(1, 0); f.Coin(2, 0, 1); f.Start(3, 0); f.Start(4, 0, 1); return f;
    }
    static void Join(Fixture f, double now)
    {
        f.flow.ObserveChallengeEligibility(f.flow.Match.MatchId, true); f.Coin(3, now, 1); f.Start(4, now, 1);
    }
    static Fixture ChallengedFight(ArcadeSettings settings = null)
    {
        var f = new Fixture(settings); f.Fight(); Join(f, 3); f.flow.Advance(5);
        f.flow.ConfirmSelection(0, 6); f.flow.ConfirmSelection(1, 6); f.flow.MatchReady(f.flow.Match.MatchId, 7); return f;
    }
    static string root;
    static readonly List<Case> cases = new List<Case>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Run(string name, Action action)
    {
        var item = new Case { name = name };
        try { action(); item.passed = true; Console.WriteLine("PASS " + name); }
        catch (Exception e) { item.error = e.ToString(); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        cases.Add(item);
    }
    static int Main(string[] args)
    {
        root = Path.Combine(args[2], "FlowChecksData");
        Run("attract demo times out and returns to title without charging", () =>
        {
            using var f = new Fixture(); f.flow.Advance(6); Check(f.flow.State == ArcadeFlowState.LoadingDemo && f.flow.Match.CpuOne && f.flow.Match.CpuTwo, "Demo request");
            f.flow.MatchReady(f.flow.Match.MatchId, 7); f.flow.Advance(37);
            Check(f.flow.State == ArcadeFlowState.Attract && f.ledger.Snapshot.starts == 0, "Demo return");
        });
        Run("zero-credit Start stays in attract", () =>
        { using var f = new Fixture(); f.Start(1, 0); Check(f.flow.State == ArcadeFlowState.Attract && f.flow.SessionId == 0, "Free start"); });
        Run("same-batch coin funds Start and select timeout picks cursor", () =>
        {
            using var f = new Fixture(); f.Coin(1, 0); f.Start(2, 0); f.flow.MoveSelection(1); f.flow.Advance(20);
            Check(f.flow.State == ArcadeFlowState.Loading && f.flow.Match.PlayerOne == "Kazuya" && !f.flow.Match.CpuOne && f.flow.Match.CpuTwo, "Selection timeout");
            Check(f.ledger.VisibleCredits(0) == 0 && f.ledger.Snapshot.starts == 1, "Debit");
        });
        Run("P2 can own a single-player ladder", () =>
        { using var f = new Fixture(); f.Fight(1); Check(f.flow.HumanSide == 1 && f.flow.Match.CpuOne && !f.flow.Match.CpuTwo && f.flow.Match.PlayerTwo == "Jin", "P2 control"); });
        Run("other-side Start without observed fight eligibility does not debit", () =>
        {
            using var f = new Fixture(); f.Fight(); f.Coin(3, 3, 1); f.Start(4, 3, 1);
            Check(f.flow.State == ArcadeFlowState.Fight && f.ledger.VisibleCredits(0) == 1 && f.ledger.Snapshot.challenges == 0, "Ineligible challenge charged");
        });
        Run("paid start supersedes a loading demo; stale readiness cannot hijack selection", () =>
        {
            using var f = new Fixture(); f.flow.Advance(6); string old = f.flow.Match.MatchId; f.Coin(1, 7); f.Start(2, 7);
            f.flow.MatchReady(old, 8); Check(f.flow.State == ArcadeFlowState.Select && f.flow.Match == null, "Stale demo became paid match");
            f.flow.ConfirmSelection(9); Check(f.flow.Match.MatchId != old, "Reused demo ID");
        });
        Run("ready notification must match the paid match ID", () =>
        {
            using var f = new Fixture(); f.Coin(1, 0); f.Start(2, 0); f.flow.ConfirmSelection(1);
            f.flow.MatchReady("wrong", 2); Check(f.flow.State == ArcadeFlowState.Loading && !f.ledger.Snapshot.sessions[0].matchReady, "Wrong readiness");
            f.flow.MatchReady(f.flow.Match.MatchId, 3); Check(f.ledger.Snapshot.sessions[0].matchReady, "Readiness not recorded");
        });
        Run("win advances ladder on one entitlement; final win ends it", () =>
        {
            using var f = new Fixture(new ArcadeSettings { ladderStages = 2 }); f.Fight(); long session = f.flow.SessionId;
            f.flow.MatchFinished(f.flow.Match.MatchId, 0, 3); f.flow.FinishVictory(4);
            Check(f.flow.StageIndex == 1 && f.flow.SessionId == session && f.flow.Match.PlayerTwo == "Kazuya", "Ladder advance");
            f.flow.MatchReady(f.flow.Match.MatchId, 5); f.flow.MatchFinished(f.flow.Match.MatchId, 0, 6); f.flow.FinishVictory(7);
            Check(f.flow.State == ArcadeFlowState.Ending && f.ledger.Snapshot.sessions[0] == null && f.ledger.Snapshot.starts == 1, "Final close");
            f.flow.Advance(12); Check(f.flow.State == ArcadeFlowState.Attract, "Ending timed out incorrectly");
        });
        Run("continue keeps stage and opponent and creates one new entitlement", () =>
        {
            using var f = new Fixture(); f.Fight(); string opponent = f.flow.Match.PlayerTwo, stage = f.flow.Match.Stage;
            long original = f.flow.SessionId; f.flow.MatchFinished(f.flow.Match.MatchId, 1, 3); f.flow.FinishVictory(4);
            Check(f.flow.State == ArcadeFlowState.Continue && f.ledger.Snapshot.sessions[0] == null, "Loss not settled");
            f.Coin(3, 5); f.Start(4, 5); f.flow.MoveSelection(1); f.flow.ConfirmSelection(6);
            Check(f.flow.Match.PlayerTwo == opponent && f.flow.Match.Stage == stage && f.flow.SessionId != original && f.ledger.Snapshot.continues == 1, "Continue mutated ladder/debit");
        });
        Run("continue exactly at deadline wins; late input does not spend", () =>
        {
            using (var f = new Fixture()) { f.Lose(); f.Coin(3, 14); f.Start(4, 14); f.flow.Advance(14); Check(f.flow.State == ArcadeFlowState.Select, "Exact deadline rejected"); }
            using (var f = new Fixture()) { f.Lose(); f.Coin(3, 14.1); f.Start(4, 14.1); f.flow.Advance(14.1); Check(f.flow.State == ArcadeFlowState.GameOver && f.ledger.VisibleCredits(0) == 1, "Late debit"); }
        });
        Run("continue expiry and game-over return keep unused coins", () =>
        {
            using var f = new Fixture(); f.Lose(); f.Coin(3, 5); f.flow.Advance(14); f.flow.Advance(17);
            Check(f.flow.State == ArcadeFlowState.Attract && f.ledger.VisibleCredits(0) == 1, "Unused credits lost");
        });
        Run("victory timeout advances without a presentation callback", () =>
        { using var f = new Fixture(); f.Fight(); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 3); f.flow.Advance(11); Check(f.flow.State == ArcadeFlowState.Continue, "Stuck victory"); });
        Run("loading failure refunds pending entitlement once and faults", () =>
        {
            using var f = new Fixture(); f.Coin(1, 0); f.Start(2, 0); f.flow.ConfirmSelection(1); f.flow.Advance(31);
            Check(f.flow.State == ArcadeFlowState.Fault && f.ledger.VisibleCredits(0) == 1 && f.ledger.Snapshot.refundedCredits == 1, "Load failure recovery");
            f.flow.Fail("Again", 32); Check(f.ledger.VisibleCredits(0) == 1, "Double refund");
        });
        Run("duplicate result cannot advance the ladder twice", () =>
        {
            using var f = new Fixture(); f.Fight(); string id = f.flow.Match.MatchId;
            f.flow.MatchFinished(id, 0, 3); f.flow.FinishVictory(4); f.flow.MatchFinished(id, 0, 5); f.flow.FinishVictory(5);
            Check(f.flow.StageIndex == 1 && f.flow.State == ArcadeFlowState.Loading, "Duplicate result advanced twice");
        });
        Run("small fighter/stage pools generate a finite deterministic ladder", () =>
        {
            var one = new ArcadeLadder(new[] { "Jin", "Kazuya" }, "Jin", "Kazuya", 20, new[] { "arena" }, 42);
            var two = new ArcadeLadder(new[] { "Jin", "Kazuya" }, "Jin", "Kazuya", 20, new[] { "arena" }, 42);
            Check(one.Count == 20 && Enumerable.Range(0, 20).All(i => one.Opponent(i) == two.Opponent(i) && one.Stage(i) == "arena"), "Fallback/seed");
        });
        Run("free-play starts and continues cost no stored credit", () =>
        {
            using var f = new Fixture(new ArcadeSettings { freePlay = true }); f.Start(1, 0); f.flow.ConfirmSelection(1); f.flow.MatchReady(f.flow.Match.MatchId, 2);
            f.flow.MatchFinished(f.flow.Match.MatchId, 1, 3); f.flow.FinishVictory(4); f.Start(2, 5);
            Check(f.flow.State == ArcadeFlowState.Select && f.ledger.VisibleCredits(0) == 0 && f.ledger.Snapshot.continues == 1, "Free-play flow");
        });

        Run("simultaneous starts pay Start price for both humans", () =>
        {
            using var f = new Fixture(new ArcadeSettings { creditsToChallenge = 2 });
            f.Coin(1, 0); f.Coin(2, 0, 1); f.Start(3, 0); f.Start(4, 0, 1);
            Check(f.flow.TwoPlayers && f.ledger.Snapshot.starts == 2 && f.ledger.Snapshot.challenges == 0 && f.ledger.VisibleCredits(0) == 0, "Wrong direct pricing");
            f.flow.ConfirmSelection(0, 1); Check(f.flow.State == ArcadeFlowState.Select, "P2 not confirmed");
            f.flow.MoveSelection(0, 1); Check(f.flow.Fighter(0) == "Jin", "Confirmed cursor moved");
            f.flow.MoveSelection(1, 2); f.flow.ConfirmSelection(1, 1);
            Check(!f.flow.Match.CpuOne && !f.flow.Match.CpuTwo && f.flow.Match.PlayerTwo == "Kazuya", "Two-human picks");
            f.flow.MatchReady(f.flow.Match.MatchId, 2);
            Check(f.ledger.Snapshot.sessions.All(x => x != null && x.matchReady), "Readiness for both");
        });
        Run("one shared credit grants just first simultaneous Start", () =>
        {
            using var f = new Fixture(); f.Coin(1, 0); f.Start(2, 0); f.Start(3, 0, 1);
            Check(!f.flow.TwoPlayers && f.ledger.Snapshot.starts == 1 && f.flow.SessionFor(1) == 0, "Second human got free start");
        });
        Run("separate pools cannot spend the other player's credit", () =>
        {
            using var f = new Fixture(new ArcadeSettings { sharedCredits = false });
            f.Coin(1, 0); f.Coin(2, 0); f.Start(3, 0); f.Start(4, 0, 1);
            Check(!f.flow.TwoPlayers && f.ledger.VisibleCredits(0) == 1, "P2 spent P1 credit");
            f.Coin(5, 1, 1); f.Start(6, 1, 1);
            Check(f.flow.TwoPlayers && f.ledger.VisibleCredits(0) == 1 && f.ledger.VisibleCredits(1) == 0, "Separate challenge pool");
        });
        Run("joining an existing selection pays Challenge price", () =>
        {
            using var f = new Fixture(new ArcadeSettings { creditsToChallenge = 2 });
            f.Coin(1, 0); f.Coin(2, 0); f.Coin(3, 0); f.Start(4, 0); f.Start(5, 1, 1);
            Check(f.flow.TwoPlayers && f.ledger.Snapshot.starts == 1 && f.ledger.Snapshot.challenges == 1 && f.ledger.VisibleCredits(0) == 0, "Select pricing");
        });
        Run("selection deadline confirms both outstanding picks", () =>
        {
            using var f = Direct(); f.flow.MoveSelection(1, 1); f.flow.ConfirmSelection(0, 1); f.flow.Advance(20);
            Check(f.flow.State == ArcadeFlowState.Loading && !f.flow.Match.CpuTwo && f.flow.Match.PlayerTwo == "Kazuya", "2P select timeout");
        });
        Run("eligible challenge retains incumbent entitlement and suspended ladder", () =>
        {
            using var f = new Fixture(); f.Fight(); string opponent = f.flow.Match.PlayerTwo, stage = f.flow.Match.Stage; long incumbent = f.flow.SessionId;
            Join(f, 3); Check(f.flow.State == ArcadeFlowState.Challenger && f.flow.SessionFor(0) == incumbent && f.flow.SessionFor(1) == 4, "Challenge ownership");
            f.flow.Advance(5); f.flow.ConfirmSelection(0, 6); f.flow.MoveSelection(1, 1); f.flow.ConfirmSelection(1, 6);
            f.flow.MatchReady(f.flow.Match.MatchId, 7); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 8); f.flow.FinishVictory(9);
            Check(f.flow.ContinueSide == 0 && f.flow.HumanSide == 1 && f.flow.SessionFor(0) == 0 && f.flow.SessionFor(1) == 4, "Loser/winner ownership");
            f.flow.Advance(19);
            Check(!f.flow.TwoPlayers && f.flow.Match.CpuOne && !f.flow.Match.CpuTwo && f.flow.Match.PlayerOne == opponent && f.flow.Match.Stage == stage && f.flow.StageIndex == 0, "Winner did not inherit suspended stage");
        });
        Run("incumbent winner resumes the same stage without a new debit", () =>
        {
            using var f = ChallengedFight(); var session = f.flow.SessionFor(0); var stage = f.flow.Match.Stage;
            f.flow.MatchFinished(f.flow.Match.MatchId, 0, 8); f.flow.FinishVictory(9); f.flow.Advance(19);
            Check(f.flow.SessionId == session && f.flow.StageIndex == 0 && f.flow.Match.Stage == stage && f.ledger.Snapshot.starts == 1, "Incumbent debit/stage");
        });
        Run("loser alone can pay Continue exactly at deadline; winner cannot", () =>
        {
            using var f = ChallengedFight(); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 8); f.flow.FinishVictory(9);
            f.Coin(5, 10); f.Start(6, 10, 1); Check(f.ledger.VisibleCredits(0) == 1 && f.flow.State == ArcadeFlowState.Continue, "Winner paid loser's continue");
            f.Start(7, 19, 0); f.flow.Advance(19);
            Check(f.flow.State == ArcadeFlowState.Select && f.flow.TwoPlayers && f.flow.SessionFor(0) == 7 && f.flow.SessionFor(1) == 4 && f.ledger.Snapshot.continues == 1, "Loser Continue contract");
        });
        Run("late loser Start preserves its credit while winner resumes", () =>
        {
            using var f = ChallengedFight(); f.flow.MatchFinished(f.flow.Match.MatchId, 0, 8); f.flow.FinishVictory(9);
            f.Coin(5, 20, 1); f.Start(6, 20, 1); f.flow.Advance(20);
            Check(!f.flow.TwoPlayers && f.ledger.VisibleCredits(1) == 1 && f.ledger.Snapshot.continues == 0, "Late continue charged");
        });
        Run("continue without reselection restores both human controllers", () =>
        {
            using var f = ChallengedFight(new ArcadeSettings { continueReselect = false });
            f.flow.MatchFinished(f.flow.Match.MatchId, 0, 8); f.flow.FinishVictory(9); f.Coin(5, 10, 1); f.Start(6, 10, 1);
            Check(f.flow.State == ArcadeFlowState.Loading && !f.flow.Match.CpuOne && !f.flow.Match.CpuTwo, "Versus continue became CPU");
        });
        Run("winner can change after rematch and keeps only their current entitlement", () =>
        {
            using var f = ChallengedFight(new ArcadeSettings { continueReselect = false });
            f.flow.MatchFinished(f.flow.Match.MatchId, 1, 8); f.flow.FinishVictory(9); f.Coin(5, 10); f.Start(6, 10);
            f.flow.MatchReady(f.flow.Match.MatchId, 11); f.flow.MatchFinished(f.flow.Match.MatchId, 0, 12); f.flow.FinishVictory(13); f.flow.Advance(23);
            Check(f.flow.HumanSide == 0 && f.flow.SessionId == 6 && f.ledger.Snapshot.sessions[1] == null && f.ledger.Snapshot.refundedCredits == 0, "Rematch ownership");
        });
        Run("KO result beats same-tick challenger and stale eligibility", () =>
        {
            using var f = new Fixture(); f.Fight(); f.flow.ObserveChallengeEligibility(f.flow.Match.MatchId, true);
            f.flow.MatchFinished(f.flow.Match.MatchId, 0, 3); f.Coin(3, 3, 1); f.Start(4, 3, 1);
            Check(f.flow.State == ArcadeFlowState.Victory && f.ledger.Snapshot.challenges == 0 && f.ledger.VisibleCredits(0) == 1, "KO race charged");
        });
        Run("rejected round/intro Start cannot replay as a later challenge", () =>
        {
            using var f = new Fixture(); f.Fight(); f.Coin(3, 3, 1); f.Start(4, 3, 1);
            f.flow.ObserveChallengeEligibility(f.flow.Match.MatchId, true); f.Start(4, 4, 1);
            Check(!f.flow.TwoPlayers && f.ledger.VisibleCredits(0) == 1, "Rejected edge replayed");
            f.Start(5, 4, 1); Check(f.flow.State == ArcadeFlowState.Challenger, "Fresh edge rejected");
        });
        Run("joinIn off rejects both select and fight challenges", () =>
        {
            using var f = new Fixture(new ArcadeSettings { joinIn = false }); f.Coin(1, 0); f.Start(2, 0); f.Coin(3, 1, 1); f.Start(4, 1, 1);
            Check(!f.flow.TwoPlayers, "Select join ignored setting"); f.flow.ConfirmSelection(2); f.flow.MatchReady(f.flow.Match.MatchId, 3);
            f.flow.ObserveChallengeEligibility(f.flow.Match.MatchId, true); f.Start(5, 4, 1);
            Check(!f.flow.TwoPlayers && f.ledger.VisibleCredits(0) == 1, "Fight join ignored setting");
        });
        Run("direct versus winner receives a new ladder", () =>
        {
            using var f = Direct(); f.flow.Advance(20); f.flow.MatchReady(f.flow.Match.MatchId, 21);
            f.flow.MatchFinished(f.flow.Match.MatchId, 1, 22); f.flow.FinishVictory(23); f.flow.Advance(33);
            Check(f.flow.StageIndex == 0 && f.flow.HumanSide == 1 && f.flow.Match.CpuOne && !f.flow.Match.CpuTwo && f.ledger.Snapshot.starts == 2, "Direct winner ladder");
        });
        Run("draw replays versus without closing or charging either session", () =>
        {
            using var f = ChallengedFight(); f.flow.MatchFinished(f.flow.Match.MatchId, -1, 8); f.flow.FinishVictory(9);
            Check(f.flow.State == ArcadeFlowState.Loading && f.flow.TwoPlayers && f.ledger.Snapshot.sessions.All(x => x != null) && f.ledger.Snapshot.continues == 0, "Draw lost entitlements");
        });
        Run("challenge load failure refunds both outstanding payments once", () =>
        {
            using var f = ChallengedFight(); f.flow.Fail("load failed", 8); f.flow.Fail("again", 9);
            Check(f.ledger.Snapshot.sessions.All(x => x == null) && f.ledger.Snapshot.refundedCredits == 2 && f.ledger.VisibleCredits(0) == 2, "Dual recovery");
        });
        Run("free-play direct and challenge never consume stored credits", () =>
        {
            using var f = new Fixture(new ArcadeSettings { freePlay = true }); f.Coin(1, 0); f.Start(2, 0); f.Start(3, 0, 1);
            Check(f.flow.TwoPlayers && f.ledger.VisibleCredits(0) == 1 && f.ledger.Snapshot.sessions.All(x => x.cost == 0), "Free play spent funds");
        });
        Run("restart after a two-player debit refunds each original pool exactly once", () =>
        {
            var settings = new ArcadeSettings { sharedCredits = false };
            using var f = ChallengedFight(settings); f.ledger.Dispose();
            using var reopened = new ArcadeLedger(f.LedgerPath, settings);
            Check(reopened.RecoverInterruptedSessions() == 2 && reopened.VisibleCredits(0) == 1 && reopened.VisibleCredits(1) == 1, "Recovery changed pool ownership");
            Check(reopened.RecoverInterruptedSessions() == 0 && reopened.Snapshot.refundedCredits == 2, "Recovery duplicated");
        });
        Run("restart during loser Continue refunds only the still-open winner", () =>
        {
            var settings = new ArcadeSettings { sharedCredits = false };
            using var f = ChallengedFight(settings); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 8); f.flow.FinishVictory(9); f.ledger.Dispose();
            using var reopened = new ArcadeLedger(f.LedgerPath, settings);
            Check(reopened.RecoverInterruptedSessions() == 1 && reopened.VisibleCredits(0) == 0 && reopened.VisibleCredits(1) == 1, "Completed loser was refunded");
        });
        var report = new Report { passed = cases.All(c => c.passed), utc = DateTime.UtcNow.ToString("O"), cases = cases.ToArray() };
        File.WriteAllText(Path.Combine(args[2], "flow-validation.json"), ArcadeJson.Encode(report));
        Console.WriteLine($"{cases.Count(c => c.passed)}/{cases.Count} flow cases passed"); return report.passed ? 0 : 1;
    }
}
