using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Versus.Remaster.Arcade;

static class RankingChecks
{
    [Serializable] public sealed class Case { public string name, error; public bool passed; }
    [Serializable] public sealed class Report { public bool passed; public string utc; public Case[] cases; }
    static string root;
    static readonly List<Case> cases = new List<Case>();
    sealed class Fixture : IDisposable
    {
        public readonly string path;
        public readonly ArcadeSettings settings;
        public readonly ArcadeLedger ledger;
        public readonly ArcadeRanking ranking;
        public readonly ArcadeFlow flow;
        public Fixture(ArcadeSettings settings = null, Action<ArcadeWriteStage> fault = null, bool corrupt = false)
        {
            this.settings = settings ?? new ArcadeSettings { ladderStages = 2 };
            string directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "ranking.json"); if (corrupt) File.WriteAllText(path, "BROKEN RANKING EVIDENCE");
            ranking = new ArcadeRanking(path, fault);
            ledger = new ArcadeLedger(Path.Combine(directory, "ledger.json"), this.settings);
            flow = new ArcadeFlow(this.settings, ledger, new[] { "Jin", "Kazuya", "Xiaoyu" }, new[] { "arena" }, 43, 0, ranking);
        }
        public ArcadeScore[] Table => ranking.Table(ArcadeRanking.Rules(settings));
        public void Event(long id, double time, ArcadeEventKind kind, int side = 0) => flow.Process(new ArcadeInputEvent(id, time, kind, side), time);
        public void Fight() { Event(1, 0, ArcadeEventKind.Coin); Event(2, 0, ArcadeEventKind.Start); flow.ConfirmSelection(1); flow.MatchReady(flow.Match.MatchId, 2); }
        public void Win(double time, long ticks = 600, int opponentRounds = 0)
        { flow.MatchFinished(flow.Match.MatchId, flow.HumanSide, time, ticks, opponentRounds); flow.FinishVictory(time + 1); }
        public void FinishLadder()
        { Fight(); Win(3); flow.MatchReady(flow.Match.MatchId, 5); Win(6); flow.Advance(12); }
        public void Dispose() { ranking.Dispose(); ledger.Dispose(); }
    }
    static ArcadeScore Score(ArcadeSettings settings, int stages = 2, long ticks = 600, int clean = 1) => new ArcadeScore {
        runId = Guid.NewGuid().ToString("N"), rules = ArcadeRanking.Rules(settings), initials = "AAA", fighter = "Jin", stagesCleared = stages, fightTicks = ticks, cleanWins = clean };
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static void Run(string name, Action body)
    {
        var item = new Case { name = name };
        try { body(); item.passed = true; Console.WriteLine("PASS " + name); }
        catch (Exception e) { item.error = e.ToString(); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        cases.Add(item);
    }
    static int Main(string[] args)
    {
        root = Path.Combine(args[2], "RankingChecksData");
        Run("ranking sorts stages then fight time then clean wins and retains ten", () =>
        {
            using var f = new Fixture();
            for (int i = 0; i < 10; i++) f.ranking.Submit(Score(f.settings, 1, 1000 + i, 0));
            var slowHigh = Score(f.settings, 2, 9000, 0); f.ranking.Submit(slowHigh);
            var fast = Score(f.settings, 2, 600, 0); f.ranking.Submit(fast);
            var clean = Score(f.settings, 2, 600, 1); f.ranking.Submit(clean);
            Check(f.Table.Length == 10 && f.Table[0].runId == clean.runId && f.Table[1].runId == fast.runId && f.Table[2].runId == slowHigh.runId, "Ranking order/cap");
            Check(!f.ranking.Qualifies(Score(f.settings, 1, 99999, 0)), "Low score qualified");
        });
        Run("exact tie keeps earlier recorded entry at the cutoff", () =>
        {
            using var f = new Fixture(); var first = Score(f.settings);
            f.ranking.Submit(first); for (int i = 0; i < 9; i++) f.ranking.Submit(Score(f.settings));
            Check(!f.ranking.Submit(Score(f.settings)) && f.Table[0].runId == first.runId && f.Table.Length == 10, "Tie displaced earlier score");
        });
        Run("rules tables isolate difficulty and rounds but not coin prices", () =>
        {
            using var f = new Fixture(); f.ranking.Submit(Score(f.settings)); var rules = f.settings.Copy(); rules.difficulty++;
            f.ranking.Submit(Score(rules)); Check(f.Table.Length == 1 && f.ranking.Table(ArcadeRanking.Rules(rules)).Length == 1, "Difficulty mixed");
            rules = f.settings.Copy(); rules.creditsToStart = 2;
            Check(ArcadeRanking.Rules(rules) == ArcadeRanking.Rules(f.settings), "Pricing split performance table");
        });
        Run("saved scores reopen and callers cannot mutate stored entries", () =>
        {
            using var f = new Fixture(); var score = Score(f.settings); f.ranking.Submit(score); score.initials = "ZZZ"; f.Table[0].initials = "BAD";
            f.ranking.Dispose(); using var reopened = new ArcadeRanking(f.path);
            Check(reopened.Table(ArcadeRanking.Rules(f.settings))[0].initials == "AAA", "Mutable snapshot or persistence");
        });
        Run("duplicate completion cannot rename or duplicate a run", () =>
        {
            using var f = new Fixture(); var score = Score(f.settings); f.ranking.Submit(score); score.initials = "ZZZ";
            Check(f.ranking.Submit(score) && f.Table.Length == 1 && f.Table[0].initials == "AAA", "Duplicate score changed table");
        });
        Run("invalid names and zero-stage records are rejected", () =>
        {
            using var f = new Fixture(); var score = Score(f.settings); score.initials = "A1!"; Throws<InvalidDataException>(() => f.ranking.Submit(score));
            score.initials = "AAA"; score.stagesCleared = 0; Throws<InvalidDataException>(() => f.ranking.Submit(score));
        });
        Run("clearing one rules table preserves others and ledger credits", () =>
        {
            using var f = new Fixture(); f.ledger.Coin(1, 0); var other = f.settings.Copy(); other.difficulty++;
            f.ranking.Submit(Score(f.settings)); f.ranking.Submit(Score(other)); f.ranking.Clear(ArcadeRanking.Rules(f.settings));
            Check(f.Table.Length == 0 && f.ranking.Table(ArcadeRanking.Rules(other)).Length == 1 && f.ledger.VisibleCredits(0) == 1, "Clear leaked across tables/credits");
        });
        Run("corrupt rankings preserve evidence and do not stop a paid ladder", () =>
        {
            using var f = new Fixture(corrupt: true); f.FinishLadder();
            Check(!f.ranking.Available && f.flow.State == ArcadeFlowState.Attract && f.ledger.Snapshot.starts == 1 && f.ledger.Snapshot.refundedCredits == 0, "Ranking fault affected money/flow");
            Check(File.ReadAllText(f.path) == "BROKEN RANKING EVIDENCE", "Corrupt score file overwritten");
        });
        foreach (ArcadeWriteStage point in Enum.GetValues(typeof(ArcadeWriteStage)))
        Run("score save failure is independent of money at " + point, () =>
        {
            bool armed = false; using var f = new Fixture(fault: stage => { if (armed && stage == point) throw new IOException("Injected"); });
            f.FinishLadder(); f.Event(3, 13, ArcadeEventKind.Coin); armed = true;
            f.flow.Advance(32);
            Check(f.flow.State == ArcadeFlowState.Ranking && !f.ranking.Available && !f.ledger.Faulted && f.ledger.VisibleCredits(0) == 1, "Ranking write failure affected credits");
            f.ranking.Dispose(); using var reopened = new ArcadeRanking(f.path);
            Check(reopened.Available && reopened.Table(ArcadeRanking.Rules(f.settings)).Length == (point == ArcadeWriteStage.AfterCommit ? 1 : 0), "Incorrect score commit boundary");
        });
        Run("ending enters timed initials then saved rankings and title", () =>
        {
            using var f = new Fixture(); f.FinishLadder();
            Check(f.flow.State == ArcadeFlowState.NameEntry && f.flow.NameSide == 0 && f.ledger.Snapshot.sessions[0] == null, "Paid entitlement not closed before initials");
            f.flow.EditName(0, 1, 0); f.flow.ConfirmName(0, 13); f.flow.ConfirmName(0, 14); f.flow.ConfirmName(0, 15);
            Check(f.flow.State == ArcadeFlowState.Ranking && f.Table[0].initials == "BAA" && f.Table[0].stagesCleared == 2 && f.Table[0].fightTicks == 1200 && f.Table[0].cleanWins == 2, "Saved run statistics");
            f.flow.Advance(23); Check(f.flow.State == ArcadeFlowState.Attract, "Ranking return");
        });
        Run("name timeout preserves edited letters and ignores other-side buttons", () =>
        {
            using var f = new Fixture(); f.FinishLadder(); f.flow.EditName(1, 1, 0); f.flow.ConfirmName(1, 13);
            Check(f.flow.NameEntry.Initials == "AAA" && f.flow.NameEntry.Position == 0, "Other side edited name");
            f.flow.EditName(0, -1, 0); f.flow.Advance(32);
            Check(f.Table[0].initials == "ZAA", "Timeout erased edited letters");
        });
        Run("coins are counted but Start cannot spend or replay from initials", () =>
        {
            using var f = new Fixture(); f.FinishLadder(); f.Event(3, 13, ArcadeEventKind.Coin); f.Event(4, 13, ArcadeEventKind.Start);
            Check(f.flow.State == ArcadeFlowState.NameEntry && f.ledger.VisibleCredits(0) == 1, "Name entry spent a credit");
            f.flow.Advance(32); f.Event(4, 33, ArcadeEventKind.Start); Check(f.flow.State == ArcadeFlowState.Ranking, "Old Start replayed");
            f.Event(5, 33, ArcadeEventKind.Start); Check(f.flow.State == ArcadeFlowState.Select && f.ledger.VisibleCredits(0) == 0, "Fresh paid start could not interrupt idle rankings");
        });
        Run("operator pause freezes name entry and submission deadline", () =>
        {
            using var f = new Fixture(); f.FinishLadder(); f.flow.SetOperatorPaused(true, 13); f.flow.EditName(0, 1, 0); f.flow.ConfirmName(0, 70); f.flow.Advance(70);
            Check(f.flow.NameEntry.Initials == "AAA" && f.Table.Length == 0, "Operator input escaped into initials");
            f.flow.SetOperatorPaused(false, 73); f.flow.Advance(91); Check(f.Table.Length == 0, "Name timeout lost operator time");
            f.flow.Advance(92); Check(f.Table.Length == 1, "Resumed timeout failed");
        });
        Run("zero-stage loss returns to title without ranking a failed start", () =>
        {
            using var f = new Fixture(); f.Fight(); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 3, 700); f.flow.FinishVictory(4); f.flow.Advance(14); f.flow.Advance(17);
            Check(f.flow.State == ArcadeFlowState.Attract && f.Table.Length == 0, "Zero-stage score qualified");
        });
        Run("continue retains earned stages and includes lost CPU fight time", () =>
        {
            using var f = new Fixture(); f.Fight(); f.Win(3); f.flow.MatchReady(f.flow.Match.MatchId, 5); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 6, 900); f.flow.FinishVictory(7);
            f.Event(3, 8, ArcadeEventKind.Coin); f.Event(4, 8, ArcadeEventKind.Start); f.flow.ConfirmSelection(9); f.flow.MatchReady(f.flow.Match.MatchId, 10);
            f.Win(11, 300, 1); f.flow.Advance(17); f.flow.Advance(37);
            Check(f.Table[0].stagesCleared == 2 && f.Table[0].fightTicks == 1800 && f.Table[0].cleanWins == 1 && f.ledger.Snapshot.continues == 1, "Continue reset or inflated score");
        });
        Run("duplicate match callbacks cannot add score twice", () =>
        {
            using var f = new Fixture(); f.Fight(); string id = f.flow.Match.MatchId; f.Win(3); f.flow.MatchFinished(id, 0, 4, 600, 0);
            f.flow.MatchReady(f.flow.Match.MatchId, 5); f.Win(6); f.flow.Advance(12); f.flow.Advance(32);
            Check(f.Table[0].stagesCleared == 2 && f.Table[0].fightTicks == 1200, "Duplicate result counted");
        });
        Run("challenger takeover scores each human's own CPU wins and preserves winner session", () =>
        {
            using var f = new Fixture(); f.Fight(); f.Win(3); f.flow.MatchReady(f.flow.Match.MatchId, 5); f.flow.ObserveFightTicks(f.flow.Match.MatchId, 120);
            f.flow.ObserveChallengeEligibility(f.flow.Match.MatchId, true); f.Event(3, 6, ArcadeEventKind.Coin, 1); f.Event(4, 6, ArcadeEventKind.Start, 1);
            f.flow.Advance(8); f.flow.ConfirmSelection(0, 9); f.flow.ConfirmSelection(1, 9); f.flow.MatchReady(f.flow.Match.MatchId, 10);
            f.flow.MatchFinished(f.flow.Match.MatchId, 1, 11, 9000, 0); f.flow.FinishVictory(12); f.flow.Advance(22);
            Check(f.flow.State == ArcadeFlowState.NameEntry && f.flow.NameSide == 0 && f.flow.SessionFor(1) == 4, "Loser initials/winner entitlement");
            f.flow.Advance(42); Check(f.Table[0].stagesCleared == 1 && f.Table[0].fightTicks == 720, "Interrupted CPU/versus timing");
            f.Event(5, 43, ArcadeEventKind.Coin); f.Event(6, 43, ArcadeEventKind.Start); Check(f.flow.State == ArcadeFlowState.Ranking && f.ledger.VisibleCredits(0) == 1, "Start stole winner's ladder");
            f.flow.Advance(50); f.flow.MatchReady(f.flow.Match.MatchId, 51); f.Win(52, 400); f.flow.Advance(58); f.flow.Advance(78);
            Check(f.Table.Length == 2 && f.Table.All(e => e.stagesCleared == 1) && f.Table[0].fightTicks == 400 && f.ledger.Snapshot.refundedCredits == 0, "Winner inherited someone else's cleared stages");
        });
        Run("attract demo leads to rankings without creating a human score", () =>
        {
            using var f = new Fixture(); f.ranking.Submit(Score(f.settings)); f.flow.Advance(6); f.flow.MatchReady(f.flow.Match.MatchId, 7); f.flow.Advance(37);
            Check(f.flow.State == ArcadeFlowState.Ranking && f.Table.Length == 1 && f.ledger.Snapshot.starts == 0, "Demo scoring/rotation");
        });
        var report = new Report { passed = cases.All(c => c.passed), utc = DateTime.UtcNow.ToString("O"), cases = cases.ToArray() };
        File.WriteAllText(Path.Combine(args[2], "ranking-validation.json"), ArcadeJson.Encode(report));
        Console.WriteLine($"{cases.Count(c => c.passed)}/{cases.Count} ranking cases passed"); return report.passed ? 0 : 1;
    }
}
