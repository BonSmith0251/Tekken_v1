using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Versus.Remaster;
using Versus.Remaster.Arcade;

static class OperatorChecks
{
    [Serializable] public sealed class Case { public string name, error; public bool passed; }
    [Serializable] public sealed class Report { public bool passed; public string utc; public Case[] cases; }
    static string root;
    static readonly List<Case> cases = new List<Case>();
    sealed class Fixture : IDisposable
    {
        public readonly string path;
        public readonly ArcadeLedger ledger;
        public readonly ArcadeFlow flow;
        public Fixture(ArcadeSettings settings = null, Action<ArcadeWriteStage> fault = null)
        {
            settings ??= new ArcadeSettings(); path = Path.Combine(root, Guid.NewGuid().ToString("N"), "ledger.json");
            ledger = new ArcadeLedger(path, settings, fault);
            flow = new ArcadeFlow(settings, ledger, new[] { "Jin", "Kazuya" }, new[] { "arena" }, 5, 0);
        }
        public void Event(long id, double time, ArcadeEventKind kind, int side = 0) => flow.Process(new ArcadeInputEvent(id, time, kind, side), time);
        public void Fight() { Event(1, 0, ArcadeEventKind.Coin); Event(2, 0, ArcadeEventKind.Start); flow.ConfirmSelection(1); flow.MatchReady(flow.Match.MatchId, 2); }
        public void Dispose() => ledger.Dispose();
    }
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
        root = Path.Combine(args[2], "OperatorChecksData");
        Run("pre-operator ledger preserves original balances counters and event identity", () =>
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N"), "ledger.json"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Copy(Path.Combine(args[0], "Tools/ArcadeHarness/Fixtures/ledger-before-operator.json"), path);
            string before = File.ReadAllText(path);
            using (var ledger = new ArcadeLedger(path, new ArcadeSettings()))
            {
                var state = ledger.Snapshot;
                Check(state.credits[0] == 13 && state.credits[1] == 0 && state.coins[0] == 6 && state.coins[1] == 1 &&
                    state.serviceCredits == 8 && state.starts == 2 && state.lastEventId == 17 && state.generation == 21, "Legacy values changed");
                Check(state.operatorSettings == null && state.bookkeepingClears == 0 && state.creditClears == 0, "New fields did not default");
                Check(File.ReadAllText(path) == before, "Read silently rewrote original ledger");
                var settings = ledger.Settings; settings.freePlay = true; ledger.ApplyOperatorSettings(settings);
            }
            using (var reopened = new ArcadeLedger(path, new ArcadeSettings()))
                Check(reopened.Settings.freePlay && reopened.Snapshot.credits[0] == 13 && reopened.Snapshot.starts == 2 && reopened.Snapshot.lastEventId == 17, "Upgrade roundtrip lost value");
        });
        Run("original required ledger fields are still required", () =>
        {
            string fixture = File.ReadAllText(Path.Combine(args[0], "Tools/ArcadeHarness/Fixtures/ledger-before-operator.json"));
            var envelope = ArcadeJson.Decode<ArcadeAtomicFile<ArcadeLedgerState>.Envelope>(fixture);
            string missing = envelope.payload.Replace("\"starts\":2", "\"unrecognizedStarts\":2");
            Throws<System.Runtime.Serialization.SerializationException>(() => ArcadeJson.Decode<ArcadeLedgerState>(missing));
        });
        Run("operator pause preserves select deadline and blocks confirmation", () =>
        {
            using var f = new Fixture(); f.Event(1, 0, ArcadeEventKind.Coin); f.Event(2, 0, ArcadeEventKind.Start);
            f.flow.SetOperatorPaused(true, 4); f.flow.MoveSelection(1); f.flow.ConfirmSelection(40); f.flow.Advance(40);
            Check(f.flow.State == ArcadeFlowState.Select && f.flow.SelectedFighter == "Jin", "Selection advanced under menu");
            f.flow.SetOperatorPaused(false, 44); Check(f.flow.Deadline == 60, "Countdown time lost");
            f.flow.Advance(59); Check(f.flow.State == ArcadeFlowState.Select, "Expired early"); f.flow.Advance(60);
            Check(f.flow.State == ArcadeFlowState.Loading, "Never resumed");
        });
        Run("coins and service persist during operator pause while Starts are discarded", () =>
        {
            using var f = new Fixture(); f.flow.SetOperatorPaused(true, 0);
            f.Event(1, 1, ArcadeEventKind.Coin); f.Event(2, 1, ArcadeEventKind.Service); f.Event(3, 1, ArcadeEventKind.Start); f.flow.Advance(50);
            Check(f.ledger.VisibleCredits(0) == 2 && f.flow.State == ArcadeFlowState.Attract && f.ledger.Snapshot.starts == 0, "Paused accounting");
            f.flow.SetOperatorPaused(false, 51); f.Event(3, 52, ArcadeEventKind.Start);
            Check(f.flow.State == ArcadeFlowState.Attract, "Ignored Start replayed");
        });
        Run("continue countdown excludes operator time and accepts exact resumed boundary", () =>
        {
            using var f = new Fixture(); f.Fight(); f.flow.MatchFinished(f.flow.Match.MatchId, 1, 3); f.flow.FinishVictory(4);
            f.flow.SetOperatorPaused(true, 5); f.flow.AccelerateContinue(1); f.flow.Advance(90); f.flow.SetOperatorPaused(false, 95);
            Check(f.flow.Deadline == 104, "Continue deadline changed");
            f.Event(3, 104, ArcadeEventKind.Coin); f.Event(4, 104, ArcadeEventKind.Start); f.flow.Advance(104);
            Check(f.flow.State == ArcadeFlowState.Select, "Boundary resume failed");
        });
        Run("match ready while paused cannot consume the loading timeout", () =>
        {
            using var f = new Fixture(); f.Event(1, 0, ArcadeEventKind.Coin); f.Event(2, 0, ArcadeEventKind.Start); f.flow.ConfirmSelection(1);
            string id = f.flow.Match.MatchId; f.flow.SetOperatorPaused(true, 2); f.flow.MatchReady(id, 80); f.flow.Advance(80);
            Check(f.flow.State == ArcadeFlowState.Loading && !f.ledger.Snapshot.sessions[0].matchReady, "Ready during pause");
            f.flow.SetOperatorPaused(false, 81); f.flow.MatchReady(id, 82); Check(f.flow.State == ArcadeFlowState.Fight, "Load resume");
        });
        Run("controller created during operator pause cannot advance a newly loaded scene", () =>
        {
            var settings = new ArcadeSettings(); var input = new ArcadeInput(settings); input.SetOperatorPaused(true);
            var graph = new CommandGraph(); var setup = new ArcadeMatchSetup("pause", "Jin", "Kazuya", false, false);
            var controller = new ArcadeMatchController(setup, input, new[] { new CpuPlayer(graph, 1), new CpuPlayer(graph, 2) }, 45);
            var battle = new BattleModel(new MatchRules()); int ticks = battle.phaseTicks;
            for (int i = 0; i < 300; i++) controller.Tick(battle, out _, out _);
            Check(battle.phaseTicks == ticks && controller.Suspended, "New scene ticked while menu open");
            input.SetOperatorPaused(false); controller.Tick(battle, out _, out _); Check(battle.phaseTicks < ticks, "Controller failed to resume");
        });
        Run("operator Test sorts before simultaneous coin and Start", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings()); input.Sample(ArcadeControls.Test | ArcadeControls.Coin1 | ArcadeControls.Start1, 0);
            var e = input.DrainEvents(); Check(e.Select(x => x.kind).SequenceEqual(new[] { ArcadeEventKind.Test, ArcadeEventKind.Coin, ArcadeEventKind.Start }), "Test priority");
            using var f = new Fixture(); foreach (var item in e) { if (item.kind == ArcadeEventKind.Test) f.flow.SetOperatorPaused(true, 0); f.flow.Process(item, 0); }
            Check(f.ledger.VisibleCredits(0) == 1 && f.ledger.Snapshot.starts == 0, "Test plus Start spent money");
        });
        Run("held coin and Start after remap require release before an edge", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings(), 20); input.Sample(0, 0);
            input.RebaseHeldControls(ArcadeControls.Coin1 | ArcadeControls.Start1);
            input.Sample(ArcadeControls.Coin1 | ArcadeControls.Start1, 1); Check(input.DrainEvents().Length == 0, "Phantom rebinding payment");
            input.Sample(0, 2); input.Sample(ArcadeControls.Coin1 | ArcadeControls.Start1, 3);
            var e = input.DrainEvents(); Check(e.Length == 2 && e[0].id == 21 && e[1].id == 22, "Sequence/release");
        });
        Run("operator Apply and free play persist without losing stored credit", () =>
        {
            using var f = new Fixture(); f.ledger.Coin(1, 0); var next = f.ledger.Settings; next.freePlay = true; next.difficulty = 7;
            f.ledger.ApplyOperatorSettings(next); next.difficulty = 1;
            Check(f.ledger.Settings.difficulty == 7 && f.ledger.VisibleCredits(0) == 1, "Snapshot alias/balance");
            f.ledger.Dispose(); using var reopened = new ArcadeLedger(f.path, new ArcadeSettings());
            Check(reopened.Settings.freePlay && reopened.Settings.difficulty == 7, "Operator config not authoritative after restart");
            reopened.Begin(2, 0, ArcadePaidAction.Start, out _); Check(reopened.VisibleCredits(0) == 1, "Free play consumed stored credit");
        });
        Run("pool migration preserves balances and chute fractions", () =>
        {
            using var f = new Fixture(new ArcadeSettings { sharedCredits = false, coinsPerCredit2 = 2 });
            f.ledger.Coin(1, 0); f.ledger.Coin(2, 1); f.ledger.Service(3, 1);
            var next = f.ledger.Settings; next.sharedCredits = true; f.ledger.ApplyOperatorSettings(next);
            Check(f.ledger.VisibleCredits(0) == 2 && f.ledger.Snapshot.partialCoins[1] == 1, "Merge lost value");
            next.sharedCredits = false; f.ledger.ApplyOperatorSettings(next);
            Check(f.ledger.VisibleCredits(0) == 2 && f.ledger.VisibleCredits(1) == 0, "Split assignment");
            f.ledger.Coin(4, 1); Check(f.ledger.VisibleCredits(1) == 1, "Partial chute payment lost");
        });
        Run("partial coin payment blocks ratio changes without a write", () =>
        {
            using var f = new Fixture(new ArcadeSettings { coinsPerCredit1 = 2 }); f.ledger.Coin(1, 0);
            var next = f.ledger.Settings; next.coinsPerCredit1 = 3; long generation = f.ledger.Snapshot.generation;
            Throws<InvalidOperationException>(() => f.ledger.ApplyOperatorSettings(next));
            Check(f.ledger.Snapshot.generation == generation && f.ledger.Snapshot.partialCoins[0] == 1, "Rejected edit changed ledger");
        });
        Run("settings and credit clear reject an active paid session", () =>
        {
            using var f = new Fixture(); f.Fight(); long session = f.flow.SessionId;
            Throws<InvalidOperationException>(() => f.ledger.ApplyOperatorSettings(new ArcadeSettings { freePlay = true }));
            Throws<InvalidOperationException>(() => f.ledger.ClearCredits());
            Check(f.ledger.Snapshot.sessions[0].id == session, "Session lost");
        });
        Run("bookkeeping clear preserves money partial payments and paid sessions", () =>
        {
            using var f = new Fixture(new ArcadeSettings { coinsPerCredit2 = 2 }); f.Fight(); f.ledger.Coin(3, 1); f.ledger.Service(4, 0);
            var before = f.ledger.Snapshot; f.ledger.ClearBookkeeping(); var after = f.ledger.Snapshot;
            Check(after.starts == 0 && after.coins.All(c => c == 0) && after.serviceCredits == 0 && after.bookkeepingClears == 1, "Counters not reset");
            Check(after.credits.SequenceEqual(before.credits) && after.partialCoins.SequenceEqual(before.partialCoins) && after.sessions[0].id == before.sessions[0].id && after.lastEventId == before.lastEventId, "Accounting reset erased value/identity");
        });
        Run("explicit credit clear leaves bookkeeping and event identity intact", () =>
        {
            using var f = new Fixture(new ArcadeSettings { coinsPerCredit2 = 2 }); f.ledger.Coin(1, 0); f.ledger.Coin(2, 1);
            f.ledger.ClearCredits(); var s = f.ledger.Snapshot;
            Check(s.credits.All(x => x == 0) && s.partialCoins.All(x => x == 0) && s.coins[0] == 1 && s.coins[1] == 1 && s.lastEventId == 2 && s.creditClears == 1, "Credit clear affected counters");
        });
        foreach (ArcadeWriteStage point in Enum.GetValues(typeof(ArcadeWriteStage)))
        Run("operator config migration recovers one atomic generation at " + point, () =>
        {
            bool armed = false; var initial = new ArcadeSettings { sharedCredits = false };
            using var f = new Fixture(initial, stage => { if (armed && stage == point) throw new IOException("Injected"); });
            f.ledger.Coin(1, 0); f.ledger.Coin(2, 1); var next = f.ledger.Settings; next.sharedCredits = true; next.creditsToStart = 2; armed = true;
            Throws<IOException>(() => f.ledger.ApplyOperatorSettings(next)); Check(f.ledger.Faulted, "Failure not sticky"); f.ledger.Dispose();
            using var reopened = new ArcadeLedger(f.path, initial);
            bool committed = point == ArcadeWriteStage.AfterCommit;
            Check(reopened.Settings.sharedCredits == committed && reopened.Settings.creditsToStart == (committed ? 2 : 1), "Torn configuration");
            Check(reopened.Snapshot.credits.Sum() == 2 && reopened.Snapshot.lastEventId == 2, "Migration changed paid value");
        });
        Run("default bindings preserve both players and the recovery key", () =>
        {
            var map = ArcadeBindings.Defaults(); map.Validate();
            Check(map.Read(k => new[] { "W", "O", "Alpha5", "F2" }.Contains(k)) == (ArcadeControls.P1Up | ArcadeControls.P2B1 | ArcadeControls.Coin1 | ArcadeControls.Test), "Default binding routing");
            map = map.Rebind(ArcadeControls.Test, "F3"); Check(map.Read(k => k == "F2") == ArcadeControls.Test && map.Read(k => k == "F3") == ArcadeControls.Test, "Operator recovery lost");
        });
        Run("binding conflicts reserved keys and missing controls are rejected", () =>
        {
            var map = ArcadeBindings.Defaults(); Throws<ArgumentException>(() => map.Rebind(ArcadeControls.Coin1, "U"));
            Throws<ArgumentException>(() => map.Rebind(ArcadeControls.Coin1, "F2")); Throws<ArgumentException>(() => map.Rebind(ArcadeControls.P1B1, "Escape"));
            var invalid = map.Copy(); invalid.keys[0].key = null; Throws<ArgumentException>(invalid.Validate);
            invalid = map.Copy(); invalid.keys[0].control = ArcadeControls.P1B1; Throws<ArgumentException>(invalid.Validate);
        });
        Run("saved remapping reloads and old key no longer produces input", () =>
        {
            var map = ArcadeBindings.Defaults().Rebind(ArcadeControls.P1B1, "Q"); string path = Path.Combine(root, Guid.NewGuid().ToString("N"), "input.json");
            using (var file = new ArcadeAtomicFile<ArcadeBindings>(path, b => b.Validate())) file.Save(map);
            using (var file = new ArcadeAtomicFile<ArcadeBindings>(path, b => b.Validate())) map = file.Load();
            Check(map.Read(k => k == "Q") == ArcadeControls.P1B1 && map.Read(k => k == "U") == 0, "Remap persistence");
            map.keys[0].key = "T"; Check(ArcadeBindings.Defaults().keys[0].key == "W", "Default mutated");
        });
        var report = new Report { passed = cases.All(c => c.passed), utc = DateTime.UtcNow.ToString("O"), cases = cases.ToArray() };
        File.WriteAllText(Path.Combine(args[2], "operator-validation.json"), ArcadeJson.Encode(report));
        Console.WriteLine($"{cases.Count(c => c.passed)}/{cases.Count} operator cases passed"); return report.passed ? 0 : 1;
    }
}
