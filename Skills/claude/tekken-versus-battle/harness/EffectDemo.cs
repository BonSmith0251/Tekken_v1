using System;
using System.IO;
using System.Linq;
using Versus.Remaster;

// The tuning scene's exchange demonstrations (BattleEffectDemo): for each pairing and scenario, does the directed
// exchange produce the event it is for (clean hit, counter hit, guarded hit, trade with 17+ damage, KO, Rage Drive KO)?
// Prints the measured press, startup and distance per scenario. Usage: run.sh EffectDemo [project].
static class EffectDemo
{
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        int pass = 0, total = 0;
        foreach (var (a, b) in new[] { ("jin", "xia"), ("kaz", "ltn"), ("asa", "jin"), ("ltn", "asa"), ("xia", "kaz") })
        {
            var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
            var model = new BattleModel(new MatchRules { health = 170 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], 7), new CpuPlayer(graphs[1], 19) };
            var demo = new BattleEffectDemo(model, graphs, cpu);
            model.BeginFight();
            Console.WriteLine($"{a} vs {b}:");
            foreach (BattleEffectDemo.Scenario scenario in Enum.GetValues(typeof(BattleEffectDemo.Scenario)))
            {
                // Fresh round for each scenario (the KO ones end the round).
                if (model.phase != MatchPhase.Fight) { model.Restart(); model.BeginFight(); }
                int hits = demo.hits, counters = demo.counters, guards = demo.guards, trades = demo.trades, kos = demo.kos, drives = demo.rageDriveKos;
                bool tradeTick = false; var struck = new int[] { -1, -1 };
                void Watch(HitEvent h) { if (!h.blocked) struck[h.defender] = Math.Max(struck[h.defender], h.damage); }
                model.Hit += Watch;
                demo.Start(scenario);
                for (int t = 0; t < 900 && demo.Active; t++)
                {
                    var c0 = demo.Command(0, model.fighters[0], model.fighters[1]);
                    var c1 = demo.Command(1, model.fighters[1], model.fighters[0]);
                    model.Tick(c0, c1);
                    if (struck[0] > 0 && struck[1] > 0 && Math.Max(struck[0], struck[1]) >= 17) tradeTick = true;
                    struck[0] = struck[1] = -1;
                    demo.Tick();
                }
                model.Hit -= Watch;
                bool ok = scenario switch
                {
                    BattleEffectDemo.Scenario.Hit => demo.hits > hits && demo.counters == counters,
                    BattleEffectDemo.Scenario.Counter => demo.counters > counters,
                    BattleEffectDemo.Scenario.Guard => demo.guards > guards,
                    BattleEffectDemo.Scenario.Trade => tradeTick,
                    BattleEffectDemo.Scenario.Ko => demo.kos > kos,
                    BattleEffectDemo.Scenario.RageDriveKo => demo.rageDriveKos > drives,
                    _ => false,
                };
                total++; if (ok) pass++;
                Console.WriteLine($"   {(ok ? "ok  " : "FAIL")} {demo.Report}");
                // Let the round end play out before the next scenario.
                for (int t = 0; t < 400 && model.phase != MatchPhase.Fight; t++) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
            }
        }
        Console.WriteLine($"{pass} of {total} demonstrations produced their event");
        return 0;
    }
}
