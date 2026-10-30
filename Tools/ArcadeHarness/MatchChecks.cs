using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Versus.Remaster;
using Versus.Remaster.Arcade;

static class MatchChecks
{
    [Serializable] public sealed class Case { public string name, error; public bool passed; }
    [Serializable] public sealed class Report { public bool passed; public string utc, scope; public Case[] cases; }
    static readonly List<Case> cases = new List<Case>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws<T>(Action body) where T : Exception
    { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static void Run(string name, Action body)
    {
        var item = new Case { name = name };
        try { body(); item.passed = true; Console.WriteLine("PASS " + name); }
        catch (Exception e) { item.error = e.ToString(); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        cases.Add(item);
    }
    static ArcadeMatchSetup Setup(bool one = false, bool two = true, int rounds = 1) =>
        new ArcadeMatchSetup(Guid.NewGuid().ToString("N"), "Jin", "Kazuya", one, two, roundsToWin: rounds);
    static CommandGraph FixtureGraph() => new CommandGraph { demos = new[] {
        new CommandGraph.Demo { steps = new[] { 5, 2, 1, 5, 0, 1 } } } };
    static ArcadeMatchController Controller(ArcadeMatchSetup setup, ArcadeInput input, CommandGraph[] graphs = null)
    {
        graphs ??= new[] { FixtureGraph(), FixtureGraph() };
        return new ArcadeMatchController(setup, input, new[] {
            new CpuPlayer(graphs[0], 31, setup.LevelOne), new CpuPlayer(graphs[1], 48, setup.LevelTwo) }, 45);
    }
    static BattleModel Ready(ArcadeMatchSetup setup, ArcadeMatchController controller)
    {
        var battle = new BattleModel(setup.BuildRules(new MatchRules(), 170));
        for (int i = 0; i < 150; i++) controller.Tick(battle, out _, out _);
        Check(battle.phase == MatchPhase.Fight, "Not ready"); return battle;
    }

    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        Run("desktop CPU commands and battle states match pre-M2 traces", () =>
        {
            var expected = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[1]));
            var actual = LegacyReplay.Capture(args[0]);
            Check(expected.SequenceEqual(actual), "Legacy trace changed: " + string.Join(", ", actual.Except(expected)));
            Check(actual.Length == 16, "Expected 16 traces / 96,000 ticks");
        });
        Run("setup validates rules and preserves source configuration", () =>
        {
            var original = new MatchRules { seconds = 60, roundsToWin = 2, health = 100, arenaHalfWidth = 7 };
            var setup = new ArcadeMatchSetup("rules", "Jin", "Kazuya", false, true, 1, 10, 45, 3, false);
            var rules = setup.BuildRules(original, 170);
            Check(rules.seconds == 45 && rules.roundsToWin == 3 && rules.health == 170 && rules.arenaHalfWidth == 7, "Injection");
            Check(original.seconds == 60 && original.roundsToWin == 2 && original.health == 100, "Shared config mutated");
            Throws<ArgumentOutOfRangeException>(() => new ArcadeMatchSetup("bad", "Jin", "Kazuya", false, true, levelTwo: 11));
            Throws<ArgumentOutOfRangeException>(() => new ArcadeMatchSetup("bad", "Jin", "Kazuya", false, true, roundSeconds: 0));
        });
        Run("request is claimed once by its exact scene and then released", () =>
        {
            var request = new ArcadeMatchRequest(Setup(), new ArcadeInput(new ArcadeSettings()), "Assets/_Project/Scenes/LocalVersus2.unity");
            Check(!request.TryClaim("Assets/_Project/Scenes/Other.unity"), "Wrong scene claimed");
            Check(request.TryClaim(request.ScenePath) && !request.TryClaim(request.ScenePath), "Double claim");
            Check(!request.Cancel(), "Claimed request cancelled as pending");
            request.MarkReady(); Check(request.State == ArcadeRequestState.Ready, "Readiness");
            request.Release(); Check(request.State == ArcadeRequestState.Released, "Release");
            Throws<InvalidOperationException>(() => request.MarkReady());
        });
        Run("cancelled and failed requests cannot launch later matches", () =>
        {
            var request = new ArcadeMatchRequest(Setup(), new ArcadeInput(new ArcadeSettings()), "Assets/Test.unity");
            Check(request.Cancel() && !request.TryClaim(request.ScenePath), "Cancelled request consumed");
            request = new ArcadeMatchRequest(Setup(), new ArcadeInput(new ArcadeSettings()), "Assets/Test.unity");
            request.TryClaim(request.ScenePath); request.Fail(new IOException("Missing fighter")); request.Release();
            Check(request.State == ArcadeRequestState.Failed && request.Failure.Contains("Missing fighter"), "Failure evidence lost");
        });
        for (int human = 0; human < 2; human++)
        {
            int side = human;
            Run("mixed match routes human side " + side + " independently", () =>
            {
                var setup = Setup(side != 0, side != 1); var input = new ArcadeInput(new ArcadeSettings());
                var controller = Controller(setup, input); var battle = Ready(setup, controller);
                input.Sample(ArcadeControls.None, 0);
                var button = side == 0 ? ArcadeControls.P1B3 : ArcadeControls.P2B3;
                input.Sample(button, 1);
                controller.Tick(battle, out var one, out var two);
                var actual = side == 0 ? one : two; var other = side == 0 ? two : one;
                Check(actual.buttons == 4 && other.buttons != 4, "Human input leaked to CPU or was lost");
                input.Sample(ArcadeControls.None, 2);
                Check(controller.VictoryButtons(side) == 0, "Released input stuck");
            });
        }
        Run("two humans receive independent inputs and held victory selection", () =>
        {
            var setup = Setup(false, false); var input = new ArcadeInput(new ArcadeSettings());
            var controller = Controller(setup, input); var battle = Ready(setup, controller);
            input.Sample(ArcadeControls.None, 0); input.Sample(ArcadeControls.P1B1 | ArcadeControls.P2B4, 1);
            Check(controller.VictoryButtons(0) == 1 && controller.VictoryButtons(1) == 8, "Victory read");
            controller.Tick(battle, out var one, out var two);
            Check(one.buttons == 1 && two.buttons == 8, "Victory read consumed input");
        });
        Run("intro consumes no gameplay and clears queued intro taps", () =>
        {
            var setup = Setup(false, false); var input = new ArcadeInput(new ArcadeSettings());
            var controller = Controller(setup, input); var battle = new BattleModel(setup.BuildRules(new MatchRules(), 170));
            input.Sample(ArcadeControls.None, 0); input.Sample(ArcadeControls.P1B1, 1); input.Sample(ArcadeControls.None, 2);
            for (int tick = 0; tick < 150; tick++) { controller.Tick(battle, out var one, out _); Check(one.buttons == 0, "Intro command"); }
            input.Sample(ArcadeControls.None, 3); controller.Tick(battle, out var command, out _);
            Check(command.buttons == 0, "Intro tap leaked into fight");
        });
        Run("suspension freezes model while cabinet events remain available", () =>
        {
            var setup = Setup(); var input = new ArcadeInput(new ArcadeSettings()); var controller = Controller(setup, input);
            var battle = Ready(setup, controller); int ticks = battle.ticksLeft; controller.SetSuspended(true);
            input.Sample(ArcadeControls.Coin1 | ArcadeControls.Start1, 1);
            for (int i = 0; i < 120; i++) controller.Tick(battle, out _, out _);
            Check(battle.ticksLeft == ticks && input.DrainEvents().Length == 2, "Suspension affected cabinet events");
            controller.SetSuspended(false); input.Sample(ArcadeControls.None, 2); controller.Tick(battle, out _, out _);
            Check(battle.ticksLeft == ticks - 1, "Resume failed");
        });
        Run("terminal KO snapshot and completion happen once without auto-rematch", () =>
        {
            var setup = Setup(false, false); var input = new ArcadeInput(new ArcadeSettings());
            var controller = Controller(setup, input); var battle = Ready(setup, controller);
            battle.fighters[1].health = 0; controller.Tick(battle, out _, out _);
            Check(controller.TryTakeCompletion(out var result), "No completion");
            Check(result.MatchId == setup.MatchId && result.Winner == 0 && result.PlayerOneRounds == 1 && result.FinalRoundPerfect && !result.TimeUp, "Result data");
            for (int i = 0; i < 600; i++) controller.Tick(battle, out _, out _);
            Check(battle.phase == MatchPhase.MatchOver && !controller.TryTakeCompletion(out _) && controller.Result == result, "Duplicate completion or rematch");
        });
        Run("round end is not match completion", () =>
        {
            var setup = Setup(false, false, 2); var input = new ArcadeInput(new ArcadeSettings());
            var controller = Controller(setup, input); var battle = Ready(setup, controller);
            battle.fighters[1].health = 0; controller.Tick(battle, out _, out _);
            Check(battle.phase == MatchPhase.RoundOver && !controller.TryTakeCompletion(out _), "Early match end");
        });
        Run("time-up result preserves winner, time and damage facts", () =>
        {
            var setup = new ArcadeMatchSetup("timeout", "Jin", "Kazuya", false, false, roundSeconds: 10, roundsToWin: 1);
            var input = new ArcadeInput(new ArcadeSettings()); var controller = Controller(setup, input); var battle = Ready(setup, controller);
            battle.fighters[0].health = 150; battle.fighters[1].health = 120;
            for (int i = 0; i < 600; i++) controller.Tick(battle, out _, out _);
            Check(controller.TryTakeCompletion(out var result) && result.TimeUp && result.Winner == 0 && !result.FinalRoundPerfect &&
                result.FightTicks == 600 && result.FinalTicksLeft == 0, "Timeout result");
        });
        Run("higher authored CPU levels guard sooner and more often", () =>
        {
            int[] guarded = new int[2];
            for (int index = 0; index < 2; index++)
            {
                var cpu = new CpuPlayer(new CommandGraph(), 414, index == 0 ? 1 : 10);
                var self = new FighterState { x = 0, facing = 1 }; var foe = new FighterState { x = 1, attacking = true };
                for (int attack = 0; attack < 100; attack++)
                { foe.move = attack; for (int tick = 0; tick < 25; tick++) if (cpu.Next(self, foe).stick == 4) guarded[index]++; }
            }
            Check(guarded[1] > guarded[0] * 2 && guarded[0] > 0, "No useful difficulty difference");
        });
        Run("higher authored CPU levels pause less between original demo inputs", () =>
        {
            int[] attacks = new int[2];
            for (int index = 0; index < 2; index++)
            {
                var cpu = new CpuPlayer(FixtureGraph(), 123, index == 0 ? 1 : 10);
                var self = new FighterState { facing = 1 }; var foe = new FighterState { x = 1 };
                for (int tick = 0; tick < 2000; tick++) if (cpu.Next(self, foe).buttons == 2) attacks[index]++;
            }
            Check(attacks[1] > attacks[0] * 2, "Offense pacing unchanged");
        });
        foreach (string code in LegacyReplay.Codes)
            Run("real-graph arcade CPU match finishes: " + code, () =>
            {
                var graphs = new[] { CommandGraph.Load(code), CommandGraph.Load(code == "jin" ? "kaz" : "jin") };
                var setup = new ArcadeMatchSetup(code, "Jin", "Kazuya", true, true, 3, 8, 20, 1, false);
                var input = new ArcadeInput(new ArcadeSettings()); var controller = Controller(setup, input, graphs);
                var battle = new BattleModel(setup.BuildRules(new MatchRules(), 170), graphs); int completions = 0;
                for (int tick = 0; tick < 10000; tick++)
                {
                    controller.Tick(battle, out _, out _); if (controller.TryTakeCompletion(out _)) completions++;
                    if (completions > 0 && tick > 3000) break;
                }
                Check(completions == 1 && battle.phase == MatchPhase.MatchOver && controller.Result.Winner >= 0, "Match stuck or duplicate completion");
            });
        var report = new Report { passed = cases.All(c => c.passed), utc = DateTime.UtcNow.ToString("O"), cases = cases.ToArray(),
            scope = "Actual match controller/model/CPU sources, eight command graphs, legacy replay regression. Unity scene/presentation and cabinet hardware not exercised." };
        File.WriteAllText(Path.Combine(args[2], "match-validation.json"), ArcadeJson.Encode(report));
        Console.WriteLine($"{cases.Count(c => c.passed)}/{cases.Count} match cases passed");
        return report.passed ? 0 : 1;
    }
}
