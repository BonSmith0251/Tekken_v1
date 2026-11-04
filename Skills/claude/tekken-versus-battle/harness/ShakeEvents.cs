using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Versus.Remaster;

// Camera shake events in CPU matches, as VersusGame now produces them: only the original move
// properties 0x8001-0x8004 / 0x8006-0x8009 (presets 3-6). Prints events per round, per preset, and the
// moves raising them. Usage: run.sh ShakeEvents [project].
static class ShakeEvents
{
    static int Preset(long id) => id >= 0x8006 ? (int)(id - 0x8006 + 3) : (int)(id - 0x8001 + 3);
    static int Main(string[] args)
    {
        UnityEngine.Resources.Root = Path.Combine(args[0], "Assets/_Project/Resources");
        foreach (var (a, b) in new[] { ("jin", "xia"), ("kaz", "ltn"), ("asa", "jin") })
        {
            var graphs = new[] { CommandGraph.Load(a), CommandGraph.Load(b) };
            var model = new BattleModel(new MatchRules { health = 170 }, graphs);
            var cpu = new[] { new CpuPlayer(graphs[0], 7), new CpuPlayer(graphs[1], 19) };
            var presets = new Dictionary<int, int>(); var moves = new Dictionary<string, int>();
            int rounds = 0, hits = 0; var phase = model.phase;
            model.Hit += h => hits++;
            model.MoveProperty += (player, id, value) =>
            {
                if (id < 0x8001 || id > 0x8009 || id == 0x8005) return;
                int p = Preset(id); presets[p] = presets.GetValueOrDefault(p) + 1;
                var it = model.interpreters[player];
                string n = it.ActedGraph.moves[it.ActedMove].name; moves[n] = moves.GetValueOrDefault(n) + 1;
            };
            for (int t = 0; t < 60 * 300 && model.phase != MatchPhase.MatchOver; t++)
            {
                var c0 = model.phase == MatchPhase.Fight ? cpu[0].Next(model.fighters[0], model.fighters[1]) : PlayerCommand.Neutral;
                var c1 = model.phase == MatchPhase.Fight ? cpu[1].Next(model.fighters[1], model.fighters[0]) : PlayerCommand.Neutral;
                model.Tick(c0, c1);
                if (model.phase != phase && (model.phase == MatchPhase.RoundOver || model.phase == MatchPhase.MatchOver)) rounds++;
                phase = model.phase;
            }
            int total = presets.Values.Sum();
            Console.WriteLine($"{a} vs {b}: {rounds} rounds, {hits} hits, {total} shakes ({(rounds > 0 ? total / (float)rounds : 0):0.0}/round) | presets {string.Join(" ", presets.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value))}");
            Console.WriteLine("   moves: " + string.Join(", ", moves.OrderByDescending(x => x.Value).Take(8).Select(x => x.Key + "x" + x.Value)));
        }
        return 0;
    }
}
