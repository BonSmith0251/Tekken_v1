using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Versus.Remaster;

// What the loser does right after a KO, at full speed (the scene's KO near-freeze not applied): the moves it runs
// through in the first 150 ticks with the tick each started, when it first leaves the floor, when it reaches a death
// move (fatal_s, dDm_death*) and when it lies down. Summarised by the kind of reaction the KO blow gave.
// KOACTION_COLLAPSE=1 measures with BattleModel.immediateKoCollapse on (the default), else the original route (off).
// Usage: run.sh KoAction [project].
static class KoAction
{
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        var rows = new List<(string kind, int death, int down, string seq)>();
        foreach (var (a, b, seed0) in new[] { ("jin", "xia", 7), ("kaz", "ltn", 11), ("asa", "jin", 3), ("ltn", "asa", 9), ("xia", "kaz", 5), ("jin", "kaz", 13) })
            for (int k = 0; k < 4; k++)
            {
                int seed = seed0 + 31 * k;
                var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
                var model = new BattleModel(new MatchRules { health = 170 }, graphs) { immediateKoCollapse = Environment.GetEnvironmentVariable("KOACTION_COLLAPSE") == "1" };
                var cpu = new[] { new CpuPlayer(graphs[0], seed), new CpuPlayer(graphs[1], seed + 12) };
                var phase = model.phase;
                for (int t = 0; t < 60 * 400 && model.phase != MatchPhase.MatchOver; t++)
                {
                    var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                    var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                    model.Tick(c0, c1);
                    if (phase == MatchPhase.Fight && model.phase != MatchPhase.Fight && model.winner >= 0)
                    {
                        int loser = 1 - model.winner; var it = model.interpreters[loser];
                        var seq = new List<string>(); string last = null; int downAt = -1, airAt = -1, deathAt = -1;
                        for (int f = 0; f <= 150; f++)
                        {
                            string n = it.Active.moves[it.Move].name;
                            if (n != last) { seq.Add(f + ":" + n); last = n; }
                            var s = model.fighters[loser];
                            if (downAt < 0 && s.downed) downAt = f;
                            if (airAt < 0 && s.airborne) airAt = f;
                            if (deathAt < 0 && (n == "fatal_s" || n.StartsWith("dDm_death"))) deathAt = f;
                            if (f < 150) model.Tick(PlayerCommand.Neutral, PlayerCommand.Neutral);
                        }
                        string first = seq[0].Substring(seq[0].IndexOf(':') + 1);
                        string kind = airAt >= 0 && airAt <= 2 ? "launched" : first == "fatal_s" ? "fatal_s at once" : seq.Any(x => x.Contains("sKAM")) ? "stagger, back to stance, fatal_s" : "other";
                        rows.Add((kind, deathAt, downAt, $"{a} vs {b}: " + string.Join("  ", seq.Take(5))));
                    }
                    phase = model.phase;
                }
            }
        foreach (var group in rows.GroupBy(r => r.kind))
        {
            var deaths = group.Select(r => r.death).Where(x => x >= 0).OrderBy(x => x).ToList();
            var downs = group.Select(r => r.down).Where(x => x >= 0).OrderBy(x => x).ToList();
            Console.WriteLine($"{group.Key}: {group.Count()} KOs; death move starts at tick {Med(deaths)} (median, range {Range(deaths)}); lying at {Med(downs)} ({Range(downs)})");
            foreach (var r in group.Take(5)) Console.WriteLine("   " + r.seq);
        }
        return 0;
    }
    static string Med(List<int> v) => v.Count == 0 ? "-" : v[v.Count / 2].ToString();
    static string Range(List<int> v) => v.Count == 0 ? "-" : v.First() + "-" + v.Last();
}
