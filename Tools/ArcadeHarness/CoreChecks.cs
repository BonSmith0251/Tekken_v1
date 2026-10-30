using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Versus.Remaster.Arcade;

static class CoreChecks
{
    [Serializable] public sealed class Result { public string name, error; public bool passed; }
    [Serializable] public sealed class Report
    {
        public string utc, dataDirectory, scope = "Real core sources and file I/O; simulated faults, no physical power-cut or cabinet verification.";
        public bool passed;
        public Result[] cases;
    }
    static readonly List<Result> results = new List<Result>();
    static string root;
    static int directoryIndex;
    static string FilePath() { string dir = Path.Combine(root, (++directoryIndex).ToString("D3")); Directory.CreateDirectory(dir); return Path.Combine(dir, "arcade-ledger.json"); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static void Run(string name, Action body)
    {
        var result = new Result { name = name };
        try { body(); result.passed = true; Console.WriteLine("PASS " + name); }
        catch (Exception e) { result.error = e.ToString(); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        results.Add(result);
    }

    static int Main(string[] args)
    {
        root = Path.Combine(args[0], "CoreChecksData", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Run("settings roundtrip and invalid/missing fallback", () =>
        {
            string path = FilePath();
            var defaults = ArcadeSettingsFile.Load(path, out var diagnostic);
            Check(diagnostic != null && defaults.creditsToStart == 1 && defaults.continueSeconds == 10, "Missing defaults");
            var settings = new ArcadeSettings { freePlay = true, coinsPerCredit1 = 2, difficulty = 7 };
            ArcadeSettingsFile.Save(path, settings);
            var loaded = ArcadeSettingsFile.Load(path, out diagnostic);
            Check(diagnostic == null && loaded.freePlay && loaded.coinsPerCredit1 == 2 && loaded.difficulty == 7, "Roundtrip");
            settings.creditsToStart = 0;
            Throws<ArgumentOutOfRangeException>(() => ArcadeSettingsFile.Save(path, settings));
            File.WriteAllText(path, "corrupt");
            ArcadeSettingsFile.Load(path, out diagnostic);
            Check(diagnostic != null && File.ReadAllText(path) == "corrupt", "Preserve corruption evidence");
        });
        Run("partial payments survive restart and stay separate by chute", () =>
        {
            string path = FilePath(); var settings = new ArcadeSettings { coinsPerCredit1 = 2, coinsPerCredit2 = 3 };
            using (var ledger = new ArcadeLedger(path, settings))
            { ledger.Coin(1, 0); ledger.Coin(2, 1); Check(ledger.VisibleCredits(0) == 0, "Remainders must not combine"); }
            using (var ledger = new ArcadeLedger(path, settings))
            {
                Check(ledger.Snapshot.partialCoins.SequenceEqual(new[] { 1, 1 }), "Remainders lost");
                ledger.Coin(3, 0); ledger.Coin(4, 1); ledger.Coin(5, 1);
                Check(ledger.VisibleCredits(0) == 2 && ledger.VisibleCredits(1) == 2, "Shared pool");
                Check(ledger.Snapshot.coins.SequenceEqual(new long[] { 2, 3 }), "Bookkeeping");
            }
        });
        Run("duplicate coin and paid Start never apply twice", () =>
        {
            string path = FilePath(); var settings = new ArcadeSettings();
            using (var ledger = new ArcadeLedger(path, settings))
            {
                ledger.Coin(1, 0);
                Check(ledger.Coin(1, 0) == ArcadeLedgerResult.Duplicate, "Duplicate coin");
                Check(ledger.Begin(2, 0, ArcadePaidAction.Start, out var id) == ArcadeLedgerResult.Accepted && id == 2, "Start");
                Check(ledger.Begin(2, 0, ArcadePaidAction.Start, out id) == ArcadeLedgerResult.Duplicate && id == 0, "Duplicate must not authorize another match");
            }
            using (var ledger = new ArcadeLedger(path, settings))
            {
                Check(ledger.Coin(1, 0) == ArcadeLedgerResult.Duplicate, "Persisted watermark");
                Check(ledger.Snapshot.starts == 1 && ledger.VisibleCredits(0) == 0, "Double spend");
            }
        });
        Run("denied Start cannot spend a later coin on replay", () =>
        {
            using (var ledger = new ArcadeLedger(FilePath(), new ArcadeSettings()))
            {
                Check(ledger.Begin(1, 0, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.InsufficientCredit, "Empty Start");
                ledger.Coin(2, 0);
                Check(ledger.Begin(1, 0, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.Duplicate, "Old denied Start");
                Check(ledger.VisibleCredits(0) == 1, "Coin consumed");
            }
        });
        Run("shared one-credit contention grants only the first side", () =>
        {
            using (var ledger = new ArcadeLedger(FilePath(), new ArcadeSettings()))
            {
                ledger.Coin(1, 1);
                Check(ledger.Begin(2, 0, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.Accepted, "P1");
                Check(ledger.Begin(3, 1, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.InsufficientCredit, "P2");
            }
        });
        Run("separate pools, action prices and active-side protection", () =>
        {
            var settings = new ArcadeSettings { sharedCredits = false, creditsToChallenge = 2, creditsToContinue = 3 };
            using (var ledger = new ArcadeLedger(FilePath(), settings))
            {
                ledger.Coin(1, 1); ledger.Service(2, 1);
                Check(ledger.Begin(3, 0, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.InsufficientCredit, "Wrong pool spent");
                Check(ledger.Begin(4, 1, ArcadePaidAction.Challenge, out var id) == ArcadeLedgerResult.Accepted, "Challenge price");
                ledger.Service(5, 1);
                Check(ledger.Begin(6, 1, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.SideAlreadyActive, "Active side charged");
                ledger.Complete(id);
                Check(ledger.Begin(7, 1, ArcadePaidAction.Continue, out _) == ArcadeLedgerResult.InsufficientCredit, "Continue price");
                Check(ledger.Snapshot.serviceCredits == 2 && ledger.Snapshot.challenges == 1, "Separate bookkeeping");
            }
        });
        Run("credit cap retains accepted overflow", () =>
        {
            using (var ledger = new ArcadeLedger(FilePath(), new ArcadeSettings { maxCredits = 2 }))
            {
                for (int i = 1; i <= 4; i++) ledger.Coin(i, 0);
                Check(ledger.VisibleCredits(0) == 2 && ledger.OverflowCredits(0) == 2 && ledger.ShouldInhibitCoin(0), "Overflow not retained");
                ledger.Begin(5, 0, ArcadePaidAction.Start, out var id); ledger.Complete(id);
                Check(ledger.VisibleCredits(0) == 2 && ledger.OverflowCredits(0) == 1, "Overflow not released");
            }
        });
        Run("free play preserves balances across setting changes", () =>
        {
            string path = FilePath(); var settings = new ArcadeSettings();
            using (var ledger = new ArcadeLedger(path, settings)) ledger.Coin(1, 0);
            settings.freePlay = true;
            using (var ledger = new ArcadeLedger(path, settings))
            { ledger.Begin(2, 0, ArcadePaidAction.Start, out _); Check(ledger.VisibleCredits(0) == 1, "Free play debit"); Check(ledger.RecoverInterruptedSessions() == 0, "Free refund"); }
            settings.freePlay = false;
            using (var ledger = new ArcadeLedger(path, settings)) Check(ledger.VisibleCredits(0) == 1, "Stored balance lost");
        });
        Run("interrupted sessions refund once; completed games never refund", () =>
        {
            string path = FilePath(); var settings = new ArcadeSettings();
            using (var ledger = new ArcadeLedger(path, settings))
            { ledger.Coin(1, 0); ledger.Coin(2, 1); ledger.Begin(3, 0, ArcadePaidAction.Start, out var id); ledger.MarkMatchReady(id); ledger.Begin(4, 1, ArcadePaidAction.Start, out id); ledger.Complete(id); }
            using (var ledger = new ArcadeLedger(path, settings))
            { Check(ledger.RecoverInterruptedSessions() == 1, "Recovery"); Check(ledger.RecoverInterruptedSessions() == 0, "Repeated recovery"); }
            using (var ledger = new ArcadeLedger(path, settings))
            { Check(ledger.RecoverInterruptedSessions() == 0 && ledger.VisibleCredits(0) == 1 && ledger.Snapshot.refundedCredits == 1, "Recovery after reboot"); }
        });
        Run("settings and returned snapshots cannot mutate live accounting", () =>
        {
            var settings = new ArcadeSettings();
            using (var ledger = new ArcadeLedger(FilePath(), settings))
            {
                settings.freePlay = true;
                var copy = ledger.Snapshot; copy.credits[0] = 100;
                Check(ledger.Begin(1, 0, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.InsufficientCredit, "Mutation escaped");
            }
        });
        Run("pricing migration refused without losing partial payment", () =>
        {
            string path = FilePath(); var settings = new ArcadeSettings { coinsPerCredit1 = 2 };
            using (var ledger = new ArcadeLedger(path, settings)) ledger.Coin(1, 0);
            Throws<InvalidOperationException>(() => { using var bad = new ArcadeLedger(path, new ArcadeSettings()); });
            using (var ledger = new ArcadeLedger(path, settings)) Check(ledger.Snapshot.partialCoins[0] == 1, "Remainder lost");
        });
        Run("file ownership prevents two ledger writers", () =>
        {
            string path = FilePath();
            using (var ledger = new ArcadeLedger(path, new ArcadeSettings()))
                Throws<IOException>(() => { using var second = new ArcadeLedger(path, new ArcadeSettings()); });
        });
        Run("corrupt primary faults instead of rolling back to backup", () =>
        {
            string path = FilePath();
            using (var ledger = new ArcadeLedger(path, new ArcadeSettings())) ledger.Coin(1, 0);
            Check(File.Exists(path + ".bak"), "No backup"); File.WriteAllText(path, "bad data");
            Throws<Exception>(() => { using var bad = new ArcadeLedger(path, new ArcadeSettings()); });
            Check(File.ReadAllText(path) == "bad data" && File.Exists(path + ".bak"), "Evidence overwritten");
        });
        Run("checksum detects payload tampering", () =>
        {
            string path = FilePath();
            using (var ledger = new ArcadeLedger(path, new ArcadeSettings())) ledger.Coin(1, 0);
            var envelope = ArcadeJson.Decode<ArcadeAtomicFile<ArcadeLedgerState>.Envelope>(File.ReadAllText(path));
            var payload = ArcadeJson.Decode<ArcadeLedgerState>(envelope.payload); payload.credits[0] = 100;
            envelope.payload = ArcadeJson.Encode(payload); File.WriteAllText(path, ArcadeJson.Encode(envelope));
            Throws<InvalidDataException>(() => { using var bad = new ArcadeLedger(path, new ArcadeSettings()); });
        });
        Run("orphan first-save temp is evidence, never free credit or fresh zero", () =>
        {
            string path = FilePath(); File.WriteAllText(path + ".tmp", "uncommitted");
            Throws<InvalidDataException>(() => { using var bad = new ArcadeLedger(path, new ArcadeSettings()); });
            Check(!File.Exists(path), "Fabricated primary");
        });
        Run("temporary external sharing lock retries the same commit once", () =>
        {
            string path = FilePath(); Thread release = null; bool armed = false;
            using (var ledger = new ArcadeLedger(path, new ArcadeSettings(), stage =>
            {
                if (!armed || stage != ArcadeWriteStage.BeforeCommit) return;
                var held = new FileStream(path + ".tmp", FileMode.Open, FileAccess.Read, FileShare.Read);
                release = new Thread(() => { Thread.Sleep(40); held.Dispose(); }); release.Start();
            }))
            {
                armed = true; ledger.Coin(1, 0); release?.Join();
                Check(ledger.VisibleCredits(0) == 1 && ledger.Snapshot.coins[0] == 1, "Retry duplicated transaction");
            }
        });
        Run("persistent external sharing lock faults with bounded retries", () =>
        {
            string path = FilePath(); FileStream held = null; bool armed = false;
            try
            {
                using (var ledger = new ArcadeLedger(path, new ArcadeSettings(), stage =>
                {
                    if (armed && stage == ArcadeWriteStage.BeforeCommit)
                        held = new FileStream(path + ".tmp", FileMode.Open, FileAccess.Read, FileShare.Read);
                }))
                {
                    armed = true; Throws<IOException>(() => ledger.Coin(1, 0));
                    Check(ledger.Faulted && ledger.VisibleCredits(0) == 0, "Uncommitted coin published");
                }
            }
            finally { held?.Dispose(); }
            using (var ledger = new ArcadeLedger(path, new ArcadeSettings()))
                Check(ledger.VisibleCredits(0) == 0 && ledger.Snapshot.lastEventId == 0, "Uncommitted temp promoted");
        });
        foreach (ArcadeWriteStage stage in Enum.GetValues(typeof(ArcadeWriteStage)))
        {
            Run("paid-start fault at " + stage, () => FaultCase(stage, false));
            Run("refund fault at " + stage, () => FaultCase(stage, true));
        }
        Run("coin debounce per chute, held jam once, release re-arms", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings());
            input.Sample(ArcadeControls.Coin1 | ArcadeControls.Coin2, 0);
            var events = input.DrainEvents(); Check(events.Length == 2 && events.All(e => e.kind == ArcadeEventKind.Coin), "Independent chutes");
            input.Sample(ArcadeControls.None, .01); input.Sample(ArcadeControls.Coin1, .02);
            Check(input.DrainEvents().Length == 0, "Bounce counted");
            input.Sample(ArcadeControls.Coin1, 2.03);
            Check(input.DrainEvents().Single().kind == ArcadeEventKind.CoinJam, "Jam missing");
            input.Sample(ArcadeControls.Coin1, 5); Check(input.DrainEvents().Length == 0, "Held coin/jam repeats");
            input.Sample(ArcadeControls.None, 5.1); input.Sample(ArcadeControls.Coin1, 5.2);
            Check(input.DrainEvents().Single().kind == ArcadeEventKind.Coin, "Release did not rearm");
        });
        Run("same-time ordering funds Start and gives P1 priority", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings());
            input.Sample(ArcadeControls.Coin1 | ArcadeControls.Start1 | ArcadeControls.Start2, 1);
            var events = input.DrainEvents();
            Check(events.Select(e => e.kind).SequenceEqual(new[] { ArcadeEventKind.Coin, ArcadeEventKind.Start, ArcadeEventKind.Start }), "Order");
            using (var ledger = new ArcadeLedger(FilePath(), new ArcadeSettings()))
            {
                ledger.Coin(events[0].id, events[0].side);
                Check(ledger.Begin(events[1].id, events[1].side, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.Accepted, "P1");
                Check(ledger.Begin(events[2].id, events[2].side, ArcadePaidAction.Start, out _) == ArcadeLedgerResult.InsufficientCredit, "P2");
            }
            input.Sample(ArcadeControls.Coin1 | ArcadeControls.Start1 | ArcadeControls.Start2, 2);
            Check(input.DrainEvents().Length == 0, "Held Start repeats");
        });
        Run("observed buffered coins survive no fight ticks and scene reset", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings(), 50);
            for (int i = 0; i < 5; i++) { input.Sample(ArcadeControls.Coin1, i); input.Sample(ArcadeControls.None, i + .1); }
            input.ResetGameplayForTransition(); var events = input.DrainEvents();
            Check(events.Length == 5 && events[0].id == 51 && events[4].id == 55, "Queued events lost");
            Check(input.DrainEvents().Length == 0, "Events replayed");
        });
        Run("gameplay opposite directions neutral and button tap buffered once", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings());
            input.Sample(ArcadeControls.P1Left | ArcadeControls.P1Right | ArcadeControls.P1Up | ArcadeControls.P1Down | ArcadeControls.P1B1, 1);
            input.Sample(ArcadeControls.None, 1.01); var command = input.ConsumeGameplay(0);
            Check(command.stick == 5 && command.buttons == 1, "Tap/SOCD");
            Check(input.ConsumeGameplay(0).buttons == 0, "Tap replayed");
            input.Sample(ArcadeControls.P2Up | ArcadeControls.P2Right | ArcadeControls.P2B4, 2);
            command = input.ConsumeGameplay(1); Check(command.stick == 9 && command.buttons == 8, "P2 mapping");
        });
        Run("transition blocks held gameplay until release without blocking Start", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings()); input.Sample(ArcadeControls.P1B1, 1);
            input.ResetGameplayForTransition(); input.Sample(ArcadeControls.P1B1 | ArcadeControls.Start1, 2);
            Check(input.ConsumeGameplay(0).buttons == 0 && input.DrainEvents().Single().kind == ArcadeEventKind.Start, "Transition handling");
            input.Sample(ArcadeControls.None, 3); input.Sample(ArcadeControls.P1B1, 4);
            Check(input.ConsumeGameplay(0).buttons == 1, "Fresh press lost");
        });
        Run("input rejects invalid/backwards clocks", () =>
        {
            var input = new ArcadeInput(new ArcadeSettings()); input.Sample(ArcadeControls.None, 2);
            Throws<ArgumentOutOfRangeException>(() => input.Sample(ArcadeControls.Coin1, 1));
            Throws<ArgumentOutOfRangeException>(() => input.Sample(ArcadeControls.Coin1, double.NaN));
        });
        var report = new Report { utc = DateTime.UtcNow.ToString("O"), dataDirectory = root,
            passed = results.All(r => r.passed), cases = results.ToArray() };
        File.WriteAllText(Path.Combine(args[0], "core-validation.json"), ArcadeJson.Encode(report));
        Console.WriteLine($"{results.Count(r => r.passed)}/{results.Count} cases passed");
        return report.passed ? 0 : 1;
    }

    static void FaultCase(ArcadeWriteStage stage, bool refund)
    {
        string path = FilePath(); var settings = new ArcadeSettings(); bool armed = false;
        using (var ledger = new ArcadeLedger(path, settings, point => { if (armed && point == stage) throw new IOException("Simulated storage interruption"); }))
        {
            ledger.Coin(1, 0);
            if (refund) ledger.Begin(2, 0, ArcadePaidAction.Start, out _);
            armed = true;
            Throws<IOException>(() => { if (refund) ledger.RecoverInterruptedSessions(); else ledger.Begin(2, 0, ArcadePaidAction.Start, out _); });
            Check(ledger.Faulted, "Fault not latched");
            Throws<InvalidOperationException>(() => ledger.Coin(3, 0));
        }
        using (var ledger = new ArcadeLedger(path, settings))
        {
            bool committed = stage == ArcadeWriteStage.AfterCommit;
            if (!refund)
            {
                Check(ledger.VisibleCredits(0) == (committed ? 0 : 1), "Unexpected commit boundary");
                Check(ledger.RecoverInterruptedSessions() == (committed ? 1 : 0), "Pending-session recovery");
            }
            else Check(ledger.RecoverInterruptedSessions() == (committed ? 0 : 1), "Refund applied twice or lost");
            Check(ledger.VisibleCredits(0) == 1 && ledger.Snapshot.sessions.All(s => s == null), "Final entitlement/balance");
        }
        using (var ledger = new ArcadeLedger(path, settings))
            Check(ledger.RecoverInterruptedSessions() == 0 && ledger.VisibleCredits(0) == 1, "Repeated reboot changed balance");
    }
}
